using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// XRI's CharacterControllerBodyManipulator with a minimum capsule height. XRI sizes the capsule from the camera
    /// height only. A headset that isn't worn yet can report a camera below the floor: the capsule then ends below
    /// the feet and the player falls through the ground, e.g. the main menu floor right after Play.
    /// The capsule bottom always stays at the feet.
    /// </summary>
    public class XriBodyManipulator : CharacterControllerBodyManipulator
    {
        [Tooltip("Lowest capsule height in m. Below it, the HMD isn't on a head (not worn, not tracked yet).")]
        [SerializeField] private float _minHeight = 0.5f;

        public override CollisionFlags MoveBody(Vector3 motion)
        {
            if (linkedBody == null || characterController == null)
                return CollisionFlags.None;

            var bodyGroundPosition = linkedBody.GetBodyGroundLocalPosition();
            var capsuleHeight = Mathf.Max(linkedBody.xrOrigin.CameraInOriginSpaceHeight - bodyGroundPosition.y,
                _minHeight, characterController.radius * 2f);
            characterController.height = capsuleHeight;
            characterController.center = new Vector3(bodyGroundPosition.x,
                bodyGroundPosition.y + capsuleHeight * 0.5f + characterController.skinWidth, bodyGroundPosition.z);

            // Same as XRI: a disabled controller can't Move(), the origin is translated directly.
            if (characterController.enabled)
                return characterController.Move(motion);

            linkedBody.originTransform.position += motion;
            return CollisionFlags.None;
        }
    }
}
