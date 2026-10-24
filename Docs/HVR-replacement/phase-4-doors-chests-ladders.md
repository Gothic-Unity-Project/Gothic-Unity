# Phase 4 - Doors, chest lids, ladders

**Goal:** replace `HVRPhysicsDoor` (doors and chest lids) and `HVRClimbable` (ladders) on the Xri backend.

**Size:** M (1-2 weeks) · **Depends on:** Phases 0-2 · **Parallel with:** Phase 3 (chest slots)

## Exit criteria

- [ ] Doors open by grabbing and pulling/pushing, are limited to 0-90°, and play the Gothic open/close sounds from the
      door's MDS once per open/close.
- [ ] Locked doors don't move; lock picking unlocks them (`VRContainerDoorPickingInteraction`).
- [ ] Chest lids open 0-110°. The first open spawns the content (Phase 3); closing hides the slots; lock picking works.
- [ ] Ladders can be climbed hand over hand. `IVrRig.IsClimbing` is true while climbing (used by `KeepPlayerWithin`).

## Behaviour spec (what we use from `HVRPhysicsDoor` today)

From `oCMobDoor.prefab` / `oCMobContainer.prefab` and our scripts:

| Setting / API | Door | Chest lid | Used by |
|---------------|------|-----------|---------|
| Hinge axis | Y (`Axis: 1`) | X (`Axis: 0`) | prefab |
| Limits `MinAngle/MaxAngle` | 0 / 90 | 0 / 110 | prefab |
| `Mass` | 10 | 10 | prefab |
| `CloseAngle` (counts as closed below) | 5° | 5° | prefab |
| `SFXOpened/SFXClosed`, `SFXResetThreshold 1` | from the MDS animations `t_S0_2_S1` / `t_S1_2_S0` | same | `VRVobDoor.PrepareSounds`, `VRVobContainer` |
| `Lock()` / `Unlock()` / `Locked` | ✓ | ✓ | `VRContainerDoorPickingInteraction` |
| `Opened` / `Closed` state (polled every frame) | – | ✓ | `VRVobContainerPhysicsChest.Update` → `OnOpened/OnClosed` |
| Grab on the door leaf (`HVRGrabbable`) + `HVRRotationTracker` | ✓ | ✓ | prefab |
| Handle-requires-rotation, door knobs, latch | off | off | – (not needed) |

## Design

### `VrHinge` (backend-neutral component, replaces `HVRPhysicsDoor` for us)

- Adds/configures a `HingeJoint` (axis, limits, spring 0, damper) on the leaf's `Rigidbody` (mass 10, no gravity, as
  HVR's `DisableGravity`).
- `Angle` from `HingeJoint.angle` (or our own measurement relative to the closed rotation). No rotation tracker needed.
- States `IsOpen` / `IsClosed` with hysteresis (`CloseAngle`, `SFXResetThreshold`), and C# events `Opened` / `Closed`
  (no more polling in the chest).
- Sounds: play `OpenClip` when the angle passes `CloseAngle + threshold` upwards, `CloseClip` when it falls back
  below `CloseAngle`; at most once per transition, with a timeout. Clips come from `VRVobDoor` / `VRVobContainer` as
  today.
- `Lock()` / `Unlock()`: locked = rigidbody kinematic at the closed rotation (or joint limits 0/0).
- Optional soft close: below `CloseAngle` and not held → snap to 0 over `CloseOverTime` (0.25 s).
- Implements the `IVrHinge` port from Phase 0, so `VRVobDoor`, `VRVobContainer` and the lock picking don't change.

### Grabbing the leaf

`GothicGrabInteractable` (Phase 2) on the leaf with **VelocityTracking**: the joint constrains the motion, and the
hand drags the leaf physically. No position tracking of the hand → hinge, which avoids the classic "door explodes"
problem. Set `trackRotation = false` so only the position drives the leaf (feels like pushing/pulling a handle).

Alternative if VelocityTracking + HingeJoint feels jittery: kinematic leaf with our own angle solve (project the hand
movement onto the hinge plane, clamp to the limits). (Decision 2026-10-22: nothing from UltimateXR is used, not even
as a code reference.)

### Chest

`VRVobContainerPhysicsChest` today **is** an `HVRPhysicsDoor` subclass. Split it:
- `VrHinge` on the lid (this phase).
- `VrChestSlots` (Phase 3) subscribes to `VrHinge.Opened/Closed` and keeps `AlignSockets`, `InitializeContent`,
  `ParseContent` and the spawning code.

### Ladders

- XRI `ClimbInteractable` on the ladder collider of `oCMobLadder.prefab`, plus `ClimbProvider` on the rig (prepared
  in Phase 1).
- While climbing, `GravityProvider` must pause (XRI's mediator does this) and `IVrRig.IsClimbing` returns
  `ClimbProvider` active.
- Gothic ladders have no rungs as separate objects. Use one climbable collider along the ladder; the hand can grab
  anywhere on it.
- Getting off at the top: XRI climbing has no auto-mantle. Add a small `VrLadderExit`: when the head is above the top
  edge and the player pushes forward, move the rig onto the top floor (CharacterController sweep for safety). HVR
  had no auto-mantle either; check today's behaviour in a playtest and match it.

## Work items

- [ ] `VrHinge` (+ `IVrHinge` implementation), `VrLadderExit`.
- [ ] `VRVobContainerPhysicsChest` → `VrChestSlots` (logic) + `VrHinge` (lid); delete the subclassing on the Xri path.
      (3c: `VrChestSlots` exists and has `Open()`/`Close()`; `HvrPhysicsChest` is only the HVR lid. On Xri the lid is
      the interim `VrChestLidToggle` (use = swing open/closed, kinematic, `IVrHinge`) - replace it with `VrHinge`.)
- [ ] **Prefabs (UnityMCP):** `oCMobDoor.prefab` (VrHinge + grab interactable + bridge), `oCMobContainer.prefab`
      (lid), `oCMobLadder.prefab` (ClimbInteractable + bridge).
- [ ] `VRContextInteractionService.KeepPlayerWithin` uses `IVrRig.IsClimbing`; verify with a dialog at a ladder.

## Test plan

1. Old camp: open/close several doors with one hand, push with the body (the CharacterController must not push the
   door through its limits), close slowly → one close sound, close fast → one close sound.
2. Locked door (e.g. a hut in the old camp): doesn't move; pick the lock → haptics per combo → door unlocked, opens.
3. Chest: lid opens to ~110°, content appears; close → slots hidden; reopen → same content, nothing spawned twice.
4. Locked chest with lock picking; broken lock pick → error haptics.
5. Ladders: climb the watchtower in the old camp, step off at the top, climb down, let go mid-way (fall with
   gravity).
6. Teleport while holding a door → the door releases cleanly.

## Risks

| Risk | Mitigation |
|------|-----------|
| VelocityTracking + HingeJoint jitter | Try kinematic angle solving (see above) before adding tuning knobs. |
| The player's CharacterController pushes doors | Put door leaves on a layer the CharacterController doesn't push against, or make the leaf kinematic while not held. |
| Ladder dismount feels worse than HVR | Playtest HVR first and record the expected behaviour in the PR. |
