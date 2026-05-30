using GUZ.Core.Adapters.Npc;
using UnityEngine;

namespace GUZ.Core.Animations
{
    public class NpcHeadAnimationHandler : BasePlayerBehaviour
    {
        private bool _doLookAtNpc;
        private Transform _destTransform;
        // private Quaternion _initialHeadRotation;
        private Quaternion _prevHeadRotation;

        private const float _headLookDegreeMax = 80f;
        private const float _headLookRotateSpeed = 200f;

        protected override void Awake()
        {
            base.Awake();

            // Cached object which will be used later.
            NpcData.PrefabProps.AnimationHeadHandler = this;
        }

        public void StartLookAt(Transform destinationTransform)
        {
            _doLookAtNpc = true;
            _destTransform = destinationTransform;
            // _initialHeadRotation = PrefabProps.Head.rotation;
            _prevHeadRotation = PrefabProps.Head.rotation;
        }

        /// <summary>
        /// Head will immediately being managed by animation again.
        /// </summary>
        public void StopLookAt()
        {
            _doLookAtNpc = false;
        }

        /// <summary>
        /// Each NPC has an idle animation which rotates the head in a forward looking state. We need to use LateUpdate() to overwrite this rotation of the head.
        /// There are three options:
        /// 1. the LookAt feature is not active - do nothing
        /// 2. The other NPC is in visible range - RotateTowards()
        /// 3. The other NPC is not in visible range - rotate back to original head rotation
        /// </summary>
        private void LateUpdate()
        {
            // FIXME - Currently disabled as the Troll is doing a really awkward head rotation! Fix it first.
            return;
            
            // 1.
            if (!_doLookAtNpc)
            {
                return;
            }

            var lookRotationVector = _destTransform.position - PrefabProps.Head.position;
            var desiredRotation = Quaternion.LookRotation(lookRotationVector);

            // Get the parent (neck) rotation
            var parentRotation = PrefabProps.Head.parent.rotation;

            // Calculate the relative rotation between desired look direction and parent
            // i.e. we want to have the head rotating ~80 degrees based on central position on the neck only.
            var relativeRotation = Quaternion.Inverse(parentRotation) * desiredRotation;
            var relativeEuler = relativeRotation.eulerAngles;

            // Convert angles to -180 to 180 range
            var horizontalAngle = relativeEuler.y;
            if (horizontalAngle > 180f)
            {
                horizontalAngle -= 360f;
            }

            var verticalAngle = relativeEuler.x;
            if (verticalAngle > 180f)
            {
                verticalAngle -= 360f;
            }

            if (Mathf.Abs(horizontalAngle) <= _headLookDegreeMax && Mathf.Abs(verticalAngle) <= _headLookDegreeMax)
            {
                // 2.
                RotateTowardsOther(verticalAngle, horizontalAngle, parentRotation);
            }
            else
            {
                // 3.
                RotateBackToOriginal();
            }

            _prevHeadRotation = PrefabProps.Head.rotation;
        }

        private void RotateTowardsOther(float verticalAngle, float horizontalAngle, Quaternion parentRotation)
        {
            // Create clamped local rotation
            var localRotation = Quaternion.Euler(verticalAngle, horizontalAngle, 0);

            // Convert back to world space
            var worldRotation = parentRotation * localRotation;

            var currentHeadZ = PrefabProps.Head.rotation.eulerAngles.z;
            var finalRotation = Quaternion.Euler(
                worldRotation.eulerAngles.x,
                worldRotation.eulerAngles.y,
                currentHeadZ
            );

            PrefabProps.Head.rotation = Quaternion.RotateTowards(
                _prevHeadRotation,
                finalRotation,
                Time.deltaTime * _headLookRotateSpeed
            );
        }

        /// <summary>
        /// With the new AnimationSystem, head has normally an animation being set.
        /// It means we simply do not update the head rotation here at LateUpdate() and the head will turn back to its original position immediately.
        /// </summary>
        private void RotateBackToOriginal()
        {
            return;

            // HINT: Old logic returning to original position, but e.g. Orry would then rotate his body back and his "original" head position is now wrong.
            // PrefabProps.Head.rotation = Quaternion.RotateTowards(
            //     _prevHeadRotation,
            //     _initialHeadRotation,
            //     Time.deltaTime * _headLookRotateSpeed
            // );
        }
    }
}
