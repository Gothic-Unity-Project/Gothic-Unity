using Gothic.Core.Const;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    public class MusicCollisionHandler : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag))
            {
                return;
            }

            GlobalEventDispatcher.MusicZoneEntered.Invoke(gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(Constants.PlayerTag))
            {
                return;
            }

            GlobalEventDispatcher.MusicZoneExited.Invoke(gameObject);
        }
    }
}
