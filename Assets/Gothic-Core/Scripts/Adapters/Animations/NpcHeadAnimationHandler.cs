using Gothic.Core.Adapters.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using UnityEngine;

namespace Gothic.Core.Animations
{
    /// <summary>
    /// Turns the NPC's head towards a target on top of the animated pose.
    /// Two sources: Daedalus (AI_LookAt/AI_LookAtNpc/AI_StopLookAt) and the dialog itself. The dialog only fills in
    /// when Daedalus isn't looking at something already, and stopping it never ends what Daedalus started.
    /// </summary>
    public class NpcHeadAnimationHandler : BasePlayerBehaviour
    {
        private Transform _scriptTarget;
        private Transform _dialogTarget;
        // Current head offset against the body's forward direction, eased towards the target angles.
        private float _yaw;
        private float _pitch;

        private const float _headLookYawMax = 80f;
        private const float _headLookPitchMax = 30f;
        private const float _headLookRotateSpeed = 120f;
        // DeveloperConfig.EnableDialogBodyTurn: degrees per second, and the angle at which the body stops turning.
        private const float _bodyTurnSpeed = 180f;
        private const float _bodyTurnStopAngle = 10f;
        private bool _isTurningBody;
        private string _bodyTurnAnimation;
        private string _blendingOutTurnAnimation;
        // The head's pose relative to the body without the turn animation.
        private Quaternion _headRestRelative;
        private bool _hasHeadRest;
        private ConfigService _configService;
        private AnimationService _animationService;

        protected override void Awake()
        {
            base.Awake();

            // Cached object which will be used later.
            NpcData.PrefabProps.AnimationHeadHandler = this;
        }

        /// <summary>
        /// Daedalus: AI_LookAt / AI_LookAtNpc.
        /// </summary>
        public void StartLookAt(Transform destinationTransform)
        {
            _scriptTarget = destinationTransform;
        }

        /// <summary>
        /// Daedalus: AI_StopLookAt. The head eases back into the animation.
        /// </summary>
        public void StopLookAt()
        {
            _scriptTarget = null;
        }

        public void StartDialogLookAt(Transform destinationTransform)
        {
            _dialogTarget = destinationTransform;
        }

        public void StopDialogLookAt()
        {
            _dialogTarget = null;
            StopBodyTurn();
        }

        /// <summary>
        /// The Animator poses the head after Update(), so the offset is added in LateUpdate().
        /// Only active during dialogs for now: Daedalus' look-ats outside of them would also turn monster heads
        /// (the Troll did an awkward head rotation).
        /// Outside the yaw range (e.g. hero behind the NPC) or without a target, the head eases back to the animation.
        /// </summary>
        private void LateUpdate()
        {
            if (PrefabProps.Head == null)
                return;

            var target = _dialogTarget == null ? null : _scriptTarget != null ? _scriptTarget : _dialogTarget;

            if (_dialogTarget != null)
                TurnBodyToDialogPartner(_dialogTarget.position);

            // The turn animation swings the head to the side - while it plays the head keeps its pose from before
            // (relative to the body), the look-at below still follows the hero.
            // Also while it blends out after the turn - the neck swung to the side for a moment then.
            if (_blendingOutTurnAnimation != null &&
                !PrefabProps.AnimationSystem.IsPlaying(_blendingOutTurnAnimation))
                _blendingOutTurnAnimation = null;
            var headRelative = Quaternion.Inverse(Go.transform.rotation) * PrefabProps.Head.rotation;
            if (_bodyTurnAnimation == null && _blendingOutTurnAnimation == null)
            {
                _headRestRelative = headRelative;
                _hasHeadRest = true;
            }
            else if (_hasHeadRest)
            {
                PrefabProps.Head.rotation = Go.transform.rotation * _headRestRelative;
            }

            var targetYaw = 0f;
            var targetPitch = 0f;
            if (target != null)
                GetLookAngles(target.position, out targetYaw, out targetPitch);

            var step = _headLookRotateSpeed * Time.deltaTime;
            _yaw = Mathf.MoveTowards(_yaw, targetYaw, step);
            _pitch = Mathf.MoveTowards(_pitch, targetPitch, step);

            if (_yaw == 0f && _pitch == 0f)
                return;

            // Rotating around the body's axes instead of the head bone's keeps it independent of how the bone is
            // oriented in the model.
            var body = Go.transform;
            var offset = Quaternion.AngleAxis(_yaw, Vector3.up) * Quaternion.AngleAxis(_pitch, body.right);
            PrefabProps.Head.rotation = offset * PrefabProps.Head.rotation;
        }

        /// <summary>
        /// DeveloperConfig.EnableDialogBodyTurn: the hero walks behind his dialog partner - beyond what the neck can
        /// turn (_headLookYawMax) the NPC turns his body towards him until he's nearly in front again
        /// (_bodyTurnStopAngle). Within the neck's range only the head follows. Not while sitting / using a mob.
        /// </summary>
        private void TurnBodyToDialogPartner(Vector3 targetPosition)
        {
            _configService ??= ReflexProjectInstaller.DIContainer.Resolve<ConfigService>();
            if (!_configService.Dev.EnableDialogBodyTurn || NpcData.IsUsingMob())
            {
                StopBodyTurn();
                return;
            }

            var body = Go.transform;
            var flatDirection = targetPosition - body.position;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude < 0.0001f)
                return;

            var angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(body.forward, Vector3.up), flatDirection,
                Vector3.up);
            if (!_isTurningBody && Mathf.Abs(angle) > _headLookYawMax)
                _isTurningBody = true;
            else if (_isTurningBody && Mathf.Abs(angle) <= _bodyTurnStopAngle)
                StopBodyTurn();
            if (!_isTurningBody)
                return;

            // The feet step with it: the engine's turn animation (t_RunTurnL/R ...), like AI_TurnToNpc.
            _animationService ??= ReflexProjectInstaller.DIContainer.Resolve<AnimationService>();
            var turnAnimation = _animationService.GetAnimationName(
                angle < 0f ? VmGothicEnums.AnimationType.RotL : VmGothicEnums.AnimationType.RotR, NpcData);
            if (turnAnimation != _bodyTurnAnimation)
            {
                if (_bodyTurnAnimation != null)
                    PrefabProps.AnimationSystem.StopAnimation(_bodyTurnAnimation);
                _bodyTurnAnimation = turnAnimation;
                if (_bodyTurnAnimation != null)
                    PrefabProps.AnimationSystem.PlayAnimation(_bodyTurnAnimation);
            }

            body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(flatDirection),
                _bodyTurnSpeed * Time.deltaTime);
        }

        private void StopBodyTurn()
        {
            _isTurningBody = false;
            if (_bodyTurnAnimation == null)
                return;
            PrefabProps.AnimationSystem.StopAnimation(_bodyTurnAnimation);
            _blendingOutTurnAnimation = _bodyTurnAnimation;
            _bodyTurnAnimation = null;
        }

        private void GetLookAngles(Vector3 targetPosition, out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;

            var direction = targetPosition - PrefabProps.Head.position;
            var flatDirection = new Vector3(direction.x, 0f, direction.z);
            if (flatDirection.sqrMagnitude < 0.0001f)
                return;

            var bodyForward = Go.transform.forward;
            bodyForward.y = 0f;
            var angle = Vector3.SignedAngle(bodyForward, flatDirection, Vector3.up);
            if (Mathf.Abs(angle) > _headLookYawMax)
                return;

            yaw = angle;
            // Positive pitch around the body's right axis looks down.
            pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, flatDirection.magnitude) * Mathf.Rad2Deg,
                -_headLookPitchMax, _headLookPitchMax);
        }
    }
}
