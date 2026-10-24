# 02 - What Gothic-Unity uses from HVR

Snapshot: `main` @ `084e5e95` (2026-10-22). Measured by grepping C# and by resolving script/asset GUIDs in our
prefabs, scenes and ScriptableObjects against `Assets/HurricaneVR/**/*.meta`.

## 1. Coupling in numbers

| Metric | Value |
|--------|-------|
| C# files in `Gothic-VR` + `Gothic-Lab` behind `GOTHIC_HVR_INSTALLED` or referencing HVR | **62** |
| … using HVR types directly (`using HurricaneVR…` / subclassing) | **32** |
| … using HVR only through our own wrappers (`VRPlayerController`, `VRPlayerInputs`) | **8** |
| … gated by the define but **not using HVR at all** | **22** (see §6) |
| Distinct HVR types referenced in our code | 28 |
| HVR classes we **subclass** | 6: `HVRPlayerController`, `HVRPlayerInputs`, `HVRSocket`, `HVRPhysicsDoor`, `HVRSimulatorControlsGUI`, (`HVRShoulderSocket` via `VRShoulderSocket`) |
| Our prefabs/assets with HVR components | 18 (11 prefabs in `Gothic-VR/Resources/VR/Prefabs/**`, 7 assets) |
| HVR prefabs we nest | `TechDemo/Prefabs/TechDemoXRRigOpenXR` (the whole player!), `UIManager`, `HVRGlobal`, `LargeInventorySlot`, `SocketOval` |

Most-used HVR types in our code: `HVRGrabbable` (39), `HVRHandSide` (34), `HVRGrabberBase` (24), `HVRSocket` (8),
`HVRHandGrabber` (8), `HVRController` (8), `HVRInputModule` (7), `HVRButtons` (7), `HVRPhysicsDoor` (6).

> The player rig is a **prefab variant of HVR's TechDemo rig** (`Resources/VR/Prefabs/HVR - Player.prefab` →
> `TechDemoXRRigOpenXR` → `TechDemoXRRig`). So TechDemo scripts (`DemoBackpack`, `DemoHolster`, `DemoUIManager`),
> TechDemo hand models, hand poses, backpack and SFX are runtime dependencies, not just samples.

## 2. Capability usage map

Capability IDs refer to [01-hvr-capabilities.md](01-hvr-capabilities.md).

| Gothic feature | HVR capabilities | Our code (Assets/Gothic-VR/Scripts/…) | Our assets |
|---|---|---|---|
| **Player rig, movement, turning, sit/stand** | H10, H12 | `Adapters/HVROverrides/VRPlayerController.cs` (subclass; Move/Snap/Smooth turn, `CameraRig.SetSitStandMode`, `MovementEnabled`, `RotationEnabled`, `CanJump/Crouch/Sprint`, `CharacterController.stepOffset`, per-hand `AllowGrabbing/AllowHovering`, disabling hand poses) · `Services/Context/VRContextInteractionService.cs` (setup, lock/unlock, `KeepPlayerWithin` via `CharacterController.Move`, `IsGrounded`, `IsClimbing`, far-clip on `Camera`) · `Services/Context/VRContextMenuService.cs` (`HVRSitStand` default) | `HVR - Player.prefab` (HVRCameraRig, HVRHeadCollision, HVRCanvasFade …) |
| **Swimming / diving** | H10, H15 | `Adapters/Player/VRSwimDive.cs` – rewrites `Gravity`, `MoveSpeed`, `RunSpeed`, `MaxFallSpeed`, `CanSprint`, moves `CharacterController` by hand strokes using `Left/RightControllerTransform`, `CameraRig`, `CameraHeight`, `ScreenFader.Fade` | |
| **Custom inputs** (menu, both-grips, jump on A, simulator keys) | H12 | `Adapters/HVROverrides/VRPlayerInputs.cs` (subclass; `HVRController.GetButtonState`, `HVRButtonState`, `UseWASD`) · `Adapters/UI/VRCinema.cs` (skip video via Trigger/Primary) | `HVRGlobal.prefab` (input maps: Oculus, Knuckles, Vive, WMR, Cosmos) |
| **Teleport** | H11 | `VRContextInteractionService.TeleportPlayerTo` (`Teleporter.Teleport`) · `VRCinema` / `VRPlayerController` (enable/disable) · `Adapters/Player/VRSpectatorCamera.cs` (`Teleporter.PositionUpdate`) | TeleportMarker (nested) |
| **Item grabbing** (pick up = add to inventory) | H1, H2, H3, H6 | `Adapters/Vob/VRVobItem.cs` (`OnGrabbed/OnReleased`, `SetupColliders`, ghost layer while held) · `Services/VRPlayerService.cs` (`SetGrab/UnsetGrab` - inventory count, distinguishes `HVRForceGrabber` vs `HVRHandGrabber`, `IsLeftHand`, dual-grab; `IgnorePlayerCollision` via `grabbable.Colliders`) · `Adapters/Player/VRPlayerItemAdapter.cs` (`OnBeforeGrabbed` turns physics on) | `Vobs/oCItem.prefab`: `HVRGrabbable` with `PoseType=PhysicPoser`, `HoldType=ManyHands`, `ForceGrabbable=1`, `RequireLineOfSight=1`, `HVRTagSocketable` |
| **Melee combat** | H2 (rigidbody velocity of the held weapon) | `Domain/Player/VrWeaponAttackDomain.cs` (velocity history from `_weaponRigidbody.linearVelocity - characterController.velocity`) · `Services/VRWeaponService.cs` · `Adapters/Player/VRPlayerWeaponInteraction.cs` (`HVRHandGrabber.HandSide`) · `Adapters/Player/VRFistAttackAdapter.cs` (hand colliders on `Hand` layer) | |
| **Hover focus** (name label + brighten) | grab hover events | `Adapters/VRFocus.cs` (`OnHoverEnter/Exit`) | 10 `OnHoverEnter/Exit` bindings in Vob prefabs |
| **Backpack, hip/back holsters** | H7, H4 | `Adapters/Player/VRBackpack.cs` (`HVRSocketContainer.TryAddGrabbable`, `HVRSocketable`, shoulder socket) · `Adapters/HVROverrides/VRSocket.cs` (subclass; reparents the `VobLoader` root instead of the grabbable, rotates by item category) · `VRShoulderSocket.cs` · `Adapters/Vob/Container/VRVobContainerSocketInventory.cs` | `BackPack.prefab` (HVRGrabbable, HVRGrabPoints, HVRPosableGrabPoint, HVRHandPoser, HVRSocketContainer, HVRSocketLink, `DemoBackpack`) · `VRBackpack(Large)InventorySlot.prefab` (HVRTagSocketFilter, HoverMaterial, HoverScale) · 2 `HVRHandPose` assets · `GUZ-VR-Socketables.asset` |
| **Chests** (lid + slots) | H8, H7 | `Adapters/Vob/Container/VRVobContainerPhysicsChest.cs` (**subclass of `HVRPhysicsDoor`**, re-sizes sockets to chest mesh, `TryFindAvailableSocket`, socket audio) · `VRVobContainer.cs` (lid SFX) | `oCMobContainer.prefab`: HVRSocketContainer, HVRSocketContainerGrabber, HVRTriggerGrabbableBag, HVRRotationTracker, filters/hover |
| **Doors** | H8 | `Adapters/Vob/VRVobDoor.cs` (sets `SFXOpened/Closed`) · `Adapters/Vob/LockPicking/VRContainerDoorPickingInteraction.cs` (`HVRPhysicsDoor.Lock/Unlock`) | `oCMobDoor.prefab`: HVRPhysicsDoor, HVRGrabbable, HVRRotationTracker |
| **Lock picking** | H13 + hand side | `VRContainerDoorPickingInteraction.cs`, `VRLockPickInteraction.cs` | |
| **NPC loot** | H7 | `Adapters/VRNpcLoot.cs` (spawns `HVRSocket`s, `TryGrab(grabbable, true, true)`, `IsGrabbing`) | |
| **NPC talk / corpse** | "grab as click" | `Adapters/VRNpc.cs` (grab → release immediately, start dialog) · `Adapters/Marvin/VRNpcMarvinAdapter.cs` | `oCNpc.prefab` HVRGrabbable · `NPC_ForcePullSettings.asset` |
| **Mobs** (wheel, lever, bed, fire, switch, inter) | grab events | `Adapters/Vob/VRMobInteraction.cs` · `VRPlayerService.HandleMobGrab` · `Adapters/Player/VRSeat.cs` (`HVRCanvasFade`) | `oCMob{Bed,Fire,Inter,Switch,Wheel}.prefab` HVRGrabbable |
| **Ladders** | H9 | – (prefab only) | `oCMobLadder.prefab`: HVRClimbable |
| **Dig spots** | grab state | `Adapters/Vob/VRDigSpot.cs` (`FindObjectsByType<HVRHandGrabber>`, `GrabbedTarget`) | |
| **Haptics** | H13 | `Services/VrHapticsService.cs` (`HVRController.Vibrate(HapticData)`, 4 presets, fight + lockpick events) · `VRContextInteractionService.OnMarvinLogoPoked` | `HVR_GrabHaptics.asset` |
| **Menus / UI laser** | H14 | `VRContextInteractionService.InitUIInteraction` (`HVRInputModule.Instance.UICanvases`), `.Pointers` → `HandSide` · `Adapters/UI/VirtualKeyboard.cs` (`AddCanvas/RemoveCanvas`) | `UIManager.prefab` (HVRInputModule, `DemoUIManager`) · `HVRUIPointer` in hand prefabs |
| **Fades** | H15 | `VRSwimDive` (`ScreenFader`), `VRSeat` (`HVRCanvasFade`) | |
| **Finger curls** (Index / Steam Frame) | H5 | – (automatic) | `HVR_FingerSettings.asset`, XR Hands via `USING_XRHANDS` |
| **Desktop simulator** | H22 | `Adapters/HVROverrides/VRSimulatorControlsGUI.cs` · `VRContextInteractionService` (`HVRBodySimulator`) · `VRPlayerInputs` (`UseWASD`) | package `com.hurricanevrextensions.simulator` |
| **Editor/build** | – | `Editor/HurricaneActions.cs` (URP material conversion), `Editor/VRContextToolActions.cs` (toggle define), CI injects HVR from private repo | |
| **Lab** | util | `Gothic-Lab/Scripts/Handler/LabFightHandler.cs` (`HurricaneVR.Framework.Core.Utils`) | |
| **Core leaks** | layers & comments | `Gothic-Core/Scripts/Const/Constants.cs:113-117` (`Player`, `DynamicPose`, `Grabbable`, `Hand` layers), comments in `MobInteractionService`, `VobService`, `NpcHelperService` | `ProjectSettings/TagManager.asset` layers 8, 9, 20, 21 |

## 3. HVR features we do **not** use

Stabbing, guns/bow/ammo/recoil, damage/destructible, buttons/dials/levers/drawers (we use plain grab events for
mobs), pose zones, SteamVR/OVR/FinalIK/HexaBody integrations, gesture detector, most editor creators.
→ Roughly **⅓ of HVR is relevant** to us; the relevant third is, however, its hardest part (physics hands +
physics grabbing + procedural posing).

## 4. Assets that must be replaced (licensed HVR content)

| Asset | Used for |
|-------|----------|
| `TechDemo/Prefabs/TechDemoXRRig(OpenXR).prefab` | whole player rig base |
| `TechDemo/Assets/Hands/HVR_left/right_hand*.fbx/.prefab`, `Physics Left/RightHand.prefab` | visible + physics hands |
| `Framework/Models/OculusCustomHands/*` (Quest2 controller and custom hand meshes) | hand/controller visuals |
| `TechDemo/Poses/HandPoses.prefab`, `pose_fist.asset`, `DefaultPoses/OculusCustomHand{Open,Fist}.asset` | default & fist poses |
| `TechDemo/Assets/Interactables/Props/Backpack/BackPack.prefab` + `sfx_back_pack_open/close.wav` | backpack |
| `Framework/Prefabs/SocketOval.prefab`, `Models/Ring.fbx`, `Materials/{Socket,Valid,Invalid}SocketRing.mat` | socket visuals |
| `Framework/Prefabs/TeleportMarker.prefab` + materials | teleport |
| `Framework/Resources/InputSettings/*.asset`, `HVR_ControllerOffsets`, `HVR_FingerSettings`, `HVR_GrabHaptics`, `HVR_DefaultHandStrength`, `HVR_HandPullingSettings`, `HVR_ForcePullSettings` | tuning data |
| `TechDemo/Prefabs/UIManager.prefab`, `PlayerSettingsUI.prefab` | UI module host |

Note on licensing: our overrides partially **copy HVR source** (`VRSocket.OnGrabbed` says "From HVRGrabberBase.cs …
From HVRSocket.cs"). Any replacement must be written fresh, not ported line by line from HVR.

## 5. Platform matrix today

| Platform | Path | HVR involvement |
|----------|------|-----------------|
| Quest 2/3 | OpenXR + Meta Quest Support, Oculus Touch / Touch Pro profiles | `HVRInputManager` → XRInput/InputSystem, Oculus input map |
| Pico 4 / 4 Ultra / Neo3 | OpenXR + `pico-openxr-sdk` (toggled by `VRBuilderActions.SetPicoSettings`) | generic XR input map |
| PCVR (Index, Vive, Touch via Link/SteamVR) | OpenXR on Windows, Index/Vive profiles | Knuckles/Vive maps |
| Finger tracking (Index, Steam Frame) | OpenXR + Hand Tracking Subsystem (`com.unity.xr.hands` 1.7.3) | `HVROpenXRFingerCurls` (Knuckles only, fingers 2-4) |

## 6. Files gated by `GOTHIC_HVR_INSTALLED` without any HVR usage

`VRContextBootstrap` (gating is intentional - registration), `VrColliderHitTracker`, `VrNpcColliderDebugAdapter`,
`MarvinInspectorTabHandler`, `VRFistAttackAdapter`, `VRHandsOnTop`, `VRIntroduceChapter`, `TextMatcher`,
`VRSettingsAccessibilityMenu`, `VRSettingsImmersionMenu`, `TutorialHandler`, `VRBillboard`, `VRDialog`,
`VRPrintScreen`, `VRSubtitles`, `VRUiOnTop`, `VRLockPickInteraction`, `VRLockPickProperties`, `VRWeaponAttackAdapter`,
`ReflexVRSceneInstaller`, `VRUiOnTopService`, `VRUnityLifecycleEvents`.

Plus 8 that only touch HVR through our wrappers: `VRSpectatorCamera`, `SpeechToTextHandler`, `VRFollowCamera`,
`VRPlaceInFrontOfCamera`, `VRContextDialogService`, `VRCinemaService`, `HurricaneActions`, `VRContextToolActions`.

These ~30 files are free wins: once our own abstractions exist, they compile without HVR.
