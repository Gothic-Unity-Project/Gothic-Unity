using System.Collections;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The one writer of the VR player's walk and run speed. On land: the normal speeds, times a speed potion's
    /// factors for its time (GlobalEventDispatcher.HeroSpeedChanged) - a second potion starts the time anew. In water
    /// (VRSwimDive): half the walk speed, also when running, and no potion - Gothic's sprint overlay only replaces the
    /// run animations on land.
    /// </summary>
    public class VRHeroSpeed : MonoBehaviour
    {
        private IVrRig _rig;
        private Coroutine _running;
        private float _normalMoveSpeed;
        private float _normalRunSpeed;
        private float _potionWalkFactor = 1f;
        private float _potionRunFactor = 1f;
        private bool _isInWater;

        private void Awake()
        {
            _rig = GetComponent<IVrRig>();
            if (_rig == null)
                return;

            _normalMoveSpeed = _rig.MoveSpeed;
            _normalRunSpeed = _rig.RunSpeed;
        }

        private void Start()
        {
            GlobalEventDispatcher.HeroSpeedChanged.AddListener(OnHeroSpeedChanged);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroSpeedChanged.RemoveListener(OnHeroSpeedChanged);

            // The coroutine ends with the component: a rig that outlives it gets its speeds back.
            if (_running != null)
                EndPotion();
        }

        /// <summary>
        /// Knee deep or deeper (VRSwimDive): wading speed. A potion's time runs on, its speed counts on land only.
        /// </summary>
        public void SetInWater(bool isInWater)
        {
            _isInWater = isInWater;
            ApplySpeeds();
        }

        private void OnHeroSpeedChanged(float walkFactor, float runFactor, float seconds)
        {
            if (_rig == null)
            {
                Logger.LogWarning($"[VRHeroSpeed] No IVrRig on {name} - the VR player's speed stays as it is.",
                    LogCat.VR);
                return;
            }

            if (_running != null)
                StopCoroutine(_running);

            _potionWalkFactor = walkFactor;
            _potionRunFactor = runFactor;
            ApplySpeeds();
            _running = StartCoroutine(EndLater(seconds));
        }

        private IEnumerator EndLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            EndPotion();
        }

        private void EndPotion()
        {
            _potionWalkFactor = 1f;
            _potionRunFactor = 1f;
            _running = null;
            ApplySpeeds();
        }

        private void ApplySpeeds()
        {
            if (_rig == null)
                return;

            if (_isInWater)
            {
                // No running in water: running into deep water slows down like walking.
                _rig.MoveSpeed = _normalMoveSpeed / 2;
                _rig.RunSpeed = _normalMoveSpeed / 2;
            }
            else
            {
                _rig.MoveSpeed = _normalMoveSpeed * _potionWalkFactor;
                _rig.RunSpeed = _normalRunSpeed * _potionRunFactor;
            }
        }
    }
}
