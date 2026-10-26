using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Backend-neutral slot for one item: hip holster, backpack slot, chest slot, NPC loot slot. Or for one linked
    /// interactable which isn't an item (the backpack on the shoulder).
    /// Owns the Gothic behaviour:
    /// - Our items are VobLoader -> Grabbable (VrInteractable) -> Mesh. Culling and saving work on the VobLoader, so
    ///   the slot re-parents the VobLoader (not the grabbable child) and restores the previous parent on removal.
    ///   The linked interactable has no VobLoader and is re-parented itself.
    /// - Rotation by item category, scale-to-fit by the bounds of the item's renderers (cached per item), kinematic
    ///   while inside.
    /// - Sounds (the interactable's socket sounds or the slot's fallbacks) and the IVrSocket events
    ///   (BeforeGrabbed/Grabbed = placed, Released = removed).
    ///
    /// A backend part (IVrSocketBackend, e.g. XriSocketBridge) on the same GameObject detects items released into
    /// the slot and holds them in the backend's terms. Without an active backend part, the slot still works for
    /// programmatic TryPlace/ForceRelease (nothing is detected).
    /// </summary>
    public class VrSocketSlot : MonoBehaviour, IVrSocket
    {
        private const int MaxFitFrames = 5;
        private const float MinSide = 0.0002f;

        private static readonly ConditionalWeakTable<Transform, StrongBox<Bounds>> _boundsCache = new();
        private static readonly List<Renderer> _rendererBuffer = new();

        [Tooltip("Where the item is put. Empty: this transform.")]
        [SerializeField] private Transform _attachPoint;

        [Tooltip("Scales the item while it's inside, so that its largest side is Size (like HVR's socket Size: " +
                 "backpack slot 0.05, chest slot 0.18, holster 0.3). Measured in the attach point's space.")]
        [SerializeField] private bool _shouldScaleToFit = true;

        [Tooltip("Largest side of an item inside the slot (see Should Scale To Fit).")]
        [SerializeField] private float _size = 0.05f;

        [Tooltip("Rotates the item by its Gothic category (weapons diagonal, documents flat, ...).")]
        [SerializeField] private bool _shouldRotateByCategory = true;

        [Tooltip("Moves the item so that the center of its bounds is on the attach point.")]
        [SerializeField] private bool _shouldCenterItem = true;

        [Tooltip("Over-the-shoulder slot (backpack, right shoulder holster). See IVrSocket.IsShoulder.")]
        [SerializeField] private bool _isShoulder;

        [Tooltip("Only this interactable goes into the slot (e.g. the backpack on the shoulder). It needs no " +
                 "VobLoader. Empty: Gothic items only.")]
        [SerializeField] private VrInteractable _linkedInteractable;

        [Tooltip("Played when an item without own socket sounds is put in.")]
        [SerializeField] private AudioClip _placedSoundFallback;

        [Tooltip("Played when an item without own socket sounds is taken out.")]
        [SerializeField] private AudioClip _removedSoundFallback;

        [Inject] private readonly IVrSfxPlayer _sfxPlayer;

        public event Action<VrGrabArgs> BeforeGrabbed;
        public event Action<VrGrabArgs> Grabbed;
        public event Action<VrGrabArgs> Released;

        /// <summary>
        /// An interactable which the backend accepts as candidate is hovering the slot (hand-held or just released).
        /// </summary>
        public event Action<VrInteractable> HoverEntered;

        public event Action<VrInteractable> HoverExited;

        private readonly List<VrInteractable> _hovered = new();

        private IVrSocketBackend _backend;
        private bool _isBackendResolved;
        private bool _isSilent;

        private VrInteractable _held;
        private Transform _heldRoot;
        private Transform _previousParent;
        private Vector3 _previousScale;
        private Rigidbody _heldRigidbody;
        private bool _wasKinematic;
        private bool _isPlacedEventRaised;
        private bool _isReselectNeeded;
        private Coroutine _fitRoutine;

        public VrGrabberKind Kind => VrGrabberKind.Socket;
        public VrHandSide HandSide => VrHandSide.Left;
        public VrInteractable Held => _held;
        public Transform Transform => transform;
        public bool IsOccupied => _held != null;
        public bool IsShoulder => _isShoulder;
        public VrInteractable LinkedInteractable => _linkedInteractable;
        public bool IsHovered => _hovered.Count > 0;
        public Transform AttachPoint => _attachPoint != null ? _attachPoint : transform;

        /// <summary>
        /// True if a backend part of the active VR backend sits next to this slot (it detects items).
        /// </summary>
        public bool HasActiveBackend => Backend != null;

        public float Size
        {
            get => _size;
            set => _size = value;
        }

        public AudioClip PlacedSoundFallback
        {
            get => _placedSoundFallback;
            set => _placedSoundFallback = value;
        }

        public AudioClip RemovedSoundFallback
        {
            get => _removedSoundFallback;
            set => _removedSoundFallback = value;
        }

        private IVrSocketBackend Backend
        {
            get
            {
                if (!_isBackendResolved)
                {
                    _backend = FindActiveBackend();
                    _isBackendResolved = true;
                }

                return _backend;
            }
        }

        /// <summary>
        /// True if the interactable is a Gothic item (VobLoader of an oCItem). Only items go into slots.
        /// </summary>
        public static bool IsItem(VrInteractable interactable)
        {
            if (interactable == null || interactable.IsSelectOnly)
                return false;

            var vob = interactable.GetComponentInParent<VobLoader>(true)?.Container?.Vob;
            return vob != null && vob.Type == VirtualObjectType.oCItem;
        }

        /// <summary>
        /// Local rotation of an item inside a slot, by its category. Identity for unknown items.
        /// </summary>
        public static Quaternion GetCategoryRotation(ItemInstance item)
        {
            if (item == null)
                return Quaternion.identity;

            switch ((VmGothicEnums.ItemFlags)item.MainFlag)
            {
                case VmGothicEnums.ItemFlags.ItemKatNf:
                case VmGothicEnums.ItemFlags.ItemKatMun:
                    return Quaternion.Euler(-45, 0, -90);
                case VmGothicEnums.ItemFlags.ItemKatFf:
                    return Quaternion.Euler(45, 180, -90);
                case VmGothicEnums.ItemFlags.ItemKatArmor:
                    return Quaternion.Euler(0, 90, 0);
                case VmGothicEnums.ItemFlags.ItemKatFood:
                    return Quaternion.Euler(0, 0, 22.5f);
                case VmGothicEnums.ItemFlags.ItemKatDocs:
                    return Quaternion.Euler(90, 0, 90);
                case VmGothicEnums.ItemFlags.ItemKatRune:
                    // Runes and magic scrolls
                    return Quaternion.Euler(-90, 0, 90);
                case VmGothicEnums.ItemFlags.ItemKatMagic:
                    // Rings and amulets
                    return Quaternion.Euler(0, 0, 45);
                case VmGothicEnums.ItemFlags.ItemKatPotions:
                    return Quaternion.identity;
                default:
                    // Misc and all others.
                    return Quaternion.Euler(-45, 135, -45);
            }
        }

        private void Awake()
        {
            _ = Backend;
        }

        private void OnEnable()
        {
            if (!_isReselectNeeded)
                return;

            _isReselectNeeded = false;
            if (_held == null || Backend == null || Backend.TryPlace(_held))
                return;

            Logger.LogWarning($"[Socket] Backend couldn't hold {_held.name} in {name} again. Taken out.", LogCat.VR);
            Remove(true);
        }

        private void OnDisable()
        {
            // Hover exits of a disabled backend may not arrive anymore.
            _hovered.Clear();
        }

        /// <summary>
        /// True if the interactable belongs into this slot at all: the linked interactable, or else any Gothic item.
        /// </summary>
        public bool Accepts(VrInteractable interactable)
        {
            if (interactable == null)
                return false;

            return _linkedInteractable != null ? interactable == _linkedInteractable : IsItem(interactable);
        }

        /// <summary>
        /// True if the interactable can go into this slot now (free, or it's already inside).
        /// </summary>
        public bool IsValidFor(VrInteractable interactable)
        {
            return !IsOccupied || _held == interactable;
        }

        /// <summary>
        /// While silent, no placed/removed sounds are played (bulk spawning of chest content).
        /// </summary>
        public void SetSilent(bool isSilent)
        {
            _isSilent = isSilent;
        }

        /// <summary>
        /// Off: the slot doesn't detect items released into it (TryPlace still works).
        /// </summary>
        public void SetDetectionEnabled(bool isEnabled)
        {
            Backend?.SetDetectionEnabled(isEnabled);
        }

        public bool TryPlace(VrInteractable interactable, bool silent)
        {
            if (interactable == null || IsOccupied)
                return false;

            var loader = GetLoader(interactable);
            if (loader == null && interactable != _linkedInteractable)
            {
                Logger.LogWarning($"[Socket] {interactable.name} has no VobLoader and can't be put into {name}.",
                    LogCat.VR);
                return false;
            }

            var args = CreateArgs(interactable);
            BeforeGrabbed?.Invoke(args);
            Attach(interactable, loader);
            _isPlacedEventRaised = true;
            PlaySound(interactable.SocketedSound != null ? interactable.SocketedSound : _placedSoundFallback, silent);
            Grabbed?.Invoke(args);

            if (Backend == null || Backend.TryPlace(interactable))
                return true;

            Logger.LogWarning($"[Socket] Backend couldn't hold {interactable.name} in {name}. Taken out again.",
                LogCat.VR);
            Remove(true);
            return false;
        }

        public void ForceRelease()
        {
            if (!IsOccupied)
                return;

            // The backend reports its release via OnBackendRemoved.
            Backend?.ForceRelease();

            // No backend, or the backend hadn't finished the placement yet.
            if (IsOccupied)
                Remove(false);
        }

        /// <summary>
        /// The backend starts holding an interactable (released into the slot). Called before the backend moves or
        /// configures it.
        /// </summary>
        internal void OnBackendPlacing(VrInteractable interactable)
        {
            if (interactable == null || interactable == _held)
                return;

            if (IsOccupied)
            {
                Logger.LogWarning($"[Socket] {name} is occupied by {_held.name}, {interactable.name} is ignored.",
                    LogCat.VR);
                return;
            }

            var loader = GetLoader(interactable);
            if (loader == null && interactable != _linkedInteractable)
                return;

            BeforeGrabbed?.Invoke(CreateArgs(interactable));
            Attach(interactable, loader);
            _isPlacedEventRaised = false;
        }

        /// <summary>
        /// The backend holds the interactable now.
        /// </summary>
        internal void OnBackendPlaced(VrInteractable interactable)
        {
            if (interactable == null || interactable != _held || _isPlacedEventRaised)
                return;

            _isPlacedEventRaised = true;
            PlaySound(interactable.SocketedSound != null ? interactable.SocketedSound : _placedSoundFallback, false);
            Grabbed?.Invoke(CreateArgs(interactable));
        }

        /// <summary>
        /// The backend released the interactable (taken by a hand, ForceRelease, ...).
        /// </summary>
        internal void OnBackendRemoved(VrInteractable interactable)
        {
            if (interactable == null || interactable != _held)
                return;

            // The slot is being deactivated (e.g. a chest is closed): the backend lets go of everything, but the item
            // stays inside (and hidden with the slot). The backend holds it again once the slot is enabled.
            if (!gameObject.activeInHierarchy)
            {
                _isReselectNeeded = true;
                return;
            }

            Remove(false);
        }

        internal void OnBackendHoverEntered(VrInteractable interactable)
        {
            if (interactable == null || interactable == _held || _hovered.Contains(interactable))
                return;

            _hovered.Add(interactable);
            HoverEntered?.Invoke(interactable);
        }

        internal void OnBackendHoverExited(VrInteractable interactable)
        {
            if (!_hovered.Remove(interactable))
                return;

            HoverExited?.Invoke(interactable);
        }

        private IVrSocketBackend FindActiveBackend()
        {
            foreach (var backend in GetComponents<IVrSocketBackend>())
            {
                if (backend.IsActive)
                    return backend;
            }

            return null;
        }

        /// <summary>
        /// The item's VobLoader. Null for the linked interactable, which is moved itself.
        /// </summary>
        private VobLoader GetLoader(VrInteractable interactable)
        {
            return interactable == _linkedInteractable ? null : interactable.GetComponentInParent<VobLoader>(true);
        }

        private VrGrabArgs CreateArgs(VrInteractable interactable)
        {
            return new VrGrabArgs(VrGrabberKind.Socket, VrHandSide.Left, interactable, gameObject);
        }

        private void Attach(VrInteractable interactable, VobLoader loader)
        {
            var rootTransform = loader != null ? loader.transform : interactable.transform;
            _held = interactable;
            _heldRoot = rootTransform;
            OnBackendHoverExited(interactable);

            _previousParent = rootTransform.parent;
            _previousScale = rootTransform.localScale;

            // A hand moved the grabbable child away from its VobLoader. Inside the slot, both are at one place again.
            var grabbableTransform = interactable.transform;
            if (grabbableTransform != rootTransform)
            {
                if (grabbableTransform.parent != rootTransform)
                    grabbableTransform.SetParent(rootTransform, false);
                grabbableTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            _heldRigidbody = interactable.GetComponent<Rigidbody>();
            if (_heldRigidbody != null)
            {
                _wasKinematic = _heldRigidbody.isKinematic;
                if (!_heldRigidbody.isKinematic)
                {
                    _heldRigidbody.linearVelocity = Vector3.zero;
                    _heldRigidbody.angularVelocity = Vector3.zero;
                }

                _heldRigidbody.isKinematic = true;
            }

            rootTransform.SetParent(AttachPoint, false);
            rootTransform.SetLocalPositionAndRotation(Vector3.zero, GetRotation(loader));

            // Meshes of freshly spawned items may come a frame later.
            if (!TryFitToSlot(rootTransform) && isActiveAndEnabled)
                _fitRoutine = StartCoroutine(FitWhenMeshesExist(rootTransform));
        }

        private void Remove(bool silent)
        {
            var interactable = _held;
            var root = _heldRoot;
            var args = CreateArgs(interactable);

            Detach(root);
            PlaySound(interactable != null && interactable.UnsocketedSound != null
                ? interactable.UnsocketedSound
                : _removedSoundFallback, silent);
            Released?.Invoke(args);
        }

        private void Detach(Transform root)
        {
            if (_fitRoutine != null)
            {
                StopCoroutine(_fitRoutine);
                _fitRoutine = null;
            }

            if (_heldRigidbody != null)
                _heldRigidbody.isKinematic = _wasKinematic;

            var previousParent = _previousParent;
            var previousScale = _previousScale;
            _isReselectNeeded = false;
            _held = null;
            _heldRoot = null;
            _heldRigidbody = null;
            _previousParent = null;

            // Skip VobLoaders which are destroyed or being destroyed (deactivated before Destroy()).
            if (root == null || !root.gameObject.activeSelf)
                return;

            root.SetParent(previousParent, true);
            root.localScale = previousScale;
        }

        private Quaternion GetRotation(VobLoader loader)
        {
            if (!_shouldRotateByCategory || loader == null || loader.Container?.Vob == null)
                return Quaternion.identity;

            return GetCategoryRotation(loader.Container.GetItemInstance());
        }

        private IEnumerator FitWhenMeshesExist(Transform root)
        {
            for (var i = 0; i < MaxFitFrames; i++)
            {
                yield return null;
                if (root == null || root != _heldRoot)
                    yield break;
                if (TryFitToSlot(root))
                    break;
            }

            _fitRoutine = null;
        }

        private bool TryFitToSlot(Transform root)
        {
            if (!_shouldScaleToFit && !_shouldCenterItem)
                return true;
            if (!TryGetLocalBounds(root, out var bounds))
                return false;

            // Like HVR: the largest side (not half of it) becomes Size. Twice as large, a coin in a hip holster
            // reached across the body axis, where the head-down ground check found it and the player flew.
            var boundsSize = bounds.size;
            var maxSide = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
            if (_shouldScaleToFit && maxSide > MinSide)
                root.localScale = Vector3.one * (_size / maxSide);

            if (_shouldCenterItem)
            {
                var scaledCenter = Vector3.Scale(bounds.center, root.localScale);
                root.localPosition = -(root.localRotation * scaledCenter);
            }

            return true;
        }

        /// <summary>
        /// Bounds of the item's renderers in the local space of its root (the VobLoader with the grabbable child at
        /// its local origin, or the linked interactable). Cached per root once meshes exist.
        /// </summary>
        private static bool TryGetLocalBounds(Transform root, out Bounds bounds)
        {
            if (_boundsCache.TryGetValue(root, out var cached))
            {
                bounds = cached.Value;
                return true;
            }

            bounds = default;
            var hasBounds = false;
            var worldToLoader = root.worldToLocalMatrix;

            root.GetComponentsInChildren(true, _rendererBuffer);
            foreach (var itemRenderer in _rendererBuffer)
            {
                if (itemRenderer is not (MeshRenderer or SkinnedMeshRenderer))
                    continue;

                var toLoader = worldToLoader * itemRenderer.localToWorldMatrix;
                var local = itemRenderer.localBounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));
                    var loaderPoint = toLoader.MultiplyPoint3x4(point);

                    if (!hasBounds)
                    {
                        bounds = new Bounds(loaderPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(loaderPoint);
                    }
                }
            }

            _rendererBuffer.Clear();

            if (hasBounds)
            {
                _boundsCache.Remove(root);
                _boundsCache.Add(root, new StrongBox<Bounds>(bounds));
            }

            return hasBounds;
        }

        private void PlaySound(AudioClip clip, bool silent)
        {
            if (silent || _isSilent || clip == null)
                return;

            if (_sfxPlayer != null)
                _sfxPlayer.Play(clip, transform.position);
            else
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
