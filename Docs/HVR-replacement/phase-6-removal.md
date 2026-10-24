# Phase 6 - Remove HVR

**Goal:** HVR is gone from the repository, the build and the docs. Every contributor can open the project, build
the VR targets and play them, with or without a headset (simulator).

**Size:** S (2-3 days) · **Depends on:** Phases 1-4 accepted in playtests on Quest, Pico and PCVR (Phase 5 is on hold,
5d physics hands are skipped - see phase-5-hands.md)

## Entry check (go / no-go)

- [ ] Xri backend has been the default for at least one release/playtest round without HVR-only regressions.
- [ ] The playtest checklists of Phases 0-4 pass on Quest 3, Pico 4 and PCVR (Index).
- [ ] Nobody on Discord has open work that depends on HVR components.

## Work items

### Code

- [ ] Delete `Gothic-VR/Scripts/Interaction/Backends/Hvr/` (incl. `VRPlayerController`-based `HvrRig`, `VRSocket`,
      `VRShoulderSocket`, `VRSimulatorControlsGUI`, `VRVobContainerPhysicsChest` HVR version, bridges).
- [ ] Remove `DeveloperConfig.VrInteractionBackend` (C# field **and** asset value), or keep it with only `Xri`
      if a future backend is planned. The removal is easier to read.
- [ ] `VRContextBootstrap`: no backend selection, no `#if`.
- [ ] Remove `GOTHIC_HVR_INSTALLED` from `ProjectSettings/ProjectSettings.asset` (Android + Standalone defines) and
      from every remaining file (`grep -rn GOTHIC_HVR_INSTALLED Assets` = 0).
- [ ] Delete `Gothic-VR/Editor/HurricaneActions.cs` (URP material conversion) and `Gothic-VR/Editor/VRContextToolActions.cs`
      (define toggling).
- [ ] Remove `HurricaneVR.*` references from asmdefs if any (`Gothic.VR.asmdef`, `Gothic.VR.Editor`, Lab, Tests).
- [ ] `grep -rni "hvr\|hurricane" Assets --include=*.cs` = 0 (comments too).

### Packages and assets

- [ ] `Packages/manifest.json`: remove `com.hurricanevrextensions.simulator`.
- [ ] Delete your local `Assets/HurricaneVR/` (it isn't in git: `.gitignore:106`; CI injects it). Keep the ignore
      entry so nobody commits it by accident.
- [ ] **Prefabs (UnityMCP):** remove all HVR components from the 11 VR prefabs (`HVRGrabbable`, `HVRTagSocketable`,
      `HVRPhysicsDoor`, `HVRRotationTracker`, `HVRClimbable`, socket components …), delete `HVR - Player.prefab`,
      `HVRGlobal` instance in `Player.unity` / `Lab.unity`, `UIManager` (HVR) instance.
- [ ] Rename `VR - Player XRI.prefab` → `VR - Player.prefab`.
- [ ] Delete `Gothic-VR/Resources/VR/HVR Settings/`, `GUZ-VR-Socketables.asset`,
      `Player-Elements/BackPack/backpack_*_hand_pose.asset` (no replacement while Phase 5 is on hold), old slot prefabs.
- [ ] Open every VR prefab and scene once and check for "Missing script" (UnityMCP `find_gameobjects` / console).

### Build and CI

- [ ] Remove the private HVR asset-repo checkout/injection from the CI workflow.
- [ ] `pipeline-test-build` label: VR builds (Windows64, Pico, Quest) run for every contributor's PR.
- [ ] `VRBuilderActions`: nothing HVR-specific (check).

### Docs

- [ ] `CLAUDE.md`: remove the "Without HurricaneVR" and "Purple HVR hands" setup notes, the `GOTHIC_HVR_INSTALLED`
      text, and "HurricaneVR" in Key Dependencies / VR Framework; describe `Gothic.VR.Interaction` + XRI.
- [ ] `README.md`, `CONTRIBUTING.md`: VR works out of the box; the simulator explains how to play without a headset.
- [ ] Wiki (`../Gothic-Unity.wiki`, edit there, not the in-repo copy): VR setup page, architecture page.
- [ ] `Docs/HVR-replacement/`: add a short "Done in <PR>" note to `README.md`; keep the docs as history.
- [ ] Project memory/skills that mention HVR (e.g. `feature-checklist`, `unity-mcp-debug`): update the wording.

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
| Someone still has HVR locally and commits generated files | `.gitignore` entry + a CI check that fails if `Assets/HurricaneVR` exists in the PR. |
