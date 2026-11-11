using Gothic.VR.Adapters.Vob.VobItem;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// The page a mob shows with Doc_Show (G2 bookstands: Use_Bookstand*_S1) - a VRDocViewer above the mob, facing the
    /// player, like a held document or map. Closes when the player walks away.
    /// </summary>
    public class VRMobDocument : MonoBehaviour
    {
        private const string _anchorName = "_MobDocument";

        private const float _closeDistance = 3f;
        private const float _belowEyes = 0.05f;
        private const float _towardsPlayer = 0.4f;

        // VRDocViewer lays its canvas flat for a document held in the hand (local rotation 90, 0, 0).
        private static readonly Quaternion _viewerCanvasRotation = Quaternion.Euler(90f, 0f, 0f);

        private Transform _head;

        public static VRMobDocument Create(GameObject mob)
        {
            var anchor = new GameObject(_anchorName);
            anchor.transform.SetParent(mob.transform, true);
            var document = anchor.AddComponent<VRMobDocument>();
            anchor.AddComponent<VRDocViewer>();
            document.PlaceInFrontOfPlayer(mob);
            return document;
        }

        public bool HasPages => transform.Find(VRDocViewer.CanvasName) != null;

        private void PlaceInFrontOfPlayer(GameObject mob)
        {
            _head = Camera.main != null ? Camera.main.transform : null;
            var center = mob.transform.position;
            var renderers = mob.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var mobRenderer in renderers)
                    bounds.Encapsulate(mobRenderer.bounds);
                center = bounds.center;
            }
            if (_head == null)
            {
                transform.position = center + Vector3.up;
                return;
            }

            var toPlayer = _head.position - center;
            toPlayer.y = 0f;
            var direction = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : -_head.forward;
            var position = center + direction * _towardsPlayer;
            position.y = _head.position.y - _belowEyes;
            transform.position = position;

            // The canvas faces the player upright.
            transform.rotation = Quaternion.LookRotation(-direction, Vector3.up) *
                                 Quaternion.Inverse(_viewerCanvasRotation);
        }

        private void Update()
        {
            // No camera when the page opened - take it once it's there, else walking away never closes the page.
            if (_head == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera == null)
                    return;
                _head = mainCamera.transform;
            }

            if (Vector3.Distance(_head.position, transform.position) > _closeDistance)
                Destroy(gameObject);
        }
    }
}
