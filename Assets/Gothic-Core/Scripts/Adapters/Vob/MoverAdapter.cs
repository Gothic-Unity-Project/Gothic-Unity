using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Drives zCMover movement between ZenKit keyframes.
    /// Toggle behavior: Open() moves 0→1, Close() moves 1→0.
    /// Called by wheel/switch state-change propagation via VobService.ActivateMover().
    /// </summary>
    public class MoverAdapter : MonoBehaviour
    {
        [Inject] private readonly AudioService _audioService;

        private IMover _mover;
        private (Vector3 pos, Quaternion rot)[] _keyframes;
        private bool _isOpen;
        private bool _isMoving;

        private void Awake() => this.Inject();

        public void Init(IMover mover)
        {
            _mover = mover;

            var raw = mover.Keyframes;
            _keyframes = new (Vector3, Quaternion)[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                var pos = raw[i].Position.ToUnityVector();
                var r = raw[i].Rotation;
                var rot = new Quaternion(r.X, r.Y, r.Z, -r.W); // Gothic→Unity: negate W, same as AnimationService
                _keyframes[i] = (pos, rot);
            }

            if (_keyframes.Length > 0)
                (transform.position, transform.rotation) = _keyframes[0];

            Logger.Log($"[MoverAdapter] {mover.Name} init — {_keyframes.Length} keyframes, speed={mover.Speed}", LogCat.Vob);
        }

        public void Toggle()
        {
            if (_isMoving) return;
            if (_isOpen) Close(); else Open();
        }

        public void Open()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2) return;
            StartCoroutine(MoveTo(0, _keyframes.Length - 1, _mover.SfxOpenStart, _mover.SfxOpenEnd, onDone: () => _isOpen = true));
        }

        public void Close()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2) return;
            StartCoroutine(MoveTo(_keyframes.Length - 1, 0, _mover.SfxCloseStart, _mover.SfxCloseEnd, onDone: () => _isOpen = false));
        }

        private IEnumerator MoveTo(int fromIdx, int toIdx, string sfxStart, string sfxEnd, System.Action onDone)
        {
            _isMoving = true;
            PlaySfx(sfxStart);

            var startPos = _keyframes[fromIdx].pos;
            var startRot = _keyframes[fromIdx].rot;
            var endPos = _keyframes[toIdx].pos;
            var endRot = _keyframes[toIdx].rot;

            var dist = Vector3.Distance(startPos, endPos);
            var speed = _mover.Speed > 0 ? _mover.Speed / 100f : 1f; // Gothic units → m/s
            var duration = dist > 0.001f ? dist / speed : 0.5f;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                t = ApplySpeedCurve(t, _mover.SpeedType);

                transform.position = Vector3.Lerp(startPos, endPos, t);
                transform.rotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }

            transform.position = endPos;
            transform.rotation = endRot;

            PlaySfx(sfxEnd);
            _isMoving = false;
            onDone?.Invoke();
        }

        private static float ApplySpeedCurve(float t, MoverSpeedType type) => type switch
        {
            MoverSpeedType.SlowStartEnd => Mathf.SmoothStep(0, 1, t),
            MoverSpeedType.SlowStart    => t * t,
            MoverSpeedType.SlowEnd      => t * (2 - t),
            _                           => t  // Constant
        };

        private void PlaySfx(string sfxName)
        {
            if (string.IsNullOrEmpty(sfxName)) return;
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
