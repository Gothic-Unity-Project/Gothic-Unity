using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Marvin;
using Gothic.VR.Domain.Player;
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


        // FIXME - All of these values will be dynamic in the future. Based on skill level and weapon type.
        [Header("Weapon weight")]
        [SerializeField] private float _mass2HOneHanded = 5f;
        [SerializeField] private float _mass1HAnyHand2HTwoHanded = 1f;
        [SerializeField] private float _linearDamping2HOneHanded = 0f;
        [SerializeField] private float _linearDamping1HAnyHand2HTwoHanded = 0f;
        [SerializeField] private float _angularDamping2HOneHanded = 0f;
        [SerializeField] private float _angularDamping1HAnyHand2HTwoHanded = 0f;
        
        
        // Swing detection, measured in the rig's tracking space (VrSwingSampler). Changes apply on the next grab.
        [Header("Swing detection")]
        [Tooltip("One-handed weapon: hand (handle) speed in m/s a swing needs.")]
        [SerializeField] private float _handleSpeed1H = 1.2f;
        [Tooltip("One-handed weapon: blade tip speed in m/s a swing needs.")]
        [SerializeField] private float _tipSpeed1H = 3.0f;
        [Tooltip("Two-handed weapon: hand (handle) speed in m/s a swing needs.")]
        [SerializeField] private float _handleSpeed2H = 1.0f;
        [Tooltip("Two-handed weapon: blade tip speed in m/s a swing needs.")]
        [SerializeField] private float _tipSpeed2H = 2.5f;
        [Tooltip("Hand speed in m/s along the blade that counts as a thrust attack.")]
        [SerializeField] private float _thrustSpeed = 2.5f;
        [Tooltip("Seconds hand and tip have to stay over their speeds (max 0.2).")]
        [SerializeField] private float _minTimeOver = 0.06f;
        [Tooltip("Meters the tip (thrust: the hand) has to travel within the path window.")]
        [SerializeField] private float _minTipPath = 0.35f;
        [Tooltip("Seconds the tip path is measured over (max 0.4).")]
        [SerializeField] private float _pathWindow = 0.2f;
        [Tooltip("Share of the tip speed a combo has to drop below before speeding up again (0.1 = 10 %).")]
        [SerializeField] private float _weaponVelocityDropPercentage = 0.1f;

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

            var itemInstance = vobContainer.GetItemInstance();

            // We currently handle melee weapons only.
            if (!itemInstance.IsMeleeWeapon())
                return;

            // A held weapon is only the hero's readied one (VRWeaponService.UpdateHeroWeaponState) - EquippedItems
            // stays what he or the scripts equipped, that's what a save keeps. Before, the grab equipped it: a save
            // while holding it lost the weapon from before, and a grab + release left none equipped.
            _weaponService.OnGrabbed(args.HandSide, vobContainer, GetWeaponPhysicsConfig());
        }

        private void OnReleased(VrGrabArgs args)
        {
            var vobItem = args.Interactable != null ? args.Interactable.GetComponentInParent<VobLoader>() : null;
            var vobContainer = vobItem?.Container;

            // We grab something not handled as VobItem.
            if (vobContainer == null || vobContainer.Vob.Type != VirtualObjectType.oCItem)
                return;

            var itemInstance = vobContainer.GetItemInstance();

            // Currently we handle melee weapons only.
            if (!itemInstance.IsMeleeWeapon())
                return;

            // The readied weapon follows in VRWeaponService (the other hand's weapon, if it still holds one).
            _weaponService.OnReleased(args.HandSide, GetWeaponPhysicsConfig());
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
                HandleSpeed1H = _handleSpeed1H,
                TipSpeed1H = _tipSpeed1H,
                HandleSpeed2H = _handleSpeed2H,
                TipSpeed2H = _tipSpeed2H,
                ThrustSpeed = _thrustSpeed,
                MinTimeOver = _minTimeOver,
                MinTipPath = _minTipPath,
                PathWindow = _pathWindow,
                WeaponVelocityDropPercentage = _weaponVelocityDropPercentage
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
                
                new MarvinPropertyHeader("Weapon Attack - Swing (applies on next grab)"),
                new MarvinProperty<float>(
                    "Handle speed 1H (m/s)",
                    () => _handleSpeed1H,
                    value => _handleSpeed1H = value,
                    0f, 5f),
                new MarvinProperty<float>(
                    "Tip speed 1H (m/s)",
                    () => _tipSpeed1H,
                    value => _tipSpeed1H = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Handle speed 2H (m/s)",
                    () => _handleSpeed2H,
                    value => _handleSpeed2H = value,
                    0f, 5f),
                new MarvinProperty<float>(
                    "Tip speed 2H (m/s)",
                    () => _tipSpeed2H,
                    value => _tipSpeed2H = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Thrust speed (m/s)",
                    () => _thrustSpeed,
                    value => _thrustSpeed = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Min time over (s)",
                    () => _minTimeOver,
                    value => _minTimeOver = value,
                    0f, VrSwingSampler.MaxTimeOver),
                new MarvinProperty<float>(
                    "Min tip path (m)",
                    () => _minTipPath,
                    value => _minTipPath = value,
                    0f, 1.5f),
                new MarvinProperty<float>(
                    "Path window (s)",
                    () => _pathWindow,
                    value => _pathWindow = value,
                    0.05f, VrSwingSampler.MaxPathWindow),
                new MarvinProperty<float>(
                    "Combo drop percentage",
                    () => _weaponVelocityDropPercentage,
                    value => _weaponVelocityDropPercentage = value,
                    0.05f, 0.5f)
            };
        }
    }
}
