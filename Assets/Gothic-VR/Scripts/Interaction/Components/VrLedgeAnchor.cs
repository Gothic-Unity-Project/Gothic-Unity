using Gothic.Core.Models.Movement;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Grab point on a climbable lip, put under a hand by VrLedgeClimb. Gothic's world has no climbable marks, so the
    /// backend's climb interactable (XRI: ClimbInteractable) lives here and moves to whichever lip a hand reaches.
    ///
    /// Its layer (ClimbAnchor) collides with nothing: only the hands' grab casts find it, the player's ground,
    /// teleport and room checks don't. Its box may therefore stick out of the wall.
    /// Local axes: x along the lip, y up, z into the wall.
    /// </summary>
    [RequireComponent(typeof(VrInteractable))]
    public class VrLedgeAnchor : MonoBehaviour
    {
        [Tooltip("Grab box in m along the lip (x), up (y) and into the wall (z), centered on the lip.")]
        [SerializeField]
        private Vector3 _size = new(0.4f, 0.12f, 0.12f);

        [Tooltip("Grab box with the hint Assist: easier to reach.")] [SerializeField]
        private Vector3 _assistSize = new(0.4f, 0.4f, 0.4f);

        [SerializeField] private BoxCollider _collider;

        [Tooltip("Glowing strip on the lip (hints Near and Assist). Its color is the full glow, positions are local. " +
                 "On a rendered layer, as the anchor's layer may be culled.")]
        [SerializeField]
        private LineRenderer _glow;

        [Tooltip("Alpha of the strip's color when the hand is at the lip.")] [SerializeField]
        private float _glowAlpha = 0.6f;

        [Tooltip("Length in m of the glowing strip along the lip.")] [SerializeField]
        private float _glowLength = 0.35f;

        private Color _glowColor = Color.white;

        public VrInteractable Interactable { get; private set; }

        /// <summary>
        /// The lip this anchor sits on.
        /// </summary>
        public LedgeInfo Info { get; private set; }

        public bool IsPlaced => _collider != null && _collider.enabled;
        public bool IsHeld => Interactable != null && Interactable.IsHeld;

        private void Awake()
        {
            Interactable = GetComponent<VrInteractable>();
            if (_glow != null)
            {
                _glowColor = _glow.startColor;
                _glow.useWorldSpace = false;
                _glow.positionCount = 2;
                _glow.SetPosition(0, new Vector3(-_glowLength / 2f, 0.01f, -0.01f));
                _glow.SetPosition(1, new Vector3(_glowLength / 2f, 0.01f, -0.01f));
            }

            Hide();
        }

        /// <summary>
        /// Puts the anchor onto the lip at point (on the lip).
        /// </summary>
        public void Place(LedgeInfo info, Vector3 point, bool isAssist)
        {
            Info = info;
            transform.SetPositionAndRotation(point, Quaternion.LookRotation(-info.WallNormal, Vector3.up));
            _collider.size = isAssist ? _assistSize : _size;
            _collider.center = Vector3.zero;
            _collider.enabled = true;
        }

        public void Hide()
        {
            if (_collider != null)
                _collider.enabled = false;

            SetGlow(0f);
        }

        /// <summary>
        /// The point is inside the grab box, grown by margin (m).
        /// </summary>
        public bool Contains(Vector3 point, float margin)
        {
            if (!IsPlaced)
                return false;

            var local = transform.InverseTransformPoint(point);
            var half = _collider.size / 2f + Vector3.one * margin;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>
        /// Strength 0 (hidden) to 1 (full alpha).
        /// </summary>
        public void SetGlow(float strength)
        {
            if (_glow == null)
                return;

            _glow.enabled = strength > 0f;
            if (!_glow.enabled)
                return;

            // Premultiplied alpha (like the teleport arc's Default-Line): the color fades with the alpha.
            var color = _glowColor * (_glowAlpha * Mathf.Clamp01(strength));
            _glow.startColor = color;
            _glow.endColor = color;
        }
    }
}
