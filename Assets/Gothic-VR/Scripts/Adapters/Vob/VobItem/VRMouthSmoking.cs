using System.Collections;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// The part smoked at the VR mouth right now (IVrMouthSmokable: a water pipe's mouthpiece) and its drag. Owned by
    /// VRMouth, which keeps the triggers and the eating and lends its coroutines, audio source and smoke puffs.
    /// - While at the mouth only the smokable works (the pipe bubbles, its bowl smokes).
    /// - Taken away after at least _minDragSeconds: _exhaleDelay later the smokable's ExhaleSound and a cloud of
    ///   _exhalePuffs puffs of its ExhaleAnimation's smoke in front of the face. Taken away sooner: nothing.
    /// - Destroyed or culled at the mouth (its exit never came): forgotten without smoke.
    /// </summary>
    public class VRMouthSmoking
    {
        private const float _minDragSeconds = 0.8f;
        private const float _exhaleSeconds = 0.6f;
        private const int _exhalePuffs = 2;
        // The smoke is held for a moment before it is blown out.
        private const float _exhaleDelay = 1f;

        private readonly VRMouth _mouth;
        private readonly AudioSource _mouthAudio;
        private readonly AudioService _audioService;

        private IVrMouthSmokable _smokable;
        private Collider _smokedPart;
        private float _dragStart;


        public VRMouthSmoking(VRMouth mouth, AudioSource mouthAudio, AudioService audioService)
        {
            _mouth = mouth;
            _mouthAudio = mouthAudio;
            _audioService = audioService;
        }

        /// <summary>
        /// A collider touched the mouth. true: it belongs to something smokable - it's no food, whether it is smoked
        /// now or not.
        /// </summary>
        public bool TryStartSmoking(Collider other)
        {
            var smokable = other.GetComponentInParent<IVrMouthSmokable>();
            if (smokable == null)
                return false;

            // In again without having left: its exit was missed (culled at the mouth).
            if (other == _smokedPart)
                StopSmoking();

            if (!smokable.TryStartSmoking(other))
                return true;

            // Another part was still at the mouth.
            StopSmoking();
            _smokable = smokable;
            _smokedPart = other;
            _dragStart = Time.time;
            // While at the mouth only the pipe works (it bubbles, the bowl smokes); the smoke comes out of the mouth
            // when the mouthpiece is taken away (StopSmoking).
            Logger.Log($"[VRMouth] Smoking {other.name}.", LogCat.VR);
            return true;
        }

        /// <summary>
        /// A collider left the mouth: if it is the smoked part, its drag ends.
        /// </summary>
        public void OnLeftMouth(Collider other)
        {
            if (other == _smokedPart)
                StopSmoking();
        }

        /// <summary>
        /// The smoked part left the mouth. Taken away before the drag ended, no smoke comes; puffs already blown out
        /// drift on. Destroyed or culled at the mouth (its exit never came): the mouth is free again, without smoke.
        /// isExhaling false: VRMouth is disabled - its coroutines end, the smokable only hears that the part left.
        /// </summary>
        public void StopSmoking(bool isExhaling = true)
        {
            if (_smokable == null)
                return;

            // Cleared first: nothing below may leave the mouth stuck on this part.
            var smokable = _smokable;
            var part = _smokedPart;
            var dragStart = _dragStart;
            _smokable = null;
            _smokedPart = null;

            if (IsGone(smokable, part))
            {
                Logger.Log("[VRMouth] The smoked part is gone - nothing is breathed out.", LogCat.VR);
                return;
            }

            // Taken from the mouth after a drag: a breath out and a cloud of smoke in front of the face.
            if (isExhaling && Time.time >= dragStart + _minDragSeconds)
            {
                _mouth.TryExhaleHumansSmoke(smokable.ExhaleAnimation, _exhaleSeconds, _exhaleDelay, _exhalePuffs);
                _mouth.StartCoroutine(PlayExhaleSound(smokable.ExhaleSound, _exhaleDelay));
                Logger.Log($"[VRMouth] Breathing out the smoke of {part.name} in {_exhaleDelay} s.", LogCat.VR);
            }

            smokable.StopSmoking(part);
        }

        /// <summary>
        /// The part or its smokable was destroyed or deactivated (culled, world left). Unity's null check first: `?.`
        /// doesn't do it, and .name of a destroyed collider throws.
        /// </summary>
        private static bool IsGone(IVrMouthSmokable smokable, Collider part)
        {
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy)
                return true;

            return smokable is Behaviour behaviour && (behaviour == null || !behaviour.gameObject.activeInHierarchy);
        }

        private IEnumerator PlayExhaleSound(string soundName, float delay)
        {
            yield return new WaitForSeconds(delay);

            var exhaleClip = _audioService.GetRandomSoundClip(soundName);
            if (exhaleClip != null)
                _mouthAudio.PlayOneShot(exhaleClip);
            else
                Logger.LogWarning($"[VRMouth] No sound '{soundName}' for breathing out the smoke.", LogCat.VR);
        }
    }
}
