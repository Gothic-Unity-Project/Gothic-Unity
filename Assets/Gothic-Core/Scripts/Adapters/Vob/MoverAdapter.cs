using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
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
    /// Drives zCMover movement between ZenKit keyframes.
    /// Toggle behavior: Open() moves 0→1, Close() moves 1→0.
    /// Called by wheel/switch state-change propagation via VobService.ActivateMover().
    /// </summary>
    public class MoverAdapter : MonoBehaviour, IDamageable
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;

        private IMover _mover;
        private string _chainTarget;
        private (Vector3 pos, Quaternion rot)[] _keyframes;
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
                SetPose(_keyframes[_currentKeyIndex]);
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

            _isDisabled = _configService.Dev.EnableTriggerEnableState && !mover.StartEnabled;
            if (_isDisabled)
                Logger.Log($"[MoverAdapter] {mover.Name} starts disabled (startEnabled=false)", LogCat.Vob);

            if (_keyframes.Length > 0)
                SetPose(_keyframes[0]);

            if (_configService.Dev.EnableLogicVobSaveState && LogicVobSaveState.TryTake(mover, out var saved))
                RestoreState(saved);

            if (_keyframes.Length >= 2)
                Logger.Log($"[MoverAdapter] {mover.Name} init — kf[0]={_keyframes[0].pos} kf[1]={_keyframes[1].pos} dist={Vector3.Distance(_keyframes[0].pos, _keyframes[1].pos):F2} speed={mover.Speed}", LogCat.Vob);
            else
                Logger.Log($"[MoverAdapter] {mover.Name} init — {_keyframes.Length} keyframes, speed={mover.Speed}", LogCat.Vob);
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

        private void SetPose((Vector3 pos, Quaternion rot) pose) => SetPose(pose.pos, pose.rot);

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
            Toggle();
        }

        public void Toggle()
        {
            Logger.Log($"[MoverAdapter] {_mover?.Name ?? "NULL"} Toggle() on GO='{gameObject.name}' active={gameObject.activeInHierarchy} isMoving={_isMoving} isOpen={_isOpen}", LogCat.Vob);
            if (_isDisabled)
            {
                Logger.Log($"[MoverAdapter] {_mover?.Name} is disabled - trigger ignored", LogCat.Vob);
                return;
            }
            if (_isMoving) return;
            if (_isOpen) Close(); else Open();
        }

        public void Open()
        {
            if (_isMoving) return;
            if (_isOpen) return;
            Logger.Log($"[MoverAdapter] {_mover.Name} Open() — kf[{_currentKeyIndex}]→kf[{(_keyframes?.Length ?? 1) - 1}], chainTarget='{_chainTarget}'", LogCat.Vob);
            if (_keyframes != null && _keyframes.Length >= 2)
                StartCoroutine(MoveTo(_currentKeyIndex, _keyframes.Length - 1, _mover.SfxOpenStart, _mover.SfxOpenEnd, onDone: () =>
                {
                    _currentKeyIndex = _keyframes.Length - 1;
                    _isOpen = true;
                    TriggerChainedMover(opening: true);
                }));
            else
            {
                _currentKeyIndex = _keyframes != null ? _keyframes.Length - 1 : 0;
                _isOpen = true;
                TriggerChainedMover(opening: true);
            }
        }

        public void Close()
        {
            if (_isMoving) return;
            if (!_isOpen) return;
            Logger.Log($"[MoverAdapter] {_mover.Name} Close() — kf[{_currentKeyIndex}]→kf[0], chainTarget='{_chainTarget}'", LogCat.Vob);
            if (_keyframes != null && _keyframes.Length >= 2)
                StartCoroutine(MoveTo(_currentKeyIndex, 0, _mover.SfxCloseStart, _mover.SfxCloseEnd, onDone: () =>
                {
                    _currentKeyIndex = 0;
                    _isOpen = false;
                    TriggerChainedMover(opening: false);
                }));
            else
            {
                _currentKeyIndex = 0;
                _isOpen = false;
                TriggerChainedMover(opening: false);
            }
        }

        public void GoToKeyNext()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2) return;
            var from = _currentKeyIndex;
            var to = (_currentKeyIndex + 1) % _keyframes.Length;
            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyNext() [{from}]→[{to}]", LogCat.Vob);
            StartCoroutine(MoveTo(from, to, _mover.SfxOpenStart, _mover.SfxOpenEnd, onDone: () =>
            {
                _currentKeyIndex = to;
                _isOpen = to != 0;
                TriggerChainedMover(opening: _isOpen);
            }));
        }

        public void GoToKeyPrev()
        {
            if (_isMoving || _keyframes == null || _keyframes.Length < 2) return;
            var from = _currentKeyIndex;
            var to = (_currentKeyIndex - 1 + _keyframes.Length) % _keyframes.Length;
            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyPrev() [{from}]→[{to}]", LogCat.Vob);
            StartCoroutine(MoveTo(from, to, _mover.SfxCloseStart, _mover.SfxCloseEnd, onDone: () =>
            {
                _currentKeyIndex = to;
                _isOpen = to != 0;
                TriggerChainedMover(opening: _isOpen);
            }));
        }

        public void GoToKeyFixed(int key, bool direct)
        {
            if (_isMoving || _keyframes == null || _keyframes.Length == 0) return;
            key = Mathf.Clamp(key, 0, _keyframes.Length - 1);
            if (key == _currentKeyIndex) return;
            Logger.Log($"[MoverAdapter] {_mover.Name} GoToKeyFixed(key={key}, direct={direct}) from={_currentKeyIndex}", LogCat.Vob);

            if (direct)
            {
                SetPose(_keyframes[key].pos, _keyframes[key].rot);
                _currentKeyIndex = key;
                _isOpen = key != 0;
                TriggerChainedMover(opening: _isOpen);
            }
            else
            {
                var sfxStart = key > _currentKeyIndex ? _mover.SfxOpenStart : _mover.SfxCloseStart;
                var sfxEnd = key > _currentKeyIndex ? _mover.SfxOpenEnd : _mover.SfxCloseEnd;
                var targetKey = key;
                StartCoroutine(MoveTo(_currentKeyIndex, targetKey, sfxStart, sfxEnd, onDone: () =>
                {
                    _currentKeyIndex = targetKey;
                    _isOpen = targetKey != 0;
                    TriggerChainedMover(opening: _isOpen);
                }));
            }
        }

        private void TriggerChainedMover(bool opening)
        {
            if (string.IsNullOrEmpty(_chainTarget)) return;
            if (!_vobService.TryGetMovers(_chainTarget, out var containers))
            {
                Logger.LogWarning($"[MoverAdapter] {_mover.Name} → chained target '{_chainTarget}' not found", LogCat.Vob);
                return;
            }
            foreach (var container in containers)
            {
                if (container?.Go == null) continue;
                var chained = container.Go.GetComponentInChildren<MoverAdapter>();
                if (chained != null)
                    if (opening) chained.Open(); else chained.Close();
            }
        }

        private IEnumerator MoveTo(int fromIdx, int toIdx, string sfxStart, string sfxEnd, System.Action onDone)
        {
            if (_configService.Dev.EnableEngineMoverTiming)
            {
                yield return MoveThroughKeyframes(fromIdx, toIdx, sfxStart, sfxEnd, onDone);
                yield break;
            }

            _isMoving = true;
            PlaySfx(sfxStart);

            var startPos = _keyframes[fromIdx].pos;
            var startRot = _keyframes[fromIdx].rot;
            var endPos = _keyframes[toIdx].pos;
            var endRot = _keyframes[toIdx].rot;

            var dist = Vector3.Distance(startPos, endPos);
            var speed = (_mover.Speed > 0 ? _mover.Speed : 1f) * _configService.Dev.MoverSpeedMultiplier;
            var duration = dist > 0.001f ? dist / speed : 0.5f;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                t = ApplySpeedCurve(t, _mover.SpeedType);

                SetPose(Vector3.Lerp(startPos, endPos, t), Quaternion.Slerp(startRot, endRot, t));
                yield return null;
            }

            SetPose(endPos, endRot);

            PlaySfx(sfxEnd);
            _isMoving = false;
            onDone?.Invoke();
        }

        /// <summary>
        /// DeveloperConfig.EnableEngineMoverTiming: like the engine (OpenGothic MoveTrigger), through every keyframe on
        /// the way, each segment with its own time - translation: cm / speed (cm per ms), rotation only: the angle with
        /// the speed as rotations per second (very low speeds scaled up). Before, only the first and the last keyframe
        /// were used: G2 Irdorath's gate wheels make a full turn (first == last) and never moved, and a translation
        /// took meters / speed seconds (10x too slow - MoverSpeedMultiplier compensated a part, it's ignored here).
        /// </summary>
        private IEnumerator MoveThroughKeyframes(int fromIdx, int toIdx, string sfxStart, string sfxEnd,
            System.Action onDone)
        {
            _isMoving = true;
            PlaySfx(sfxStart);

            var count = _keyframes.Length;
            // Neighbours (GoToKeyNext/Prev, also wrapping last <-> 0) move one segment, otherwise step towards toIdx.
            var step = toIdx == (fromIdx + 1) % count ? 1 : toIdx == (fromIdx - 1 + count) % count ? -1 :
                toIdx > fromIdx ? 1 : -1;
            var current = fromIdx;
            while (current != toIdx)
            {
                var next = (current + step + count) % count;
                var segment = step > 0 ? current : next;
                var seconds = GetSegmentSeconds(segment);
                var (startPos, startRot) = _keyframes[current];
                var (endPos, endRot) = _keyframes[next];

                var elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / seconds);
                    t = GetSegmentCurve(t, current == fromIdx, next == toIdx);

                    SetPose(Vector3.Lerp(startPos, endPos, t), Quaternion.Slerp(startRot, endRot, t));
                    yield return null;
                }

                SetPose(endPos, endRot);
                current = next;
            }

            PlaySfx(sfxEnd);
            _isMoving = false;
            onDone?.Invoke();
        }

        /// <summary>
        /// Like the engine: Slow* ease the start/end of the whole way (first/last segment), Segment* every segment.
        /// </summary>
        private float GetSegmentCurve(float t, bool isFirst, bool isLast)
        {
            var easeStart = _mover.SpeedType is MoverSpeedType.SegmentSlowStart or MoverSpeedType.SegmentSlowStartEnd ||
                            (isFirst && _mover.SpeedType is MoverSpeedType.SlowStart or MoverSpeedType.SlowStartEnd);
            var easeEnd = _mover.SpeedType is MoverSpeedType.SegmentSlowEnd or MoverSpeedType.SegmentSlowStartEnd ||
                          (isLast && _mover.SpeedType is MoverSpeedType.SlowEnd or MoverSpeedType.SlowStartEnd);
            if (easeStart && easeEnd)
                return ApplySpeedCurve(t, MoverSpeedType.SlowStartEnd);
            if (easeStart)
                return ApplySpeedCurve(t, MoverSpeedType.SlowStart);
            return easeEnd ? ApplySpeedCurve(t, MoverSpeedType.SlowEnd) : t;
        }

        /// <summary>
        /// Seconds from keyframe i to i+1 (OpenGothic MoveTrigger: ticks in ms).
        /// </summary>
        private float GetSegmentSeconds(int index)
        {
            var speed = _mover.Speed;
            if (speed <= 0f)
                return 0.5f;

            var (pos0, rot0) = _keyframes[index];
            var (pos1, rot1) = _keyframes[(index + 1) % _keyframes.Length];
            const float rotationThreshold = 0.01f;
            var lengthCm = Vector3.Distance(pos0, pos1) * 100f;

            float milliseconds;
            if (lengthCm > rotationThreshold)
            {
                milliseconds = lengthCm / speed;
            }
            else
            {
                var dot = Quaternion.Dot(rot0, rot1);
                var theta = 2f * dot * dot - 1f;
                var angle = Mathf.Acos(Mathf.Clamp(theta, -1f, 1f)) * Mathf.Rad2Deg;
                milliseconds = angle / 360f * 1000f / ScaleRotationSpeed(speed);
            }

            return Mathf.Max(1f, milliseconds) / 1000f;
        }

        /// <summary>
        /// OpenGothic's measurement: rotation speed is rotations per second, very low values are scaled up
        /// (EVT_RIGHT_WHEEL_01: speed 0.00068, one turn in 5 s).
        /// </summary>
        private static float ScaleRotationSpeed(float speed)
        {
            const float scaleFactor = 300f;
            const float lowSpeed = 0.006f;
            return speed < lowSpeed ? scaleFactor * speed : speed;
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
