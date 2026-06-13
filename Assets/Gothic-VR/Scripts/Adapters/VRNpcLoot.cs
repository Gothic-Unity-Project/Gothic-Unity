#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.HurricaneVR.Framework.Shared.Utilities;
using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;

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
            _isOpen = true;
            PlayOpenSound();
            CreateSockets();
            StartCoroutine(FillSockets());
        }

        public void Close()
        {
            _isOpen = false;
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

            var itemName = vobLoader.Container.Vob.Name;
            var itemData = _vmCacheService.TryGetItemData(itemName);
            if (itemData == null)
                return;

            // If this was an equipped weapon: destroy the mesh from NPC body and remove from equipped list
            var equippedMatch = _npcContainer.Props.EquippedItems
                .FirstOrDefault(e => string.Equals(
                    _gameStateService.GothicVm.GetSymbolByIndex(e.Index)?.Name,
                    itemName,
                    StringComparison.OrdinalIgnoreCase));
            if (equippedMatch != null)
            {
                _npcContainer.Props.EquippedItems.Remove(equippedMatch);
                var weaponGo = FindEquippedWeaponGo(equippedMatch);
                if (weaponGo != null)
                    Destroy(weaponGo);
            }

            _npcInventoryService.ExtRemoveInvItems(_npcContainer.Instance, itemData.Index, item.Amount);

            if (_pendingRefresh != null)
                StopCoroutine(_pendingRefresh);
            _pendingRefresh = StartCoroutine(RefreshAfterDelay());
        }

        private IEnumerator RefreshAfterDelay()
        {
            yield return new WaitForSeconds(RefreshDelay);
            _pendingRefresh = null;
            StartCoroutine(ClearAndRefill());
        }

        /// <summary>
        /// Finds the weapon GameObject in the NPC skeleton using the same slot mapping as NpcWeaponMeshBuilder.
        /// Tries the holster slot first, then ZS_RIGHTHAND as fallback (for NPCs that died mid-draw).
        /// </summary>
        private GameObject FindEquippedWeaponGo(ItemInstance equipped)
        {
            if (_npcLoader == null)
                return null;

            var mainFlag = (VmGothicEnums.ItemFlags)equipped.MainFlag;
            var flags = (VmGothicEnums.ItemFlags)equipped.Flags;
            var npcRoot = _npcLoader.gameObject;

            string holsterSlot;
            switch (mainFlag)
            {
                case VmGothicEnums.ItemFlags.ItemKatNf:
                    switch (flags)
                    {
                        case VmGothicEnums.ItemFlags.Item2HdAxe:
                        case VmGothicEnums.ItemFlags.Item2HdSwd:
                            holsterSlot = "ZS_LONGSWORD";
                            break;
                        default:
                            holsterSlot = "ZS_SWORD";
                            break;
                    }
                    break;
                case VmGothicEnums.ItemFlags.ItemKatFf:
                    holsterSlot = flags == VmGothicEnums.ItemFlags.ItemCrossbow ? "ZS_CROSSBOW" : "ZS_BOW";
                    break;
                default:
                    return null;
            }

            // Try holster slot, fall back to drawn position
            foreach (var slotName in new[] { holsterSlot, "ZS_RIGHTHAND" })
            {
                var slotGo = npcRoot.FindChildRecursively(slotName);
                if (slotGo != null && slotGo.transform.childCount > 0)
                    return slotGo.transform.GetChild(0).gameObject;
            }

            return null;
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
