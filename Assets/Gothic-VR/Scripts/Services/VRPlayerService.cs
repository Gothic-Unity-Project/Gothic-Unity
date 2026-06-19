#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Services.Context;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Bags;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Contains global states about Hurricane VR player.
    /// </summary>
    public class VRPlayerService
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VobService _vobService;
        
        public VRContextInteractionService VRContextInteractionService => _contextInteractionService.GetImpl<VRContextInteractionService>();
        public VRPlayerInputs VRPlayerInputs => VRContextInteractionService.GetVRPlayerInputs();
        
        public GameObject GrabbedItemLeft;
        public GameObject GrabbedItemRight;

        public enum HandType
        {
            Left,
            Right
        }

        public bool IsDualGrabbed => GrabbedItemLeft != null && GrabbedItemLeft == GrabbedItemRight;

        public void SetGrab(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            HVRHandGrabber handGrabber;
            if (grabber is HVRForceGrabber forceGrabber)
                handGrabber = forceGrabber.HandGrabber;
            else
                handGrabber = grabber as HVRHandGrabber;

            if (handGrabber == null)
                return;

            if (handGrabber.IsLeftHand)
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemLeft == grabbable.gameObject)
                    return;
                
                GrabbedItemLeft = grabbable.gameObject;
            }
            else
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemRight == grabbable.gameObject)
                    return;

                GrabbedItemRight = grabbable.gameObject;
            }

            // If we grabbed the element with second hand, return.
            if (IsDualGrabbed)
                return;

            // Otherwise alter inventory count
            var vobItem = grabbable.GetComponentInParent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem == null) return;
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            _playerService.AddItem(instanceName, vobItem.Amount);
        }

        public void UnsetGrab(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            var dualGrabPrev = IsDualGrabbed;

            HVRHandGrabber handGrabber;
            if (grabber is HVRForceGrabber forceGrabber)
                handGrabber = forceGrabber.HandGrabber;
            else
                handGrabber = grabber as HVRHandGrabber;

            if (handGrabber == null)
                return;

            if (handGrabber.IsLeftHand)
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemLeft == null)
                    return;

                GrabbedItemLeft = null;
            }
            else
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemRight == null)
                    return;

                GrabbedItemRight = null;
            }

            // If we removed one hand from our item.
            if (dualGrabPrev)
                return;

            // Otherwise alter inventory count
            var vobItem = grabbable.GetComponentInParent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem == null) return;
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            _playerService.RemoveItem(instanceName, vobItem.Amount);
        }

        public void HandleMobGrab(VobLoader loader)
        {
            var mob = loader.Container.VobAs<IInteractiveObject>();
            if (mob == null) return;

            var vm = _gameStateService.GothicVm;
            if (vm == null) return;

            var condFunc = mob.ConditionFunction ?? "";
            Logger.Log($"[VRPlayerService] HandleMobGrab mob={loader.gameObject.name} condFunc={(condFunc.Length > 0 ? condFunc : "empty")}", LogCat.Ai);

            // ConditionFunction is a Daedalus check: returns 0 = interaction denied.
            if (condFunc.Length > 0)
            {
                var symbol = vm.GetSymbolByName(condFunc);
                if (symbol == null)
                {
                    Logger.LogWarning($"[VRPlayerService] ConditionFunction '{condFunc}' not found in VM", LogCat.Ai);
                    return;
                }
                var allowed = vm.Call<int>(symbol.Index);
                if (allowed == 0)
                {
                    Logger.Log($"[VRPlayerService] HandleMobGrab: '{condFunc}' denied (returned 0)", LogCat.Ai);
                    return;
                }
            }

            // Condition passed (or no condition) — trigger the linked mover via mob.Target.
            var moverTarget = mob.Target;
            if (string.IsNullOrEmpty(moverTarget))
            {
                Logger.LogWarning($"[VRPlayerService] HandleMobGrab: no Target on {loader.gameObject.name}", LogCat.Ai);
                return;
            }
            if (!_vobService.TryGetMover(moverTarget, out var moverVob) || moverVob?.Go == null)
            {
                Logger.LogWarning($"[VRPlayerService] HandleMobGrab: mover '{moverTarget}' not found", LogCat.Ai);
                return;
            }
            if (!moverVob.Go.TryGetComponent<MoverAdapter>(out var adapter))
            {
                Logger.LogWarning($"[VRPlayerService] HandleMobGrab: MoverAdapter missing on '{moverTarget}'", LogCat.Ai);
                return;
            }
            Logger.Log($"[VRPlayerService] HandleMobGrab: triggering mover '{moverTarget}'", LogCat.Ai);
            adapter.Toggle();
        }

        public HVRController GetHand(HVRHandSide side)
        {
            if (side == HVRHandSide.Left)
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().LeftHand.Controller;
            else
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().RightHand.Controller;
        }

        public HVRForceGrabber GetForceGrabber(HVRHandSide side)
        {
            var ctrl = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
            if (ctrl == null) return null;
            return side == HVRHandSide.Left ? ctrl.LeftHand.ForceGrabber : ctrl.RightHand.ForceGrabber;
        }

        // True during any active spell (telekinesis or combat targeting) — blocks NPC dialog.
        public bool IsSpellActive => IsTelekinesisActive || IsSpellTargetingActive;
        public bool IsSpellTargetingActive { get; private set; }

        // Telekinesis state — lives here so it survives VRRuneCaster destruction when one hand releases.
        public bool IsTelekinesisActive { get; private set; }
        public event System.Action TelekinesisDeactivated;
        private float _savedForceGrabRange;
        private float _savedBagMaxDistance;
        private Vector3 _savedCapsuleCenter;
        private float _savedCapsuleHeight;
        private float _savedCapsuleRadius;

        // Spell targeting state (combat spells — extends rune hand bags only).
        private HVRHandSide _spellTargetingSide;
        private Vector3 _savedSpellCapsuleCenter;
        private float _savedSpellCapsuleHeight;
        private float _savedSpellCapsuleRadius;
        private float _savedSpellBagMaxDistance;

        public void ActivateSpellTargeting(HVRHandSide runeHandSide, float range)
        {
            if (IsSpellTargetingActive) return;
            _spellTargetingSide = runeHandSide;
            var fg = GetForceGrabber(runeHandSide);
            if (fg == null) return;
            var savedOnce = false;
            foreach (var bag in fg.GrabBags)
            {
                if (!(bag is HVRForceGrabberBag fgBag)) continue;
                var capsule = bag.GetComponent<CapsuleCollider>();
                if (capsule == null) continue;
                if (!savedOnce)
                {
                    _savedSpellCapsuleCenter = capsule.center;
                    _savedSpellCapsuleHeight = capsule.height;
                    _savedSpellCapsuleRadius = capsule.radius;
                    _savedSpellBagMaxDistance = fgBag.MaxDistanceAllowed;
                    savedOnce = true;
                }
                var newHeight = _savedSpellCapsuleHeight * 2.5f;
                capsule.height = newHeight;
                capsule.center = new Vector3(0f, 0f, newHeight * 0.5f);
                fgBag.MaxDistanceAllowed = range;
                Logger.Log($"[SpellTarget] {fg.gameObject.name}/{bag.gameObject.name} capsule extended", LogCat.VR);
            }
            IsSpellTargetingActive = true;
        }

        public void DeactivateSpellTargeting()
        {
            if (!IsSpellTargetingActive) return;
            var fg = GetForceGrabber(_spellTargetingSide);
            if (fg != null)
            {
                foreach (var bag in fg.GrabBags)
                {
                    if (!(bag is HVRForceGrabberBag fgBag)) continue;
                    var capsule = bag.GetComponent<CapsuleCollider>();
                    if (capsule != null)
                    {
                        capsule.center = _savedSpellCapsuleCenter;
                        capsule.height = _savedSpellCapsuleHeight;
                        capsule.radius = _savedSpellCapsuleRadius;
                    }
                    fgBag.MaxDistanceAllowed = _savedSpellBagMaxDistance;
                }
            }
            IsSpellTargetingActive = false;
        }

        public void ActivateTelekinesis(float range)
        {
            if (IsTelekinesisActive) return;

            var left  = GetForceGrabber(HVRHandSide.Left);
            var right = GetForceGrabber(HVRHandSide.Right);
            if (right != null) _savedForceGrabRange = right.MaxRayCastDistance;
            else if (left != null) _savedForceGrabRange = left.MaxRayCastDistance;
            else _savedForceGrabRange = 10f;

            var savedOnce = false;
            foreach (var fg in new[] { left, right })
            {
                if (fg == null) continue;
                fg.MaxRayCastDistance = range;

                foreach (var bag in fg.GrabBags)
                {
                    if (!(bag is HVRForceGrabberBag fgBag)) continue;
                    var capsule = bag.GetComponent<CapsuleCollider>();
                    if (capsule == null) continue;
                    if (!savedOnce)
                    {
                        _savedCapsuleCenter = capsule.center;
                        _savedCapsuleHeight = capsule.height;
                        _savedCapsuleRadius = capsule.radius;
                        _savedBagMaxDistance = fgBag.MaxDistanceAllowed;
                        savedOnce = true;
                    }
                    var newHeight = _savedCapsuleHeight * 2.5f;
                    capsule.height = newHeight;
                    capsule.center = new Vector3(0f, 0f, newHeight * 0.5f);
                    fgBag.MaxDistanceAllowed = range;
                    Logger.Log($"[Telekinesis] {fg.gameObject.name}/{bag.gameObject.name} capsule extended to {range}", LogCat.VR);
                }

                fg.Grabbed.AddListener(OnTelekinesisItemGrabbed);
            }

            IsTelekinesisActive = true;
        }

        public void DeactivateTelekinesis()
        {
            if (!IsTelekinesisActive) return;

            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                var fg = GetForceGrabber(side);
                if (fg == null) continue;
                fg.MaxRayCastDistance = _savedForceGrabRange;

                foreach (var bag in fg.GrabBags)
                {
                    if (!(bag is HVRForceGrabberBag fgBag)) continue;
                    var capsule = bag.GetComponent<CapsuleCollider>();
                    if (capsule != null)
                    {
                        capsule.center = _savedCapsuleCenter;
                        capsule.height = _savedCapsuleHeight;
                        capsule.radius = _savedCapsuleRadius;
                    }
                    fgBag.MaxDistanceAllowed = _savedBagMaxDistance;
                }

                fg.Grabbed.RemoveListener(OnTelekinesisItemGrabbed);
            }

            IsTelekinesisActive = false;
            TelekinesisDeactivated?.Invoke();
        }

        public bool TryTelekinesisGrab()
        {
            foreach (var side in new[] { HVRHandSide.Right, HVRHandSide.Left })
            {
                var fg = GetForceGrabber(side);
                if (fg == null || fg.HoverTarget == null) continue;
                if (fg.TryGrab(fg.HoverTarget, force: true))
                    return true;
            }
            return false;
        }

        private void OnTelekinesisItemGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            DeactivateTelekinesis();
        }
    }
}
#endif
