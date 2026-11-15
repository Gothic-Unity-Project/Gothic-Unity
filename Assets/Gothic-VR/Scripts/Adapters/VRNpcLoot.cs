using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
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
        private const string _equippedLabelName = "EquippedLabel";

        [Tooltip("Loot slot (VrSocketSlot, \"VR Socket Slot.prefab\").")]
        [SerializeField] private GameObject _slotPrefab;

        [Tooltip("Vanilla equipped-slot background quad (\"EquippedLabel.prefab\"), placed behind an equipped item.")]
        [SerializeField] private GameObject _equippedLabelPrefab;

        [Tooltip("Seconds until an NPC's loot backpack nobody picked up disappears (the loot closes like clicking " +
                 "the body again).")]
        [SerializeField] private float _lootBackpackTimeoutSeconds = 20f;

        /// <summary>
        /// The loot socket - also used for the trade counter's offer zones.
        /// </summary>
        public GameObject SocketPrefab => _slotPrefab;

        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly InventoryTransferService _inventoryTransferService;

        private IInventoryOwner _lootOwner;
        private VRBackpack _lootBackpack;
        private GameObject _lootBackpackRoot;
        private Coroutine _lootBackpackTimeout;

        private NpcContainer _npcContainer;
        private NpcLoader _npcLoader;
        private readonly List<IVrSocket> _sockets = new();
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
            // Update only watches for the NPC standing up while the loot is open - not every frame on every NPC.
            enabled = false;
        }

        private void Update()
        {
            // A knocked out NPC that gets up takes his things back out of reach.
            if (_isOpen && _npcContainer != null && !_npcContainer.IsDown())
                Close();
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
            // Vanilla: nothing to take (equipped items and armor don't count) - no inventory, only a "nothing to get".
            if (!HasLoot(npc))
            {
                Logger.Log($"[VRNpcLoot] {npc.Instance.GetName(NpcNameSlot.Slot0)} has nothing to loot - not opened",
                    LogCat.VR);
                _npcInventoryService.PrintNothingToGet();
                return;
            }

            _npcContainer = npc;
            _lootOwner = _inventoryTransferService.GetOwner(npc);
            _isOpen = true;
            GlobalEventDispatcher.NpcInventoryChanged.AddListener(OnNpcInventoryChanged);
            enabled = true;
            PlayOpenSound();

            // DeveloperConfig.NpcLootMode: loot sockets, the NPC's backpack, or both.
            var lootMode = _configService.Dev.NpcLootMode;
            if (lootMode != DeveloperConfigEnums.NpcLootMode.Backpack)
            {
                CreateSockets();
                StartCoroutine(FillSockets());
            }
            if (lootMode != DeveloperConfigEnums.NpcLootMode.Sockets)
                SpawnLootBackpack(npc);
        }

        /// <summary>
        /// DeveloperConfig.NpcLootMode Backpack/Both: the NPC's items in a tinted copy of the hero's backpack (categories,
        /// pages) lying next to the NPC - next to the loot sockets, both stay in sync (NpcInventoryChanged).
        /// </summary>
        private void SpawnLootBackpack(NpcContainer npc)
        {
            var npcTransform = npc.Go.transform;
            var position = npcTransform.position + npcTransform.right * 0.7f + Vector3.up * 0.4f;
            _lootBackpack = VRBackpack.SpawnCopy(position, Quaternion.LookRotation(-npcTransform.right),
                $"LootBackpack_{npc.Instance.GetName(NpcNameSlot.Slot0)}", out var backpackGo);
            if (_lootBackpack == null)
                return;
            _lootBackpackRoot = backpackGo;
            _lootBackpack.SetNpcOwner(npc);

            // Lying untouched for a while: gone like closing the loot - no backpacks lying around the battlefield.
            // Held, it stays; let go, the time starts again.
            var interactable = VrInteractable.Find(_lootBackpack);
            if (interactable != null)
            {
                interactable.Grabbed += _ => StopLootBackpackTimeout();
                interactable.Released += _ => StartLootBackpackTimeout();
            }
            StartLootBackpackTimeout();
            Logger.Log($"[VRNpcLoot] Loot backpack of {npc.Go.name} spawned", LogCat.VR);
        }

        private void StartLootBackpackTimeout()
        {
            StopLootBackpackTimeout();
            if (_isOpen && isActiveAndEnabled)
                _lootBackpackTimeout = StartCoroutine(CloseUnusedLootBackpack());
        }

        private void StopLootBackpackTimeout()
        {
            if (_lootBackpackTimeout == null)
                return;
            StopCoroutine(_lootBackpackTimeout);
            _lootBackpackTimeout = null;
        }

        private IEnumerator CloseUnusedLootBackpack()
        {
            yield return new WaitForSeconds(Mathf.Max(1f, _lootBackpackTimeoutSeconds));
            _lootBackpackTimeout = null;
            if (!_isOpen)
                yield break;

            Logger.Log("[VRNpcLoot] Loot backpack not picked up - loot closed", LogCat.VR);
            Close();
        }

        private void DespawnLootBackpack()
        {
            StopLootBackpackTimeout();
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
                if (socket == null || !socket.IsOccupied)
                    continue;
                var heldRoot = socket.Held.transform.parent.gameObject;
                socket.ForceRelease();
                Destroy(heldRoot);
            }
            DestroySockets();
            _tempIgnoreSocketing = false;
            _vrWeaponService.DrawSoundsActive = true;
        }

        /// <summary>
        /// Something left to take: equipped weapons and inventory items, without armor (BuildLootList).
        /// </summary>
        public bool HasLoot(NpcContainer npc)
        {
            var previous = _npcContainer;
            _npcContainer = npc;
            try
            {
                return BuildLootList().Count > 0;
            }
            finally
            {
                if (_isOpen)
                    _npcContainer = previous;
            }
        }

        /// <summary>
        /// A dead NPC with nothing left to take closes its loot (the backpack
        /// goes) and can't be hovered or grabbed anymore - the looted corpse still lit up and opened an empty loot.
        /// </summary>
        public bool TryMakeEmptyCorpseInert(NpcContainer npc)
        {
            if (npc?.Props == null ||
                npc.Props.BodyState != VmGothicEnums.BodyState.BsDead || HasLoot(npc))
                return false;

            if (_isOpen)
                Close();
            var interactable = VrInteractable.Find(this);
            if (interactable != null)
            {
                if (interactable.IsHeld)
                    interactable.ForceRelease();
                interactable.IsGrabEnabled = false;
            }
            Logger.Log($"[VRNpcLoot] {npc.Instance.GetName(NpcNameSlot.Slot0)} looted empty - corpse no longer " +
                       "grabbable", LogCat.VR);
            return true;
        }

        /// <summary>
        /// Everything the NPC gives away into the hero's inventory (like the loot's "Take all" button); an empty corpse closes its loot and can't be grabbed anymore.
        /// </summary>
        public int TakeAll(NpcContainer npc)
        {
            var itemCount = _inventoryTransferService.TakeAll(_inventoryTransferService.GetOwner(npc));
            PlayOpenSound();
            Logger.Log($"[VRNpcLoot] Took all from {npc.Instance.GetName(NpcNameSlot.Slot0)}: {itemCount} item(s)",
                LogCat.VR);
            if (!TryMakeEmptyCorpseInert(npc) && _isOpen)
                Close();
            return itemCount;
        }

        private void OnNpcInventoryChanged(NpcContainer npc)
        {
            if (!_isOpen || npc != _npcContainer)
                return;

            if (TryMakeEmptyCorpseInert(npc))
                return;

            if (_pendingRefresh != null)
                StopCoroutine(_pendingRefresh);
            _pendingRefresh = StartCoroutine(RefreshAfterDelay());
        }

        public void Close()
        {
            _isOpen = false;
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            DespawnLootBackpack();
            enabled = false;
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
                var socketGo = Instantiate(_slotPrefab, transform);
                var xOffset = -totalWidth / 2f + i * SocketSpacing;
                socketGo.transform.localPosition = new Vector3(xOffset, SocketHeight, 0f);
                socketGo.transform.localRotation = Quaternion.identity;
                socketGo.transform.localScale = Vector3.one * 1.6f;

                var socket = socketGo.GetComponentInChildren<IVrSocket>();
                socket.Released += OnItemTakenFromLoot;

                _socketRoots.Add(socketGo);
                _sockets.Add(socket);
            }
        }

        private void DestroySockets()
        {
            foreach (var socket in _sockets)
                socket.Released -= OnItemTakenFromLoot;

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
                if (!equipped.IsMeleeWeapon() && (mainFlag & VmGothicEnums.ItemFlags.ItemKatFf) == 0)
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

                var interactable = vobContainer.Go.GetComponentInChildren<VrInteractable>(true);
                var freeSocket = _sockets.FirstOrDefault(s => !s.IsOccupied);
                if (freeSocket != null)
                {
                    freeSocket.TryPlace(interactable, true);
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
                var label = root.transform.Find(_equippedLabelName);
                if (label != null)
                    Destroy(label.gameObject);
            }

            foreach (var socket in _sockets)
            {
                if (!socket.IsOccupied)
                    continue;

                var heldRoot = socket.Held.transform.parent.gameObject;
                socket.ForceRelease();
                heldRoot.SetActive(false);
                StartCoroutine(DestroyNextFrame(heldRoot));
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

        private static IEnumerator DestroyNextFrame(GameObject go)
        {
            yield return null;
            Destroy(go);
        }

        private void OnItemTakenFromLoot(VrGrabArgs args)
        {
            if (_tempIgnoreSocketing || args.Interactable == null)
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            if (vobLoader?.Container == null)
                return;

            var item = vobLoader.Container.VobAs<IItem>();
            if (item == null)
                return;

            // An equipped weapon also leaves the NPC's body. NpcInventoryChanged refreshes us and the loot backpack.
            _lootOwner.Remove(item.GetSymbolName(), Mathf.Max(1, item.Amount));
        }

        private IEnumerator RefreshAfterDelay()
        {
            yield return new WaitForSeconds(RefreshDelay);
            _pendingRefresh = null;
            StartCoroutine(ClearAndRefill());
        }

        private GameObject GetSocketRoot(IVrSocket socket)
        {
            var idx = _sockets.IndexOf(socket);
            return idx >= 0 && idx < _socketRoots.Count ? _socketRoots[idx] : null;
        }

        private void AddEquippedLabel(GameObject socketRoot)
        {
            if (socketRoot == null)
                return;

            if (_textureService.EquippedSlotMaterial == null)
                return;

            var labelGo = Instantiate(_equippedLabelPrefab);
            labelGo.name = _equippedLabelName;
            labelGo.GetComponent<VREquippedSlotMarker>().Place(socketRoot.transform, _textureService.EquippedSlotMaterial);
        }
    }
}
