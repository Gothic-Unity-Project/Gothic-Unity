#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.HurricaneVR.Framework.Shared.Utilities;
using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Gothic.Core.Logging;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters
{
    public class VRNpcLoot : MonoBehaviour
    {
        private const int MaxVisibleSlots = 5;
        private const float RefreshDelay = 0.6f;
        private const float SocketHeight = 0.8f;
        private const float SocketSpacing = 0.22f;

        [SerializeField] private GameObject _socketPrefab;

        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;

        private const string _backpackPrefabPath = "VR/Prefabs/Player-Elements/BackPack";
        private NpcInventoryOwner _lootOwner;
        private VRBackpack _lootBackpack;
        private GameObject _lootBackpackRoot;

        private NpcContainer _npcContainer;
        private NpcLoader _npcLoader;
        private readonly List<HVRSocket> _sockets = new();
        private readonly List<GameObject> _socketRoots = new();
        private bool _isOpen;
        private bool _tempIgnoreSocketing;
        private Coroutine _pendingRefresh;

        private readonly struct LootEntry
        {
            public readonly ContentItem Item;
            public readonly bool IsEquipped;

            public LootEntry(ContentItem item, bool isEquipped)
            {
                Item = item;
                IsEquipped = isEquipped;
            }
        }

        private void Awake()
        {
            _npcLoader = GetComponentInParent<NpcLoader>();
        }

        public void Toggle(NpcContainer npc)
        {
            if (_isOpen)
                Close();
            else
                Open(npc);
        }

        public void Open(NpcContainer npc)
        {
            _npcContainer = npc;
            _lootOwner = new NpcInventoryOwner(npc);
            _isOpen = true;
            GlobalEventDispatcher.NpcInventoryChanged.AddListener(OnNpcInventoryChanged);
            PlayOpenSound();
            CreateSockets();
            StartCoroutine(FillSockets());

            if (_configService.Dev.EnableNpcLootBackpack)
                SpawnLootBackpack(npc);
        }

        /// <summary>
        /// DeveloperConfig.EnableNpcLootBackpack: the NPC's items in a tinted copy of the hero's backpack (categories,
        /// pages) lying next to the NPC - next to the loot sockets, both stay in sync (NpcInventoryChanged).
        /// </summary>
        private void SpawnLootBackpack(NpcContainer npc)
        {
            var prefab = Resources.Load<GameObject>(_backpackPrefabPath);
            if (prefab == null)
            {
                Logger.LogWarning($"[VRNpcLoot] Backpack prefab {_backpackPrefabPath} not found.", LogCat.VR);
                return;
            }

            var npcTransform = npc.Go.transform;
            var position = npcTransform.position + npcTransform.right * 0.7f + Vector3.up * 0.4f;
            var backpackGo = Instantiate(prefab, position, Quaternion.LookRotation(-npcTransform.right));
            backpackGo.name = $"LootBackpack_{npc.Instance.GetName(NpcNameSlot.Slot0)}";
            backpackGo.Inject();

            _lootBackpack = backpackGo.GetComponentInChildren<VRBackpack>(true);
            if (_lootBackpack == null)
            {
                Destroy(backpackGo);
                return;
            }
            _lootBackpackRoot = backpackGo;
            _lootBackpack.SetNpcOwner(npc);
            Logger.Log($"[VRNpcLoot] Loot backpack of {npc.Go.name} spawned", LogCat.VR);
        }

        private void DespawnLootBackpack()
        {
            if (_lootBackpack != null)
                _lootBackpack.Despawn(_lootBackpackRoot);
            else if (_lootBackpackRoot != null)
                Destroy(_lootBackpackRoot);
            _lootBackpack = null;
            _lootBackpackRoot = null;
        }

        /// <summary>
        /// The NPC got culled while its loot was open: no Update/coroutines anymore - close everything right now
        /// (the loot backpack stayed lying around).
        /// </summary>
        private void OnDisable()
        {
            if (!_isOpen)
                return;

            _isOpen = false;
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            DespawnLootBackpack();
            _pendingRefresh = null;

            // Socketed loot items must not count as taken from the NPC.
            _tempIgnoreSocketing = true;
            foreach (var socket in _sockets)
            {
                if (socket == null || !socket.IsGrabbing)
                    continue;
                var heldRoot = socket.HeldObject.transform.parent.gameObject;
                socket.ForceRelease();
                Destroy(heldRoot);
            }
            DestroySockets();
            _tempIgnoreSocketing = false;
            _vrWeaponService.DrawSoundsActive = true;
        }

        private void OnNpcInventoryChanged(NpcContainer npc)
        {
            if (!_isOpen || npc != _npcContainer)
                return;

            if (_pendingRefresh != null)
                StopCoroutine(_pendingRefresh);
            _pendingRefresh = StartCoroutine(RefreshAfterDelay());
        }

        private void Update()
        {
            if (!_isOpen || _npcContainer == null)
                return;

            var state = _npcContainer.Props.BodyState;
            if (state != VmGothicEnums.BodyState.BsDead && state != VmGothicEnums.BodyState.BsUnconscious)
                Close();
        }

        public void Close()
        {
            _isOpen = false;
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            DespawnLootBackpack();
            if (_pendingRefresh != null)
            {
                StopCoroutine(_pendingRefresh);
                _pendingRefresh = null;
            }
            StartCoroutine(CloseSockets());
        }

        private void PlayOpenSound()
        {
            var clip = _audioService.CreateAudioClip(_audioService.InvOpen.File);
            if (clip == null)
                return;

            var audioSource = GetComponentInChildren<AudioSource>();
            audioSource?.PlayOneShot(clip);
        }

        private void CreateSockets()
        {
            var totalWidth = (MaxVisibleSlots - 1) * SocketSpacing;

            for (var i = 0; i < MaxVisibleSlots; i++)
            {
                var socketGo = Instantiate(_socketPrefab, transform);
                var xOffset = -totalWidth / 2f + i * SocketSpacing;
                socketGo.transform.localPosition = new Vector3(xOffset, SocketHeight, 0f);
                socketGo.transform.localRotation = Quaternion.identity;
                socketGo.transform.localScale = Vector3.one * 1.6f;

                var socket = socketGo.GetComponentInChildren<HVRSocket>();
                socket.Released.AddListener(OnItemTakenFromLoot);

                _socketRoots.Add(socketGo);
                _sockets.Add(socket);
            }
        }

        private void DestroySockets()
        {
            foreach (var root in _socketRoots)
            {
                if (root != null)
                    Destroy(root);
            }
            _socketRoots.Clear();
            _sockets.Clear();
        }

        private List<LootEntry> BuildLootList()
        {
            var result = new List<LootEntry>();
            var equippedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Equipped weapons first (melee + ranged) — shown with [E] badge and GO removal on take
            foreach (var equipped in _npcContainer.Props.EquippedItems)
            {
                var mainFlag = (VmGothicEnums.ItemFlags)equipped.MainFlag;
                if (mainFlag != VmGothicEnums.ItemFlags.ItemKatNf && mainFlag != VmGothicEnums.ItemFlags.ItemKatFf)
                    continue;

                var symbolName = _gameStateService.GothicVm.GetSymbolByIndex(equipped.Index)?.Name;
                if (symbolName == null)
                    continue;

                equippedNames.Add(symbolName);
                result.Add(new LootEntry(new ContentItem(symbolName, 1), isEquipped: true));
            }

            // All inventory items: skip armor, skip equipped weapons already listed above
            foreach (var invItem in _npcInventoryService.GetAllInventoryItems(_npcContainer.Instance))
            {
                if (equippedNames.Contains(invItem.Name))
                    continue;

                var itemData = _vmCacheService.TryGetItemData(invItem.Name);
                if (itemData != null)
                {
                    var cat = ((VmGothicEnums.ItemFlags)itemData.MainFlag).ToInventoryCategory();
                    if (cat == VmGothicEnums.InvCats.InvArmor)
                        continue;
                }

                result.Add(new LootEntry(invItem, isEquipped: false));
            }

            return result;
        }

        private IEnumerator FillSockets()
        {
            yield return PopulateSockets(clearFirst: false);
        }

        private IEnumerator ClearAndRefill()
        {
            yield return PopulateSockets(clearFirst: true);
        }

        private IEnumerator PopulateSockets(bool clearFirst)
        {
            _tempIgnoreSocketing = true;
            _vrWeaponService.DrawSoundsActive = false;

            if (clearFirst)
            {
                ClearSocketContents();
                yield return null;
            }

            foreach (var entry in BuildLootList().Take(MaxVisibleSlots))
            {
                var vobContainer = _vobService.CreateItem(new Item
                {
                    Name = entry.Item.Name,
                    Visual = new VisualMesh(),
                    Instance = entry.Item.Name,
                    Amount = entry.Item.Amount
                });

                vobContainer.Go.GetComponentInChildren<Rigidbody>().isKinematic = false;

                yield return null;

                var grabbable = vobContainer.Go.GetComponentInChildren<HVRGrabbable>(true);
                var freeSocket = _sockets.FirstOrDefault(s => !s.IsGrabbing);
                if (freeSocket != null)
                {
                    freeSocket.TryGrab(grabbable, true, true);
                    if (entry.IsEquipped)
                        AddEquippedLabel(GetSocketRoot(freeSocket));
                }
            }

            yield return null;
            _tempIgnoreSocketing = false;
            _vrWeaponService.DrawSoundsActive = true;
        }

        private void ClearSocketContents()
        {
            // Remove equipped labels before clearing items
            foreach (var root in _socketRoots)
            {
                var label = root.transform.Find("EquippedLabel");
                if (label != null)
                    Destroy(label.gameObject);
            }

            foreach (var socket in _sockets)
            {
                if (!socket.IsGrabbing)
                    continue;

                var heldRoot = socket.HeldObject.transform.parent.gameObject;
                socket.ForceRelease();
                heldRoot.SetActive(false);
                this.ExecuteNextUpdate(() => Destroy(heldRoot));
            }
        }

        private IEnumerator CloseSockets()
        {
            _tempIgnoreSocketing = true;
            _vrWeaponService.DrawSoundsActive = false;

            ClearSocketContents();
            yield return null;

            DestroySockets();
            _tempIgnoreSocketing = false;
            _vrWeaponService.DrawSoundsActive = true;
        }

        public void OnItemTakenFromLoot(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_tempIgnoreSocketing)
                return;

            var vobLoader = grabbable.GetComponentInParent<VobLoader>();
            if (vobLoader?.Container == null)
                return;

            var item = vobLoader.Container.VobAs<IItem>();
            if (item == null)
                return;

            // An equipped weapon also leaves the NPC's body. NpcInventoryChanged refreshes us and the loot backpack.
            _lootOwner.Remove(vobLoader.Container.Vob.Name, Mathf.Max(1, item.Amount));
        }

        private IEnumerator RefreshAfterDelay()
        {
            yield return new WaitForSeconds(RefreshDelay);
            _pendingRefresh = null;
            StartCoroutine(ClearAndRefill());
        }

        private GameObject GetSocketRoot(HVRSocket socket)
        {
            var idx = _sockets.IndexOf(socket);
            return idx >= 0 && idx < _socketRoots.Count ? _socketRoots[idx] : null;
        }

        private static void AddEquippedLabel(GameObject socketRoot)
        {
            if (socketRoot == null)
                return;

            var labelGo = new GameObject("EquippedLabel");
            labelGo.transform.SetParent(socketRoot.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            labelGo.transform.localScale = Vector3.one * 0.013f;

            var tmp = labelGo.AddComponent<TextMeshPro>();
            // Assign font immediately after AddComponent to suppress repeated OnPreRenderObject warnings.
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font != null)
                tmp.font = font;
            tmp.text = "[E]";
            tmp.fontSize = 12;
            tmp.color = new Color(1f, 0.65f, 0f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.fontStyle = FontStyles.Bold;
        }
    }
}
#endif
