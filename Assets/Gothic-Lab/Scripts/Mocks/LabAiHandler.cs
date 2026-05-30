using UnityEngine;

namespace GUZ.Lab.Mocks
{
    public class LabAiHandler : MonoBehaviour
    {

        /// <summary>
        /// Used to remove errors which are thrown, if no Component is fetching the event
        /// </summary>
        public void AnimationEndCallback(string eventEndSignalParam)
        {
            // NOP
        }
    }
}
