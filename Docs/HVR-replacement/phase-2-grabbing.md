# Phase 2 - Grabbing (items, mobs, NPCs, distance grab, hover)

**Goal:** on the Xri backend, everything that is an `HVRGrabbable` today works through `VrInteractable` +
`XriInteractableBridge`: picking items up (inventory), melee weapons, distance grab, hover focus, using mobs, and
talking to or looting NPCs.

**Size:** M-L (2 weeks) · **Depends on:** Phase 0 + Phase 1 · **Parallel with:** Phases 3, 4

## Exit criteria

- [x] Item pickup by hand and by distance grab adds to the inventory exactly once; two-hand grab doesn't double-count;
      drop removes. (2a. Note: world items have `Amount = 0` in the ZEN, so `VRPlayerService` adds 0 of them on both
      backends; verified with `Amount = 1` set at runtime. Pre-existing, not fixed here.)
- [x] Melee: weapon velocity detection (`VrWeaponAttackDomain`) triggers hits with the same thresholds as on HVR.
      (2b: thresholds unchanged, no per-backend value. XRI's VelocityTracking with `velocityDamping = 1`,
      `velocityScale = 1` and no velocity limit sets the Rigidbody's velocity to (target - position) / fixedDeltaTime
      every physics step, so the measured speed is the hand speed at the item's pivot, without the lag of HVR's
      jointed physics hand. A swing reaches 2 m/s at least as easily as on HVR. Hand side verified (`_handlesRightHand`
      for the right Near-Far). Feel and false positives from tracking jitter still need a headset playtest.)
- [x] Hover shows the name label and highlight (`VRFocus`). (2a: items. 2b: mobs; NPCs show their health bar.)
- [x] Grab on NPC = talk / skip line; grab on a dead NPC = loot panel; grab on a mob = use it.
      (2b: verified with the simulator: Diego talks, the loot panel toggles on a dead NPC, fire and wheel are used.
      Skipping a line goes through the same branch (`IsInDialog`), but no line was spoken while testing.)
- [x] Held/socketed items don't push the player's `CharacterController`. (2a: held items; XRI ignores the
      CharacterController of the selecting rig on top of `VRPlayerService.IgnorePlayerCollision`. Sockets in phase 3.)
- [x] Swimming disables grabbing and hovering (`IVrRig.SetGrabbingEnabled(false)`). (2a; UI pointing stays.)

## Split

- **2a - items** (`engine/hvr-replacement-2a`): Near-Far per hand, `GothicGrabInteractable`, `XriInteractableBridge`,
  `XriGrabberBridge`, `XriLineOfSightFilter`, `oCItem.prefab`, hover, swim toggle.
- **2b - mobs and NPCs**: `XRSimpleInteractable` + bridge on mobs/`oCNpc` (removes the `ForceRelease()` workaround in
  `VRNpc.OnGrabbed`), melee velocity tuning, dig spots, `VRMouth` on the XRI camera.

### 2a notes (where reality deviated)

- `Kind` doesn't come from `selectionRegion`: with `farAttachMode = Near` (pull to the hand, like HVR's force grab)
  a far grab has no attach offset, so Near-Far reports `Near` for it. `XriGrabberBridge` decides by the distance
  hand → closest collider point at selection (near caster radius + 5 cm). Release events reuse the kind of the grab.
- `XriGrabberBridge.Kind` is always `Hand` (one interactor per hand does both); the per-event `VrGrabArgs.Kind` says
  `Hand` or `Distance`. So `VRPlayerWeaponInteraction` (filters grabbers by `Kind == Hand`) also gets distance grabs,
  which end in the hand on XRI.
- Prefabs carry both backends. `IVrInteractableBackend.IsActive` (bridges `[Inject] IVrBackend`) picks the bridge;
  `VrInteractable` uses the active one. `GothicGrabInteractable` is **disabled in `oCItem.prefab`** and only
  `XriInteractableBridge.Start` enables it on Xri (after collecting the runtime colliders), so the Hvr backend never
  creates an `XRInteractionManager`. `SetupColliders()` re-collects and re-registers (disable/enable) when not held.
- `unparentTransformOnGrab = false`: `VRPlayerService` finds the item's `VobLoader` via `GetComponentInParent`.
- Hover: Near-Far only ever has one valid target (closest). The bridge forwards hovers of hands only and counts
  them: `HoverEntered` for the first hand, `HoverExited` for the last. Near-Far stops hovering while it selects,
  so the label goes away once the item is in the hand (same order as HVR: hover, grab, hover exit).
- Layer masks: near caster `VobItem, VobItemNoWorldCollision, VobNpcOrMonster, VobMovable, Grabbable`; far caster
  the same + `Default, UI` (far ray also does UI, cast distance 30 m like the 1c UI ray); line of sight blocked by
  `Default, VobItem, VobNpcOrMonster, VobMovable, Grabbable`, grab/hover range 10 m (`XriLineOfSightFilter`).
- Held items get `RigidbodyInterpolation.Interpolate` (restored on drop). Throw: XRI defaults (`throwOnDetach`,
  `throwVelocityScale` 1.5), not tuned against HVR yet.
- Interim hand visual: a 4 cm sphere (`Hand Placeholder`, URP Lit, no collider) under each Near-Far interactor.

### 2b notes (where reality deviated)

- Mobs (`oCMobBed/Fire/Inter/Switch/Wheel`, so also the `oCMobInter/Seat` and `DigSpot` variants) and `oCNpc`
  (`BIP01/RootMotionCollider`) got `XRSimpleInteractable` (disabled in the prefab, default settings, select mode
  Single) + `XriInteractableBridge` next to `HVRGrabbable`/`HvrInteractableBridge`. Doors, chests and ladders
  (phase 4) have no XRI bridge yet: their `VrInteractable` has no active backend on Xri.
- `IVrInteractableBackend.IsSelectOnly` / `VrInteractable.IsSelectOnly`: true for XRI simple interactables (select =
  use/talk, nothing moves, no `BeforeGrabbed`), false on HVR. `VRNpc` only does the `ForceRelease()` +
  `DisablePhysicsForNpc()` workaround when it's false, so HVR behaves as before. On XRI the selection lasts while
  the grip is held; the next grip press talks again or skips a line. Mobs keep the selection while held on both
  backends (`VRMobInteraction` resets on release).
- `XriColliderUtility.TryGetClosestPoint` replaces `XRInteractableUtility.TryGetClosestPointOnCollider` in
  `XriGrabberBridge` and `XriLineOfSightFilter`: Unity's `Collider.ClosestPoint` doesn't support non-convex
  `MeshCollider`s (mobs) and logged a warning per frame while pointing at one. Non-convex colliders use their bounds.
- Dig spots: no change. `VRDigSpot` reads `VRPlayerService.GrabbedItemLeft/Right`, filled from
  `VrGrabArgs.HandSide` = the Near-Far's `handedness` (`XriGrabberBridge.HandSide`).
- `VRMouth`: the `Mouth` GameObject (VRMouth, trigger SphereCollider r = 2 at scale 0.05 -> 10 cm, AudioSource,
  layer TransparentFX) is copied from the HVR rig's camera to `Main Camera` of `VR - Player XRI.prefab`. It's
  backend neutral (trigger + held item's Rigidbody). Verified: berries held to the mouth are eaten.
- Not caused by XRI (the code path is backend neutral), not fixed: an item eaten while held throws a `MissingReferenceException` in
  `VobMeshCullingDomain.StopTrackVobPositionUpdatesDelayed` (the release starts a delayed coroutine on the
  destroyed item).

## Mapping

| Today (HVR) | Xri |
|-------------|-----|
| `HVRGrabbable` on `oCItem` (PoseType PhysicPoser, HoldType ManyHands, ForceGrabbable, RequireLineOfSight) | `XRGrabInteractable`, **movementType = VelocityTracking**, `selectMode = Multiple`, `XRGeneralGrabTransformer` (two hands), `useDynamicAttach = true` (hand grabs where it touches; no authored attach points needed for runtime meshes) |
| `HVRHandGrabber` | `NearFarInteractor` per hand (near = direct grab, far = distance grab + UI). It replaces Phase 1's UI-only ray. |
| `HVRForceGrabber` (pull to hand) | Near-Far far-select; the interactable flies to the attach point. Pull speed via `attachEaseInTime`. Line of sight: our own `XRBaseTargetFilter` doing a linecast hand → target (excluding the item's own colliders). |
| `HVRGrabbable` on mobs (`oCMobBed/Fire/Inter/Switch/Wheel`) and `oCNpc` | `XRSimpleInteractable` (select = "use/talk", nothing moves). This removes the "grab then `ForceRelease()` immediately" workaround in `VRNpc.OnGrabbed`. |
| `HVRGrabbable.HoverEnter/Exit` | `hoverEntered/hoverExited` → `VrInteractable.HoverEntered/Exited` |
| grabber `BeforeGrabbed` | No direct XRI equivalent. Options: (a) `XRGrabInteractable` subclass overriding `OnSelectEntering` (called before `selectEntered`), or (b) a select filter that runs our callback in `Process`. Use (a): `GothicGrabInteractable : XRGrabInteractable`. |
| `Grabbed` / `Released` | `selectEntered` / `selectExited` |
| `HVRGrabbable.SetupColliders()` (`VRVobItem.Start`) | XRI collects colliders on `Awake`/registration. Our items get the `MeshCollider` at runtime, so call `interactionManager.UnregisterInteractable` + `RegisterInteractable` after the collider exists, or create the interactable after the collider. |
| `HVRGrabbable.Colliders` | `XRBaseInteractable.colliders` |
| `IsBeingHeld` | `isSelected` |
| Per-hand `AllowGrabbing/AllowHovering` | `NearFarInteractor.enabled` / `allowSelect` / `allowHover` |

### `VrGrabArgs.Kind` with Near-Far

The same interactor does both near and far, so `Kind` comes from `NearFarInteractor.selectionRegion`
(`Near` → `Hand`, `Far` → `Distance`) at selection time. HVR raised two grabs for distance grab (force grabber,
then hand grabber); XRI raises one. Keep `VRPlayerService`'s duplicate guard anyway; it's harmless.

## Item flow (unchanged game logic)

`VRPlayerItemAdapter.OnBeforeGrabbed` enables physics (Gothic items lie kinematic) → `VRVobItem.OnGrabbed`:
ghost layer `VobItemNoWorldCollision` + transparency for 1 s, culling tracking, `VRPlayerService.SetGrab` (inventory)
→ release reverses it. These adapters only change their subscription (done in Phase 0). Check:

- VelocityTracking needs a non-kinematic rigidbody **before** the first fixed update of the grab. That's what
  `OnBeforeGrabbed` already guarantees.
- `throwOnDetach` stays on (throwing items). Tune `throwVelocityScale` against HVR's feel.
- Ghost layer: the `VobItemNoWorldCollision` layer must still collide with nothing in the world during the grab
  but also not block the interactor's physics queries. Check the Near-Far casting mask.

## Melee (`VrWeaponAttackDomain`, `VRWeaponService`, `VRPlayerWeaponInteraction`)

- The velocity source stays `weaponRigidbody.linearVelocity - characterController.velocity`
  (`VrWeaponAttackDomain.cs:361`). It works because VelocityTracking keeps the weapon dynamic.
- Tune `velocityDamping`, `velocityScale`, `angularVelocityDamping` and `angularVelocityScale` of
  `XRGrabInteractable` so a fast swing reaches `WeaponPhysicsConfig.WeaponVelocityThreshold`. If XRI's damping lowers
  peak speeds, lower the threshold per backend rather than changing the HVR values.
- Heavy two-handers: HVR had per-grabbable hand strength. XRI has no "strength"; VelocityTracking always reaches the
  target. Optional: scale `velocityScale` by item weight (Gothic `ItemInstance` has no mass; use the weapon category).
- Hand side for `VRWeaponService.OnGrabbed(handSide, …)`: from the interactor's handedness
  (`InteractorHandedness`) → `VrHandSide`.

## Hover focus (`VRFocus`)

Subscribe to `VrInteractable.HoverEntered/Exited`. Only show it for the **closest** hovered target per hand:
Near-Far already sorts targets, so react to the first valid target only (avoid label flicker when several items
overlap).

## Dig spots (`VRDigSpot`)

After Phase 0 it asks `VRPlayerService` for the held items. No XRI work beyond verifying the left/right mapping.

## Prefab work (UnityMCP only)

| Prefab | Change |
|--------|--------|
| `Vobs/oCItem.prefab` | add `GothicGrabInteractable` (VelocityTracking, dynamic attach, multiple select, throw on detach) + `XriInteractableBridge`. Keep the HVR components while both backends exist; only the bridge for the active backend initialises (it checks `VrInteractionBackend`). |
| `Vobs/oCMob{Bed,Fire,Inter,Switch,Wheel}.prefab`, `Vobs/oCNpc.prefab` | add `XRSimpleInteractable` + bridge |
| `VR - Player XRI.prefab` | Near-Far interactor per hand, layer masks (`Grabbable`, `VobItem`, `VobNpcOrMonster`, mob layers) |

Both backends on one prefab double the component count for a while. That's acceptable until Phase 6 removes the
HVR ones.

## Test plan (Xri backend; repeat the relevant steps on Hvr to catch regressions)

1. Pick up apple, sword, torch: inventory +1 (check in the backpack / status). Drop: -1.
2. Hold the sword with both hands, release one hand: no inventory change. Release both: -1.
3. Distance grab an item behind a fence: blocked by line of sight. In the open: flies to the hand.
4. Pull an item out of a chest that it was overlapping: no getting stuck, ghost transparency for ~1 s.
5. Melee a scavenger: hits register on fast swings only; fight-window haptics on the weapon hand.
6. Hover over many items on a table: one label at a time, per hand.
7. Grab an NPC: dialog starts; grab during the dialog: line skipped; dead NPC: loot panel.
8. Bed (sleep), wheel, switch, fire (cook): mob use triggers; the seated NPC stays kinematic.
9. Swim: no grab or hover possible; leave the water: possible again.
10. Throw an item: flies plausibly far.

## Risks

| Risk | Mitigation |
|------|-----------|
| Hand and item separate visually (no physics hands: 5d is skipped) | Accept, or attach the hand model to the item while held (`XRGrabInteractable` → parent the hand visual to the attach transform). That's a cheap interim fix worth doing. |
| VelocityTracking jitter while walking | Turn on rigidbody interpolation for held items and test XRI's visual-smoothing options for velocity-tracked grabs. Test while running. |
| API names in this doc drift from the pinned XRI version | Names here follow XRI 3.x docs. Verify each one with `unity_reflect` / the package docs before implementing. |
| Runtime colliders not registered | Register the interactable after `VobItem` creation (see the mapping table). Add a log warning if `colliders.Count == 0` on hover registration. |
