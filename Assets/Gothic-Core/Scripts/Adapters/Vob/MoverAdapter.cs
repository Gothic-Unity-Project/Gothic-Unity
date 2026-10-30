using System;
using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// Drives zCMover movement along its ZenKit keyframes (gates, drawbridges, lifts), segment by segment.
    /// Open() moves to the last keyframe, Close() back to the first one, Toggle() switches between both.
    /// Triggered by mobs (wheels, levers) via VRPlayerService.HandleMobGrab and by the trigger system
    /// (VobService.DispatchTrigger). When it arrives, it opens/closes its chained target mover(s) as well,
    /// or sends a trigger event if the target isn't a mover.
    /// Timing follows OpenGothic (common/world/triggers/movetrigger.cpp).
    /// </summary>
    public class MoverAdapter : MonoBehaviour
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;

        // Below this distance (in cm, like Gothic) a segment counts as rotation-only (OpenGothic threshold).
        private const float _rotationOnlyThresholdCm = 0.01f;

        // OpenGothic: rotation speeds of slow movers are scaled up, otherwise e.g. temple doors take minutes.
        private const float _lowRotationSpeed = 0.006f;
        private const float _lowRotationSpeedFactor = 300f;

        private IMover _mover;
        private string _chainTarget;

        private (Vector3 pos, Quaternion rot)[] _keyframes;

        // Seconds from keyframe i to keyframe i + 1 (cyclic) at original speed.
        private float[] _segmentSeconds;
        private bool _isOpen;
        private bool _isMoving;
        private int _currentKeyIndex;

        // Key restored from a save game, applied in Start() (after the child VOBs are created).
        private int _savedKeyIndex;

        // The running move (MoveTo()): segment, its progress (0...1 of its time), direction and destination key.
        private int _segmentStart;
        private int _segmentEnd;
        private float _segmentProgress;
        private int _moveStep;
        private int _moveDestination;

        /// <summary>
        /// The VOB keeps the current key for save games (zCMover actKeyframe).
        /// </summary>
        private int CurrentKeyIndex
        {
            get => _currentKeyIndex;
            set
            {
                _currentKeyIndex = value;
                _mover.ActKeyframe = value;
            }
        }

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.StoreWorldStateStart.AddListener(StoreMoveState);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.StoreWorldStateStart.RemoveListener(StoreMoveState);
        }

        private void Start()
        {
            if (_keyframes == null || _savedKeyIndex < 0 || _savedKeyIndex >= _keyframes.Length)
                return;

            // Chained movers restore their own key and move. No trigger is sent.
            if (_savedKeyIndex > 0)
            {
                (transform.position, transform.rotation) = _keyframes[_savedKeyIndex];
                _currentKeyIndex = _savedKeyIndex;
                _isOpen = true;
                Logger.Log($"[MoverAdapter] {_mover.Name} restored at key {_savedKeyIndex}.", LogCat.Vob);
            }

            ResumeSavedMove();
        }

        /// <summary>
        /// Like Gothic, a save game stores a running move in the mover's save fields: the segment (actKeyframe ->
        /// nextKeyframe), the position inside it (actKeyframeF), the direction (advanceDir) and opening/closing
        /// (moverState). Written when the world is saved or left.
        /// </summary>
        private void StoreMoveState(bool isSaving)
        {
            if (_mover == null)
                return;

            if (_isMoving)
            {
                _mover.ActKeyframe = _segmentStart;
                _mover.NextKeyframe = _segmentEnd;
                _mover.ActKeyframeF = _segmentStart + _moveStep * _segmentProgress;
                _mover.AdvanceDir = _moveStep;
                _mover.MoverState = (int)(_moveDestination == 0
                    ? VmGothicEnums.MoverState.Closing
                    : VmGothicEnums.MoverState.Opening);
                return;
            }

            _mover.ActKeyframe = _currentKeyIndex;
            _mover.NextKeyframe = _currentKeyIndex;
            _mover.ActKeyframeF = _currentKeyIndex;
            _mover.AdvanceDir = 0;
            _mover.MoverState = (int)(_isOpen ? VmGothicEnums.MoverState.Open : VmGothicEnums.MoverState.Closed);
        }

        /// <summary>
        /// A move saved while running (StoreMoveState()) continues inside its segment, without start sound. It ends
        /// like the original move: opening at the last key, closing at the first. SingleKeys movers (goto messages)
        /// stop at the segment's end key.
        /// </summary>
        private void ResumeSavedMove()
        {
            var state = (VmGothicEnums.MoverState)_mover.MoverState;
            var step = Mathf.RoundToInt(_mover.AdvanceDir);
            var from = _mover.ActKeyframe;
            var next = _mover.NextKeyframe;
            if (state is not (VmGothicEnums.MoverState.Opening or VmGothicEnums.MoverState.Closing) ||
                Mathf.Abs(step) != 1 || _keyframes.Length < 2 || from < 0 || from >= _keyframes.Length ||
                next != (from + step + _keyframes.Length) % _keyframes.Length)
                return;

            var isOpening = state == VmGothicEnums.MoverState.Opening;
            var destination = _mover.Behavior == MoverBehavior.SingleKeys
                ? next
                : isOpening ? _keyframes.Length - 1 : 0;
            var progress = Mathf.Clamp01(Mathf.Abs(_mover.ActKeyframeF - from));
            Logger.Log($"[MoverAdapter] {_mover.Name} continues its move [{from}]->[{next}] ({progress:P0}) " +
                       $"to key {destination}.", LogCat.Vob);

            _currentKeyIndex = from;
            StartCoroutine(MoveTo(from, destination, step, null, isOpening ? _mover.SfxOpenEnd : _mover.SfxCloseEnd,
                () =>
                {
                    CurrentKeyIndex = destination;
                    _isOpen = destination != 0;
                    TriggerChainedMover(opening: _isOpen);
                }, progress));
        }

        public void Init(IMover mover, string chainTarget = "")
        {
            _mover = mover;
            _chainTarget = chainTarget;

            var raw = mover.Keyframes;
            _keyframes = new (Vector3, Quaternion)[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                var pos = raw[i].Position.ToUnityVector();
                var r = raw[i].Rotation;
                var rot = new Quaternion(r.X, r.Y, r.Z, -r.W); // Gothic→Unity: negate W, same as AnimationService
                _keyframes[i] = (pos, rot);
            }

            _segmentSeconds = new float[_keyframes.Length];
            for (var i = 0; i < _keyframes.Length; i++)
                _segmentSeconds[i] = CalculateSegmentSeconds(i, (i + 1) % _keyframes.Length, mover.Speed);

            if (_keyframes.Length > 0)
                (transform.position, transform.rotation) = _keyframes[0];

            // A save game (or a world we left) stores the reached key.
            _savedKeyIndex = mover.ActKeyframe;

            Logger.Log($"[MoverAdapter] {mover.Name} init - {_keyframes.Length} keyframes, speed={mover.Speed}",
                LogCat.Vob);
        }

        /// <summary>
        /// Gothic's mover speed is cm per millisecond for moving segments (OpenGothic: ticks = length / speed).
        /// Rotation-only segments treat it as rotations per second (slow ones scaled up, see OpenGothic
        /// scaleRotSpeed - its per-world boat exception isn't ported).
        /// </summary>
        private float CalculateSegmentSeconds(int from, int to, float speed)
        {
            if (speed <= 0f)
                return 0f;

            var lengthCm = Vector3.Distance(_keyframes[from].pos, _keyframes[to].pos) * 100f;
            float milliseconds;
            if (lengthCm > _rotationOnlyThresholdCm)
            {
                milliseconds = lengthCm / speed;
            }
            else
            {
                var angle = Quaternion.Angle(_keyframes[from].rot, _keyframes[to].rot);
                var rotationSpeed = speed < _lowRotationSpeed ? speed * _lowRotationSpeedFactor : speed;
                milliseconds = angle / 360f * 1000f / rotationSpeed;
            }

            return Mathf.Max(1f, milliseconds) / 1000f;
        }

        /// <summary>
        /// Trigger event, behaves per zCMover behavior (OpenGothic MoveTrigger::onTrigger).
        /// SingleKeys movers ignore it - they are driven by goto messages only (GoToKey*).
        /// Loop movers toggle only; continuous looping isn't implemented.
        /// </summary>
        public void OnTrigger()
        {
            switch (_mover.Behavior)
            {
                case MoverBehavior.TriggerControl:
                case MoverBehavior.OpenTime:
                    Open();
                    break;
                case MoverBehavior.SingleKeys:
                    break;
                default: // Toggle, Loop
                    Toggle();
                    break;
            }
        }

        /// <summary>
        /// Untrigger event: only trigger-controlled movers close again (OpenGothic MoveTrigger::onUntrigger).
        /// </summary>
        public void OnUntrigger()
        {
            if (_mover.Behavior == MoverBehavior.TriggerControl)
                Close();
        }

        public void Toggle()
        {
            if (_isMoving)
                return;

            if (_isOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (_isMoving || _isOpen)
                return;

            Logger.Log($"[MoverAdapter] {_mover.Name} Open(), chainTarget='{_chainTarget}'", LogCat.Vob);
            if (_keyframes != null && _keyframes.Length >= 2)
            {
                var lastIndex = _keyframes.Length - 1;
                StartCoroutine(MoveTo(_currentKeyIndex, lastIndex, 1, _mover.SfxOpenStart, _mover.SfxOpenEnd, () =>
                {
                    CurrentKeyIndex = lastIndex;
                    _isOpen = true;
                    TriggerChainedMover(opening: true);
                }));
            }
            else
            {
                CurrentKeyIndex = _keyframes != null ? _keyframes.Length - 1 : 0;
                _isOpen = true;
                TriggerChainedMover(opening: true);
            }
        }

        public void Close()
        {
            if (_isMoving || !_isOpen)
                return;

            Logger.Log($"[MoverAdapter] {_mover.Name} Close(), chainTarget='{_chainTarget}'", LogCat.Vob);
            if (_keyframes != null && _keyframes.Length >= 2)
            {
                StartCoroutine(MoveTo(_currentKeyIndex, 0, -1, _mover.SfxCloseStart, _mover.SfxCloseEnd, () =>
                {
                    CurrentKeyIndex = 0;
                    _isOpen = false;
                    TriggerChainedMover(opening: false);
                }));
            }
            else
            {
                CurrentKeyIndex = 0;
                _isOpen = false;
                TriggerChainedMover(opening: false);
            }
        }

        public void GoToKeyNext()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2)
                return;

            var from = _currentKeyIndex;
            var to = (_currentKeyIndex + 1) % _keyframes.Length;
            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyNext() [{from}]->[{to}]", LogCat.Vob);
            StartCoroutine(MoveTo(from, to, 1, _mover.SfxOpenStart, _mover.SfxOpenEnd, () =>
            {
                CurrentKeyIndex = to;
                _isOpen = to != 0;
                TriggerChainedMover(opening: _isOpen);
            }));
        }

        public void GoToKeyPrev()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2)
                return;

            var from = _currentKeyIndex;
            var to = (_currentKeyIndex - 1 + _keyframes.Length) % _keyframes.Length;
            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyPrev() [{from}]->[{to}]", LogCat.Vob);
            StartCoroutine(MoveTo(from, to, -1, _mover.SfxCloseStart, _mover.SfxCloseEnd, () =>
            {
                CurrentKeyIndex = to;
                _isOpen = to != 0;
                TriggerChainedMover(opening: _isOpen);
            }));
        }

        public void GoToKeyFixed(int key, bool direct)
        {
            if (_isMoving || _keyframes == null || _keyframes.Length == 0)
                return;

            key = Mathf.Clamp(key, 0, _keyframes.Length - 1);
            if (key == _currentKeyIndex)
                return;

            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyFixed(key={key}, direct={direct}) from={_currentKeyIndex}",
                LogCat.Vob);

            if (direct)
            {
                transform.position = _keyframes[key].pos;
                transform.rotation = _keyframes[key].rot;
                CurrentKeyIndex = key;
                _isOpen = key != 0;
                TriggerChainedMover(opening: _isOpen);
            }
            else
            {
                var isForward = key > _currentKeyIndex;
                var sfxStart = isForward ? _mover.SfxOpenStart : _mover.SfxCloseStart;
                var sfxEnd = isForward ? _mover.SfxOpenEnd : _mover.SfxCloseEnd;
                var targetKey = key;
                StartCoroutine(MoveTo(_currentKeyIndex, targetKey, isForward ? 1 : -1, sfxStart, sfxEnd, () =>
                {
                    CurrentKeyIndex = targetKey;
                    _isOpen = targetKey != 0;
                    TriggerChainedMover(opening: _isOpen);
                }));
            }
        }

        private void TriggerChainedMover(bool opening)
        {
            if (string.IsNullOrEmpty(_chainTarget))
                return;

            if (!_vobService.TryGetMovers(_chainTarget, out var containers))
            {
                // Target isn't a mover (trigger list, script, ...): OpenGothic sends it a trigger event whenever
                // a move ends (MoveTrigger::postProcessTrigger), for opening and closing alike.
                _vobService.DispatchTrigger(_chainTarget, _mover.Name);
                return;
            }

            foreach (var container in containers)
            {
                if (container?.Go == null)
                    continue;

                var chained = container.Go.GetComponentInChildren<MoverAdapter>();
                if (chained == null)
                    continue;

                if (opening)
                    chained.Open();
                else
                    chained.Close();
            }
        }

        /// <summary>
        /// Moves keyframe by keyframe from fromIdx to toIdx (step +1 or -1, cyclic), each segment with its own
        /// duration. startProgress (0...1) skips the beginning of the first segment (a move continued from a save).
        /// </summary>
        private IEnumerator MoveTo(int fromIdx, int toIdx, int step, string sfxStart, string sfxEnd, Action onDone,
            float startProgress = 0f)
        {
            _isMoving = true;
            _moveStep = step;
            _moveDestination = toIdx;
            PlaySfx(sfxStart);

            var count = _keyframes.Length;
            var segmentStart = fromIdx;
            while (segmentStart != toIdx)
            {
                var segmentEnd = (segmentStart + step + count) % count;
                // Moving backwards runs the segment that starts at the lower keyframe in reverse.
                var segmentIndex = step > 0 ? segmentStart : segmentEnd;
                var duration = _segmentSeconds[segmentIndex];
                var isFirstSegment = segmentStart == fromIdx;
                var isLastSegment = segmentEnd == toIdx;
                _segmentStart = segmentStart;
                _segmentEnd = segmentEnd;

                var (startPos, startRot) = _keyframes[segmentStart];
                var (endPos, endRot) = _keyframes[segmentEnd];
                var elapsed = isFirstSegment ? startProgress * duration : 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    _segmentProgress = Mathf.Clamp01(elapsed / duration);
                    var t = ApplySpeedCurve(_segmentProgress, isFirstSegment, isLastSegment);
                    transform.position = Vector3.Lerp(startPos, endPos, t);
                    transform.rotation = Quaternion.Slerp(startRot, endRot, t);
                    yield return null;
                }

                transform.position = endPos;
                transform.rotation = endRot;
                segmentStart = segmentEnd;
            }

            PlaySfx(sfxEnd);
            _isMoving = false;
            onDone?.Invoke();
        }

        /// <summary>
        /// OpenGothic calcProgress: "slow" types ease only the first/last segment of a move, the Segment* types
        /// ease every segment.
        /// </summary>
        private float ApplySpeedCurve(float t, bool isFirstSegment, bool isLastSegment)
        {
            var slowStart = (2 - t) * t * t;
            var slowEnd = ((1 - t) * t + 1) * t;
            var slowStartEnd = (3 - 2 * t) * t * t;

            switch (_mover.SpeedType)
            {
                case MoverSpeedType.SlowStartEnd:
                    if (isFirstSegment && isLastSegment)
                        return slowStartEnd;
                    if (isFirstSegment)
                        return slowStart;
                    return isLastSegment ? slowEnd : t;
                case MoverSpeedType.SegmentSlowStartEnd:
                    return slowStartEnd;
                case MoverSpeedType.SlowStart:
                    return isFirstSegment ? slowStart : t;
                case MoverSpeedType.SegmentSlowStart:
                    return slowStart;
                case MoverSpeedType.SlowEnd:
                    return isLastSegment ? slowEnd : t;
                case MoverSpeedType.SegmentSlowEnd:
                    return slowEnd;
                default:
                    return t;
            }
        }

        private void PlaySfx(string sfxName)
        {
            if (string.IsNullOrEmpty(sfxName))
                return;

            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
