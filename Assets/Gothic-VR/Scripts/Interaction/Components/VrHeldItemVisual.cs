using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Shows a held item where the hand holds it, not where the physics step left it. VelocityTracking moves the
    /// item towards last frame's hand pose and it's drawn one more physics step behind, while the hand visual is at
    /// this frame's controller pose. Without this, the item slides in the hand while walking or swinging.
    ///
    /// One per item, shared by all hands holding it. The first hand (owner, XRI's primary hand) sets the item pose.
    /// With two hands, every hand is shown on its grip, like HVR's jointed hands: the item's pose comes from both
    /// hands, so neither controller pose matches it.
    ///
    /// Render-only: the transform is set right before rendering (after the late-latched controller pose) and put
    /// back to the Rigidbody pose after rendering and before each physics step, so the physics never sees it.
    /// Added at runtime by VrPhysicsHand.
    /// </summary>
    public class VrHeldItemVisual : MonoBehaviour
    {
        private readonly List<(VrPhysicsHand Hand, Transform Grip)> _holders = new();

        private Rigidbody _body;
        private Transform _anchor;
        private Vector3 _localPosition;
        private Quaternion _localRotation = Quaternion.identity;
        private bool _hasPose;
        private bool _isOverridden;

        public int HolderCount => _holders.Count;

        public static VrHeldItemVisual Hold(Rigidbody body, VrPhysicsHand hand, Transform grip)
        {
            if (!body.TryGetComponent<VrHeldItemVisual>(out var visual))
            {
                visual = body.gameObject.AddComponent<VrHeldItemVisual>();
                visual._body = body;
            }

            visual.Remove(hand);
            visual._holders.Add((hand, grip));
            return visual;
        }

        /// <summary>
        /// The hand let go. The next hand becomes the owner, the last one removes the visual.
        /// </summary>
        public void Release(VrPhysicsHand hand)
        {
            var wasOwner = IsOwner(hand);
            Remove(hand);

            if (_holders.Count == 0)
            {
                ClearPose();
                Destroy(this);
                return;
            }

            if (!wasOwner)
                return;

            _hasPose = false;
            Restore();
            _holders[0].Hand.OnHeldItemOwnerChanged();
        }

        public bool IsOwner(VrPhysicsHand hand)
        {
            return _holders.Count > 0 && _holders[0].Hand == hand;
        }

        /// <summary>
        /// World pose to show this frame (owner only). It's stored relative to the anchor (the owner's attach point),
        /// so that it moves with the controller's late-latched pose before rendering.
        /// </summary>
        public void SetPose(Transform anchor, Vector3 position, Quaternion rotation)
        {
            _anchor = anchor;
            var inverseAnchorRotation = Quaternion.Inverse(anchor.rotation);
            _localPosition = inverseAnchorRotation * (position - anchor.position);
            _localRotation = inverseAnchorRotation * rotation;
            _hasPose = true;
        }

        public void ClearPose()
        {
            _hasPose = false;
            Restore();
        }

        private void Remove(VrPhysicsHand hand)
        {
            for (var i = _holders.Count - 1; i >= 0; i--)
            {
                if (_holders[i].Hand == hand || _holders[i].Hand == null)
                    _holders.RemoveAt(i);
            }
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            Restore();
        }

        private void FixedUpdate()
        {
            // Before the physics step: it syncs changed transforms into the Rigidbody.
            Restore();
        }

        private void OnBeforeRender()
        {
            if (!_hasPose || _anchor == null || _body == null)
                return;

            var anchorRotation = _anchor.rotation;
            var position = _anchor.position + anchorRotation * _localPosition;
            var rotation = anchorRotation * _localRotation;
            transform.SetPositionAndRotation(position, rotation);
            _isOverridden = true;

            if (_holders.Count < 2)
                return;

            foreach (var (hand, grip) in _holders)
            {
                if (hand != null && grip != null)
                    hand.ShowOnGrip(position, rotation, grip);
            }
        }

        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            Restore();
        }

        private void Restore()
        {
            if (!_isOverridden)
                return;

            _isOverridden = false;
            foreach (var (hand, _) in _holders)
            {
                if (hand != null)
                    hand.RestoreFromGrip();
            }

            // The Rigidbody pose, not the interpolated one: the physics step would teleport the body to it.
            if (_body != null)
                transform.SetPositionAndRotation(_body.position, _body.rotation);
        }
    }
}
