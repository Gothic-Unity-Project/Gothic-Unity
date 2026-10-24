# Phase 5 - Hands (models, finger curls, procedural posing, physics hands)

**Goal:** hands that look and feel at least as good as HVR's on the Xri backend: our own hand meshes, fingers
driven by input and finger tracking, fingers wrapping around any runtime-loaded Gothic item, and optional physics
hands that don't pass through walls.

**Size:** L-XL (3-5 weeks) · **Depends on:** Phases 1-3 · **Riskiest phase** (see 04, "What we accept")

> **Decision 2026-10-22 (team): Phase 5 is on hold, 5d is skipped.**
> - **5d physics hands: skipped.** Not part of the HVR replacement. Hands follow the controllers directly (Phase 2
>   state incl. the interim "parent the hand visual to the held item" fix).
> - **Hand models: Gothic's own fist meshes**, loaded at runtime via ZenKit like every other asset (future task).
>   **Nothing is imported or copied from UltimateXR** - no hand meshes, no pose editor, no code. The UXR references
>   below are history.
> - 5a-5c are designed for articulated hands. With fist-only meshes, finger curls (5b) and procedural finger posing
>   (5c) probably don't apply; re-plan them once the Gothic fist meshes are in. Until then the Xri rig shows a simple
>   placeholder (or no) hand visual.
> - Phase 6 doesn't wait for Phase 5.
>
> The rest of this document is the original plan, kept for reference.

Split into four parts that ship separately. 5a-5c are required, 5d is optional.

| Part | Content | Size |
|------|---------|------|
| 5a | Hand models + rig + `VRHandsOnTop` | S |
| 5b | `FingerCurlProvider` (buttons, touches, XR Hands) | S-M |
| 5c | `ProceduralHandPoser` + few authored poses | M-L |
| 5d | ~~`PhysicsHand` (joint-driven), ghost hand, teleport handling~~ **skipped** | L |

## Exit criteria

- [ ] No HVR hand/controller meshes, poses or settings are referenced by the Xri rig.
- [ ] Fingers react to grip, trigger and capacitive touch on Quest/Pico/Touch; on Index (and Steam Frame, if its
      runtime exposes hand joints, see 5b), middle/ring/pinky follow the real fingers.
- [ ] Holding any Gothic item (sword, apple, torch, bottle, scroll, ring) shows fingers closed around it, not inside
      it and not floating.
- [ ] 5d: with "physics hands" on (immersion setting), hands stop at walls, chests and doors; held items too; a ghost
      hand shows the real controller pose when separated.

## 5a - Hand models

- **Source:** UltimateXR hand meshes (`ultimatexr-unity/Runtime/Art/Avatars/BigHands`, `SmallHands`, MIT). Copy
  them into `Gothic-VR/Art/Hands/` with `THIRD-PARTY-NOTICES` (MIT text, VRMADA copyright). Alternative: any CC0
  rigged hand. Avoid Meta/Oculus hand assets (license).
- Rig: wrist + 3 bones per finger + thumb. Write a `VrHandSkeleton` component that maps the bones once (name pattern
  per model), so the poser and the curl logic don't depend on bone names.
- Materials: URP Lit, plus the on-top variant for `VRHandsOnTop` (render queue 3950, as today).
- Hand layer stays `Hand` (`Constants.HandLayer`). `VRFistAttackAdapter` resets the attack collider layer; keep it.
- Controller models: not needed (the hands replace them). The simulator shows hands as well.

## 5b - Finger curls (`FingerCurlProvider`)

Replaces `HVRHandAnimator` + `HVRFingerSettings` + `HVROpenXRFingerCurls`.

- Output: `float[5]` curls per hand (thumb…pinky, 0 = open, 1 = fist).
- Input source per finger (configurable ScriptableObject `VrFingerSettings`, replacing `HVR_FingerSettings.asset`):

  | Finger | Source |
  |--------|--------|
  | Thumb | max(primary/secondary/thumbstick touch) → 0.6-0.8 |
  | Index | trigger value (+ trigger touch → 0.2) |
  | Middle, ring, pinky | grip value |

- Finger tracking: if `XRHandSubsystem` is running and the hand is tracked, compute curls from joint angles
  (metacarpal → tip) and **override middle/ring/pinky** (as HVR does today for Knuckles, fingers 2-4). Index and
  thumb stay on buttons, because the trigger/joystick need them.
  - Index under SteamVR OpenXR exposes controller-driven hand joints through `XR_EXT_hand_tracking`. That's what HVR
    uses today via `USING_XRHANDS`.
  - Steam Frame: verify on a device that its runtime exposes the same. If yes, it works without extra code.
  - Quest/Pico controller-free hand tracking is out of scope (HVR doesn't support it either). The provider design
    allows adding it later.
- The pose blend per finger: `curl` lerps between the open and the fist pose (two `VrHandPose` assets), unless a
  grab pose overrides that finger (5c).
- Swimming: `IVrRig.SetGrabbingEnabled(false)` also forces open hands (today `VRPlayerController.ChangeGrabbing`
  disables poses).

## 5c - Procedural hand posing (`ProceduralHandPoser`)

Replaces `HVRPhysicsPoser` (`PoseType: PhysicPoser` on every `oCItem`). It's needed because no one can author
poses for thousands of runtime-loaded Gothic items.

Algorithm (one-shot on grab, not every frame):
1. Hand attach: with XRI dynamic attach (Phase 2), the item is held where the palm touched. Align the palm normal
   to the closest point on the item's collider (`Collider.ClosestPoint` from the palm center), and offset the palm by
   a small skin distance.
2. For each finger, iterate the bones from proximal to distal: rotate the bone in steps (e.g. 5°) around its curl
   axis until a `Physics.ComputePenetration`/`CapsuleCast` of the bone capsule against **the item's colliders only**
   hits, then step back one increment. Cap at the fist pose.
3. Thumb: same, but with its opposition axis first.
4. Store the result as a `VrHandPose` and apply it while held (the item doesn't change shape).
5. Cache per item **instance name** + attach-point bucket (e.g. a 2 cm grid in item space). Gothic has many copies of
   the same item, so most grabs are cache hits.

Budget: ~15 bones × ~20 steps × 1 capsule query = ~300 queries per grab, done once. Fine even on Quest. Measure with
the profiler (`unity_profiler_frame`).

Reference implementations to read for ideas (not to copy): HVR's poser (licensed, read only for behaviour); the
UltimateXR Hand Pose Editor (MIT, authored poses).

**Authored poses** (few): backpack strap (today `backpack_left/right_hand_pose.asset`), ladder, door handle (optional).
`VrHandPose` asset = per-bone local rotations; capture them in the editor from the posed hand (a small "Save pose"
inspector button) instead of porting a full pose editor.

## 5d - Physics hands (optional, immersion setting) - SKIPPED (see decision at the top)

Replaces `HVRJointHand` + `HVRHandPhysics` + `HVRGhostHand` + the hand part of `HVRTeleportCollisonHandler`.

- Physics hand = `Rigidbody` (interpolated, continuous dynamic collision) with simple colliders (palm box + finger
  capsules), driven towards the tracked controller pose every `FixedUpdate`:
  - Position: velocity-based (`rb.linearVelocity = (target - pos) / dt`, clamped to max speed) or a
    `ConfigurableJoint` with drives. Start velocity-based: fewer parameters, and it's what XRI's VelocityTracking
    already does for items.
  - Rotation: angular velocity from the quaternion delta, clamped.
- The visible hand follows the physics hand. A ghost hand (transparent) follows the controller when the distance is
  > ~5 cm.
- Max distance: if physics hand ↔ controller > ~0.5 m for > 0.2 s (stuck behind a wall), snap the physics hand back
  to the controller (with the held item released if it's stuck, as HVR's `MaxDistanceBehaviour`).
- Teleport and scene changes: after `IVrRig.Teleported`, move the hands and held items with the rig (same offset),
  then sweep; if they overlap geometry, snap to the controller. HVR spent most of its fixes here (see 04 data points).
- Held items: Phase 2 VelocityTracking targets the **physics hand's** attach point, not the controller, so the item
  stops at walls together with the hand.
- Collision layers: hands collide with the world, mobs, doors and NPCs; not with the player's CharacterController,
  not with held items of the same hand.
- Comfort: a setting "physics hands" in the immersion menu (`VRSettingsImmersionMenu`, Gothic.ini key in
  `VRConstants.IniNames`). Off = hands follow the controllers directly (Phase 2 state + interim parenting fix).
- Fist attacks (`VRFistAttackAdapter`, disabled today) become possible with physics hands. Enabling them is a
  separate gameplay task.

## Work items

- [ ] 5a: art import + notices, `VrHandSkeleton`, hand prefabs (UnityMCP), `VRHandsOnTop` renderers.
- [ ] 5b: `VrFingerSettings` asset, `FingerCurlProvider`, `VrHandPose` asset type, open/fist poses.
- [ ] 5c: `ProceduralHandPoser` + cache, authored backpack/ladder poses, an editor "Save pose" button.
- [ ] 5d: `PhysicsHand`, `GhostHand`, teleport handling, immersion setting (C# + menu + ini), retarget VelocityTracking.
- [ ] PlayMode test: poser produces no finger-collider overlaps for a sword, an apple and a bottle (`Gothic.Tests.PlayMode`).

## Test plan

1. Quest 3, Pico 4: grip/trigger/touch animate the fingers; the menu laser hand pose is readable.
2. Index (and Steam Frame): middle/ring/pinky follow the real fingers; the index finger follows the trigger.
3. Grab 20 different items (weapons of each category, food, potions, scrolls, runes, rings, torch, lock pick): fingers
   wrap plausibly; regrab the same item → instant (cache).
4. Backpack strap and ladder use the authored poses.
5. 5d on: punch a wall → the hand stops, ghost visible; pull back → reconnects; hold a sword against a wall → it
   stops; teleport and Marvin "goto" while holding → nothing stuck; dialog + `KeepPlayerWithin` unaffected.
6. 5d off: same as the Phase 2 state.
7. Performance: Quest 3 frame time with 5d on during a fight in the old camp.

## Risks

| Risk | Mitigation |
|------|-----------|
| Poser looks wrong on odd meshes (rings, runes, tiny items) | Fallback: if fewer than 2 fingers make contact, use a generic "pinch" pose for small bounds and a "power grip" for long bounds. |
| Physics hands tuning eats time (HVR history: ~170 release-note lines on this) | Keep 5d optional and off by default until playtests approve it; ship 5a-5c first. |
| Steam Frame doesn't expose hand joints via OpenXR | Fall back to button curls (Index-like capacitive data may still come through the interaction profile). |
