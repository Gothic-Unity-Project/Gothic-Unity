#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using Assets.HurricaneVR.Framework.Shared.Utilities;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrPickpocket: G1's pickpocketing in VR (rules in PickpocketService).
    /// - Sneaking with the talent, humans nearby who can't see the hero show a purse (a small copy of the backpack) on
    ///   their right hip - opposite of the one-handed sword (ZS_SWORD mirrored). It glows; a hand near it lights it up.
    /// - Grip on the purse: the victim's pockets open as a backpack in that hand (his items without what he wears or
    ///   wields). The other hand takes items out - each one can get the hero caught.
    /// - Caught (a roll, or the victim sees the hero while he robs him): the backpack is gone, the victim gets
    ///   PERC_CATCHTHIEF, witnesses PERC_ASSESSTHEFT. Letting go of the backpack or walking away closes the pockets.
    /// Runtime only - the stolen items are in the inventories, which are saved.
    /// </summary>
    public class VRPickpocket : MonoBehaviour
    {
        [Inject] private readonly PickpocketService _pickpocketService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly AudioService _audioService;

        private const string _backpackPrefabPath = "VR/Prefabs/Player-Elements/BackPack";
        private const string _backpackBodyName = "BackPack-Body";
        private const string _pelvisBoneName = "BIP01 PELVIS";
        private const float _scanInterval = 0.25f;
        private const float _purseRange = 3f;
        private const int _maxPurses = 3;
        private const float _purseSize = 0.14f;
        // A big reach: the NPC's body (armor) is in the way of the hand.
        private const float _hoverDistance = 0.2f;
        private const float _hoverScale = 1.3f;
        private const float _pelvisSideOffset = 0.18f;
        // Hanging out of the hip towards the back (in the body it couldn't be reached) - tried out on Lukis and Ghorri.
        private static readonly Vector3 _purseOffset = new(0f, 0f, -0.1f);
        private static readonly Quaternion _purseRotation = Quaternion.Euler(-90f, 0f, 90f);
        private const float _robRange = 2.5f;
        private const float _caughtCooldown = 30f;
        private const float _pulseSpeed = 3f;
        private static readonly Color _glowColor = new(1f, 0.75f, 0.2f);

        private readonly Dictionary<NpcContainer, Purse> _purses = new();
        private readonly Dictionary<NpcContainer, float> _caughtUntil = new();
        private float _nextScan;
        private Mesh _purseMesh;
        private Material _purseMaterial;
        private Purse _hovered;
        private HVRHandSide _hoveredSide;

        private NpcContainer _victim;
        private PickpocketInventoryOwner _pockets;
        private VRBackpack _backpack;
        private GameObject _backpackRoot;
        private HVRGrabbable _backpackGrabbable;
        private bool _isCaught;

        private class Purse
        {
            public NpcContainer Npc;
            public GameObject Go;
            public Transform SwordSlot;
            public Transform Pelvis;
            public Material Material;
        }

        public bool IsRobbing => _victim != null;

        private void Awake()
        {
            this.Inject();
        }

        private void Update()
        {
            if (IsRobbing)
            {
                UpdateRobbery();
                return;
            }

            if (!_pickpocketService.IsEnabled || !_pickpocketService.CanHeroSteal())
            {
                ClearPurses();
                return;
            }

            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + _scanInterval;
                Scan();
            }
            UpdatePurses();
            UpdateHover();
        }

        private void Scan()
        {
            var hero = _pickpocketService.Hero;
            if (hero?.Go == null)
                return;

            var heroPosition = hero.Go.transform.position;
            var victims = _multiTypeCacheService.NpcCache
                .Where(npc => npc?.Go != null && npc.Go.activeInHierarchy &&
                              (npc.Go.transform.position - heroPosition).sqrMagnitude <= _purseRange * _purseRange &&
                              !IsCoolingDown(npc) && _pickpocketService.CanBeRobbed(npc))
                .OrderBy(npc => (npc.Go.transform.position - heroPosition).sqrMagnitude)
                .Take(_maxPurses)
                .ToList();

            foreach (var npc in _purses.Keys.Where(npc => !victims.Contains(npc)).ToList())
                RemovePurse(npc);
            foreach (var npc in victims.Where(npc => !_purses.ContainsKey(npc)))
                AddPurse(npc);
        }

        private bool IsCoolingDown(NpcContainer npc)
        {
            return _caughtUntil.TryGetValue(npc, out var until) && Time.time < until;
        }

        private void AddPurse(NpcContainer npc)
        {
            if (!TryLoadPurseLook())
                return;

            var go = new GameObject($"Purse_{npc.Instance.GetName(NpcNameSlot.Slot0)}");
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = _purseMesh;
            var material = new Material(_purseMaterial);
            material.EnableKeyword("_EMISSION");
            model.AddComponent<MeshRenderer>().sharedMaterial = material;
            model.transform.localRotation = _purseRotation;
            FitSize(model, _purseSize);
            model.transform.localPosition += _purseOffset;

            _purses[npc] = new Purse
            {
                Npc = npc,
                Go = go,
                SwordSlot = npc.Go.FindChildRecursively(Gothic.Core.Const.Constants.SlotSword)?.transform,
                Pelvis = npc.Go.FindChildRecursively(_pelvisBoneName)?.transform,
                Material = material
            };
            Logger.Log($"[Pickpocket] Purse shown on {go.name}", LogCat.VR);
        }

        private void RemovePurse(NpcContainer npc)
        {
            if (!_purses.Remove(npc, out var purse))
                return;
            if (purse == _hovered)
                _hovered = null;
            if (purse.Go != null)
                Destroy(purse.Go);
            if (purse.Material != null)
                Destroy(purse.Material);
        }

        private void ClearPurses()
        {
            foreach (var npc in _purses.Keys.ToList())
                RemovePurse(npc);
        }

        /// <summary>
        /// The backpack's own mesh and material, made small - the purse.
        /// </summary>
        private bool TryLoadPurseLook()
        {
            if (_purseMesh != null)
                return true;

            var prefab = Resources.Load<GameObject>(_backpackPrefabPath);
            var body = prefab != null ? prefab.FindChildRecursively(_backpackBodyName) : null;
            var mesh = body != null ? body.GetComponent<MeshFilter>()?.sharedMesh : null;
            var material = body != null ? body.GetComponent<MeshRenderer>()?.sharedMaterial : null;
            if (mesh == null || material == null)
            {
                Logger.LogWarning($"[Pickpocket] No {_backpackBodyName} mesh in {_backpackPrefabPath} - no purses.",
                    LogCat.VR);
                return false;
            }
            _purseMesh = mesh;
            _purseMaterial = material;
            return true;
        }

        private static void FitSize(GameObject model, float size)
        {
            var renderer = model.GetComponent<Renderer>();
            var largest = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.y, renderer.bounds.size.z);
            if (largest > 0.0001f)
                model.transform.localScale *= size / largest;
            model.transform.position += model.transform.parent.position - renderer.bounds.center;
        }

        /// <summary>
        /// Right hip: the sword slot (left hip) mirrored on the NPC's middle plane, or beside the pelvis.
        /// </summary>
        private void UpdatePurses()
        {
            var pulse = 0.35f + 0.25f * (1f + Mathf.Sin(Time.time * _pulseSpeed));
            foreach (var purse in _purses.Values)
            {
                if (purse.Go == null || purse.Npc.Go == null)
                    continue;

                var root = purse.Npc.Go.transform;
                Vector3 position;
                if (purse.SwordSlot != null)
                {
                    var slot = purse.SwordSlot.position;
                    position = slot - 2f * Vector3.Dot(slot - root.position, root.right) * root.right;
                }
                else if (purse.Pelvis != null)
                    position = purse.Pelvis.position + root.right * _pelvisSideOffset;
                else
                    position = root.position + Vector3.up * 0.9f + root.right * _pelvisSideOffset;

                purse.Go.transform.SetPositionAndRotation(position, root.rotation);
                var isHovered = purse == _hovered;
                purse.Go.transform.localScale = Vector3.one * (isHovered ? _hoverScale : 1f);
                purse.Material.SetColor("_EmissionColor", _glowColor * (isHovered ? 1.2f : pulse));
            }
        }

        private void UpdateHover()
        {
            Purse hovered = null;
            var hoveredSide = HVRHandSide.Right;
            var best = _hoverDistance;
            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                if (!IsHandFree(side))
                    continue;
                var hand = _vrPlayerService.GetHandModelGo(side);
                if (hand == null)
                    continue;
                foreach (var purse in _purses.Values)
                {
                    if (purse.Go == null)
                        continue;
                    var distance = Vector3.Distance(hand.transform.position, purse.Go.transform.position);
                    if (distance >= best)
                        continue;
                    best = distance;
                    hovered = purse;
                    hoveredSide = side;
                }
            }

            if (hovered != _hovered)
            {
                _hovered = hovered;
                _hoveredSide = hoveredSide;
                if (hovered != null)
                    _hapticsService.Vibrate(hoveredSide, VrHapticsService.VibrationType.Info);
            }

            if (_hovered == null)
                return;

            // The same grip grabs the NPC (VRNpc: talk) - not while a hand is at his purse.
            _pickpocketService.BlockTalk(_hovered.Npc);
            if (HVRController.GetButtonState(_hoveredSide, HVRButtons.Grip).JustActivated)
                StartRobbery(_hovered.Npc, _hoveredSide);
        }

        /// <summary>
        /// Nothing in the hand - or the NPC itself (HVR grabs him with the grip at his hip, VRNpc lets go again).
        /// </summary>
        private bool IsHandFree(HVRHandSide side)
        {
            var target = GetHandGrabber(side)?.GrabbedTarget;
            return target == null || target.GetComponentInParent<VRNpc>() != null;
        }

        private HVRHandGrabber GetHandGrabber(HVRHandSide side)
        {
            var controller = GetComponentInParent<VRPlayerController>();
            if (controller == null)
                return null;
            return side == HVRHandSide.Left ? controller.LeftHand : controller.RightHand;
        }

        private void StartRobbery(NpcContainer victim, HVRHandSide side)
        {
            var hand = GetHandGrabber(side);
            var handModel = _vrPlayerService.GetHandModelGo(side);
            if (hand == null || handModel == null)
                return;

            if (hand.GrabbedTarget != null)
                hand.ForceRelease();

            var handTransform = handModel.transform;
            var victimName = victim.Instance.GetName(NpcNameSlot.Slot0);
            _backpack = VRBackpack.SpawnCopy(handTransform.position + handTransform.forward * 0.1f,
                handTransform.rotation, $"Pockets_{victimName}", out _backpackRoot);
            if (_backpack == null)
                return;

            ClearPurses();
            _victim = victim;
            _pickpocketService.RobbedNpc = victim;
            _isCaught = false;
            _pockets = new PickpocketInventoryOwner(victim);
            _pockets.ItemTaken += OnItemTaken;
            _backpack.SetContainerOwner(_pockets);

            _backpackGrabbable = _backpack.GetComponent<HVRGrabbable>();
            if (_backpackGrabbable == null || !hand.TryGrab(_backpackGrabbable, true))
            {
                Logger.LogWarning($"[Pickpocket] The pockets of {victimName} couldn't be put into the hand - closed.",
                    LogCat.VR);
                EndRobbery();
                return;
            }
            _backpackGrabbable.Released.AddListener(OnBackpackReleased);

            var clip = _audioService.CreateAudioClip(_audioService.InvOpen.File);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, handTransform.position);
            _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Success);
            Logger.Log($"[Pickpocket] Robbing {victimName}", LogCat.VR);
        }

        private void OnItemTaken(string itemInstanceName, int amount)
        {
            if (_isCaught || _victim == null)
                return;

            Logger.Log($"[Pickpocket] Took {amount}x {itemInstanceName} from " +
                       $"{_victim.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.VR);
            // Inside HVR's socket event - the backpack goes in the next Update.
            if (_pickpocketService.RollCaught())
                _isCaught = true;
        }

        private void OnBackpackReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (!_isCaught && _victim != null)
                Logger.Log("[Pickpocket] Backpack let go - pockets closed", LogCat.VR);
            // Not inside the release itself: HVR still works on the grabbable.
            this.ExecuteNextUpdate(EndRobbery);
        }

        private void UpdateRobbery()
        {
            if (_victim?.Go == null || !_victim.Go.activeInHierarchy || NpcHelperService.IsDown(_victim))
            {
                EndRobbery();
                return;
            }

            var hero = _pickpocketService.Hero;
            if (!_isCaught && hero?.Instance != null &&
                _npcHelperService.CanSeeNpc(_victim.Instance, hero.Instance, false))
            {
                Logger.Log($"[Pickpocket] {_victim.Instance.GetName(NpcNameSlot.Slot0)} sees the hero robbing him",
                    LogCat.VR);
                _isCaught = true;
            }

            if (_isCaught)
            {
                Caught();
                return;
            }

            if (hero?.Go != null &&
                (hero.Go.transform.position - _victim.Go.transform.position).sqrMagnitude > _robRange * _robRange)
            {
                Logger.Log("[Pickpocket] Walked away - pockets closed", LogCat.VR);
                EndRobbery();
            }
        }

        private void Caught()
        {
            var victim = _victim;
            _caughtUntil[victim] = Time.time + _caughtCooldown;
            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
                _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Error);
            EndRobbery();
            _pickpocketService.CatchThief(victim);
        }

        private void EndRobbery()
        {
            if (_pockets != null)
                _pockets.ItemTaken -= OnItemTaken;
            if (_backpackGrabbable != null)
            {
                _backpackGrabbable.Released.RemoveListener(OnBackpackReleased);
                if (_backpackGrabbable.IsBeingHeld)
                    _backpackGrabbable.ForceRelease();
            }
            if (_backpack != null)
                _backpack.Despawn(_backpackRoot);
            else if (_backpackRoot != null)
                Destroy(_backpackRoot);

            _victim = null;
            _pickpocketService.RobbedNpc = null;
            _pockets = null;
            _backpack = null;
            _backpackRoot = null;
            _backpackGrabbable = null;
            _isCaught = false;
            _nextScan = 0f;
        }

        private void OnDisable()
        {
            if (IsRobbing)
                EndRobbery();
            ClearPurses();
        }
    }
}
#endif
