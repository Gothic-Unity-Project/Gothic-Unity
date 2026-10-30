using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Casting in VR: hold a rune or scroll and press its trigger - Gothic's action key. While the trigger is held the
    /// spell takes mana as the scripts say (MagicService), letting go casts it, unless the scripts cast or stop it
    /// earlier. It flies at the NPC nearest to where the hand points, or straight ahead. Each portion of mana ticks the
    /// hand, the cast buzzes, a failure (circle too low, no mana) gives the error buzz. A scroll is used up one by one.
    /// Like Gothic's drawn spell, a held rune or scroll is the hero's active spell and his fight mode is magic.
    /// </summary>
    public class VRSpellCaster : MonoBehaviour
    {
        // NPCs within this angle of where the hand points and this far away can be the spell's target.
        private const float _aimAngleDegrees = 15f;
        private const float _aimRange = 35f;
        // The spell leaves the hand this far in front of the controller.
        private const float _handOffset = 0.15f;

        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrHaptics _haptics;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MagicService _magicService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly PlayerService _playerService;

        private IVrRig _rig;
        private SpellCast _cast;
        private VrHandSide _castSide;
        private GameObject _castItem;
        private int _lastManaInvested;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
        }

        private void Update()
        {
            if (_rig == null)
                return;

            UpdateDrawnSpell();
            if (_cast != null)
            {
                UpdateCast();
                return;
            }

            TryBeginCast(VrHandSide.Left, _vrPlayerService.GrabbedItemLeft);
            if (_cast == null)
                TryBeginCast(VrHandSide.Right, _vrPlayerService.GrabbedItemRight);
        }

        /// <summary>
        /// The scripts ask for the hero's active spell and fight mode (Npc_GetActiveSpell, Npc_IsInFightMode(FMODE_MAGIC)):
        /// a rune or scroll in a hand is drawn, letting go of it puts it away.
        /// </summary>
        private void UpdateDrawnSpell()
        {
            var hero = _npcService.GetHeroContainer();
            // A cast going on keeps its spell active until it's over.
            if (hero?.Props == null || hero.Vob == null || hero.Props.SpellCast != null)
                return;

            var held = GetSpellItem(_vrPlayerService.GrabbedItemRight) ?? GetSpellItem(_vrPlayerService.GrabbedItemLeft);
            if (held == hero.Props.ActiveSpellItem || _cast != null)
                return;

            var weaponState = (VmGothicEnums.WeaponState)hero.Vob.FightMode;
            hero.Props.ActiveSpellItem = held;
            if (held != null && weaponState == VmGothicEnums.WeaponState.NoWeapon)
                hero.Vob.FightMode = (int)VmGothicEnums.WeaponState.Mage;
            else if (held == null && weaponState == VmGothicEnums.WeaponState.Mage)
                hero.Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;
        }

        private static ItemInstance GetSpellItem(GameObject heldItem)
        {
            var item = heldItem?.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            return MagicService.IsSpellItem(item) ? item : null;
        }

        private void TryBeginCast(VrHandSide side, GameObject heldItem)
        {
            if (heldItem == null || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                return;

            var item = heldItem.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (!MagicService.IsSpellItem(item))
                return;

            var hero = _npcService.GetHeroContainer();
            if (hero == null || hero.Props.BodyState is VmGothicEnums.BodyState.BsDead
                    or VmGothicEnums.BodyState.BsUnconscious)
                return;

            var cast = _magicService.CanCast(hero, item)
                ? _magicService.BeginCast(hero, FindTarget(GetHand(side)), item, 0)
                : null;
            if (cast == null)
            {
                _haptics.Vibrate(side, VrHapticPreset.Error);
                return;
            }

            _cast = cast;
            _castSide = side;
            _castItem = heldItem;
            _lastManaInvested = cast.ManaInvested;
            _haptics.Vibrate(side, VrHapticPreset.Info);
        }

        private void UpdateCast()
        {
            // The rune or scroll left the hand: the spell is gone.
            if (GetHeldItem(_castSide) != _castItem)
            {
                _magicService.Stop(_cast);
                EndCast();
                return;
            }

            var hand = GetHand(_castSide);
            if (_cast.Spell.CanChangeTargetDuringInvest != 0)
                _cast.Target = FindTarget(hand);

            var phase = _magicService.TickCast(_cast);
            if (phase == SpellCastPhase.Investing && !_vrInput.GetButton(_castSide, VrButton.Trigger).IsActive)
                phase = _magicService.ReleaseCast(_cast);

            if (_cast.ManaInvested != _lastManaInvested)
            {
                _lastManaInvested = _cast.ManaInvested;
                _haptics.Vibrate(_castSide, VrHapticPreset.Info);
            }

            switch (phase)
            {
                case SpellCastPhase.Casting:
                    Commit(hand);
                    break;
                case SpellCastPhase.Stopped or SpellCastPhase.Done:
                    EndCast();
                    break;
            }
        }

        private void Commit(Transform hand)
        {
            var origin = hand.position + hand.forward * _handOffset;
            var direction = _cast.Target?.Go != null ? FightService.GetAimPoint(_cast.Target) - origin : hand.forward;
            var isScroll = MagicService.IsScroll(_cast.Item);

            _magicService.CommitCast(_cast, origin, direction);
            _haptics.Vibrate(_castSide, VrHapticPreset.Success);

            if (isScroll)
                UseUpScroll();

            EndCast();
        }

        /// <summary>
        /// One scroll of the held stack is used. Taken from the backpack, the stack is part of the inventory. The last
        /// one vanishes from the hand.
        /// </summary>
        private void UseUpScroll()
        {
            var loader = _castItem.GetComponentInParent<VobLoader>();
            var stack = loader?.Container?.VobAs<IItem>();
            if (stack == null || stack.Amount <= 1)
            {
                Destroy(loader != null ? loader.gameObject : _castItem);
                return;
            }

            var instanceName = stack.Instance;
            var inInventory = _playerService.GetInventory(VmGothicEnums.InvCats.InvRune)
                .Find(i => i.Name.EqualsIgnoreCase(instanceName))?.Amount ?? 0;
            if (inInventory >= stack.Amount)
                _playerService.RemoveItem(instanceName, 1);
            stack.Amount--;
        }

        private void EndCast()
        {
            _cast = null;
            _castItem = null;
        }

        /// <summary>
        /// The NPC closest to the line the hand points along, within the aim angle and range.
        /// </summary>
        private NpcContainer FindTarget(Transform hand)
        {
            NpcContainer best = null;
            var bestAngle = _aimAngleDegrees;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc?.Go == null || !npc.Go.activeInHierarchy || npc.IsHero() ||
                    npc.Props?.BodyState == VmGothicEnums.BodyState.BsDead)
                    continue;

                var toNpc = FightService.GetAimPoint(npc) - hand.position;
                if (toNpc.sqrMagnitude > _aimRange * _aimRange)
                    continue;

                var angle = Vector3.Angle(hand.forward, toNpc);
                if (angle >= bestAngle)
                    continue;

                best = npc;
                bestAngle = angle;
            }

            return best;
        }

        private Transform GetHand(VrHandSide side)
        {
            return side == VrHandSide.Left ? _rig.LeftHand : _rig.RightHand;
        }

        private GameObject GetHeldItem(VrHandSide side)
        {
            return side == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
        }
    }
}
