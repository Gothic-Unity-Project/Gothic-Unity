# 04 - Recommendation and migration path

## Verdict

**Don't adopt UltimateXR as the base, and don't rebuild everything from scratch.**
Build a thin, Gothic-owned interaction layer (`Gothic.VR.Interaction`) **on top of Unity XR Interaction Toolkit 3.x**,
write the three things no free framework offers ourselves (procedural finger posing, physics hands, Gothic
containers/doors), and **borrow selectively from UltimateXR (MIT)**, mainly its hand art and pose-editor ideas.

### Why (from the data in 01-03)

1. **Our hardest requirements aren't covered by UltimateXR either.** Runtime-loaded items need procedural finger
   fitting (H3), and melee needs held weapons to stay dynamic rigidbodies (H2, `VrWeaponAttackDomain.cs:361`). UXR
   makes grabbed objects kinematic (`UxrGrabManager.Manipulation.cs:286`) and only supports authored poses. We'd
   build H1/H3 in either case, and with UXR we'd also have to rework combat.
2. **Platform reach.** Our stack is OpenXR + Input System + XR Hands on Quest, Pico, and PCVR. XRI uses exactly this
   stack. UXR's device classes expect the Oculus SDK (Quest 3), the PicoXR SDK (Pico), and the SteamVR plugin (Index
   finger skeletons). On OpenXR it falls back to `UxrGenericOpenXRInput`, and it has no XR Hands support, so Index and
   Steam Frame finger tracking would be ours to write anyway.
3. **Maintenance.** UXR has ~109k LoC, targets Unity 2021.2/URP 12, its 1.0 is still "Unreleased", and the last
   commit was 4.5 months ago. Adopting it means forking and maintaining a framework 2.5× HVR's size, most of which
   (networking, state sync, avatars) we don't need. XRI ships as a Unity 6 core package and Unity maintains it.
4. **Contributors.** XRI's Interaction Simulator replaces our HVR-dependent simulator package (H22), so someone
   without a headset can still run the VR scene. UXR has no equivalent.
5. **From scratch is wasteful.** Input mapping, UI raycasting, teleport, turn/move providers, sockets and the
   simulator are solved problems, and XRI covers 13 of our 16 used capabilities natively or partially.

### What we accept

- **No physics hands until Phase 5.** Without them, the hand model follows the tracked controller 1:1, so a real
  hand that moves "into" a virtual wall, chest or door puts the virtual hand through it. With XRI `VelocityTracking`
  the held item (a sword) still stops at the wall while the hand keeps going, so hand and item visibly separate.
  HVR avoids this with a rigidbody hand pulled towards the controller by a joint (`HVRJointHand`), plus a ghost hand
  that shows the real controller pose.
  - What Gothic depends on today: only the look and feel. The one gameplay use, fist attacks through hand
    colliders, is still disabled (`VRFistAttackAdapter.Awake`: "TODO - Implement before enable"). Our player prefab
    doesn't override HVR's hand strength settings, so we run HVR defaults.
  - Size of HVR's solution: ~5.8k LoC directly involved (`HVRHandGrabber` 3,277, `HVRTeleportCollisonHandler` 762,
    `HVRPhysicsPoser` 551, `HVRJointHand` 363, `HVRJointSettings` 223, strength/joint utils ~470) and ~145 public
    tuning fields across them (`HVRHandGrabber` 79, `HVRJointSettings` 36, teleport handler 13, joint hand 12,
    `PDStrength` 6).
  - Maturity: `Documentation/ReleaseNotes.txt` lists ~70 releases (1.1 → 2.9.3). 174 release-note lines mention
    joints, strength, stuck hands, collisions or teleporting with physics hands - for example "hand stuck in
    collision geometry", "physics hands jump after teleporting when Update/Fixed are out of sync", "default wrist
    strength … too under damped". The notes have no dates, so the timespan isn't measurable from the repo.
  - Takeaway: a basic joint hand is small (a few hundred LoC). The cost is in the edge cases: teleport, stuck
    hands, held-object return, two-hand strength, and tuning across Quest/Pico/PCVR frame rates. Treat Phase 5 as
    the phase most likely to overrun, and keep physics hands optional.
- During the transition there are two backends. Phase 0 keeps that cheap.

> **Update 2026-10-22:** physics hands (5d) are skipped, hands will be Gothic's own fist meshes, and nothing is
> taken from UltimateXR. Phase 5 is on hold; see `phase-5-hands.md`.

## Target architecture

```
Gothic.VR (adapters/services: VRVobItem, VRBackpack, VRSwimDive, VrHapticsService, …)
        │  depends only on ↓ (no XRI / HVR types in game code)
Gothic.VR.Interaction  (our abstractions + components)
   ├─ Model:   VrHandSide, VrButton, VrButtonState, VrHapticPreset
   ├─ Ports:   IVrRig (move/turn/teleport/lock/gravity/speeds/sit-stand/fade/camera/hand transforms)
   │           IVrInput (GetButtonState(side, button), menu/both-grips helpers)
   │           IVrHaptics (Vibrate(side, preset))
   │           IVrGrabbable / IVrGrabber events: HoverEnter/Exit, BeforeGrabbed, Grabbed, Released (+ grabber kind:
   │           Hand | Distance | Socket, + HandSide)
   │           IVrSocket / IVrSocketContainer (TryPlace, TryFindFree, IsOccupied, ForceRelease, filter by item category)
   │           IVrUiPointers (register canvas, pointer → hand side)
   ├─ Own components: ProceduralHandPoser, PhysicsHand, VrDoor (hinge/lock/SFX), VrContainerSockets,
   │           VrClimbable bridge, WallFade, FingerCurlProvider (input + XR Hands)
   └─ Backend: XRI implementation (later the only one); temporary HVR implementation during migration
```

Rules: game code never touches `UnityEngine.XR.Interaction.Toolkit` types, so the next framework swap only touches
the backend. Our own `VrHandSide` replaces `HVRHandSide` (34 uses) and `GlobalEventDispatcher.HandSide`.

## Phases

Detailed plans: `phase-0-decouple.md` … `phase-6-removal.md` in this folder (see the README).

Each phase is its own PR (see `commit-pr` skill) and is playable on Quest, Pico and PCVR before merging.

| Phase | Content | Replaces (HVR) | Size* |
|-------|---------|----------------|-------|
| **0 - Decouple** (HVR stays) | Add `Gothic.VR.Interaction` ports + HVR backend. Move the 32 direct-HVR files onto the ports. Drop `#if GOTHIC_HVR_INSTALLED` from the 22 files that don't need it (02 §6). Move `Player/DynamicPose/Grabbable/Hand` layer names out of HVR wording. | – | M (1-2 wk) |
| **1 - Rig, input, locomotion, UI, simulator** | New XRI player prefab (XROrigin + CharacterController, move/turn/snap/teleport/jump/gravity, sit/stand via tracking origin + height offset). Input System actions for menu, both-grips, jump, sprint, crouch. `XRUIInputModule` (deletes the `InitUIInteraction` FindObjectsOfType hack). Screen fade + wall fade. XR Interaction Simulator. Swim/dive on the new rig knobs. | H10, H11, H12, H14, H15, H22, `VRPlayerController`, `VRPlayerInputs`, `VRSimulatorControlsGUI`, hvr-simulator package | L (2-3 wk) |
| **2 - Grabbing** | `XRGrabInteractable` (VelocityTracking) on `oCItem`, mobs, NPCs; near-far interactor for distance grab with line-of-sight; hover → `VRFocus`; inventory bookkeeping in `VRPlayerService` on the new events; collision ignore for held items; combat velocity stays rigidbody-based. | H2, H6, grab events | M-L (2 wk) |
| **3 - Sockets & inventory** | Socket interactor wrapper that reparents the `VobLoader` root (today's `VRSocket` behaviour), category rotation, filters by item flag (replaces tag socketables), hover scale/material, backpack + shoulder socket, hip holsters, chest slots resized to mesh, NPC loot sockets, "drop into chest" volume. | H7, `VRSocket`, `VRShoulderSocket`, `GUZ-VR-Socketables` | L (2-3 wk) |
| **4 - Mechanics** | `VrDoor`: HingeJoint + grab, lock/unlock, open/close threshold → Gothic SFX (replaces `HVRPhysicsDoor` + `HVRRotationTracker`, used by `VRVobDoor`, `VRVobContainerPhysicsChest`, lock picking). Ladders via XRI `ClimbInteractable`. | H8, H9 | M (1-2 wk) |
| **5 - Hands** | Hand meshes (UXR MIT hands or other CC0). `FingerCurlProvider`: grip/trigger/touch + `XRHandSubsystem` curls (Index, Steam Frame). `ProceduralHandPoser`: per-finger sweep until contact, cached per item instance. Authored poses for backpack/ladder. `PhysicsHand` (ConfigurableJoint follow, ghost hand, fist collider layer) behind an immersion setting. | H1, H3, H4, H5, H13 tuning | L-XL (3-5 wk) |
| **6 - Removal** | Delete HVR backend, `Assets/HurricaneVR`, `GOTHIC_HVR_INSTALLED`, `HurricaneActions.cs`, `VRContextToolActions.cs`, CI private-repo injection, hvr-simulator package; update CLAUDE.md, wiki, CONTRIBUTING (VR now builds for everyone). | everything | S (2-3 d) |

\*Rough single-developer estimates, including playtesting on three platforms. Total ≈ **12-18 weeks**. Phases 2-4
can run in parallel once Phase 1 has landed.

### Ordering rationale

- Phase 0 pays off right away: HVR moves into one folder, ~30 files compile without it, and later phases only swap
  the backend.
- Phase 1 already unblocks contributors for menus, dialogs, UI, cinema, subtitles and locomotion work. To get there
  early, the VR scene can boot with the XRI rig while grabbing is still missing (DeveloperConfig flag
  `VrInteractionBackend`, see `feature-checklist` skill).
- Phase 5 is last because it carries the most tuning risk and the game is playable without it (non-physics hands
  with procedural poses are acceptable first).

## Assets plan (Prefabs via UnityMCP only, per CLAUDE.md)

| HVR asset | Replacement |
|-----------|-------------|
| TechDemo XR rig | New `VR - Player.prefab` from XRI Starter Assets as reference, built through UnityMCP |
| Hand models / controller models | Gothic's own fist meshes at runtime via ZenKit (future task, decision 2026-10-22; nothing from UXR); controller models none |
| Socket rings, teleport marker | Simple procedural/CC0 meshes + our URP materials |
| Backpack prefab + SFX | Gothic asset at runtime (a bag/pouch mesh from the Gothic install, loaded via ZenKit) + Gothic SFX - keeps the "no bundled assets" principle |
| Input maps, controller offsets | XRI Input Actions asset + per-profile pose offsets (OpenXR grip/aim poses mostly make them unnecessary) |
| HVR settings ScriptableObjects (joints, haptics, force pull) | Our own ScriptableObjects in `Gothic-VR/Resources/VR/Settings/` |

## Risks and mitigations

| Risk | Mitigation |
|------|-----------|
| The game feels worse than HVR (grab feel, hands) | Keep the HVR backend until Phase 5 is accepted in playtests. Compare side by side with the `playtest-planner` agent. |
| VelocityTracking grabs jitter with CharacterController motion | Same compensation as today (`linearVelocity - characterController.velocity`). Tune the XRI velocity/angular damping per weight class. |
| Procedural poser is expensive on Quest | Pose once on grab, cache per item instance, use simple colliders (we already compute item bounds). |
| Pico OpenXR quirks with XRI | XRI is the Unity-supported path for Pico's OpenXR SDK. Test early in Phase 1. |
| Accidental HVR code copies (license) | Write new code from behaviour specs (02 tables), not from HVR source. Reviewers check this. |

## Fallback

If XRI turns out unsuitable during Phase 1 (e.g. Pico input), the Phase 0 ports still make **UXR (forked) or a
from-scratch backend** possible without touching game code. That's the main reason Phase 0 comes first.
