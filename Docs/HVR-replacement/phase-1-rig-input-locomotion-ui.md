# Phase 1 - XRI rig, input, locomotion, UI, simulator

**Goal:** a second player rig built on XR Interaction Toolkit that implements the Phase 0 ports `IVrRig`,
`IVrInput`, `IVrHaptics` and `IVrUiPointers`. A contributor without HVR can boot into the world, walk, swim, use
menus and dialogs, and watch videos, on a headset or in the simulator. Grabbing is not part of this phase.

**Size:** L (2-3 weeks) · **Depends on:** Phase 0 · **Unblocks:** Phases 2-4 (parallel)

## Exit criteria

- [ ] `DeveloperConfig.VrInteractionBackend = Xri` boots Quest, Pico and PCVR builds into the world without HVR
      installed. (Editor play mode boots into the world on Xri; the project compiles without `GOTHIC_HVR_INSTALLED`.
      Builds: first run of the CI job `buildWithoutHVR`, then a headset.)
- [ ] Every comfort setting from the accessibility menu works on the XRI rig, and existing Gothic.ini values load.
      (Implemented in 1b; snap turn and move speeds checked in the simulator. Needs headset playtests.)
- [ ] Menus, dialogs, virtual keyboard, cinema, subtitles, print-screen messages and the spectator camera work.
      (Simulator: in-game menu laser click, dialog choice, video skip. Virtual keyboard, print-screen and spectator
      camera untested; needs a headset.)
- [ ] Swimming/diving behaves as on HVR (strokes, gravity, fade with air, walking out of water).
      (Simulator: swim mode, gravity, strokes; dive fade via code in 1c. Climbing out and the feel need a headset.)
- [x] The XR Interaction Simulator replaces the HVR simulator for the Xri backend. (1d)
- [ ] CI builds the Xri backend without the private HVR repo (new job or label). (1d: job `buildWithoutHVR` added,
      not run yet.)

## Packages

- [x] Add `com.unity.xr.interaction.toolkit` 3.x. Pin an exact version that is at least 2 weeks old and compatible with
  Unity 6000.3 (check the package manager "compatible" list). `com.unity.inputsystem` and `com.unity.xr.core-utils`
  come in as dependencies. (1a: **3.6.1**, latest stable in the compatible list, published 2026-09-17. Unity's
  "recommended" for 6000.3 is still 3.3.2. Input System 1.19 and core-utils were already installed. XRI created its
  settings assets in `Assets/XRI/Settings/`, which are committed.)
- [x] Import the samples **Starter Assets** (input actions, reference rig) and **XR Interaction Simulator**. Copy what we
  need into `Gothic-VR/` and don't reference the `Samples/` folder from our prefabs, so a sample re-import can't break
  us. (1a: only the input actions are copied so far. `Assets/Samples/` is **git-ignored**: 44 MB of demo content
  that can be re-imported from the Package Manager, and a fresh clone without it shows any accidental reference as
  missing. Import both samples locally (Package Manager > XR Interaction Toolkit > Samples) before copying more,
  e.g. the reference rig (1b) or the simulator prefab (1d). Copied prefabs that use sample scripts need those
  scripts copied too.)
- Keep OpenXR 1.16, the Meta Quest feature, `pico-openxr-sdk` and XR Hands 1.7 as they are. XRI uses exactly these.

## Backend switch

- [x] Add `DeveloperConfig.VrInteractionBackend { Hvr, Xri }`: the C# field **and** the value in the DeveloperConfig
  asset (see the `feature-checklist` skill). Default `Hvr` while HVR is installed, `Xri` otherwise.
  (1a: enum `Gothic.Core.Models.Context.VrInteractionBackend` (`Hvr = 0`, `Xri = 1`); `Production.asset` and
  `Lab.HVR.Simulator.asset` set to `Hvr`. "Xri otherwise" is a runtime fallback: `VRContextBootstrap.SelectBackend()`
  returns the XRI backend when `Hvr` is requested but `GOTHIC_HVR_INSTALLED` is missing, with a warning.
  `ReflexVRSceneInstaller` and `VRContextBootstrap` both call `SelectBackend(DeveloperConfig)`; it can't return
  null any more. `XriBackend` registers `XriInput`, `XriHaptics`, `XriSfxPlayer` (minimal `PlayClipAtPoint`, no
  pool) and a placeholder `XriUiPointers` that only warns until 1c. Without an `IVrRig` on the player,
  `SetupPlayerController` now fails with a clear error instead of a NullReferenceException.)
- [x] `Player.unity` (and `Lab.unity`) contain both rigs, disabled. `VRContextInteractionService.SetupPlayerController`
  enables the one matching the backend (today it already enables `transform.parent.parent`).
  (1b: both scenes have `HVR - Player` and `VR - Player XRI` as inactive root prefab instances; `HVR - XRDeviceSimulator`
  is inactive too, `IVrInput.SetDeviceSimulatorActive` switches it as before. The backend is registered in DI as
  `IVrBackend` (`ReflexVRSceneInstaller`), and `IVrBackend.OwnsRig(IVrRig)` picks the `VRPlayer` whose rig belongs to
  it. Its scene root (`transform.root`) is enabled, the other player roots are **destroyed**, so scene-wide lookups
  (`VRDialog`, `RegisterAllCanvases`, ...) only find the active player; the Marvin menu is looked up inside the active
  player only, because `Destroy` finishes at the end of the frame. On Hvr the HVR player is now activated in
  `SetupPlayerController` (same frame as the scene load, before `Start`) instead of being active in the scene.)
- The Xri backend files carry no define, since XRI is free and always installed. The Hvr backend stays behind
  `GOTHIC_HVR_INSTALLED`.

## Rig (`Gothic-VR/Resources/VR/Prefabs/VR - Player XRI.prefab`, built via UnityMCP)

```
VR - Player XRI
└─ XR Origin (XROrigin, CharacterController, XRBodyTransformer, LocomotionMediator)
   ├─ Camera Offset
   │  ├─ Main Camera (TrackedPoseDriver, Camera, AudioListener, WallFade, ScreenFade)
   │  ├─ Left Controller  (TrackedPoseDriver / XRI controller input, NearFarInteractor [Phase 2], hand visual)
   │  └─ Right Controller (same)
   ├─ Locomotion
   │  ├─ ContinuousMoveProvider   (forward source = head / left / right controller)
   │  ├─ SnapTurnProvider + ContinuousTurnProvider (one enabled at a time)
   │  ├─ TeleportationProvider
   │  ├─ GravityProvider, JumpProvider
   │  └─ ClimbProvider [Phase 4]
   └─ Gothic components moved from the HVR rig: MenuHandler, VRCinema, VRSwimDive, VRSpectatorCamera,
      VRUiOnTop, VRPlaceInFrontOfCamera, VRPrintScreen anchor, VRMouth, VRHandsOnTop, …
```

`XriRig : MonoBehaviour, IVrRig` maps the port onto these components.

- [x] Done in 1b (prefab `Gothic-VR/Resources/VR/Prefabs/VR - Player XRI.prefab`, built via UnityMCP). Notes:
  - Made from a copy of `HVR - Player.prefab` with `UIManager`, `HVRGlobal` and `TechDemoXRRigOpenXR` removed, so the
    nested Gothic prefabs keep their overrides: `Rain`, `Menu-Dialog`, `MenuHandler` (inactive), `Cinema` (inactive),
    `Spectator Camera` (now pointing at the XRI camera). `Menu-MarvinMode` moved to `Left Controller` with its pose
    relative to the HVR physics hand (check its placement on a headset).
  - `XR Origin` (layer/tag Player) carries `CharacterController` (values of the HVR one), `AudioSource`,
    `NpcPrefabProperties`, `VRPlayer`, `VRSwimDive`, `VRPlayerWeaponInteraction`, `VRPlayerItemAdapter`,
    `VRHandsOnTop` (no hand renderers until phase 2), `XROrigin` (Floor), `XRBodyTransformer`, `LocomotionMediator`,
    `InputActionManager` (GothicVR asset, enables all its maps) and `XriRig`. `Main Camera` (tag MainCamera) has the
    HVR camera's `Camera`/URP data/`AudioListener` plus `TrackedPoseDriver` (XRI Head). Controllers: `TrackedPoseDriver`
    with `XRI Left/Right` Position/Rotation/Tracking State. No hand visuals, interactors, ray or teleport interactors
    yet; `WallFade`/`ScreenFade` are 1c, `ClimbProvider` phase 4.
  - Providers on `Locomotion`: move = `XRI Left Locomotion/Move`, snap turn = `XRI Right Locomotion/Snap Turn`,
    smooth turn = `XRI Right Locomotion/Turn` (turn-around off for both), jump = `Gothic/Jump` (0.46 m = HVR's
    3 m/s jump velocity), teleportation (delay 0), gravity (terminal velocity 20).
  - `MoveSpeed`/`RunSpeed` 1.5/3.5 as HVR. Sprint (`Gothic/Sprint`): a click sprints until the stick is released,
    holding the key sprints too. Crouch (`Gothic/Crouch`) toggles; the camera offset moves down until the camera is at
    0.7 m (HVR's `CrouchHeight`). The `CharacterController` height follows the camera height on its next move
    (XRI's `CharacterControllerBodyManipulator`), so it shrinks only once the player moves.
  - `Gravity` maps to `GravityProvider.gravityAccelerationModifier` (relative to `Physics.gravity`), 0 disables gravity;
    the grounded sphere cast uses every layer the Player layer collides with. `IsGrounded` =
    `GravityProvider.isGrounded || CharacterController.isGrounded`.
  - `TeleportEnabled` is only a flag for player-initiated teleports (no teleport interactor yet). The
    `TeleportationProvider` stays enabled, so `Teleport()` also works while the player is locked (dialogs, cinema).
  - `Teleport` queues `MatchOrientation.TargetUpAndForward` (the camera, not the origin, ends above the position and
    looks along the rotation; `default(Quaternion)` = identity). The HVR workaround of setting the rotation afterwards
    is not needed. `Teleported` is raised from `locomotionEnded`, which XRI defers until the body transformer applied
    the move (same frame, one frame after the request), plus `Physics.SyncTransforms()`.
  - Sitting: `Device` tracking + `CameraYOffset` = eye height 1.66. Standing: `Floor` + `Camera Offset` scaled by
    `EyeHeight / HMD height` at the time the setting is applied (no scaling below 0.5 m HMD height). PlayerHeight:
    `Floor`, no scaling.
  - `Fade()` only stores the value until 1c (`FadeAlpha` returns it). `SetGrabbingEnabled` is a no-op,
    `HandGrabbers` is empty, `IsClimbing` is false.
  - `IVrRig.Root` is the XR Origin, which stays at the tracking-space origin: in room-scale the body is up to the
    play-area size away from it (HVR moves its root under the head). `KeepPlayerWithin` and the hero position
    (`GetCurrentPlayerController().transform`) use it. Fine for walking with the stick; revisit if room-scale
    walking out of dialogs or NPC distances look wrong.

| `IVrRig` member | XRI implementation |
|-----------------|--------------------|
| `MovementEnabled` / `TurnEnabled` / `TeleportEnabled` | enable/disable the providers (or their input actions) |
| `MoveSpeed` / `RunSpeed` / `CanSprint` | `ContinuousMoveProvider.moveSpeed`; sprint is **ours**: a sprint action switches between the two speeds |
| `Gravity` / `MaxFallSpeed` | `GravityProvider` (gravity, terminal velocity); swimming sets 0 |
| `CanJump` | `JumpProvider.enabled` |
| `CanCrouch` | **ours**: a crouch action toggles an extra `Camera Offset` y-offset and shrinks the `CharacterController` height |
| `StepOffset` | `CharacterController.stepOffset` |
| `IsGrounded` | `CharacterController.isGrounded` (or `GravityProvider.isGrounded`) |
| `IsClimbing` | `ClimbProvider` state (Phase 4; `false` until then) |
| `Teleport(pos, rot)` | `TeleportationProvider.QueueTeleportRequest` with `MatchOrientation.TargetUpForward`; raise `Teleported` after the provider applied it. Today's workaround of setting rotation afterwards (`VRContextInteractionService.cs:202`) may no longer be needed. Verify. |
| `ApplyComfortSettings` | `VrSitStand.Sitting` → `XROrigin.RequestedTrackingOriginMode = Device` + `CameraYOffset` = eye height. `Standing`/`PlayerHeight` → `Floor`. HVR's `Standing` *scales* the camera (`HVRCameraRig.cs:244`); if we want that, scale `Camera Offset` by `EyeHeight / HMD height` ourselves. `VrMoveDirection` → `ContinuousMoveProvider.forwardSource`. `VrTurnType` → enable snap or continuous turn. `snapDegrees` → `SnapTurnProvider.turnAmount`. `smoothSpeed` → `ContinuousTurnProvider.turnSpeed`. |
| `SetGrabbingEnabled` | no-op in Phase 1; Phase 2 toggles the interactors |
| `Fade(alpha, speed)` | own `ScreenFade` (see below) |

## Input (`Gothic-VR/Resources/VR/Input/GothicVR.inputactions`)

Start from the Starter Assets actions and add Gothic actions per hand:

| Action | Binding (Quest/Pico/Touch-style) | Index | Simulator key |
|--------|----------------------------------|-------|---------------|
| Menu | left secondary (Y) | left B | Esc |
| Jump | right primary (A) | right A | Space |
| Crouch | right secondary (B), as today (`HVRPlayerInputs.GetCrouch`; Vive: trackpad up) | right B | Z / X |
| Sprint | left thumbstick click, as today (`HVRPlayerInputs.GetSprinting`) | same | Left Shift |
| BothGrips | derived in `VRPlayerInputs` from both grip actions | | T |
| SkipVideo | trigger or primary, either hand (`VRCinema`) | | |

`XriInput : IVrInput` reads the actions and keeps a `VrButtonState` per hand/button (pressed this frame / held /
released this frame). `IsSimulator` is true when the simulator is active.

- [x] Done in 1a. `GothicVR.inputactions` = the Starter Assets actions (without the mobile-AR maps "Touchscreen
  Gestures" and "Android Mouse Interaction") plus three own maps:
  - `Gothic Left` / `Gothic Right`: `Grip`, `Trigger`, `Primary`, `Secondary`, `Menu`, `Joystick Button` (bound via
    the generic `<XRController>{Hand}/{Usage}` paths, order = `VrButton`), `Velocity` (`deviceVelocity` or
    `devicePose/velocity`, for swimming) and `Haptic` (pass-through on the device, for `XriHaptics`).
  - `Gothic`: `Menu`, `Jump`, `Crouch`, `Sprint` with the controller bindings and simulator keys from the table.
    Read by the XRI rig via `XriInput.GetGothicAction(XriGothicAction)`. The keyboard bindings are always active
    (not only in the simulator); 1d should check them against the simulator's own keys. Vive trackpad-up crouch is
    not bound.
  - BothGrips and SkipVideo stay derived in `VRPlayer` / `VRCinema` from `IVrInput.GetButton`, no own actions.
  - `XriInput` enables only the three `Gothic*` maps. The `XRI *` maps are left for the rig's components (1b),
    e.g. via an `InputActionManager` on the rig.
  - `VrButtonState` comes from `InputAction.IsPressed()` / `WasPressedThisFrame()` / `WasReleasedThisFrame()`.
  - `IsSimulator` / `SetDeviceSimulatorActive` look for an `XRInteractionSimulator` in the active scene (warning if
    missing until 1d).

## Haptics

`XriHaptics : IVrHaptics` uses one `HapticImpulsePlayer` per controller. Map the presets from `VrHapticsService`
(amplitude/duration/frequency as today: Info 0.15/0.25/30, Warning 0.25/0.5/100, Error 0.4/0.8/175,
Success 0.5/0.6/80). Note: HVR's `HapticData(duration, amplitude, frequency)` argument order. Check it and keep the
same perceived strength.

- [x] Done in 1a. Checked: `HapticData`'s constructor is `(duration, amplitude, frequency)`, so the values listed
  above (written as amplitude/duration/frequency in `HvrHaptics`) were really sent as amplitude 0.25/0.5/0.8/0.6 and
  duration 0.15/0.25/0.4/0.5 s. `XriHaptics` sends exactly those (frequencies unchanged), so perceived strength stays
  the same. The `HvrHaptics` comment is misleading but left untouched (Hvr backend unchanged).
  Deviation: no `HapticImpulsePlayer` component; `XriHaptics` builds an `XRInputHapticImpulseProvider` per hand
  from the `Gothic Left/Right/Haptic` actions (what the player does internally), so it needs nothing on the rig.

## UI

- `EventSystem` gets `XRUIInputModule` instead of `HVRInputModule` (rig variant only).
- `XriUiPointers.RegisterCanvas` adds a `TrackedDeviceGraphicRaycaster` to the canvas (if missing).
  `RegisterAllCanvases` keeps today's one-shot scan in `InitUIInteraction`, but it can be removed later once all VR
  canvases carry the raycaster in their prefabs.
- `TryGetHandSide(eventData)`: `TrackedDeviceEventData.interactor` → which controller → `VrHandSide` (Marvin logo
  haptics).
- Ray interactor per hand for UI only in this phase (Phase 2 replaces it with the Near-Far interactor).

- [x] Done in 1c. Notes:
  - `VR - Player XRI.prefab` got a root child `UIManager` with `EventSystem`, `XRUIInputModule` (XR input only; mouse,
    touch, gamepad, joystick and the built-in fallback actions are off) and an `XRInteractionManager`. The HVR player
    keeps its `HVRInputModule`.
  - `Left/Right Controller/UI Ray`: `XRRayInteractor` (handedness set, interaction layers *Nothing*, raycast mask
    *UI* only, straight line, 30 m, select/activate input unused, UI press = `XRI Left/Right Interaction/UI Press`
    (+ `UI Press Value`), UI scroll = `UI Scroll`), `LineRenderer` (built-in `Sprites-Default` material) and
    `XRInteractorLineVisual` (5 mm, white; invalid/blocked gradient fully transparent, so the laser only shows while
    it points at UI, like HVR). All Gothic UI graphics are on the UI layer, so the UI-only mask hits every menu.
  - `XriUiPointers`: `RegisterCanvas` adds (or re-enables) a `TrackedDeviceGraphicRaycaster`, `UnregisterCanvas`
    disables it, `RegisterAllCanvases` keeps the one-shot scan (logs the count). `TryGetHandSide` maps
    `TrackedDeviceEventData.interactor` (`XRBaseInteractor.handedness`) to `VrHandSide`; other events -> false.
  - Hero subtitles and the status bars were missing on the XRI camera since 1b (they were children of the removed
    HVR camera; `Output.PrintDialog` threw NREs when the hero spoke). `Menu-Subtitles` and the three `StatusBar*`
    instances were copied with the HVR player's overrides under `Main Camera`. Still missing from the HVR camera:
    `Mouth` (VRMouth, eating/drinking, phase 2) and `HeadRelativeInventory` (shoulder sockets, phase 3).
  - Fixed on both backends: `VRCinema.LockPlayer` never found the `VRPlayer` (the cinema is a sibling of the rig,
    not a child), so videos never locked the player. It now searches the player prefab root. Unlocking keeps values
    others enabled meanwhile (`|=` with the saved ones): the world load unlocks the player while the intro video
    plays, and restoring the lock saved at video start would have left the player stuck.

## Fades

- `ScreenFade`: a camera-attached inverted sphere or quad with an unlit URP material whose alpha is driven by
  `Fade(alpha, speed)`. Used by `VRSwimDive` (dive darkness by air), `VRSeat`, and scene transitions where
  `HVRCanvasFade` is used today.
- `WallFade` (replaces `HVRHeadCollision`): `Physics.OverlapSphere` on the camera with the world layers. Fade towards
  black by penetration depth. UltimateXR's `UxrCameraWallFade` (MIT) is a reference.

- [x] Done in 1c (`Gothic-VR/Scripts/Adapters/Player/ScreenFade.cs`, `WallFade.cs`, both on the XRI `Main Camera`;
  game code, no XRI types). Notes:
  - What HVR did: `HVRCanvasFade` "GlobalCameraFade" was the `ScreenFader` behind `IVrRig.Fade` (VRSwimDive dive
    darkness, VRSeat), a second one "HeadCollisionFade" was driven by `HVRHeadCollision` (layer Default only,
    radius 0.1 m). Ours combines both in one `ScreenFade` with two channels (requested fade and wall fade, the
    stronger one is shown); `IVrRig.FadeAlpha` returns the requested channel only, so `VRSeat` waits as before.
  - `ScreenFade` draws the inner faces of a 0.3 m cube around the head with own shader `Gothic/VR Screen Fade`
    (`Shaders/GothicVRScreenFade.shader`: color only, `ZTest Always`, `Cull Front`, `ColorMask RGB`, stereo
    instancing macros) and material `Materials/VRScreenFade-XRI.mat` (render queue 3898: after world transparents,
    before the on-top UI at 3899+, so dialogs/subtitles/menus stay readable). The cube is on the UI layer, so it also
    works in the cinema. Runs on unscaled time. `Fade(a, float.MaxValue)` is immediate.
  - `WallFade` (own design, `UxrCameraWallFade` only read): distance to the nearest Default-layer collider by a
    binary search of `Physics.CheckSphere` radii (works for the non-convex world mesh, where `ClosestPoint`
    doesn't; `ComputePenetration` needs an enabled collider), fade from 0.1 m to black at 0.03 m. Through a
    one-sided wall mesh, a ray from the last free head position (both directions) keeps it black. Jumps > 1 m per
    frame (teleport) reset the free position.
  - Verified without headset by rendering the camera into a texture (fade 1 = black) and moving the camera into the
    ground (ramps to 0.71 at 5 cm, to 1 below the surface, back to 0 above). The MCP Game-view screenshot doesn't show
    the fade (captured before `LateUpdate`?), so the pixel check was used instead.

## Swim/dive (`VRSwimDive.cs`)

Already on `IVrRig` after Phase 0. Check:
- Stroke movement reads `IVrRig.LeftHand/RightHand` positions and moves `CharacterController` directly. The XRI
  providers must not fight it, so `MovementEnabled = false` while swimming, as today.
- Rotation math (`VRSwimDive.cs:350-415`) assumed HVR's rig hierarchy (`CameraRig.TransformDirection`). Re-check
  with `XROrigin.Origin` and the camera's yaw.

- [x] Checked in 1c, no code change needed:
  - `IVrRig.TrackingSpace` is `Camera Offset` on XRI. It never rotates relative to the XR Origin, which the turn
    providers rotate, and controller velocities are relative to it. So `TrackingSpace.TransformDirection` contains
    the synthetic (stick) yaw but not the real head yaw, like HVR's `CameraRig` ("Test 5"). Checked: origin yaw 90°
    turns local forward into world +x.
  - `MovementEnabled` is **not** set to false while swimming, neither on HVR today nor on XRI (the line above was
    wrong): the stick keeps moving the player in water on both backends. On XRI this doesn't fight the strokes:
    with gravity 0 the `ContinuousMoveProvider` moves horizontally via the body transformer, the strokes call
    `CharacterController.Move` directly, both add up.
  - Play mode (Xri, no headset): teleported into G1 water 2.7 m deep -> `Swim`, gravity/max fall 0, jump/crouch off.
    `StartDive` -> `Dive`, the fade ramps up with `1/air` (0.32 after a few seconds); back to `Swim` -> fade 0
    immediately. Strokes themselves need a headset or the simulator (1d).
  - `VRSwimDive` raycasts from the XR Origin (`transform.position`), which in room-scale can be up to the play area
    away from the body (see 1b notes on `IVrRig.Root`). Fine with stick walking; check on a headset.

## Simulator

- Use the XR Interaction Simulator prefab in `Player.unity`. Enable it via `EnableVRDeviceSimulator`, as today with
  `HVRBodySimulator`.
- Port the on-screen help from `VRSimulatorControlsGUI` (keys list) as a small `OnGUI`/UGUI overlay.

- [x] Done in 1d. Notes:
  - Prefab `Gothic-VR/Resources/VR/Prefabs/VR - XR Interaction Simulator.prefab` (copy of the sample prefab:
    `XRInteractionSimulator`, `SimulatedDeviceLifecycleManager`, `InputActionManager`, `SimulatedHandPlaybackManager`,
    all package scripts) plus our `XriSimulatorControlsGUI`. Its input actions are copied to `Gothic-VR/Simulator/`
    (`XR Interaction Simulator Controls`, `XR Interaction Controller Controls`); no reference into `Samples/` is left.
    Left out: the sample UI prefab (1 MB, sample scripts; replaced by our overlay) and the hand mode (hand controls,
    expressions and 32 MB of hand recordings; hand tracking capability off, expression lists empty), as the rig
    uses controllers only.
  - `Player.unity` and `Lab.unity` hold it inactive next to `HVR - XRDeviceSimulator`. `XriInput.SetDeviceSimulatorActive`
    (called from `SetupPlayerController` with `EnableVRDeviceSimulator`) enables it; the Hvr backend still toggles the
    HVR simulator only. `XriInput.IsSimulator` is true while it is active.
  - The simulator finds controllers only through an `XRInputModalityManager` (our rig has none) and fell back to the
    camera, so point-and-click aimed the camera. `XriInput` assigns the rig's `Left/Right Controller` to it before
    enabling it.
  - Simulated controllers report no velocity, so swim strokes did nothing. While the simulator is active,
    `XriInput.GetControllerVelocity` derives it from the controller's position change in the tracking space.
  - `XriSimulatorControlsGUI` (own code, `OnGUI`, F1 toggles): Gothic keys, the simulator's keys and the device it
    currently drives.
  - Key conflicts with the simulator, resolved:

    | Gothic key (1a) | Simulator use | Now |
    |-----------------|---------------|-----|
    | Esc (menu) | - | unchanged |
    | Space (jump) | toggle quick action | Space stays jump; quick action moved to **Enter** in our copy of the simulator controls |
    | Z / X (crouch) | Z: Z-axis constraint, X: action menu of the sample UI (not used) | crouch is **X** only |
    | Left Shift (sprint) | Shift = buttons/stick go to the left controller | sprint is **F**; walking is Shift + I/J/K/L (left stick) |
    | T (both grips, `VRPlayer`) | trigger (= UI click) | T is the trigger only on Xri; `IVrInput.UsesBothGripsSimulatorKey` (Hvr true, Xri false). Both grips: `[` and `]` together, then G |

    The Gothic keyboard bindings are in binding group `Simulator`, the controller bindings in `XR`. `XriInput` masks the
    `Gothic` map to `XR` unless the simulator is active, so the keyboard does nothing on a headset (like on HVR).
    Before, T in the XRI simulator started speech-to-text recording on every trigger press (and threw in
    `SpeechToTextHandler.GetMicrophoneDeviceName` without a microphone).
  - Play mode test (G1, Xri, simulator; keys injected via the Input System, Game view at 919x301), see the test plan
    section below.

## CI

- Add a build job that doesn't inject HVR, removes `GOTHIC_HVR_INSTALLED` and sets the backend to `Xri`. Methods
  stay `VRBuilderActions.Perform*Build`.

- [x] Done in 1d: job `buildWithoutHVR` in `.github/workflows/test_and_build.yml` replaces the disabled
  `testCompileWithoutHVR`. Same matrix (Windows64, Pico, Quest) and build methods as `buildForAllPlatforms`, runs on
  the label `pipeline-test-build` (together with the HVR build) or `pipeline-test-build-xri` (alone). It removes
  `GOTHIC_HVR_INSTALLED` from `ProjectSettings.asset` and fails if the define or `Assets/HurricaneVR` is still there,
  uses its own Library cache key (`Library-Xri-*`) and uploads `Gothic-Unity-<device>-Xri`. The backend isn't set
  in an asset: `VRContextBootstrap.SelectBackend` falls back to Xri when HVR isn't compiled in (1a). Not run yet (no CI
  access from here); YAML checked with a parser.

## Test plan

On Quest 3, Pico 4 and PCVR (Index), backend `Xri`:
1. Main menu: laser click on every menu page, virtual keyboard (save name), settings sliders.
2. All accessibility settings: sit-stand (sitting in a chair → eye height correct), move direction head/left/right,
   snap 5°…, smooth speed min/max. Restart → kept.
3. Walk, sprint, jump, crouch, fall from a ledge, walk up stairs, Xardas' tower water exit (step height).
4. Teleport from code: new game spawn, `TeleportPlayerTo` (Marvin goto, chapter change), spectator camera follows.
5. Dialog with an NPC (start with the menu fallback until Phase 2 grab-to-talk), subtitles, cinema video, skip.
6. Swim and dive; air bar fade; climb out.
7. Head into a wall → fades.
8. Simulator on a desktop without a headset: all of the above that applies.

1d results with the simulator (G1 new game, Xri backend, `EnableVRDeviceSimulator`, editor play mode without a
headset; keys injected into the Input System from editor scripts, not typed by a person):
- OK: `IsSimulator` true, Gothic keyboard bindings unmasked; intro video skipped with the simulated right primary
  button (key 1); walk with the simulated left stick (Shift + I, 1.5 m/s, stopped by a rock); sprint (F, move speed
  3.5 until the stick is released); snap turn (L, 15°); jump (Space, +0.35 m); crouch toggle (X, camera 1.66 -> 0.70
  m and back); menu (Esc); laser click in the in-game menu (mouse aims the right controller ray, T = trigger opened
  Options); Diego's start dialog: choice clicked with the ray + T, dialog went on; walking away from a dialog chose
  END; swim (teleport into 3.3 m deep water -> Swim, gravity 0, jump/crouch off) and strokes (] + hold G + S: hand
  pulled back, player swam 9 m forward); T no longer triggers both grips/speech-to-text.
- Not tested in 1d: dive and air fade with the simulator (1c checked them via code), climbing out of water, main
  menu pages/virtual keyboard/sliders, accessibility settings, subtitles visually, the help overlay visually (only
  compiled and enabled; OnGUI isn't in MCP screenshots), Lab.unity.
- Needs a headset: everything on Quest 3 / Pico 4 / PCVR (items 1-7 above), room-scale, comfort settings, haptics.

## Risks

| Risk | Mitigation |
|------|-----------|
| Sitting/Standing semantics differ from HVR | Implement `PlayerHeight` first (most players), then `Sitting`; check `Standing` scaling with a playtester. |
| Pico button names differ in OpenXR profiles | Test the Pico 4 profile early; bind via the generic `<XRController>{LeftHand}/secondaryButton` paths where possible. |
| Gravity/jump providers vs. our `KeepPlayerWithin` (`controller.Move`) | Both move the `CharacterController`. Keep `KeepPlayerWithin` after the providers (late update order) and verify dialogs. 1b: already the case, `DialogService` calls it from a coroutine (`yield return null`, after all `Update`s) and the providers/`XRBodyTransformer` run in `Update` (execution order -210/-205). Dialogs not tested on XRI yet (needs UI, 1c). |
