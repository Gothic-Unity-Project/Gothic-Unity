using UnityEngine;
using UnityEngine.Serialization;

namespace GUZ.Core.Adapters.Vob
{
    public class ChangeLevelTriggerHandler : MonoBehaviour
    {
        [FormerlySerializedAs("levelName")] public string LevelName;
        [FormerlySerializedAs("startVob")] public string StartVob;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            GlobalEventDispatcher.LevelChangeTriggered.Invoke(LevelName, StartVob.Trim());
        }
    }
}
