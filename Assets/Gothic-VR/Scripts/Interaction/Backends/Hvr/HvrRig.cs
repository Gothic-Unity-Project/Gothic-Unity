#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Player;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// IVrRig on top of HVR's player controller. Gothic logic (menu, control modes, ini settings) lives in VRPlayer
    /// and VRContextInteractionService, not here.
    /// </summary>
    public class HvrRig : HVRPlayerController, IVrRig
    {
        private Camera _cameraComponent;
        private List<IVrGrabber> _handGrabbers;

        public event Action<Vector3> Teleported;

        Transform IVrRig.Root => transform;
        Transform IVrRig.TrackingSpace => CameraRig.transform;
        Transform IVrRig.Head => Camera;
        Transform IVrRig.LeftHand => LeftControllerTransform;
        Transform IVrRig.RightHand => RightControllerTransform;
        Camera IVrRig.Camera => _cameraComponent ??= Camera.GetComponent<Camera>();
        CharacterController IVrRig.CharacterController => CharacterController;
        float IVrRig.CameraHeight => CameraHeight;
        bool IVrRig.IsGrounded => IsGrounded;
        bool IVrRig.IsClimbing => IsClimbing;

        bool IVrRig.MovementEnabled
        {
            get => MovementEnabled;
            set => MovementEnabled = value;
        }

        bool IVrRig.TurnEnabled
        {
            get => RotationEnabled;
            set => RotationEnabled = value;
        }

        bool IVrRig.TeleportEnabled
        {
            get => Teleporter.enabled;
            set => Teleporter.enabled = value;
        }

        bool IVrRig.CanJump
        {
            get => CanJump;
            set => CanJump = value;
        }

        bool IVrRig.CanCrouch
        {
            get => CanCrouch;
            set => CanCrouch = value;
        }

        bool IVrRig.CanSprint
        {
            get => CanSprint;
            set => CanSprint = value;
        }

        float IVrRig.Gravity
        {
            get => Gravity;
            set => Gravity = value;
        }

        float IVrRig.MaxFallSpeed
        {
            get => MaxFallSpeed;
            set => MaxFallSpeed = value;
        }

        float IVrRig.MoveSpeed
        {
            get => MoveSpeed;
            set => MoveSpeed = value;
        }

        float IVrRig.RunSpeed
        {
            get => RunSpeed;
            set => RunSpeed = value;
        }

        float IVrRig.StepOffset
        {
            get => CharacterController.stepOffset;
            set => CharacterController.stepOffset = value;
        }

        float IVrRig.FadeAlpha => ScreenFader ? ScreenFader.CurrentFade : 0f;

        IReadOnlyList<IVrGrabber> IVrRig.HandGrabbers
        {
            get
            {
                if (_handGrabbers != null)
                    return _handGrabbers;

                _handGrabbers = new List<IVrGrabber>();
                AddGrabber(LeftHand);
                AddGrabber(LeftHand ? LeftHand.ForceGrabber : null);
                AddGrabber(RightHand);
                AddGrabber(RightHand ? RightHand.ForceGrabber : null);
                return _handGrabbers;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            if (Teleporter)
                Teleporter.PositionUpdate.AddListener(OnTeleporterPositionUpdate);
        }

        private void OnTeleporterPositionUpdate(Vector3 feetPosition)
        {
            Teleported?.Invoke(feetPosition);
        }

        public void ApplyComfortSettings(VrSitStand sitStand, VrMoveDirection direction, VrTurnType turn,
            float snapDegrees, float smoothSpeed)
        {
            CameraRig.SetSitStandMode(sitStand.ToHvr());
            DirectionStyle = direction.ToHvr();
            RotationType = turn.ToHvr();
            SnapAmount = snapDegrees;
            SmoothTurnSpeed = smoothSpeed;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Teleporter.Teleport(position);

            // Changing the rotation inside HVRTeleporter didn't work for y-axis. Therefore, setting it now.
            transform.rotation = rotation;
        }

        public void Fade(float alpha, float speed)
        {
            if (ScreenFader)
                ScreenFader.Fade(alpha, speed);
        }

        public void SetGrabbingEnabled(bool isEnabled)
        {
            SetGrabbingEnabled(LeftHand, isEnabled);
            SetGrabbingEnabled(RightHand, isEnabled);
        }

        private void AddGrabber(HVRGrabberBase grabber)
        {
            if (grabber == null)
                return;

            if (grabber.TryGetComponent(out IVrGrabber bridge))
                _handGrabbers.Add(bridge);
            else
                Logger.LogError($"[HVR] {grabber.name} has no HvrGrabberBridge. Its grab events are lost.", LogCat.VR);
        }

        private static void SetGrabbingEnabled(HVRHandGrabber hand, bool isEnabled)
        {
            hand.AllowGrabbing = isEnabled;
            hand.ForceGrabber.AllowGrabbing = isEnabled;
            hand.AllowHovering = isEnabled;
            hand.ForceGrabber.AllowHovering = isEnabled;

            // Disable hand animations. Basically open the hand fully if disabled.
            if (!hand.HandAnimator || !hand.HandAnimator.CurrentPoser)
                return;

            var poser = hand.HandAnimator.CurrentPoser;
            if (poser.PrimaryPose != null)
                poser.PrimaryPose.Disabled = !isEnabled;

            poser.Blends?.ForEach(i => i.Disabled = !isEnabled);
        }
    }
}
#endif
