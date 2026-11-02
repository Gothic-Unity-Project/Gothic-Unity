using System;
using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
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
    public class MoverAdapter : MonoBehaviour, IDamageable
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;

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

        // DeveloperConfig.EnableTriggerEnableState: a disabled mover ignores triggers (zCMessageFilter Disable).
        private bool _isDisabled;
        public bool IsDisabled => _isDisabled;

        // DeveloperConfig.EnableLogicVobSaveState: the saved pose is set again once the VOB is fully created.
        private bool _isSavedPosePending;

        private void Awake() => this.Inject();

        /// <summary>
        /// After InitVob: the saved pose is set once the mover's children exist, so they move along (G2's canyon teleport
        /// platform stayed underground), and after a child mover was put back to its world file position
        /// (EnableWorldSpaceChildVobs - G2's Adanos temple door was restored open but stood closed).
        /// </summary>
        private void Start()
        {
            if (!_isSavedPosePending)
                return;

            _isSavedPosePending = false;
            if (_keyframes != null && _keyframes.Length > 0)
                SetPose(_keyframes[_currentKeyIndex].pos, _keyframes[_currentKeyIndex].rot);
        }

        private void OnDestroy() => DamageTargets.Unregister(transform);

        public void Init(IMover mover, string chainTarget = "")
        {
            // Awake (and its injection) doesn't run while the GameObject is inactive - lazy loaded VOBs are.
            if (_configService == null)
                this.Inject();

            _mover = mover;
            if (_mover.ReactToOnDamage)
                DamageTargets.Register(transform);
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

            if (_configService.Dev.EnableMoverRelativeKeyframes)
                MakeKeyframesRelativeToVob(mover);

            _segmentSeconds = new float[_keyframes.Length];
            for (var i = 0; i < _keyframes.Length; i++)
                _segmentSeconds[i] = CalculateSegmentSeconds(i, (i + 1) % _keyframes.Length, mover.Speed);

            _isDisabled = _configService.Dev.EnableTriggerEnableState && !mover.StartEnabled;
            if (_isDisabled)
                Logger.Log($"[MoverAdapter] {mover.Name} starts disabled (startEnabled=false)", LogCat.Vob);

            if (_keyframes.Length > 0)
                SetPose(_keyframes[0].pos, _keyframes[0].rot);

            if (_configService.Dev.EnableLogicVobSaveState && LogicVobSaveState.TryTake(mover, out var saved))
                RestoreState(saved);

            Logger.Log($"[MoverAdapter] {mover.Name} init - {_keyframes.Length} keyframes, speed={mover.Speed}",
                LogCat.Vob);
        }

        /// <summary>
        /// DeveloperConfig.EnableMoverRelativeKeyframes: the engine moves a mover by its keyframes relative to where the
        /// VOB stands (OpenGothic MoveTrigger: pos0 = vob * inverse(keyframe[0]), shown at pos0 * keyframe[i]). We put it
        /// on the keyframes themselves - the same as long as the VOB stands at its first keyframe (G2 Irdorath's gates do).
        /// </summary>
        private void MakeKeyframesRelativeToVob(IMover mover)
        {
            if (_keyframes.Length == 0)
                return;

            var vobPos = mover.Position.ToUnityVector();
            var vobRot = mover.Rotation.ToUnityQuaternion();
            var (kfPos, kfRot) = _keyframes[0];
            var rotOffset = vobRot * Quaternion.Inverse(kfRot);
            var rotAngle = Quaternion.Angle(rotOffset, Quaternion.identity);
            var posOffset = vobPos - kfPos;
            if (posOffset.sqrMagnitude < 0.0001f && rotAngle < 1f)
                return;

            // Rotation conversions of keyframes and VOB may differ slightly - only a real difference turns them.
            if (rotAngle < 1f)
                rotOffset = Quaternion.identity;

            for (var i = 0; i < _keyframes.Length; i++)
            {
                var (pos, rot) = _keyframes[i];
                _keyframes[i] = (vobPos + rotOffset * (pos - kfPos), rotOffset * rot);
            }

            Logger.Log($"[MoverAdapter] {mover.Name} keyframes relative to the VOB - offset {posOffset} " +
                       $"({posOffset.magnitude:F2} m), {rotAngle:F1} deg", LogCat.Vob);
        }

        /// <summary>
        /// DeveloperConfig.EnableNestedMovers: like the engine (OpenGothic MoveTrigger moves the local transform), a mover
        /// below another mover moves along with it - its keyframes count from where the parent mover started. G2's
        /// Jharkendar portal: the petals (Z movers) are children of XY movers; set in world space they ignored their
        /// parent and after loading a save only one was moved out.
        /// </summary>
        private void SetPose(Vector3 position, Quaternion rotation)
        {
            var parentMover = _configService.Dev.EnableNestedMovers && transform.parent != null
                ? transform.parent.GetComponentInParent<MoverAdapter>()
                : null;
            if (parentMover != null)
                parentMover.MapFromStartPose(ref position, ref rotation);
            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// A pose given relative to this mover's start (keyframe 0) moved along with this mover's current pose.
        /// </summary>
        private void MapFromStartPose(ref Vector3 position, ref Quaternion rotation)
        {
            if (_keyframes == null || _keyframes.Length == 0)
                return;

            var (startPos, startRot) = _keyframes[0];
            var delta = transform.rotation * Quaternion.Inverse(startRot);
            position = transform.position + delta * (position - startPos);
            rotation = delta * rotation;
        }

        /// <summary>
        /// DeveloperConfig.EnableLogicVobSaveState: a mover moved or disabled since the world was loaded. A moving one
        /// is saved where it came from - it is triggered again after loading.
        /// </summary>
        public bool TryCaptureState(out LogicVobSaveEntry entry)
        {
            entry = null;
            if (_mover == null || (_currentKeyIndex == 0 && !_isOpen && _isDisabled == !_mover.StartEnabled))
                return false;

            entry = new LogicVobSaveEntry
            {
                Key = LogicVobSaveState.MakeKey(_mover),
                KeyIndex = _currentKeyIndex,
                IsOpen = _isOpen,
                IsDisabled = _isDisabled
            };
            return true;
        }

        /// <summary>
        /// Saved state back without moving or triggering its chain - the chain's targets have their own saved state.
        /// </summary>
        private void RestoreState(LogicVobSaveEntry saved)
        {
            if (_keyframes.Length > 0)
            {
                // Not moved yet: the mover's child VOBs (G2 teleport platforms, the focus stone) are created after Init
                // at their world file positions - moved now, they stayed 9 m below. Start moves it with them.
                _currentKeyIndex = Mathf.Clamp(saved.KeyIndex, 0, _keyframes.Length - 1);
                _isSavedPosePending = true;
            }
            _isOpen = saved.IsOpen;
            _isDisabled = saved.IsDisabled;
            _mover.MoverState = (int)(_isOpen ? VmGothicEnums.MoverState.Open : VmGothicEnums.MoverState.Closed);
            Logger.Log($"[MoverAdapter] {_mover.Name} restored from save - key {_currentKeyIndex}, open={_isOpen}, " +
                       $"disabled={_isDisabled}", LogCat.Vob);
        }

        /// <summary>
        /// zCMessageFilter Enable/Disable/Toggle (OpenGothic AbstractTrigger T_Enable/T_Disable/T_ToggleEnable).
        /// </summary>
        public void SetEnabled(MessageFilterAction action)
        {
            _isDisabled = action switch
            {
                MessageFilterAction.Enable => false,
                MessageFilterAction.Disable => true,
                MessageFilterAction.Toggle => !_isDisabled,
                _ => _isDisabled
            };
            Logger.Log($"[MoverAdapter] {_mover?.Name} {action} -> {(_isDisabled ? "disabled" : "enabled")}", LogCat.Vob);
        }

        /// <summary>
        /// Hit by an arrow/bolt/spell: a mover with reactToOnDamage acts like it was triggered.
        /// </summary>
        public void OnDamage(NpcContainer attacker)
        {
            if (_mover == null || !_mover.ReactToOnDamage || _isDisabled)
                return;

            Logger.Log($"[MoverAdapter] {_mover.Name} hit by a projectile", LogCat.Vob);
            OnTrigger();
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
            if (IsDisabledLogged())
                return;

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
            if (IsDisabledLogged())
                return;

            if (_mover.Behavior == MoverBehavior.TriggerControl)
                Close();
        }

        public void Toggle()
        {
            if (_isMoving || IsDisabledLogged())
                return;

            if (_isOpen)
                Close();
            else
                Open();
        }

        private bool IsDisabledLogged()
        {
            if (_isDisabled)
                Logger.Log($"[MoverAdapter] {_mover?.Name} is disabled - trigger ignored", LogCat.Vob);
            return _isDisabled;
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
                    _currentKeyIndex = lastIndex;
                    _isOpen = true;
                    TriggerChainedMover(opening: true);
                }));
            }
            else
            {
                _currentKeyIndex = _keyframes != null ? _keyframes.Length - 1 : 0;
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
                    _currentKeyIndex = 0;
                    _isOpen = false;
                    TriggerChainedMover(opening: false);
                }));
            }
            else
            {
                _currentKeyIndex = 0;
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
                _currentKeyIndex = to;
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
                _currentKeyIndex = to;
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
                SetPose(_keyframes[key].pos, _keyframes[key].rot);
                _currentKeyIndex = key;
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
                    _currentKeyIndex = targetKey;
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
        /// duration.
        /// </summary>
        private IEnumerator MoveTo(int fromIdx, int toIdx, int step, string sfxStart, string sfxEnd, Action onDone)
        {
            _isMoving = true;
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

                var (startPos, startRot) = _keyframes[segmentStart];
                var (endPos, endRot) = _keyframes[segmentEnd];
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    var t = ApplySpeedCurve(Mathf.Clamp01(elapsed / duration), isFirstSegment, isLastSegment);
                    SetPose(Vector3.Lerp(startPos, endPos, t), Quaternion.Slerp(startRot, endRot, t));
                    yield return null;
                }

                SetPose(endPos, endRot);
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
