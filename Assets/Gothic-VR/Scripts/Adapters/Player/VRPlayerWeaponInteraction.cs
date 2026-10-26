using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Marvin;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models.Vob;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    public class VRPlayerWeaponInteraction : MonoBehaviour, IMarvinPropertyCollector
    {
        [Inject] private readonly VRWeaponService _weaponService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;

        // DeveloperConfig.EnableRingWeaponSlots: the melee weapon equipped before one was grabbed.
        private ZenKit.Daedalus.ItemInstance _equippedBeforeGrab;
        private bool _isEquippedBeforeGrabKept;


        // FIXME - All of these values will be dynamic in the future. Based on skill level and weapon type.
        [Header("Weapon weight")]
        [SerializeField] private float _mass2HOneHanded = 5f;
        [SerializeField] private float _mass1HAnyHand2HTwoHanded = 1f;
        [SerializeField] private float _linearDamping2HOneHanded = 0f;
        [SerializeField] private float _linearDamping1HAnyHand2HTwoHanded = 0f;
        [SerializeField] private float _angularDamping2HOneHanded = 0f;
        [SerializeField] private float _angularDamping1HAnyHand2HTwoHanded = 0f;
        
        
        // if something in hand. Activate VRWeaponFighting
        [Header("Weapon Velocity Settings")]
        [SerializeField] private float _weaponVelocityThreshold = 2.0f;
        [SerializeField] private float _weaponVelocityDropPercentage = 0.1f; // 10% drop
        
        [Header("Weapon Attack Velocity Settings")]
        [SerializeField] private float _velocityCheckDuration = 0.5f;
        [SerializeField] private int _velocitySampleCount = 5;

        private IVrRig _rig;


        private void Awake()
        {
            _rig = GetComponent<IVrRig>();
            foreach (var grabber in _rig.HandGrabbers)
            {
                // Melee weapons are handled once they're in a hand, not while being pulled by distance.
                if (grabber.Kind != VrGrabberKind.Hand)
                    continue;

                grabber.Grabbed += OnGrabbed;
                grabber.Released += OnReleased;
            }
        }

        private void OnDestroy()
        {
            if (_rig == null)
                return;

            foreach (var grabber in _rig.HandGrabbers)
            {
                grabber.Grabbed -= OnGrabbed;
                grabber.Released -= OnReleased;
            }
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            var vobItem = args.Interactable != null ? args.Interactable.GetComponentInParent<VobLoader>() : null;
            var vobContainer = vobItem?.Container;

            // We grab something not handled as VobItem.
            if (vobContainer == null || vobContainer.Vob.Type != VirtualObjectType.oCItem)
                return;

            vobContainer.IsHeldByPlayer = true;

            var itemInstance = vobContainer.GetItemInstance();

            // We currently handle melee weapons only.
            if (itemInstance!.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatNf)
                return;

            _weaponService.OnGrabbed(args.HandSide, vobContainer, GetWeaponPhysicsConfig());
            SyncEquippedWeaponToHero(itemInstance, equip: true);
        }

        private void OnReleased(VrGrabArgs args)
        {
            var vobItem = args.Interactable != null ? args.Interactable.GetComponentInParent<VobLoader>() : null;
            var vobContainer = vobItem?.Container;

            // We grab something not handled as VobItem.
            if (vobContainer == null || vobContainer.Vob.Type != VirtualObjectType.oCItem)
                return;

            // The releasing hand might still hold it at this point - only count the other hand.
            vobContainer.IsHeldByPlayer = IsHeldByOtherHand(args);

            var itemInstance = vobContainer.GetItemInstance();

            // Currently we handle melee weapons only.
            if (itemInstance!.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatNf)
                return;

            _weaponService.OnReleased(args.HandSide, GetWeaponPhysicsConfig());
            SyncEquippedWeaponToHero(itemInstance, equip: false);
        }

        private bool IsHeldByOtherHand(VrGrabArgs args)
        {
            foreach (var grabber in _rig.HandGrabbers)
            {
                if (grabber.HandSide != args.HandSide && grabber.Held == args.Interactable)
                    return true;
            }
            return false;
        }

        private void SyncEquippedWeaponToHero(ZenKit.Daedalus.ItemInstance itemInstance, bool equip)
        {
            var hero = _gameStateService.GothicVm?.GlobalHero as ZenKit.Daedalus.NpcInstance;
            if (hero == null)
                return;

            var heroContainer = hero.GetUserData();
            if (heroContainer == null)
                return;

            var equippedItems = heroContainer.Props.EquippedItems;

            // DeveloperConfig.EnableRingWeaponSlots: the held weapon counts as equipped only while held - letting go
            // gives the equipped one back (the favorites ring shows it). Before, any weapon grabbed and let go left
            // the hero with no equipped melee weapon at all (Rockefeller's orc hammer was gone after the first grab).
            if (_configService.Dev.EnableRingWeaponSlots)
            {
                if (equip)
                {
                    if (!_isEquippedBeforeGrabKept)
                    {
                        _equippedBeforeGrab = equippedItems.Find(i => i.MainFlag == itemInstance.MainFlag);
                        _isEquippedBeforeGrabKept = true;
                    }
                }
                else
                {
                    // The ring's own weapon too: where it ends up decides (VRFavoritesRing, EnableRingWeaponLeavesOnDrop).
                    equippedItems.RemoveAll(i => i.MainFlag == itemInstance.MainFlag);
                    if (_equippedBeforeGrab != null)
                        equippedItems.Add(_equippedBeforeGrab);
                    _equippedBeforeGrab = null;
                    _isEquippedBeforeGrabKept = false;
                    return;
                }
            }

            equippedItems.RemoveAll(i => i.MainFlag == itemInstance.MainFlag);
            if (equip)
                equippedItems.Add(itemInstance);
        }

        /// <summary>
        /// As we can change the values in Editor mode, we need to create them every time we need it.
        /// TODO - Can be optimized by caching this struct when not in Editor mode for marginal performance impact.
        /// </summary>
        private WeaponPhysicsConfig GetWeaponPhysicsConfig()
        {
            return new WeaponPhysicsConfig
            {
                Mass2HOneHanded = _mass2HOneHanded,
                Mass1HAnyHand2HTwoHanded = _mass1HAnyHand2HTwoHanded,
                LinearDamping2HOneHanded = _linearDamping2HOneHanded,
                LinearDamping1HAnyHand2HTwoHanded = _linearDamping1HAnyHand2HTwoHanded,
                AngularDamping2HOneHanded = _angularDamping2HOneHanded,
                AngularDamping1HAnyHand2HTwoHanded = _angularDamping1HAnyHand2HTwoHanded,
                WeaponVelocityThreshold = _weaponVelocityThreshold,
                WeaponVelocityDropPercentage = _weaponVelocityDropPercentage,
                VelocityCheckDuration = _velocityCheckDuration,
                VelocitySampleCount = _velocitySampleCount
            };
        }

        private void OnDrawGizmos()
        {
            // DEBUG - Drawing colliders of weapons in hand to see if they are overlapping with each other.
            // if (_rightHandPlayerWeaponFightHandler == null)
            //     return;
            //
            // foreach (var weaponCollider in _rightHandWeapon.Go.GetComponentsInChildren<Collider>())
            // {
            //     switch (weaponCollider)
            //     {
            //         case BoxCollider boxCollider:
            //             _rightHandPlayerWeaponFightHandler.CalculateBoxColliderOverlap(boxCollider, out var point, out var size, out var rotation);
            //
            //             Gizmos.color = Color.blue;
            //             Gizmos.DrawCube(point, rotation * size);
            //
            //             break;
            //         case CapsuleCollider capsuleCollider:
            //             _rightHandPlayerWeaponFightHandler.CalculateCapsuleOverlap(capsuleCollider, out var point0, out var point1, out var radius);
            //             Gizmos.color = Color.red;
            //             Gizmos.DrawWireSphere(point0, radius);
            //             Gizmos.DrawWireSphere(point1, radius);
            //
            //             break;
            //     }
            // }
        }
        
        public IEnumerable<object> CollectMarvinInspectorProperties()
        {
            return new List<object>
            {
                new MarvinPropertyHeader("VRWeapon - RigidBody settings"),
                new MarvinProperty<float>(
                    "Mass one handed",
                    () => _mass2HOneHanded,
                    value => _mass2HOneHanded = value,
                    0f, 15f),
                new MarvinProperty<float>(
                    "Mass two handed",
                    () => _mass1HAnyHand2HTwoHanded,
                    value => _mass1HAnyHand2HTwoHanded = value,
                    0f, 5f),
                new MarvinProperty<float>(
                    "Move damping one handed",
                    () => _linearDamping2HOneHanded,
                    value => _linearDamping2HOneHanded = value,
                    0f, 25f),
                new MarvinProperty<float>(
                    "Move damping two handed",
                    () => _linearDamping1HAnyHand2HTwoHanded,
                    value => _linearDamping1HAnyHand2HTwoHanded = value,
                    0f, 25f),
                new MarvinProperty<float>(
                    "Rotation damping one handed",
                    () => _angularDamping2HOneHanded,
                    value => _angularDamping2HOneHanded = value,
                    0f, 25f),
                new MarvinProperty<float>(
                    "Rotation damping two handed",
                    () => _angularDamping1HAnyHand2HTwoHanded,
                    value => _angularDamping1HAnyHand2HTwoHanded = value,
                    0f, 25f),
                
                new MarvinPropertyHeader("Weapon Attack - Velocity"),
                new MarvinProperty<float>(
                    "Threshold",
                    () => _weaponVelocityThreshold,
                    value => _weaponVelocityThreshold = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Drop Percentage",
                    () => _weaponVelocityDropPercentage,
                    value => _weaponVelocityDropPercentage = value,
                    0.05f, 0.5f),

                new MarvinPropertyHeader("Weapon Attack - Velocity Check Settings"),
                new MarvinProperty<float>(
                    "Duration",
                    () => _velocityCheckDuration,
                    value => _velocityCheckDuration = value,
                    0.1f, 1f),
                new MarvinProperty<int>(
                    "Sample Count",
                    () => _velocitySampleCount,
                    value => _velocitySampleCount = value,
                    1, 20)
            };
        }
    }
}
