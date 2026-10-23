# HurricaneVR replacement assessment

Goal: remove the paid HurricaneVR (HVR) dependency so every contributor can build and run the VR target on
Quest 2/3, Pico, PCVR, including Index / Steam Frame finger tracking.

| Doc | Answers |
|-----|---------|
| [01-hvr-capabilities.md](01-hvr-capabilities.md) | What can HVR do (high and low level)? |
| [02-gothic-hvr-usage.md](02-gothic-hvr-usage.md) | Which of it do we use, where (code and prefabs), and which assets are licensed? |
| [03-framework-comparison.md](03-framework-comparison.md) | How does it map to UltimateXR, XR Interaction Toolkit, Meta ISDK and others? |
| [04-recommendation.md](04-recommendation.md) | What do we do, in which order, and at what cost? |

### Implementation phases

| Phase | Doc | Size |
|-------|-----|------|
| 0 | [phase-0-decouple.md](phase-0-decouple.md) - Gothic-owned ports, HVR behind one backend folder | M |
| 1 | [phase-1-rig-input-locomotion-ui.md](phase-1-rig-input-locomotion-ui.md) - XRI rig, input, locomotion, UI, simulator, CI | L |
| 2 | [phase-2-grabbing.md](phase-2-grabbing.md) - items, melee, distance grab, hover, mobs, NPCs | M-L |
| 3 | [phase-3-sockets-inventory.md](phase-3-sockets-inventory.md) - backpack, holsters, chest slots, NPC loot | L |
| 4 | [phase-4-doors-chests-ladders.md](phase-4-doors-chests-ladders.md) - hinges, lock picking, chest lids, ladders | M |
| 5 | [phase-5-hands.md](phase-5-hands.md) - **on hold**; physics hands skipped, Gothic fist meshes later | L-XL |
| 6 | [phase-6-removal.md](phase-6-removal.md) - delete HVR, define, CI injection; update docs | S |

Bugs found on the way (not caused by the replacement): [found-bugs.md](found-bugs.md).

## TL;DR

- HVR 2.9.3: ~43k LoC. We use about a third of it, but that third is the hard part: **physics hands, physics
  grabbing, procedural finger posing, sockets, physics doors, player controller and input**.
- Coupling: 62 VR files are gated by `GOTHIC_HVR_INSTALLED`. 32 use HVR directly, 8 only through our wrappers, and
  **22 don't use it at all**. 18 of our prefabs/assets contain HVR components, and the player rig is a variant of
  HVR's *TechDemo* rig.
- **UltimateXR** (MIT, ~109k LoC, 1.0 unreleased, Unity 2021/URP 12): good hand art and pose editor, but grabbing
  is kinematic, posing is authored-only, there's no distance grab, climbing or simulator, and its device layer targets
  Oculus/PicoXR/SteamVR SDKs instead of our OpenXR + XR Hands stack. As a base, that's a poor fit.
- **XR Interaction Toolkit 3.x** (free, Unity-maintained, same OpenXR/Input System/XR Hands stack) covers rig,
  locomotion, teleport, climb, distance grab, velocity-tracked grabbing, sockets, UI, haptics and a simulator.
  Missing: physics hands, procedural/authored hand posing.
- **Recommendation:** our own `Gothic.VR.Interaction` ports, with XRI as the backend, our own procedural poser,
  physics hands and door/container components, plus selected UltimateXR borrowings (hands art, pose ideas).
- **Update 2026-10-22:** physics hands are skipped, hands will use Gothic's own fist meshes, and nothing from
  UltimateXR is used. Phase 5 is on hold.
  Six phases, ≈12-18 developer-weeks. Phase 0 (decoupling behind ports while HVR still runs) comes first and keeps
  every other option open.

Snapshot: `main` @ `084e5e95`, 2026-10-22. UltimateXR checked at `dde5c47` (2026-06-08).
