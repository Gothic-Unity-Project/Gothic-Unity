# 01 - HurricaneVR capability catalog

Source: `Assets/HurricaneVR/` (HVR **2.9.3**, `Framework/Editor/HVREditorManager.cs:12`).
294 C# files, ~43k LoC in `Framework/`, plus a `TechDemo/` with demo scripts, prefabs, art and SFX.
License: Unity Asset Store EULA (paid, per-seat, no redistribution) - the reason contributors can't build the VR target.

Legend for the "Gothic" column (details in [02-gothic-hvr-usage.md](02-gothic-hvr-usage.md)):
**✅ used** · **◐ used indirectly** (part of the rig/prefab we inherit, not touched by our code) · **– unused**

## High-level capabilities

| # | Capability | What HVR provides | Gothic |
|---|------------|-------------------|--------|
| H1 | **Physics hands** | Hands are rigidbodies driven by `ConfigurableJoint`s towards the tracked controller (`HVRJointHand`, `HVRHandPhysics`, `HVRHandStrengthHandler`, `PDStrength`). Hands collide with the world, can't pass through walls, have strength limits; `HVRGhostHand` shows the tracked pose when the physics hand lags. | ✅ |
| H2 | **Physics grabbing** | Grabbed objects stay dynamic rigidbodies, joined to the hand (`HVRGrabTracking`: ConfigurableJoint / FixedJoint / None). Weight, two-hand strength (`HVRTwoHandStrength`), collisions while held, throwing with velocity history (`HVRThrowingCenterOfMass`). | ✅ (weapon velocity drives combat) |
| H3 | **Procedural hand posing** | `HVRPhysicsPoser` closes each finger by sweeping until it touches the collider - works on **any** mesh without authored poses. Fallback when no authored pose exists (`PhysicsPoserFallback`). | ✅ (all Gothic items use `PoseType: PhysicPoser`) |
| H4 | **Authored hand posing** | `HVRHandPoser`, `HVRHandPose(Data)`, `HVRPosableHand/Finger/Bone`, blend poses, pose masks, mirroring, in-editor pose editor + recorder, grab points (`HVRGrabPoints`, `HVRPosableGrabPoint`), pose zones. | ✅ (backpack only) |
| H5 | **Finger animation from input** | `HVRHandAnimator` + `HVRFingerSettings` map grip/trigger/capacitive touches to finger curls; on Index (Knuckles) under OpenXR, `HVROpenXRFingerCurls` reads `XRHandSubsystem` for middle/ring/pinky. | ✅ (Index/Steam Frame finger tracking) |
| H6 | **Distance / force grab** | `HVRForceGrabber` (pull or gravity-gun style), `HVRForceGrabberLaser`, `HVRForcePullSettings`, line-of-sight checks, force-grab indicators. | ✅ |
| H7 | **Sockets (inventory/holsters)** | `HVRSocket`, `HVRSocketContainer`, `HVRSocketable`, filters (tag/string/enum/grabbable), hover feedback (scale/material), socket links, shoulder socket, socket bags, `HVRSocketContainerGrabber` (fill a box by dropping items into it). | ✅ heavy |
| H8 | **Physics mechanics** | `HVRPhysicsDoor` (hinge + lock + open/close SFX), `HVRPhysicsDrawer`, `HVRPhysicsLever`, `HVRPhysicsDial`, `HVRPhysicsButton`, `HVRRotationTracker`, `HVRRotationLimiter`, `HVRRotationSFX`. | ✅ doors + chest lids |
| H9 | **Climbing** | `HVRClimbable` - grab and pull the player rig. | ✅ ladders |
| H10 | **Player controller** | `HVRPlayerController` on a `CharacterController`: smooth move (camera/hand direction), snap/smooth turn, sprint, jump, crouch, gravity, fall speed, `HVRCameraRig` sit/stand/height calibration, `HVRHeadCollision` (fade when head clips walls), `HVRPlayerWaist`. | ✅ heavy |
| H11 | **Teleport** | `HVRTeleporter` (arc, validation, collision handler, marker, `PositionUpdate` event). | ✅ |
| H12 | **Input abstraction** | `HVRInputManager` auto-detects SDK (XRInput / InputSystem / Oculus OVR / SteamVR) and controller type (Touch, Knuckles, Vive, WMR, Cosmos…), per-controller `HVRInputSettings` maps, deadzones; `HVRController.GetButtonState(side, button)` with `JustActivated/Active/JustDeactivated`; `HVRPlayerInputs` (overridable). | ✅ heavy |
| H13 | **Haptics** | `HVRController.Vibrate(HapticData)`, grab haptics, impact haptics (hand/object), gun haptics. | ✅ |
| H14 | **World-space UI** | `HVRInputModule` + `HVRUIPointer` (laser per hand) + `HVRGraphicRaycaster`; canvases must be registered. | ✅ |
| H15 | **Fades** | `HVRScreenFade`, `HVRCanvasFade`, `HVRGlobalFadeFinder`. | ✅ |
| H16 | **Stabbing** | `HVRStabber/Stabbable` (knife penetration). | – |
| H17 | **Weapons** | Guns, magazines, bolts, shotgun, recoil, bow & arrow (`HVRPhysicsBow`), ballistic solver. | – |
| H18 | **Damage helpers** | `HVRDamageProvider/Handler`, `HVRDestructible`. | – |
| H19 | **Highlights** | `HVRMeshRendererHighlight`, glow, ring, scale highlight (hover feedback). | ◐ (part of hand prefab; we do our own `VRFocus`) |
| H20 | **Platform integrations** | SteamVR/OpenVR, Oculus OVR (incl. Oculus hand animator + gesture detector), FinalIK, HexaBody, EmeraldAI packages. | – (we are OpenXR-only) |
| H21 | **Editor tooling** | Setup window, layer setup, gun creator, lever creator, pose editor, socketable tag editor. | ◐ |
| H22 | **Device simulator** | Not in HVR itself - we use the `com.hurricanevrextensions.simulator` package (`HVRBodySimulator`, `HVRHandsSimulator`) which **depends on HVR**. | ✅ |

## Low-level building blocks

| Area | Classes | Notes |
|------|---------|-------|
| Grab core | `HVRGrabberBase`, `HVRHandGrabber` (3.1k LoC), `HVRGrabbable`, `HVRGrabbableChild`, `HVRGrabArgs`, `HVRGrabberManager`, `HVRGrabbableBag/TriggerGrabbableBag/SocketBag/ForceGrabberBag` | Hover/grab/release lifecycle with UnityEvents. Grabbable: `Activated/Deactivated`, `HoverEnter/Exit`, `Grabbed`, `Released`, `Collided`, `Destroyed`, `Socketed/UnSocketed`, `DistanceGrabbed/Released`. Grabber (hand/socket/force): `BeforeGrabbed`, `Grabbed`, `Released`, `BeforeHoverEnter`, `HoverEnter/Exit`. Bags = trigger-volume candidate collectors. |
| Grab options | `HVRHoldType` (OneHand/Swap/TwoHanded/ManyHands), `PoseType` (HandPoser/PhysicPoser/Offset), `HVRGrabTracking`, `HVRGrabControl`, `HVRGrabTrigger` (active/toggle/manual release), `RequireLineOfSight`, `ParentHandModel`, `Stationary`, `LinkedGrabbables`, `RequireOtherGrabbable`. | |
| Joints | `HVRJointSettings` (ScriptableObject), `HVRJointUtilities`, `ConfigurableJointExtensions`, `HVRRigidBodyOverrides`, `HVRRigidbodyLocker` | Tuned joint drives - the "feel" of HVR. |
| Collision | `HVRObjectCollisionDisabler`, `HVRHandColliderController`, layers `Player/DynamicPose/Grabbable/Hand` | Our `Constants.cs:113-117` layers are HVR-suggested. |
| Input | `HVRController` (XR `InputDevice` based), `HVRInputSystemController`, `HVRXRInputController`, `HVRTrackedController`, `HVRControllerOffset(s)` (per-device pose offsets), `HVRGlobalInputs`, `HVRControllerEvents` + 17 `*ButtonEvents` components | |
| Managers | `HVRManager`, `HVRInputManager`, `HVRTimeManager`, `HVRSettings` (global ScriptableObject), `HVRDontDestroy`, `SFXPlayer` | Live in `HVRGlobal.prefab`. |
| Pose data | `HVRHandPoseData`, `HVRPosableBoneData`, `HVRPosableFingerData`, `HVRRuntimePosesData`, `HVRHandMirrorSettings` | |

## Tech stack facts

- Input: legacy `UnityEngine.XR.InputDevice` *and* Input System (`HVR_INPUT_SYSTEM`); we run with `USE_INPUT_SYSTEM_POSE_CONTROL`.
- Tracking backends via asmdef versionDefines: OpenXR, OpenVR, Oculus, XR Hands (`USING_XRHANDS`), XR Management.
- Rendering: shipped Built-in materials → we run `Gothic > Build > HVR - Convert Materials to URP` (`Gothic-VR/Editor/HurricaneActions.cs`).
- Physics-first design: everything assumes `FixedUpdate`-driven rigidbodies and joints.
- Extension model: inheritance (`protected virtual`), UnityEvents in the inspector.
