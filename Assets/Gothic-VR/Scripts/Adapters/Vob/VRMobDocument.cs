#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Adapters.Vob.VobItem;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// DeveloperConfig.EnableMobDocuments: the page a mob shows with Doc_Show (G2 bookstands: Use_Bookstand*_S1) -
    /// a VRDocViewer above the mob, facing the player, like a held document or map. Closes when the player walks away.
    /// </summary>
    public class VRMobDocument : MonoBehaviour
    {
        public const string AnchorName = "_MobDocument";

        private const float _closeDistance = 3f;
        private const float _belowEyes = 0.05f;
        private const float _towardsPlayer = 0.4f;

        // VRDocViewer lays its canvas flat for a document held in the hand (local rotation 90, 0, 0).
        private static readonly Quaternion _viewerCanvasRotation = Quaternion.Euler(90f, 0f, 0f);

        public static VRMobDocument Create(GameObject mob)
        {
            var anchor = new GameObject(AnchorName);
            anchor.transform.SetParent(mob.transform, true);
            var document = anchor.AddComponent<VRMobDocument>();
            anchor.AddComponent<VRDocViewer>();
            document.PlaceInFrontOfPlayer(mob);
            return document;
        }

        public bool HasPages => transform.Find("_DocCanvas") != null;

        private void PlaceInFrontOfPlayer(GameObject mob)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            var center = mob.transform.position;
            var renderers = mob.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var mobRenderer in renderers)
                    bounds.Encapsulate(mobRenderer.bounds);
                center = bounds.center;
            }
            if (head == null)
            {
                transform.position = center + Vector3.up;
                return;
            }

            var toPlayer = head.position - center;
            toPlayer.y = 0f;
            var direction = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : -head.forward;
            var position = center + direction * _towardsPlayer;
            position.y = head.position.y - _belowEyes;
            transform.position = position;

            // The canvas faces the player upright.
            transform.rotation = Quaternion.LookRotation(-direction, Vector3.up) * Quaternion.Inverse(_viewerCanvasRotation);
        }

        private void Update()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null && Vector3.Distance(head.position, transform.position) > _closeDistance)
                Destroy(gameObject);
        }
    }
}
#endif
