using System.Collections;
using Gothic.Core;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// A speed potion (GlobalEventDispatcher.HeroSpeedChanged): the VR player walks and runs as much faster as Gothic's
    /// sprint overlay for its time. A second potion starts the time anew.
    /// </summary>
    public class VRHeroSpeed : MonoBehaviour
    {
        private IVrRig _rig;
        private Coroutine _running;
        private float _normalMoveSpeed;
        private float _normalRunSpeed;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            GlobalEventDispatcher.HeroSpeedChanged.AddListener(OnHeroSpeedChanged);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroSpeedChanged.RemoveListener(OnHeroSpeedChanged);
        }

        private void OnHeroSpeedChanged(float walkFactor, float runFactor, float seconds)
        {
            if (_rig == null)
                return;

            if (_running != null)
                StopCoroutine(_running);
            else
            {
                _normalMoveSpeed = _rig.MoveSpeed;
                _normalRunSpeed = _rig.RunSpeed;
            }

            _rig.MoveSpeed = _normalMoveSpeed * walkFactor;
            _rig.RunSpeed = _normalRunSpeed * runFactor;
            _running = StartCoroutine(EndLater(seconds));
        }

        private IEnumerator EndLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            _rig.MoveSpeed = _normalMoveSpeed;
            _rig.RunSpeed = _normalRunSpeed;
            _running = null;
        }
    }
}
