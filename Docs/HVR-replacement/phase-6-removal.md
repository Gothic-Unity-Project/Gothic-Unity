# Phase 6 - Remove HVR

**Goal:** HVR is gone from the repository, the build and the docs. Every contributor can open the project, build
the VR targets and play them, with or without a headset (simulator).

**Size:** S (2-3 days) · **Depends on:** Phases 1-4 accepted in playtests on Quest, Pico and PCVR (Phase 5 has no
HVR dependency and can land before or after - see phase-5-hands.md)

## Entry check (go / no-go)

- [ ] Xri backend has been the default for at least one release/playtest round without HVR-only regressions.
- [ ] The playtest checklists of Phases 0-4 pass on Quest 3, Pico 4 and PCVR (Index).
- [ ] Nobody on Discord has open work that depends on HVR components.

Decided on 2026-10-28: XRI is the only VR backend from now on, so the removal starts without waiting for these
checks. The playtests happen on the removal branch instead.

## Work items

### Code

- [x] Delete `Gothic-VR/Scripts/Interaction/Backends/Hvr/` (incl. `VRPlayerController`-based `HvrRig`, `VRSocket`,
      `VRShoulderSocket`, `VRSimulatorControlsGUI`, `VRVobContainerPhysicsChest` HVR version, bridges).
- [x] Remove `DeveloperConfig.VrInteractionBackend` (C# field **and** asset value), or keep it with only `Xri`
      if a future backend is planned. The removal is easier to read. (Removed with its enum and `IVrBackend.Kind`;
      `ReflexVRSceneInstaller` creates the XRI backend.)
- [x] `VRContextBootstrap`: no backend selection, no `#if`.
- [x] Remove `GOTHIC_HVR_INSTALLED` from `ProjectSettings/ProjectSettings.asset` (Android + Standalone defines) and
      from every remaining file (`grep -rn GOTHIC_HVR_INSTALLED Assets` = 0).
- [x] Delete `Gothic-VR/Editor/HurricaneActions.cs` (URP material conversion) and
      `Gothic-VR/Editor/VRContextToolActions.cs` (define toggling).
- [x] Remove `HurricaneVR.*` references from asmdefs if any (`Gothic.VR.asmdef`, `Gothic.VR.Editor`, Lab, Tests).
      (None found.)
- [ ] `grep -rni "hvr\|hurricane" Assets --include=*.cs` = 0 (comments too). (Open: the wording clean-up below, in its
      own PR.)

### Clean-room cleanup (distance to HVR)

A review on `engine/hvr-replacement-5b` (2026-10-25) compared the Xri path (`Interaction/Components`,
`Backends/Xri`, `Ports`, `Models`, `ScreenFade`, `WallFade`) with the HVR source. Result: no copied code and no copied
assets. The algorithms are our own. What still points back to HVR is wording: comments, tooltips and a few names.
This is an optics problem rather than a license problem, and it's cheap to fix.

| Dimension | Finding | Confidence "ours" |
|-----------|---------|-------------------|
| HVR source/assets distributed | `Assets/HurricaneVR/` is ignored, 0 tracked files. The Xri prefabs (`VR - Player XRI`, `BackPack XRI`, `VR Socket Slot`, simulator) and new materials reference none of the HVR asset GUIDs. The screen fade shader and block hands are our own. | 95% |
| Verbatim / near-verbatim code | None. Different structure and style (HVR: `m_` fields, public fields, `virtual` hooks; ours: ports + bridges). | 95% |
| Algorithms | Different (see below). Exception: teleport line of sight. | 90% |
| Class names | All `Vr*`/`Xri*`, but `VrSocketLink`, `VrSocketContainer` and `VrGhostHand` map 1:1 onto `HVRSocketLink`, `HVRSocketContainer` and `HVRGhostHand`. | 80% |
| API vocabulary | The ports were cut along HVR's API and kept its member names: `BeforeGrabbed`, `ForceRelease`, `HandGrabbers`, `MovementEnabled`, `CanCrouch`, `CanSprint`, `MaxFallSpeed`, `MoveSpeed`, `RunSpeed`, hinge `CloseAngle` / `CloseOverTime`. API names are generally not protectable, but together they show where the design came from. | 75% |
| Paper trail | About 25 comments/tooltips in the Xri and backend-neutral code cite HVR ("like HVR", "(HVR: MaxJumpDistance)", "Same as HVR's GetBottom()", "HVR CheckVerticalDistance / CheckDestinationLineOfSight", "ParentDisablesGrab"). | weakest point |

Algorithm comparison:
- **Physical hands:** HVR (`HVRJointHand` + `PDStrength`) drives the hand with a ConfigurableJoint and spring/damper
  settings. `VrPhysicsHand` uses its own velocity-change controller with a detached body, follow-vs-push force
  splitting and a visual offset applied in LateUpdate.
- **Doors/lids:** HVR (`HVRPhysicsDoor`) is a joint-based door with latch and handle trackers. `VrHinge` is a
  kinematic angle solver with contact-lever pushing from the hands. Only `CloseAngle = 5` and `CloseOverTime = 0.25`
  match HVR's field names, defaults and meaning.
- **Sockets:** both scale items to fit as `Size / extent` (generic idea). HVR takes the size from `HVRSocketable`,
  `VrSocketSlot` measures the renderer bounds itself. The category rotations come from our own pre-branch `VRSocket.cs`.
- **Ghost hand:** HVR toggles it on a distance check every 0.1 s. `VrGhostHand` builds mesh copies of the hand blocks
  with show/hide hysteresis.
- **Socket container, socket link, SFX player, hover ring, snap to ground:** trivial or generic logic, and implemented
  differently.
- **Teleport (`XriTeleportAimer`)** is the closest match: `HasLineOfSight` has the same structure as HVR's
  `CheckDestinationLineOfSight` (N rays from the head to points spaced `i/N * height` above the target, true on the
  first free one), with the same defaults (MaxJump 1.5, MaxDrop 3, height 1) and tooltips naming the HVR fields.

Work items (no behaviour change):
- [ ] Remove HVR references from comments and tooltips in the Xri and backend-neutral code and describe the behaviour
      in our own words: `XriTeleportAimer`, `XriTeleportSettings`, `XriRig`, `XriSfxPlayer`, `XriHaptics`, `XriInput`,
      `XriSocketBridge`, `XriInteractableBridge`, `GothicGrabInteractable`, `VrSocketSlot`, `VrChestSlots`, `VrHinge`,
      `VrLadderExit`, `IVrInput`, `IVrInteractableBackend`, `IVrRig`, `VrGrabberKind`. (Covered by the grep = 0 item
      above once `Backends/Hvr/` is deleted, but rewrite the sentences instead of just dropping the HVR part.)
- [ ] Teleport: restructure `XriTeleportAimer.HasLineOfSight` (e.g. sample from top to bottom, own sample count and
      height defaults), drop the "(HVR: …)" tooltip suffixes in `XriTeleportSettings` and the `GetBottom` remark.
- [ ] Hinge: rename `VrHinge._closeAngle` / `_closeOverTime` (e.g. `_closedThreshold`, `_softCloseDuration`). Keep the
      serialized values via `[FormerlySerializedAs]` or re-set them on the prefabs (UnityMCP).
- [ ] Optional: rename `VrSocketLink` (e.g. `VrHomeSlot`) and `VrGhostHand` (e.g. `VrControllerEcho`), and the
      HVR-flavoured port members (`HandGrabbers`, `BeforeGrabbed` → e.g. `Hands`, `Grabbing`). The least legal value
      of all items; only for a full clean break. Script renames need the `.meta` GUID kept (prefab references).

### Packages and assets

- [x] `Packages/manifest.json`: remove `com.hurricanevrextensions.simulator`.
- [x] Delete your local `Assets/HurricaneVR/` (it isn't in git: `.gitignore:106`; CI injects it). Keep the ignore
      entry so nobody commits it by accident. (The CI build now fails if the folder exists.)
- [x] **Prefabs (UnityMCP):** remove all HVR components from the 11 VR prefabs (`HVRGrabbable`, `HVRTagSocketable`,
      `HVRPhysicsDoor`, `HVRRotationTracker`, `HVRClimbable`, socket components …), delete `HVR - Player.prefab`,
      `HVRGlobal` instance in `Player.unity` / `Lab.unity`, `UIManager` (HVR) instance. (Done for 10 VOB prefabs plus
      their variants. Also deleted `HVR - XRDeviceSimulator`, the old `BackPack`, the HVR chest inventory, the broken
      `TechDemoXRRigOpenXR` variant, the unused `VRHands` materials, and the HVR demo props in `Lab.unity`. The GUID
      scan against an HVR 2.9 copy is empty.)
- [ ] Rename `VR - Player XRI.prefab` → `VR - Player.prefab`. (Later, together with the other `XRI` names, once the open
      PRs that edit these prefabs are merged.)
- [x] Delete `Gothic-VR/Resources/VR/HVR Settings/`, `GUZ-VR-Socketables.asset`,
      `Player-Elements/BackPack/backpack_*_hand_pose.asset` (no replacement while 5f posing is on hold), old slot
      prefabs.
- [x] Open every VR prefab and scene once and check for "Missing script" (UnityMCP `find_gameobjects` / console). (All
      prefabs and scenes in `Gothic-*`: no missing HVR scripts. Two old non-HVR leftovers stay: a dead script on the
      Flat `Player.prefab` and one in Lab `SpatialPanelBase.prefab`.)

### Build and CI

- [x] Remove the private HVR asset-repo checkout/injection from the CI workflow.
- [x] `pipeline-test-build` label: VR builds (Windows64, Pico, Quest) run for every contributor's PR. (One build job, no
      HVR secret needed.)
- [x] `VRBuilderActions`: nothing HVR-specific (check).

### Docs

- [x] `CLAUDE.md`: remove the "Without HurricaneVR" and "Purple HVR hands" setup notes, the `GOTHIC_HVR_INSTALLED`
      text, and "HurricaneVR" in Key Dependencies / VR Framework; describe `Gothic.VR.Interaction` + XRI.
- [x] `README.md`, `CONTRIBUTING.md`: VR works out of the box; the simulator explains how to play without a headset.
      (README links to the wiki; CONTRIBUTING got the note.)
- [ ] Wiki (`../Gothic-Unity.wiki`, edit there, not the in-repo copy): VR setup page, architecture page.
- [x] `Docs/HVR-replacement/`: add a short "Done in <PR>" note to `README.md`; keep the docs as history. (#459)
- [x] Project memory/skills that mention HVR (e.g. `feature-checklist`, `unity-mcp-debug`): update the wording.

## Test plan

1. Fresh clone on a machine that never had HVR: open the project, no compile errors, no missing scripts in the VR
   scenes.
2. Play the Bootstrap scene with `GameControls = VR` + simulator: new game, walk, grab, backpack, chest, door, ladder,
   dialog, fight.
3. CI: Windows64, Pico and Quest builds green without secrets for HVR.
4. Install the three builds: one full playtest loop each (G1 + G2 start areas).
5. Flat mode: unaffected.

## Risks

| Risk | Mitigation |
|------|-----------|
| Hidden references (serialized fields typed as HVR classes in scenes) produce missing scripts | Use the GUID scan from 02 (resolve `m_Script` GUIDs against `Assets/HurricaneVR/**/*.meta` before deleting the folder); the result must be empty. |
| Our code reads like a port of HVR (comments, names, teleport line of sight) | Clean-room cleanup above; reviewers check new comments and names for HVR wording. |
| Someone still has HVR locally and commits generated files | `.gitignore` entry + a CI check that fails if `Assets/HurricaneVR` exists in the PR. |
