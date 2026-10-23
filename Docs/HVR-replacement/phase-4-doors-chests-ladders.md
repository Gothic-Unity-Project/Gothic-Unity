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
- [x] Chest lids open 0-110°. The first open spawns the content (Phase 3); closing hides the slots; lock picking works.
- [x] Ladders can be climbed hand over hand. `IVrRig.IsClimbing` is true while climbing (used by `KeepPlayerWithin`).
      (4c: `ClimbInteractable` + `ClimbProvider`; stepping off at the top by `VrLadderExit`.)

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
      joint. 4b moved `Opened`/`Closed` onto `IVrHinge`; `HvrHingeBridge` raises them from `HVRPhysicsDoor.Opened/Closed`.)
- [x] `VrLadderExit` (4c: `Interaction/Components/VrLadderExit.cs`, see status after 4c).
- [x] `VRVobContainerPhysicsChest` → `VrChestSlots` (logic) + `VrHinge` (lid); delete the subclassing on the Xri path.
      (3c: `VrChestSlots` exists and has `Open()`/`Close()`; `HvrPhysicsChest` is only the HVR lid. On Xri the lid is
      the interim `VrChestLidToggle` (use = swing open/closed, kinematic, `IVrHinge`) - replace it with `VrHinge`.
      4b done: `VrChestLidToggle` deleted, the lid has `VrHinge`; `VrChestSlots` subscribes to the active `IVrHinge`
      (`VrHinges.FindActive`), `HvrPhysicsChest` is only the "switch off on other backends" shell.)
- [ ] **Prefabs (UnityMCP):** `oCMobDoor.prefab` (VrHinge + grab interactable + bridge) (4a done: the VR prefab's door
      leaf got `VrHinge`, `GothicGrabInteractable` (disabled, Kinematic movement, tracking off, no unparenting) and
      `XriInteractableBridge`; `HvrHingeBridge` now switches `HVRPhysicsDoor` off on other
      backends), `oCMobContainer.prefab` (lid, 4b: done, see below), `oCMobLadder.prefab` (ClimbInteractable + bridge, 4c: done, see below).
- [x] `VRContextInteractionService.KeepPlayerWithin` uses `IVrRig.IsClimbing` (it already did since Phase 0/1; now `XriRig.IsClimbing`
      is real). Not verified with a dialog at a ladder.

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

## Status after 4b

Xri + simulator, scripted hand (`TrackedPoseDriver` of the controller disabled, hand moved along the lid's arc,
`SelectEnter`/`SelectExit`), G1 small crate near the start (51, 60, 287), editor at a few fps:
- **Prefab:** the lid (`BIP01 CHEST_*_1`) lost `XRSimpleInteractable` and `VrChestLidToggle`; it got `GothicGrabInteractable`
  (disabled, copy of the door's settings) and `VrHinge` (axis X, 0-110°). `XriInteractableBridge` was already there.
- **Events on the port:** `IVrHinge.Opened/Closed`. `HvrHingeBridge.Update` raises them from `HVRPhysicsDoor.Opened/Closed`
  (same flags as `HvrPhysicsChest` had), so `VrChestSlots` is backend-neutral (`Start` subscribes to the active hinge).
  `HvrPhysicsChest` no longer calls the slots itself (the Hvr chest was not played again: only the event path moved).
- Hand pulls the lid 0 → 115°: the lid follows 1:1 (0.2° behind), clamps at 110°; content (5 items) appears on the first
  `Opened`; released at 110° it stays (no bounce).
- Took one item by hand (4 left), closed by hand 110 → 0°: `Closed` once, slots hidden; opened again: the same 4 items,
  nothing spawned twice.
- `Lock()`: the lid snaps to 0° and the slots hide, a hand pulling to 60° does not move it; `Unlock()` pushes it open to
  ~14° after a moment and opens the slots.
- Not tested: a real pick-lock sequence by hand, the open/close sounds audibly (set by `VRVobContainer` via
  `VrHinges.FindActive`, played by `VrHinge` from the state change), a headset, the Hvr chest (HVR physics) after the
  event move, a lid hanging open when the chest is re-created by save/load.
- Known differences to the interim toggle: the lid has to be pulled by hand (use = grab no longer opens it by a click);
  the lid has no gravity (stays where it's let go, closes softly only below 5°).

## HVR ladders today (behaviour only)

`oCMobLadder.prefab` has an `HVRGrabbable` + `HVRClimbable` on the ladder's collider (one mesh collider along the whole
ladder, the hand can grab anywhere on it). A hand that holds a climbable makes `IsClimbing` true; the player controller
then moves the rig by the opposite of the hands' movement (both hands: summed, clamped to the faster hand) through the
CharacterController, and teleporting is off while climbing. Gravity does not pull meanwhile; letting go falls. There is
no auto-mantle: at the top the player lets go and walks (or is pushed) onto the floor by hand. Not replayed on Hvr again.

## Status after 4c

Xri + simulator, scripted hand (the Near-Far interactor's transform moved, `SelectEnter`/`SelectExit` via the interaction
manager), G1 `LADDER_4.ASC` at (34.7, 60.3, 323.1), 4.9 m, editor at a few fps:
- **Rig:** `ClimbProvider` next to the other providers on the rig's "Locomotion" object, wired to `XriRig._climbProvider`
  (it forces gravity off through the `GravityProvider` lock while a hand holds a `ClimbInteractable`).
  `XriRig.IsClimbing` = the provider has a climb anchor. `providersToDisable` stays empty: `XriRig` itself switches the
  move and jump providers off while climbing and restores what the game set (`MovementEnabled`/`CanJump` are stored, so
  a dialog that disables movement is not undone by the end of a climb).
- **Prefab:** `BIP01 LADDER` (the one collider along the ladder) got `ClimbInteractable` (disabled, multiple selection,
  range 0.15 m), `XriInteractableBridge` (enables it at `Start`; hover label/brightness through `VRFocus` unchanged) and
  `VrLadderExit`. `ClimbInteractable` demands a `Rigidbody` (kinematic, no gravity); for `HVRGrabbable` next to it that
  is the same as before (it already set itself `Stationary` without a body).
- **Distance:** XRI measures the climb range to the collider's transform (the ladder's base) or needs
  `Collider.ClosestPoint` (not supported by concave mesh colliders). `XriInteractableBridge` therefore sets
  `getDistanceOverride` to the distance to the collider's bounds for `ClimbInteractable` (bounds of a vertical ladder are
  tight enough).
- **`VrLadderExit`** (backend-neutral, inactive on Hvr): counts the hands on the ladder (`VrInteractable.Grabbed/Released`).
  When the head is 0.15 m above the highest point of the ladder's colliders, it looks for a floor 0.5 m, then 0.8 m in
  front of the head (ray down, normal up, within 0.5 m of the top edge) with room for the CharacterController (capsule
  overlap against the player's colliding layers). Found: the hands let go and `IVrRig.Teleport` puts the player there
  looking the same way; not found: stays on the ladder, a one-time warning, retry every 0.5 s. There is no
  thumbstick-forward condition: `IVrInput` has no stick axis, and the head rising over the edge is the signal.
- Hover on the ladder with the hand at its surface works. Grab at 1.2 m, pulled the hand down relative to the rig in
  steps of 0.4-1.2 m: the rig follows 1:1 and is not pulled down meanwhile (gravity lock), `IsClimbing` true, move/jump
  providers off. Released at 62.1 and at 63.0 m: `IsClimbing` false, falls with gravity (63.0 → 60.2 m within two
  frames), providers restored.
- At the top (test platform at the ladder's top edge, created at runtime): head over the edge → the player stands on it
  0.9 m further, `IsClimbing` false, grounded, move provider back on.
- Not tested: a real hand-over-hand climb with two hands (one hand), climbing down, the Hvr ladder (not replayed), a
  headset, thumbstick/teleport input while climbing (`TeleportEnabled` isn't touched; a teleport from code lets go of
  the ladder first (review fix: `XriRig.ReleaseClimb()`, otherwise the ClimbProvider pulls the rig back), untested), the exit in the real world: the two ladders around the start had no adjacent floor at the top edge (they
  belong to a different structure), so the exit rule was only run against a platform.
- Known differences to HVR: the player steps onto the top floor automatically; the climb sticks to the hand position and
  not to the CharacterController's collision (XRI's body transformer still stops at walls).

## Risks

| Risk | Mitigation |
|------|-----------|
| VelocityTracking + HingeJoint jitter | Try kinematic angle solving (see above) before adding tuning knobs. |
| The player's CharacterController pushes doors | Put door leaves on a layer the CharacterController doesn't push against, or make the leaf kinematic while not held. |
| Ladder dismount feels worse than HVR | HVR has no mantle; ours steps off only with a floor and room (see status after 4c). |
