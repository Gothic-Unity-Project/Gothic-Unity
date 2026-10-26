#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableBackpackEquipToggle: an empty hand at an armor, amulet, ring or belt in the hero's backpack
    /// and a trigger click (released quickly, without moving) puts it on or takes it off - like a click on a spell in
    /// the spell ring. The backpack shows [E] for the equipped ones.
    /// </summary>
    public class VRBackpackEquipToggle : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly ConfigService _configService;

        private const float _itemDistance = 0.1f;
        private const float _clickSeconds = 0.5f;
        private const float _clickMoveDistance = 0.03f;

        private readonly ClickState[] _clicks = { new(), new() };

        private class ClickState
        {
            public bool IsDown;
            public float DownTime;
            public Vector3 DownPosition;
        }

        private void Awake()
        {
            this.Inject();
        }

        private void Update()
        {
            if (!_configService.Dev.EnableBackpackEquipToggle)
                return;

            UpdateHand(VrHandSide.Left, _clicks[0]);
            UpdateHand(VrHandSide.Right, _clicks[1]);
        }

        private void UpdateHand(VrHandSide side, ClickState click)
        {
            var hand = _vrPlayerService.GetHandModelGo(side);
            if (hand == null)
                return;

            var trigger = VrControls.GetButton(side, VrButton.Trigger);
            var isGripHeld = VrControls.GetButton(side, VrButton.Grip).IsActive;
            var isEmpty = (side == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight)
                          == null;

            if (trigger.IsJustActivated)
            {
                click.IsDown = isEmpty && !isGripHeld;
                click.DownTime = Time.time;
                click.DownPosition = hand.transform.position;
                return;
            }

            if (!click.IsDown || !trigger.IsJustDeactivated)
                return;

            click.IsDown = false;
            if (!isEmpty || isGripHeld || Time.time - click.DownTime > _clickSeconds ||
                Vector3.Distance(hand.transform.position, click.DownPosition) > _clickMoveDistance)
                return;

            TryToggle(side, hand.transform.position);
        }

        private void TryToggle(VrHandSide side, Vector3 handPosition)
        {
            foreach (var backpack in FindObjectsByType<VRBackpack>(FindObjectsSortMode.None))
            {
                if (backpack.IsNpcBackpack || backpack.IsTradeBackpack)
                    continue;

                var grabbable = backpack.FindStackNear(handPosition, _itemDistance);
                var item = grabbable != null ? grabbable.GetComponentInParent<VobLoader>()?.Container?.VobAs<IItem>() : null;
                if (item == null)
                    continue;

                var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                var itemData = _vmCacheService.TryGetItemData(instanceName);
                if (itemData == null || !NpcInventoryService.IsWearable(itemData))
                    continue;

                var hero = _playerService.HeroContainer.Instance;
                var isEquipped = _npcInventoryService.IsEquipped(hero, instanceName);
                if (isEquipped)
                    _npcInventoryService.UnequipItemWithEffects(hero, itemData);
                else
                    _npcInventoryService.EquipItemWithEffects(hero, itemData);

                _hapticsService.Vibrate(side, VrHapticPreset.Success);
                backpack.RefreshInventoryView();
                Logger.Log($"[BackpackEquip] {(isEquipped ? "took off" : "put on")} '{itemData.Name}'", LogCat.VR);
                return;
            }
        }
    }
}
#endif
