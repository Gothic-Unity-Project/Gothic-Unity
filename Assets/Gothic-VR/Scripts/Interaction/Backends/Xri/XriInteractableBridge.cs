using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Sits next to an XRI interactable (GothicGrabInteractable for items, XRSimpleInteractable for mobs and NPCs)
    /// and its VrInteractable. Forwards XRI's hover/select events synchronously to the VrInteractable and executes
    /// its commands on the interactable.
    ///
    /// Prefabs carry the components of both backends. The XRI interactable is disabled in the prefab and only this
    /// bridge enables it, if XRI is the active backend. Otherwise, it stays disabled and never looks for (or
    /// creates) an XRInteractionManager.
    /// </summary>
    public class XriInteractableBridge : MonoBehaviour, IVrInteractableBackend
    {
        [Tooltip("Trigger colliders that count for hovering and grabbing by hand anyway (XRI ignores triggers). " +
                 "E.g. the backpack's grab volume, which HVR used, as its hull alone is hard to grab.")]
        [SerializeField]
        private Collider[] _grabTriggers;

        [Inject] private readonly IVrBackend _vrBackend;

        private readonly Dictionary<IXRInteractor, VrGrabArgs> _selectArgs = new();
        private readonly HashSet<IXRInteractor> _handHovers = new();
        private readonly List<Collider> _colliderBuffer = new();

        private XRBaseInteractable _xrInteractable;
        private VrInteractable _interactable;
        private bool _isSubscribed;
        private float _lastHandReleaseTime = float.NegativeInfinity;
        private bool _isGrabEnabled = true;
        private bool _isStarted;

        public bool IsActive => _vrBackend is XriBackend;
        public bool IsHeld => _xrInteractable != null && _xrInteractable.isSelected;

        /// <summary>
        /// Mobs and NPCs carry an XRSimpleInteractable (select = use/talk), items a GothicGrabInteractable.
        /// </summary>
        public bool IsSelectOnly => _xrInteractable != null && _xrInteractable is not XRGrabInteractable;

        /// <summary>
        /// Selected by a player hand (XriGrabberBridge).
        /// </summary>
        public bool IsHeldByHand
        {
            get
            {
                if (_xrInteractable == null)
                    return false;

                foreach (var interactor in _xrInteractable.interactorsSelecting)
                {
                    if (interactor is Component component && component.TryGetComponent(out XriGrabberBridge _))
                        return true;
                }

                return false;
            }
        }

        public IReadOnlyList<Collider> Colliders => _xrInteractable != null
            ? _xrInteractable.colliders
            : System.Array.Empty<Collider>();

        /// <summary>
        /// The XR interactable is enabled in Start (runtime colliders), only if grabbing is enabled by then.
        /// </summary>
        public bool IsGrabEnabled
        {
            get => _isGrabEnabled;
            set
            {
                _isGrabEnabled = value;
                if (_isSubscribed && _isStarted)
                    _xrInteractable.enabled = value;
            }
        }

        public bool IsOneHandOnly
        {
            get => _xrInteractable is GothicGrabInteractable grab && grab.IsOneHandOnly;
            set
            {
                if (_xrInteractable is GothicGrabInteractable grab)
                    grab.IsOneHandOnly = value;
            }
        }

        public bool IsDistanceGrabbable
        {
            get => _xrInteractable is not GothicGrabInteractable grab || grab.IsDistanceGrabbable;
            set
            {
                if (_xrInteractable is GothicGrabInteractable grab)
                    grab.IsDistanceGrabbable = value;
            }
        }

        private void Awake()
        {
            if (!IsActive)
                return;

            _interactable = GetComponent<VrInteractable>();
            _xrInteractable = GetComponent<XRBaseInteractable>();
            if (_interactable == null || _xrInteractable == null)
            {
                Logger.LogError($"[XRI] {name} needs a VrInteractable and an XR interactable next to " +
                                $"{nameof(XriInteractableBridge)}.", LogCat.VR);
                return;
            }

            _xrInteractable.hoverEntered.AddListener(OnHoverEntered);
            _xrInteractable.hoverExited.AddListener(OnHoverExited);
            _xrInteractable.selectEntered.AddListener(OnSelectEntered);
            _xrInteractable.selectExited.AddListener(OnSelectExited);
            _isSubscribed = true;
        }

        private void Start()
        {
            if (!_isSubscribed)
                return;

            // Gothic meshes and their colliders are created at runtime, after XRI collected the colliders in Awake.
            CollectColliders();

            // The climb range (ClimbInteractable.maxInteractionDistance) is measured to the ladder's body.
            if (_xrInteractable is ClimbInteractable)
                _xrInteractable.getDistanceOverride = GetDistanceToBounds;

            _isStarted = true;
            _xrInteractable.enabled = _isGrabEnabled;
        }

        private void OnDestroy()
        {
            if (!_isSubscribed || _xrInteractable == null)
                return;

            _xrInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            _xrInteractable.hoverExited.RemoveListener(OnHoverExited);
            _xrInteractable.selectEntered.RemoveListener(OnSelectEntered);
            _xrInteractable.selectExited.RemoveListener(OnSelectExited);
        }

        public void ForceRelease()
        {
            if (IsHeld && _xrInteractable.interactionManager != null)
                _xrInteractable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_xrInteractable);
        }

        /// <summary>
        /// Re-collects the colliders. The interaction manager maps colliders to interactables on registration only,
        /// therefore an enabled interactable is registered again (not while it's held or hovered).
        /// </summary>
        public void SetupColliders(bool isChildLinked = false)
        {
            if (!_isSubscribed)
                return;

            CollectColliders();

            if (!_xrInteractable.isActiveAndEnabled || _xrInteractable.isSelected || _xrInteractable.isHovered)
                return;

            _xrInteractable.enabled = false;
            _xrInteractable.enabled = true;
        }

        public void SetSocketSounds(AudioClip socketed, AudioClip unsocketed)
        {
            // Nothing to do: VrInteractable keeps the clips and VrSocketSlot plays them (backend-neutral).
        }

        /// <summary>
        /// A player hand released this interactable within the last seconds (XriSocketBridge takes such items).
        /// </summary>
        public bool IsJustReleasedByHand(float seconds)
        {
            return Time.time - _lastHandReleaseTime <= seconds;
        }

        /// <summary>
        /// Called by GothicGrabInteractable before XRI moves the interactable (BeforeGrabbed of HVR).
        /// </summary>
        internal void OnSelectEntering(SelectEnterEventArgs args)
        {
            if (!_isSubscribed)
                return;

            var grabArgs = CreateSelectArgs(args.interactorObject);
            _selectArgs[args.interactorObject] = grabArgs;

            if (args.interactorObject is Component component && component.TryGetComponent(out XriGrabberBridge grabber))
                grabber.RaiseBeforeGrabbed(grabArgs);

            _interactable.RaiseBeforeGrabbed(grabArgs);
        }

        private void CollectColliders()
        {
            GetComponentsInChildren(_colliderBuffer);
            _xrInteractable.colliders.Clear();
            foreach (var itemCollider in _colliderBuffer)
            {
                // Same as XRI: triggers are usually snap volumes and don't count.
                if (!itemCollider.isTrigger)
                    _xrInteractable.colliders.Add(itemCollider);
            }

            _colliderBuffer.Clear();

            // Only the near casters of the hands see them (XriGrabberBridge), the far ray ignores triggers.
            if (_grabTriggers != null)
            {
                foreach (var grabTrigger in _grabTriggers)
                {
                    if (grabTrigger != null && !_xrInteractable.colliders.Contains(grabTrigger))
                        _xrInteractable.colliders.Add(grabTrigger);
                }
            }

            if (_xrInteractable.colliders.Count == 0)
                Logger.LogWarning($"[XRI] {name} has no colliders. It can't be grabbed or hovered.", LogCat.VR);
        }

        /// <summary>
        /// Distance to the colliders' bounds. XRI's own modes measure to the collider's transform (the ladder's base)
        /// or need Collider.ClosestPoint, which Gothic's concave mesh colliders don't support.
        /// </summary>
        private DistanceInfo GetDistanceToBounds(IXRInteractable interactable, Vector3 position)
        {
            var result = new DistanceInfo { distanceSqr = float.MaxValue, point = position };
            foreach (var itemCollider in interactable.colliders)
            {
                if (itemCollider == null || !itemCollider.enabled)
                    continue;

                var distanceSqr = itemCollider.bounds.SqrDistance(position);
                if (distanceSqr >= result.distanceSqr)
                    continue;

                result.distanceSqr = distanceSqr;
                result.point = itemCollider.bounds.ClosestPoint(position);
                result.collider = itemCollider;
            }

            return result;
        }

        private VrGrabArgs CreateSelectArgs(IXRInteractor interactor)
        {
            if (interactor is Component component && component.TryGetComponent(out XriGrabberBridge grabber))
                return grabber.CreateSelectArgs(_interactable, _xrInteractable);

            return CreateOtherArgs(interactor);
        }

        private VrGrabArgs CreateOtherArgs(IXRInteractor interactor)
        {
            var kind = interactor is XRSocketInteractor ? VrGrabberKind.Socket : VrGrabberKind.Other;
            var grabberGo = interactor is Component component ? component.gameObject : null;
            return new VrGrabArgs(kind, VrHandSide.Left, _interactable, grabberGo);
        }

        /// <summary>
        /// Only hands hover (focus label, highlight). With both hands on one object, HoverEntered is raised for the
        /// first and HoverExited for the last hand, so that the label doesn't vanish while one hand still hovers.
        /// Near-Far hovers only its closest target, so overlapping objects don't flicker.
        /// </summary>
        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (args.interactorObject is not Component component ||
                !component.TryGetComponent(out XriGrabberBridge grabber))
                return;

            if (_handHovers.Add(args.interactorObject) && _handHovers.Count == 1)
                _interactable.RaiseHoverEntered(grabber.CreateHoverArgs(_interactable, _xrInteractable));
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (args.interactorObject is not Component component ||
                !component.TryGetComponent(out XriGrabberBridge grabber))
                return;

            if (_handHovers.Remove(args.interactorObject) && _handHovers.Count == 0)
                _interactable.RaiseHoverExited(grabber.CreateHoverArgs(_interactable, _xrInteractable));
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!_selectArgs.TryGetValue(args.interactorObject, out var grabArgs))
            {
                // Interactables without GothicGrabInteractable (e.g. XRSimpleInteractable) have no OnSelectEntering.
                grabArgs = CreateSelectArgs(args.interactorObject);
                _selectArgs[args.interactorObject] = grabArgs;
            }

            _interactable.RaiseGrabbed(grabArgs);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!_selectArgs.Remove(args.interactorObject, out var grabArgs))
                grabArgs = CreateOtherArgs(args.interactorObject);

            if (args.interactorObject is Component component && component.TryGetComponent(out XriGrabberBridge _))
                _lastHandReleaseTime = Time.time;

            _interactable.RaiseReleased(grabArgs);
        }
    }
}
