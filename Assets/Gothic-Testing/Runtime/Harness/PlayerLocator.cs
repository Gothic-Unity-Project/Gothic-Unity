using Gothic.Core.Const;
using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// Finds and caches the player GameObject, i.e. the minimal state observation everything else in V0 is built
    /// on: the watchdog reads it to tell a stall from a slow step, and every locomotion verb closes its control
    /// loop against it. (ADR-0001 §3.11, build order row 1)
    ///
    /// The lookup is by tag - the same seam NpcService and MusicDomain already use - and it is re-run whenever the
    /// cached object is gone, because a world change destroys and recreates the player rig.
    /// </summary>
    public class PlayerLocator
    {
        private GameObject _player;


        /// <summary>
        /// The player rig, or null while no world is loaded (i.e. during boot or in the main menu).
        /// </summary>
        public GameObject GameObject
        {
            get
            {
                // Unity's fake null: a destroyed object is != null in C# terms but == null against UnityEngine.Object.
                if (_player == null)
                    _player = UnityEngine.GameObject.FindWithTag(Constants.PlayerTag);

                return _player;
            }
        }

        public bool IsAvailable => GameObject != null;

        public Vector3 Position => IsAvailable ? GameObject.transform.position : Vector3.zero;

        /// <summary>
        /// Heading in degrees. The turn verbs measure their own effect with it rather than trusting an input to
        /// rotation conversion which lives in the VR rig and may be retuned at any time.
        /// </summary>
        public float Yaw => IsAvailable ? GameObject.transform.eulerAngles.y : 0f;

        public Transform Transform => IsAvailable ? GameObject.transform : null;
    }
}
