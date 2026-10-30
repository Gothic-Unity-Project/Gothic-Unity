using System;
using System.Collections.Generic;
using Gothic.VR.Interaction.Models;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// The VR player body: tracked head/controllers, character controller, locomotion and screen fade.
    /// Implemented by the active VR backend, which lives on the player prefab root.
    /// </summary>
    public interface IVrRig
    {
        /// <summary>
        /// Root of the player. Moved by locomotion and teleports, rotated by thumbstick turning.
        /// </summary>
        Transform Root { get; }

        /// <summary>
        /// Origin of the tracked devices. Controller velocities (IVrInput) are relative to it.
        /// </summary>
        Transform TrackingSpace { get; }

        Transform Head { get; }

        /// <summary>
        /// Tracked controller pose (not the physics hand, which can be blocked by colliders).
        /// </summary>
        Transform LeftHand { get; }

        /// <summary>
        /// Tracked controller pose (not the physics hand, which can be blocked by colliders).
        /// </summary>
        Transform RightHand { get; }

        Camera Camera { get; }
        CharacterController CharacterController { get; }
        float CameraHeight { get; }
        bool IsGrounded { get; }
        bool IsClimbing { get; }

        bool MovementEnabled { get; set; }
        bool TurnEnabled { get; set; }
        bool TeleportEnabled { get; set; }
        bool CanJump { get; set; }
        bool CanCrouch { get; set; }
        bool CanSprint { get; set; }
        float Gravity { get; set; }
        float MaxFallSpeed { get; set; }
        float MoveSpeed { get; set; }
        float RunSpeed { get; set; }
        float StepOffset { get; set; }

        /// <summary>
        /// Current screen fade. 0 = clear, 1 = black.
        /// </summary>
        float FadeAlpha { get; }

        /// <summary>
        /// Direct (Hand) and distance grabbers of both hands.
        /// </summary>
        IReadOnlyList<IVrGrabber> HandGrabbers { get; }

        /// <summary>
        /// Raised with the new feet position whenever the player is teleported.
        /// </summary>
        event Action<Vector3> Teleported;

        void ApplyComfortSettings(VrSitStand sitStand, VrMoveDirection direction, VrTurnType turn, float snapDegrees,
            float smoothSpeed);

        void Teleport(Vector3 position, Quaternion rotation);

        /// <summary>
        /// Grabbing and hovering of both hands (direct and distance). Disabled hands are fully opened.
        /// </summary>
        void SetGrabbingEnabled(bool isEnabled);

        /// <summary>
        /// false after SetGrabbingEnabled(false) (e.g. swimming). The block hands open meanwhile.
        /// Default: true, for backends whose hand visuals open by themselves (HVR).
        /// </summary>
        bool IsGrabbingEnabled(VrHandSide side)
        {
            return true;
        }

        /// <summary>
        /// Fades the screen towards alpha (0 = clear, 1 = black) with speed alpha per second.
        /// </summary>
        void Fade(float alpha, float speed);

        /// <summary>
        /// Camera height above the feet once standing: CameraHeight without a crouch of the backend.
        /// Default: CameraHeight, for backends without crouching (HVR).
        /// </summary>
        float StandingCameraHeight => CameraHeight;

        /// <summary>
        /// Asked by jumps and teleports near ledges (ledge climbing). null: normal jumps and teleports only.
        /// Default: ignored, for backends without (HVR).
        /// </summary>
        void SetLedgeHandler(IVrLedgeHandler handler)
        {
        }
    }
}
