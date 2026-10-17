#if GOTHIC_HVR_INSTALLED
using System;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Services.Context;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Manager;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Adapters.Vob.Container;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Bags;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using EventType = ZenKit.EventType;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly DocService _docService;
        
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

        /// <summary>
        /// DeveloperConfig.EnableMobRequiredItem: like the engine (OpenGothic Interactive::checkUseConditions), a mob with
        /// useWithItem only works when the player has that item, else Player_Mob_Missing_Item is said. G2's Jharkendar
        /// portal (ItMi_PortalRing_Addon) opened without the ornament ring. Only for mobs with a trigger target - the
        /// crafting mobs (anvil, stove, ...) are left as they are. The item isn't taken, as in OpenGothic.
        /// </summary>
        private bool IsMissingRequiredItem(IInteractiveObject mob, DaedalusVm vm)
        {
            if (!_configService.Dev.EnableMobRequiredItem || string.IsNullOrEmpty(mob.Item))
                return false;

            var itemSymbol = vm.GetSymbolByName(mob.Item);
            var hero = (NpcInstance)vm.GlobalHero;
            if (itemSymbol == null || hero == null || _npcInventoryService.ExtNpcHasItems(hero, itemSymbol.Index) > 0)
                return false;

            Logger.Log($"[VRPlayerService] HandleMobGrab: '{mob.Name}' needs '{mob.Item}' - the hero has none", LogCat.Ai);
            var missingItemFunc = vm.GetSymbolByName("Player_Mob_Missing_Item");
            if (missingItemFunc != null)
            {
                var oldSelf = vm.GlobalSelf;
                vm.GlobalSelf = vm.GlobalHero;
                try
                {
                    vm.Call(missingItemFunc.Index);
                }
                finally
                {
                    vm.GlobalSelf = oldSelf;
                }
            }
            return true;
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
                // Like the engine: self = the player. The "missing something/item" reactions of a denied mob
                // (G2's last door on Irdorath: Player_Mob_Missing_*) talk through self - without it nothing was said.
                var oldSelf = vm.GlobalSelf;
                vm.GlobalSelf = vm.GlobalHero;
                int allowed;
                try
                {
                    allowed = vm.Call<int>(symbol.Index);
                }
                finally
                {
                    vm.GlobalSelf = oldSelf;
                }
                if (allowed == 0)
                {
                    Logger.Log($"[VRPlayerService] HandleMobGrab: '{condFunc}' denied (returned 0)", LogCat.Ai);
                    return;
                }
            }

            // DeveloperConfig.EnableFireStates: touching a fireplace/torch lights it or puts it out.
            if (_configService.Dev.EnableFireStates && mob is IFire &&
                loader.GetComponentInChildren<VobFireHandler>(true) is { } fire)
            {
                fire.Toggle();
                return;
            }

            // A container without a lid (mummy) is a plain mob: the click shows/hides a loot backpack like a corpse.
            // Chests with a lid (VRVobContainer) come here too when their lid is grabbed - they open physically.
            if (mob is IContainer container && loader.GetComponentInChildren<VRVobContainer>(true) == null)
            {
                if (!loader.TryGetComponent<VRStaticContainerLoot>(out var loot))
                    loot = loader.gameObject.AddComponent<VRStaticContainerLoot>();
                loot.Toggle(container);
                return;
            }

            // Crafting mobs that take their useWithItem into the hand (forge, anvil, ...): items swapped like the engine.
            if (_configService.Dev.EnableMobItemEvents && string.IsNullOrEmpty(mob.Target) &&
                TryUseMobWithItemEvents(loader, mob))
                return;

            // Mobs without a mover target but with an onStateFunc (G2 shrines, alchemy/rune tables, bookstands, ...).
            if (_configService.Dev.EnableMobsiDialogs && string.IsNullOrEmpty(mob.Target) &&
                TryUseMobStateFunction(loader, mob))
                return;

            if (!string.IsNullOrEmpty(mob.Target) && IsMissingRequiredItem(mob, vm))
                return;

            // Condition passed (or no condition) — trigger the linked mover via mob.Target.
            var moverTarget = mob.Target;
            if (string.IsNullOrEmpty(moverTarget))
            {
                Logger.LogWarning($"[VRPlayerService] HandleMobGrab: no Target on {loader.gameObject.name}", LogCat.Ai);
                return;
            }
            // Direct mover: Toggle (player physically interacts — can open and close).
            if (_vobService.TryGetMovers(moverTarget, out var moverVobs))
            {
                Logger.Log($"[VRPlayerService] HandleMobGrab: toggling mover '{moverTarget}' ({moverVobs.Count} instance(s))", LogCat.Ai);
                foreach (var moverVob in moverVobs)
                {
                    if (moverVob?.Go == null || !moverVob.Go) continue;
                    var adapter = moverVob.Go.GetComponentInChildren<MoverAdapter>();
                    if (adapter != null) adapter.Toggle();
                }
                return;
            }
            // Target is a CodeMaster, TriggerList, TriggerScript, or chained mover — dispatch through the chain.
            // Pass the mob's VOB name as sender so CodeMaster can match it against its Slaves list.
            var senderName = loader.Container.Vob.Name;
            Logger.Log($"[VRPlayerService] HandleMobGrab: dispatching trigger chain '{moverTarget}' from '{senderName}'", LogCat.Ai);
            _vobService.DispatchTrigger(moverTarget, senderName);
        }

        /// <summary>
        /// Like the engine reaching mob state 1: call "[onStateFunc]_S1" with self = hero. MOBSI scripts like
        /// PrayShrine_S1 then open a dialog via AI_ProcessInfos(hero) whose choices are C_INFOs with npc = PC_Hero.
        /// The dialog box is anchored next to the mob (like at an NPC).
        /// </summary>
        private bool TryUseMobStateFunction(VobLoader loader, IInteractiveObject mob, bool isItemInHand = false)
        {
            if (string.IsNullOrEmpty(mob.OnStateChangeFunction))
                return false;

            // X marks are dug up by hitting them (VRDigSpot) - grabbing must not dig.
            if (VRDigSpot.IsDigSpot(loader.Container, _vmCacheService))
                return false;

            var vm = _gameStateService.GothicVm;
            var functionName = mob.OnStateChangeFunction + "_S1";
            var symbol = vm.GetSymbolByName(functionName);
            if (symbol == null)
            {
                Logger.Log($"[MobDialog] '{functionName}' not in the scripts - mob '{loader.name}' ignored.", LogCat.Dialog);
                return false;
            }

            // Like the engine: a mob used with an item needs it in the inventory (e.g. a lab flask).
            // Not when it was already taken into the hand (TryUseMobWithItemEvents).
            var hero = _playerService.HeroContainer;
            if (!isItemInHand && !string.IsNullOrEmpty(mob.Item))
            {
                var item = _vmCacheService.TryGetItemData(mob.Item);
                if (item != null && _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
                {
                    Logger.Log($"[MobDialog] '{loader.name}' needs '{mob.Item}'.", LogCat.Dialog);
                    PlayMobMissingItemReaction(hero);
                    return true;
                }
            }

            // DeveloperConfig.EnableMobDocuments: an open page (bookstand) closes on the next touch.
            VRMobDocument document = null;
            if (_configService.Dev.EnableMobDocuments)
            {
                var openDocument = loader.transform.Find(VRMobDocument.AnchorName);
                if (openDocument != null)
                {
                    UnityEngine.Object.Destroy(openDocument.gameObject);
                    Logger.Log($"[MobDialog] Closed the page of '{loader.name}'.", LogCat.Dialog);
                    return true;
                }
                document = VRMobDocument.Create(loader.gameObject);
                _docService.PendingItemGo = document.gameObject;
            }

            _dialogService.PrepareMobDialog(CreateMobDialogAnchor(loader, _configService.Dev.EnableMobDialogOwnBounds));

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(symbol.Index);
                Logger.Log($"[MobDialog] Called '{functionName}' for '{loader.name}'.", LogCat.Dialog);
            }
            catch (Exception e)
            {
                Logger.LogError($"[MobDialog] '{functionName}' failed: {e.Message}", LogCat.Dialog);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }

            if (document != null)
            {
                _docService.PendingItemGo = null;
                if (document.HasPages)
                    Logger.Log($"[MobDialog] '{loader.name}' shows a page (Doc_Show).", LogCat.Dialog);
                else
                    UnityEngine.Object.Destroy(document.gameObject);
            }

            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableMobItemEvents: like the engine, the human mob animations move the mob's useWithItem
        /// (HUMANS.MDS events of T_[scheme]_S0_2_S1 / _S1_2_S0): ItemInsert takes one into the hand, ItemExchange swaps
        /// it, ItemRemove puts it back into the inventory, ItemDestroy uses it up. G2's forge turns raw steel into hot
        /// steel (BSFIRE), the anvil takes the hot steel before SMITHWEAPON_S1 counts it (Npc_HasItems + 1). Before,
        /// the forge did nothing and the anvil only said "With what?".
        /// </summary>
        private bool TryUseMobWithItemEvents(VobLoader loader, IInteractiveObject mob)
        {
            if (string.IsNullOrEmpty(mob.Item) || string.IsNullOrEmpty(mob.Visual?.Name))
                return false;

            var scheme = mob.Visual.Name.Split('_')[0];
            var mds = _resourceCacheService.TryGetModelScript("HUMANS", false);
            var useAnim = mds?.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase($"T_{scheme}_S0_2_S1"));
            if (useAnim == null || useAnim.EventTags.All(e => e.Type != EventType.ItemInsert))
                return false;

            var hero = _playerService.HeroContainer;
            var item = _vmCacheService.TryGetItemData(mob.Item);
            if (item == null || _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
            {
                Logger.Log($"[MobItem] '{loader.name}' ({scheme}) needs '{mob.Item}'.", LogCat.Ai);
                PlayMobMissingItemReaction(hero);
                return true;
            }

            _playerService.RemoveItem(mob.Item);
            var handItem = mob.Item;
            foreach (var tag in useAnim.EventTags)
            {
                if (tag.Type == EventType.ItemExchange && !string.IsNullOrEmpty(tag.Item))
                    handItem = tag.Item;
            }

            if (!string.IsNullOrEmpty(mob.OnStateChangeFunction))
                TryUseMobStateFunction(loader, mob, isItemInHand: true);

            var leaveAnim = mds.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase($"T_{scheme}_S1_2_S0"));
            var isPutBack = leaveAnim?.EventTags.Any(e => e.Type == EventType.ItemRemove) ?? false;
            if (isPutBack)
                _playerService.AddItem(handItem);

            Logger.Log($"[MobItem] '{loader.name}' ({scheme}): '{mob.Item}' taken" +
                       (isPutBack ? $", '{handItem}' back into the inventory" : " and used up"), LogCat.Ai);
            return true;
        }

        /// <summary>
        /// Like the engine (OpenGothic printMobMissingItem): G2 scripts have PLAYER_MOB_MISSING_ITEM (missing item text +
        /// the hero's "$MISSINGITEM" line). G1 has none - nothing to say then.
        /// </summary>
        private void PlayMobMissingItemReaction(Gothic.Core.Models.Container.NpcContainer hero)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByName("PLAYER_MOB_MISSING_ITEM");
            if (symbol == null)
                return;

            hero.Props.AnimationQueue.Clear();
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(symbol.Index);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[MobDialog] PLAYER_MOB_MISSING_ITEM failed: {e.Message}", LogCat.Dialog);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }

            _dialogService.RunHeroQueue();
        }

        /// <summary>
        /// Next to the mob on the player's left (= the mob's right, where NPC dialogs are), at eye height.
        /// </summary>
        private static GameObject CreateMobDialogAnchor(VobLoader loader, bool isOwnBoundsOnly)
        {
            var cam = Camera.main!.transform;

            var bounds = new Bounds(loader.transform.position, Vector3.one * 0.5f);
            var hasBounds = false;
            foreach (var mobRenderer in loader.GetComponentsInChildren<Renderer>())
            {
                // DeveloperConfig.EnableMobDialogOwnBounds: VOBs nested below the mob have their own loader. One of MT's
                // anvils has three more below it - the anchor landed hundreds of meters away ("hero farther than 5 m").
                if (isOwnBoundsOnly && mobRenderer.GetComponentInParent<VobLoader>(true) != loader)
                    continue;
                if (!hasBounds) { bounds = mobRenderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(mobRenderer.bounds);
            }

            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var position = bounds.center - right * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.4f);
            position.y = cam.position.y - 0.2f;

            var anchor = new GameObject("MobDialogAnchor");
            anchor.transform.position = position;
            new GameObject("DialogMenuRootPos").transform.SetParent(anchor.transform, false);
            return anchor;
        }

        public HVRController GetHand(HVRHandSide side)
        {
            if (side == HVRHandSide.Left)
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().LeftHand.Controller;
            else
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().RightHand.Controller;
        }

        public GameObject GetHandModelGo(HVRHandSide side)
        {
            var ctrl = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
            if (ctrl == null) return null;
            var hand = side == HVRHandSide.Left ? ctrl.LeftHand : ctrl.RightHand;
            var modelName = side == HVRHandSide.Left ? "LeftHandModel" : "RightHandModel";
            var model = hand.gameObject.FindChildRecursively(modelName);
            return model != null ? model : hand.gameObject;
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
