using System;
using System.Collections.Generic;
using Gothic.VR.Interaction.Components;
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

        /// <summary>
        /// The player crouches (crouch button or the head held low). Crouched = sneaking for the scripts.
        /// </summary>
        bool IsCrouching { get; }

        /// <summary>
        /// Extra camera height on top of the tracked one (m). Lowered while the hero lies on the ground, changed to a
        /// monster's eye height while transformed. Save it before changing it and set it back afterwards.
        /// </summary>
        float CameraYOffset { get; set; }

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
        /// The hand as the player sees it (HVR: the physics hand's model, XRI: the block hand). Unlike LeftHand/RightHand
        /// it stops at walls. Gestures, the favorites ring and spells measure from it.
        /// </summary>
        Transform GetHandVisual(VrHandSide side);

        /// <summary>
        /// The solid (non-trigger) colliders of a hand - not the triggers it finds grabbables with.
        /// </summary>
        IReadOnlyList<Collider> GetHandColliders(VrHandSide side);

        /// <summary>
        /// Range in m of a hand's distance grab (force grab, the far ray). Telekinesis and spell targeting extend it.
        /// </summary>
        float GetDistanceGrabRange(VrHandSide side);

        /// <summary>
        /// Extends (or shortens) a hand's distance grab. ResetDistanceGrabRange goes back to the backend's range.
        /// </summary>
        void SetDistanceGrabRange(VrHandSide side, float range);

        void ResetDistanceGrabRange(VrHandSide side);

        /// <summary>
        /// What the hand's distance grab points at (item, mob, NPC), null if nothing.
        /// </summary>
        VrInteractable GetDistanceHovered(VrHandSide side);

        /// <summary>
        /// Pulls the item the hand's distance grab points at into the hand (telekinesis). false if there is none.
        /// </summary>
        bool TryDistanceGrabHovered(VrHandSide side);

        /// <summary>
        /// Fades the screen towards alpha (0 = clear, 1 = black) with speed alpha per second.
        /// </summary>
        void Fade(float alpha, float speed);
    }
}
