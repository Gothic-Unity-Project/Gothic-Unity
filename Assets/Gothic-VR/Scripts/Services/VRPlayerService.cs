#if GOTHIC_HVR_INSTALLED
using System;
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
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using HurricaneVR.Framework.Core;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        
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
            var vobItem = grabbable.GetComponentInParent<VobLoader>().Container.VobAs<IItem>();
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
            var vobItem = grabbable.GetComponentInParent<VobLoader>().Container.VobAs<IItem>();
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            _playerService.RemoveItem(instanceName, vobItem.Amount);
        }

        public HVRController GetHand(HVRHandSide side)
        {
            if (side == HVRHandSide.Left)
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().LeftHand.Controller;
            else
                return _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>().RightHand.Controller;
        }

        /// <summary>
        /// The hero grabbed a mob (wheel, lever, ...): like AI_UseMob, its ConditionFunction decides whether it may be
        /// used, then its Target (e.g. a gate mover, a code master or a trigger list) is triggered.
        /// </summary>
        public void HandleMobGrab(VobLoader loader)
        {
            var vm = _gameStateService.GothicVm;
            if (loader.Container.Vob is not IInteractiveObject mob || vm == null)
                return;

            var condFunc = mob.ConditionFunction ?? "";
            Logger.Log($"[VRPlayerService] HandleMobGrab mob={loader.gameObject.name} condFunc={condFunc}", LogCat.Vob);

            // ConditionFunction is a Daedalus check: returns 0 = interaction denied.
            if (condFunc.Length > 0)
            {
                var symbol = vm.GetSymbolByName(condFunc);
                if (symbol == null)
                {
                    Logger.LogWarning($"[VRPlayerService] ConditionFunction '{condFunc}' not found in VM", LogCat.Vob);
                    return;
                }
                if (CallConditionAsHero(symbol.Index, condFunc) == 0)
                {
                    Logger.Log($"[VRPlayerService] HandleMobGrab: '{condFunc}' denied (returned 0)", LogCat.Vob);
                    return;
                }
            }

            // Mobs without a mover target but with an onStateFunc (G2 shrines, alchemy/rune tables, bookstands, ...).
            if (_configService.Dev.EnableMobsiDialogs && string.IsNullOrEmpty(mob.Target) &&
                TryUseMobStateFunction(loader, mob))
                return;

            var target = mob.Target;
            if (string.IsNullOrEmpty(target))
            {
                Logger.Log($"[VRPlayerService] HandleMobGrab: no Target on {loader.gameObject.name}", LogCat.Vob);
                return;
            }

            // A mover target toggles (the player physically interacts - can open and close), other targets
            // (CodeMaster, TriggerList, TriggerScript, ...) run their chain. The mob's VOB name is the sender, so a
            // CodeMaster can match it against its Slaves list.
            _vobService.DispatchTrigger(target, loader.Container.Vob.Name);
        }

        /// <summary>
        /// Like the engine reaching mob state 1: call "[onStateFunc]_S1" with self = hero. MOBSI scripts like
        /// PrayShrine_S1 then open a dialog via AI_ProcessInfos(hero) whose choices are C_INFOs with npc = PC_Hero.
        /// The dialog box is anchored next to the mob (like at an NPC).
        /// </summary>
        private bool TryUseMobStateFunction(VobLoader loader, IInteractiveObject mob)
        {
            if (string.IsNullOrEmpty(mob.OnStateChangeFunction))
                return false;

            // X marks are dug up by hitting them (VRDigSpot) - grabbing must not dig.
            if (_vmCacheService.IsDigSpot(loader.Container.Vob))
                return false;

            var vm = _gameStateService.GothicVm;
            var functionName = mob.OnStateChangeFunction + "_S1";
            var symbol = vm.GetSymbolByName(functionName);
            if (symbol == null)
            {
                Logger.Log($"[MobDialog] '{functionName}' not in the scripts - mob '{loader.name}' ignored.",
                    LogCat.Dialog);
                return false;
            }

            // Like the engine: a mob used with an item needs it in the inventory (e.g. a lab flask).
            var hero = _playerService.HeroContainer;
            if (!string.IsNullOrEmpty(mob.Item))
            {
                var item = _vmCacheService.TryGetItemData(mob.Item);
                if (item != null && _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
                {
                    Logger.Log($"[MobDialog] '{loader.name}' needs '{mob.Item}'.", LogCat.Dialog);
                    PlayMobMissingItemReaction(hero);
                    return true;
                }
            }

            _dialogService.PrepareMobDialog(CreateMobDialogAnchor(loader));
            CallAsHero(symbol.Index, functionName);
            Logger.Log($"[MobDialog] Called '{functionName}' for '{loader.name}'.", LogCat.Dialog);

            // Not every _S1 opens a dialog (e.g. a script only prints a message) - then the anchor isn't needed.
            _dialogService.ReleaseUnusedMobDialogAnchor();

            return true;
        }

        /// <summary>
        /// Like the engine (OpenGothic printMobMissingItem): G2 scripts have PLAYER_MOB_MISSING_ITEM (missing item
        /// text + the hero's "$MISSINGITEM" line). G1 has none - nothing to say then.
        /// </summary>
        private void PlayMobMissingItemReaction(NpcContainer hero)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName("PLAYER_MOB_MISSING_ITEM");
            if (symbol == null)
                return;

            hero.Props.AnimationQueue.Clear();
            CallAsHero(symbol.Index, symbol.Name);
            _dialogService.RunHeroQueue();
        }

        /// <summary>
        /// Like the engine (OpenGothic invokeCond): a mob's ConditionFunction runs with self = hero.
        /// A script error denies the interaction.
        /// </summary>
        private int CallConditionAsHero(int symbolIndex, string functionName)
        {
            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                return vm.Call<int>(symbolIndex);
            }
            catch (Exception e)
            {
                Logger.LogError($"[VRPlayerService] ConditionFunction '{functionName}' failed: {e.Message}",
                    LogCat.Vob);
                return 0;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        private void CallAsHero(int symbolIndex, string functionName)
        {
            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(symbolIndex);
            }
            catch (Exception e)
            {
                Logger.LogError($"[MobDialog] '{functionName}' failed: {e.Message}", LogCat.Dialog);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        /// <summary>
        /// Next to the mob on the player's left (= the mob's right, where NPC dialogs are), at eye height.
        /// </summary>
        private static GameObject CreateMobDialogAnchor(VobLoader loader)
        {
            var cam = Camera.main != null ? Camera.main.transform : loader.transform;

            var bounds = new Bounds(loader.transform.position, Vector3.one * 0.5f);
            var hasBounds = false;
            foreach (var mobRenderer in loader.GetComponentsInChildren<Renderer>())
            {
                if (!hasBounds)
                {
                    bounds = mobRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(mobRenderer.bounds);
                }
            }

            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var position = bounds.center - right * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.4f);
            position.y = cam.position.y - 0.2f;

            var anchor = new GameObject("MobDialogAnchor");
            anchor.transform.position = position;
            new GameObject("DialogMenuRootPos").transform.SetParent(anchor.transform, false);
            return anchor;
        }
    }
}
#endif
