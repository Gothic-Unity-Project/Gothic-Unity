using System.Collections.Generic;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Shooting in VR: hold a bow or crossbow and use its trigger. A bow draws while the trigger is held - full power
    /// after a moment, the hand buzzes - and shoots when it's let go. A crossbow shoots on the press and needs a moment
    /// to reload. The arrow or bolt comes from the hero's inventory (FightService.ShootByHand) and flies where the hand
    /// points. Without munition, or while reloading, the error buzz.
    /// </summary>
    public class VRBowShooter : MonoBehaviour
    {
        [Tooltip("Seconds a bow needs to be fully drawn.")]
        [SerializeField] private float _fullDrawSeconds = 0.6f;
        [Tooltip("Seconds a crossbow needs to reload after a shot.")]
        [SerializeField] private float _crossbowReloadSeconds = 1.5f;
        [Tooltip("The arrow starts this far in front of the hand, clear of the bow.")]
        [SerializeField] private float _muzzleOffset = 0.3f;

        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrHaptics _haptics;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly FightService _fightService;

        private IVrRig _rig;
        private readonly Dictionary<VrHandSide, float> _drawStartTimes = new();
        private readonly HashSet<VrHandSide> _fullyDrawn = new();
        private float _crossbowReadyTime;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
        }

        private void Update()
        {
            if (_rig == null)
                return;

            HandleHand(VrHandSide.Left, _vrPlayerService.GrabbedItemLeft);
            HandleHand(VrHandSide.Right, _vrPlayerService.GrabbedItemRight);
        }

        private void HandleHand(VrHandSide side, GameObject heldItem)
        {
            var weapon = GetRangedWeapon(heldItem);
            if (weapon == null)
            {
                _drawStartTimes.Remove(side);
                _fullyDrawn.Remove(side);
                return;
            }

            var trigger = _vrInput.GetButton(side, VrButton.Trigger);
            if (((VmGothicEnums.ItemFlags)weapon.Flags).HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow))
            {
                if (!trigger.IsJustActivated)
                    return;

                if (Time.time < _crossbowReadyTime)
                    _haptics.Vibrate(side, VrHapticPreset.Warning);
                else if (Shoot(side, weapon, 1f))
                    _crossbowReadyTime = Time.time + _crossbowReloadSeconds;
                return;
            }

            if (trigger.IsJustActivated)
            {
                _drawStartTimes[side] = Time.time;
                _fullyDrawn.Remove(side);
                _haptics.Vibrate(side, VrHapticPreset.Info);
            }
            else if (trigger.IsActive && _drawStartTimes.TryGetValue(side, out var drawStart) &&
                     !_fullyDrawn.Contains(side) && Time.time - drawStart >= _fullDrawSeconds)
            {
                _fullyDrawn.Add(side);
                _haptics.Vibrate(side, VrHapticPreset.Info);
            }
            else if (trigger.IsJustDeactivated && _drawStartTimes.TryGetValue(side, out drawStart))
            {
                _drawStartTimes.Remove(side);
                _fullyDrawn.Remove(side);
                Shoot(side, weapon, Mathf.Clamp01((Time.time - drawStart) / _fullDrawSeconds));
            }
        }

        private bool Shoot(VrHandSide side, ItemInstance weapon, float draw)
        {
            var hand = side == VrHandSide.Left ? _rig.LeftHand : _rig.RightHand;
            var hero = _npcService.GetHeroContainer();
            if (hero == null || !_fightService.ShootByHand(hero, weapon, hand.position + hand.forward * _muzzleOffset,
                    hand.forward, draw))
            {
                _haptics.Vibrate(side, VrHapticPreset.Error);
                return false;
            }

            _haptics.Vibrate(side, VrHapticPreset.Success);
            return true;
        }

        private static ItemInstance GetRangedWeapon(GameObject heldItem)
        {
            if (heldItem == null)
                return null;

            var item = heldItem.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null || item.Type != DaedalusInstanceType.Item ||
                (item.MainFlag & (int)VmGothicEnums.ItemFlags.ItemKatFf) == 0)
                return null;

            return item;
        }
    }
}
