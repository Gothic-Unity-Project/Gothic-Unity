# Phase 4 - Doors, chest lids, ladders

**Goal:** replace `HVRPhysicsDoor` (doors and chest lids) and `HVRClimbable` (ladders) on the Xri backend.

**Size:** M (1-2 weeks) · **Depends on:** Phases 0-2 · **Parallel with:** Phase 3 (chest slots)

Split into three PRs: **4a** `VrHinge` + doors on Xri (lock picking, soft close, hand break distance), **4b** chest lid
on `VrHinge` (replaces the interim `VrChestLidToggle`), **4c** ladders + `KeepPlayerWithin`/`IsClimbing`.

## Exit criteria

- [x] Doors open by grabbing and pulling/pushing, are limited to 0-90°, and play the Gothic open/close sounds from the
      door's MDS once per open/close. (4a; the Gothic sounds are handed to the hinge by `VRVobDoor`, played once per
      change with a 0.5 s timeout.)
- [x] Locked doors don't move; lock picking unlocks them (`VRContainerDoorPickingInteraction`). (4a: `Lock()` holds the
      leaf at 0° and a hand can't pull it open; the picking script is unchanged. Unlock + push: see status below.)
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
movement onto the hinge plane, clamp to the limits). (4a: this is what `VrHinge` does, see the status below.)
(Decision 2026-10-22: nothing from UltimateXR is used, not even as a code reference.)

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

- [x] `VrHinge` (+ `IVrHinge` implementation). (4a: `Interaction/Components/VrHinge.cs`, kinematic angle solver, no
      joint. `Opened`/`Closed` C# events are on `VrHinge`, not on `IVrHinge` yet (4b adds them with the chest).)
- [ ] `VrLadderExit` (4c).
- [ ] `VRVobContainerPhysicsChest` → `VrChestSlots` (logic) + `VrHinge` (lid); delete the subclassing on the Xri path.
      (3c: `VrChestSlots` exists and has `Open()`/`Close()`; `HvrPhysicsChest` is only the HVR lid. On Xri the lid is
      the interim `VrChestLidToggle` (use = swing open/closed, kinematic, `IVrHinge`) - replace it with `VrHinge`.)
- [ ] **Prefabs (UnityMCP):** `oCMobDoor.prefab` (VrHinge + grab interactable + bridge) (4a done: the VR prefab's door
      leaf got `VrHinge`, `GothicGrabInteractable` (disabled, Kinematic movement, tracking off, no unparenting) and
      `XriInteractableBridge`; `HvrHingeBridge` now switches `HVRPhysicsDoor` off on other
      backends), `oCMobContainer.prefab` (lid, 4b), `oCMobLadder.prefab` (ClimbInteractable + bridge, 4c).
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

## Status after 4a

Xri + simulator, programmatic hand (hand controller moved by script, `SelectEnter`/`SelectExit` on the interactor),
G1 old camp doors at (302, 4.5, 140) and (183, -2, 291, locked), editor at a few fps (so only logic was judged, not
feel):
- **Decision: kinematic angle solving instead of velocity tracking.** The first version (leaf on a `HingeJoint` /
  `ConfigurableJoint` to an anchor body, hand drags it by velocity tracking) was played first: `HingeJoint.angle` has
  an arbitrary zero (limits outside -180..180 break), and with the joint the leaf overshot 8° past the limit, jammed
  and recoiled when released. `VrHinge` now rotates the kinematic leaf itself: the hand's angle around the hinge axis
  (in the hinge plane) turns the leaf, clamped to 0-90°; after letting go it swings on with the hand's speed and
  stops at the limits. `GothicGrabInteractable` on the leaf only selects (Kinematic movement, position and rotation
  tracking off). Side effect: the CharacterController can't push the leaf (risk "CharacterController pushes the
  door" is solved by design), no `Rigidbody` dynamics, no recoil.
- Pull open with a hand moving 25°/s: leaf follows 1:1 up to 75°, back to closed; `Opened` once, release at 3° →
  soft close to 0° in 0.25 s and `Closed` once. Sweep of the hand to 125°: leaf clamps at 90°. Released while moving
  at ~50°/s: swings to 90° and stays without overshoot.
- Open/close clips are set by `VRVobDoor` (`Door_open01`/`Door_close01b`), played from the state change (not heard).
- `Lock()`: leaf snaps to 0° and a hand pulling 40°/s doesn't move it; `Unlock()` gives a push open (~60°/s, comes
  to rest at ~20°). The real locked door (hut at 183, -2, 291) gets `Lock()` from `VRContainerDoorPickingInteraction`
  (`IsLocked` true). The picking script pushes only non-kinematic hinges (HVR), `VrHinge`
  pushes itself in `Unlock()`.
- Hand farther than 1 m from the leaf (teleport) lets go of it.
- Hvr backend: `HVRPhysicsDoor` is disabled on Xri (`HvrHingeBridge.Awake`), `VrHinge` disables itself on Hvr
  (checked by the components' state at runtime on Xri; the Hvr door was not played).
- Not tested: a real pick-lock sequence by hand (haptics), closing a door by hand with sound audible, a headset
  (hand tracking noise: the hand angle is used raw), Hvr door play test, two hands on one door (first hand wins).
- Known differences to HVR: the leaf has no physical collision with hands (Xri hands aren't colliders), so touching
  a door without grabbing doesn't move it, and the player's body doesn't push it.

## Risks

| Risk | Mitigation |
|------|-----------|
| VelocityTracking + HingeJoint jitter | Try kinematic angle solving (see above) before adding tuning knobs. |
| The player's CharacterController pushes doors | Put door leaves on a layer the CharacterController doesn't push against, or make the leaf kinematic while not held. |
| Ladder dismount feels worse than HVR | Playtest HVR first and record the expected behaviour in the PR. |
