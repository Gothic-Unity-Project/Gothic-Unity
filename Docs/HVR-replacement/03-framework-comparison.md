# 03 - HVR vs. UltimateXR vs. other frameworks

Candidates assessed:

| Framework | Version checked | License | Maintainer / activity | Fits "free for every contributor"? |
|-----------|-----------------|---------|------------------------|-----------------------------------|
| **HurricaneVR** (today) | 2.9.3 | Asset Store EULA, paid | 1 dev, active | ❌ |
| **UltimateXR (UXR)** | `ultimatexr-unity/` 1.0.0 *(Unreleased in CHANGELOG)*, last commit `dde5c47` 2026-06-08, 184 commits | MIT | VRMADA (small studio), slowed down | ✅ |
| **Unity XR Interaction Toolkit (XRI)** | 3.x (3.1 released, 3.2 in docs) | Unity Companion License (free, any Unity project) | Unity, active, ships as a Unity 6 core package | ✅ |
| Meta Interaction SDK (ISDK) | – | Proprietary Meta SDK license | Meta, active; officially **Quest only** | ❌ (Pico/PCVR not supported, not OSS) |
| MRTK3 | – | BSD-3 / MIT | community org; **built on XRI**; mixed-reality UI focus | ◐ (adds little for a game) |
| VRTK 4, NewtonVR, HPTK (Hand Physics Toolkit) | – | MIT | unmaintained | ❌ (dead dependencies) |
| AutoHand, VR Interaction Framework, HexaBody | – | paid | active | ❌ (same problem as HVR) |
| Build our own | – | ours | us | ✅ |

## 1. UltimateXR - tech stack facts (from the source in `ultimatexr-unity/`)

- 573 runtime C# files, **~109k LoC** (2.5× HVR). Huge share is multiplayer/state-sync/serialization
  (`Core/StateSync`, `Core/StateSave`, `Networking/*` with Photon/Mirror/FishNet/NetCode), avatar/IK, weapons, tweens.
- `package.json`: `"unity": "2021.2"`, depends on `com.unity.render-pipelines.universal: 12.1.6`. We are on
  **Unity 6000.3.13f1 / URP 17.3**. Unity-6 compatibility is unverified upstream.
- **Avatar-centric**: everything hangs off `UxrAvatar` + `UxrAvatarRig` + `UxrStandardAvatarController`.
  Locomotion, grabbing and hand poses all expect a configured UXR avatar.
- **Kinematic manipulation**: while grabbed, the object's rigidbody is forced kinematic
  (`Manipulation/UxrGrabManager.Manipulation.cs:284-286`), physics come back on release with a throw impulse
  (`…:1280`). No joint-driven objects, no physics hands.
- **Authored poses**: `UxrHandPoseAsset` + a very good Hand Pose Editor, grab-point shapes
  (`UxrGrabPointShapeAxisAngle` lets a hand slide along an axis). No procedural finger fitting.
- **Sockets** = `UxrGrabbableObjectAnchor` with compatible tags and `MaxPlaceDistance`.
- **Constraints** on grabbables (translation/rotation limits) give kinematic doors/levers/dials.
- **Input via legacy `UnityEngine.XR.InputDevice`** per device class. On our pure-OpenXR stack:
  `UxrMetaTouchQuest3Input` requires the *Oculus* SDK (`SDKDependency => SdkOculus`), `UxrPicoNeo3Input` requires the
  *PicoXR* (non-OpenXR) SDK and only knows "PicoXR Controller-Left/Right", `UxrValveIndexInput` derives from
  `UxrSteamVRControllerInput` (SteamVR plugin). Only `UxrGenericOpenXRInput` works for us, with generic capabilities.
- **Hand tracking**: `UxrMetaHandTracking` uses `OVRPlugin`; no `com.unity.xr.hands` support anywhere in Runtime.
- Locomotion: `UxrSmoothLocomotion` on `CharacterController` (gravity, sprint, terminal velocity),
  `UxrTeleportLocomotion`.
- UI: `UxrPointerInputModule`, laser pointer and finger-tip (poke) raycasters, keyboard UI.
- Camera: `UxrCameraFade`, `UxrCameraWallFade` (= HVRHeadCollision).
- Art: MIT hand meshes (`Art/Avatars/BigHands`, `SmallHands`, skin variations) - a real asset win.

## 2. XRI 3.x - tech stack facts

- OpenXR-first through the **Input System** (action maps per interaction profile) - the same stack we already ship
  (OpenXR 1.16, Meta Quest feature, Pico OpenXR SDK, Index/Vive/Touch profiles, XR Hands 1.7).
- `XROrigin` (tracking origin floor/device → sit/stand), locomotion providers: continuous move, snap/continuous turn,
  teleport, **climb** (`ClimbProvider` / `ClimbInteractable`), gravity + jump (3.1), works with `CharacterController`.
- Interactors: direct, ray, **near-far (distance grab + UI in one)**, poke, socket. Interactables:
  `XRGrabInteractable` with movement types *Instantaneous / Kinematic / **VelocityTracking*** (object stays a
  dynamic rigidbody → real velocity for our melee), multi-hand grab transformers, `XRSocketInteractor` with hover mesh,
  select filters.
- UI: `XRUIInputModule` + `TrackedDeviceGraphicRaycaster` (no manual canvas registration list).
- Haptics: `HapticImpulsePlayer`, simple haptic feedback component.
- **XR Interaction Simulator** (keyboard/mouse driven HMD + controllers + hands) → replaces our HVR simulator package.
- No physics hands, no hand poser (XR Hands has *gesture* shapes, not grab poses), no physics door/lever components.

## 3. Capability mapping (only capabilities we use)

✅ native · ◐ partial / needs our code · ❌ missing (we must build it)

| # | Capability (Gothic use) | HVR | UltimateXR | XRI 3.x | Meta ISDK | Notes |
|---|-------------------------|-----|-----------|---------|-----------|-------|
| H1 | Physics hands (no hands through walls, fist hits) | ✅ | ❌ | ❌ | ◐ (sync-hand) | Must be built in any free option. Can be optional (comfort setting). |
| H2 | Dynamic grabbed objects (melee velocity, collisions) | ✅ joints | ❌ kinematic | ✅ VelocityTracking | ◐ | **Decisive for combat.** UXR would force us to compute weapon velocity from transform deltas and lose held-object collisions. |
| H3 | Procedural finger fitting on runtime-loaded meshes | ✅ PhysicsPoser | ❌ | ❌ | ❌ | **Gothic-specific must-have**: all items come from ZenKit at runtime, nobody can author per-item poses. Build ourselves (finger sweep/capsule-cast, ~500-800 LoC). |
| H4 | Authored poses (backpack, a few special grips) | ✅ | ✅ best-in-class editor | ❌ | ✅ | Few uses. Can borrow UXR's pose asset format/editor (MIT) or keep simple static poses. |
| H5 | Finger curls from buttons + Index/Steam Frame finger tracking | ✅ (XR Hands for Knuckles) | ◐ (SteamVR / OVR only) | ◐ (input actions + XR Hands available, mapping is ours) | ◐ | Small: read grip/trigger/touch actions + `XRHandSubsystem` curls. |
| H6 | Distance grab (items, NPC select) | ✅ | ❌ | ✅ Near-Far | ✅ | |
| H7 | Sockets: backpack, holsters, chest slots, loot, filters, hover feedback | ✅ | ◐ anchors + tags | ◐ socket interactor + filters | ◐ | Container/auto-fill/“drop into chest” logic is ours in every option. |
| H8 | Physics doors & chest lids (hinge, lock, SFX) | ✅ | ◐ kinematic constraints | ◐ grab + HingeJoint | ◐ | Small component on our side (hinge limits, lock, open/close detection, SFX). |
| H9 | Ladders | ✅ | ❌ | ✅ Climb | ◐ | |
| H10 | Player controller (move/turn/sprint/jump/crouch/gravity/sit-stand/swim tweaks) | ✅ | ◐ (avatar-bound) | ✅ providers + XROrigin | ◐ | Swim/dive keep our own code; needs gravity/speed knobs - available in both. |
| H11 | Teleport | ✅ | ✅ | ✅ | ✅ | |
| H12 | Cross-device input | ✅ | ◐ (per-SDK classes, wrong SDKs for us) | ✅ (OpenXR profiles) | ◐ | |
| H13 | Haptics | ✅ | ✅ | ✅ | ✅ | |
| H14 | UI laser on world canvases | ✅ | ✅ | ✅ | ✅ | |
| H15 | Fades, head-in-wall fade | ✅ | ✅ | ◐ (vignette; wall-fade is ours) | ◐ | |
| H22 | Desktop simulator | ✅ (ext. package) | ❌ | ✅ | ◐ | Critical for contributors without a headset. |
| – | Quest 2/3 | ✅ | ◐ generic OpenXR | ✅ | ✅ | |
| – | Pico 4 / Ultra (OpenXR) | ✅ | ◐ generic OpenXR | ✅ | ❌ | |
| – | PCVR Index/Vive/Touch | ✅ | ◐ (SteamVR plugin for Index) | ✅ | ❌ | |
| – | Unity 6000.3 / URP 17 | ✅ | ❓ (declares 2021.2/URP12) | ✅ (core package) | ✅ | |

### Score (20 rows above, ✅=2, ◐=1, ❌/❓=0…1)

| | HVR | UXR | XRI | Meta ISDK |
|---|---|---|---|---|
| Points (max 40) | 40 | 19 (❓ counted as 1) | 30 | 24 |
| Missing (❌) | 0 | 6 (H1, H2, H3, H6, H9, H22) | 3 (H1, H3, H4) | 3 (H3, Pico, PCVR) - and disqualified by license |

Weighting matters more than points: H2 (combat), H3 (runtime items), H12 (all platforms) and H22 (contributors
without headsets) are the four rows that decide the outcome. XRI covers H2/H12/H22 natively; UXR covers none of them
on our stack.

## 4. What each option would cost us

| Option | We must build | We must adapt / fight | Risk |
|--------|---------------|-----------------------|------|
| **UltimateXR as base** | physics hands, dynamic grab (or rewrite combat to kinematic), procedural poser, distance grab, climbing, simulator, OpenXR/XR-Hands input for Quest/Pico/Index | Adopt the `UxrAvatar` model for a player that Gothic drives itself; port 109k LoC to Unity 6/URP 17; strip networking/state-save; carry an owner-less fork if upstream stalls | **High** - we'd fork and own a framework 2.5× HVR's size, and still build the hard parts |
| **XRI as base + own Gothic interaction layer** | procedural poser, physics hands (optional), hand visuals, finger curls, door/lid/container components, wall fade | XRI events (`selectEntered/Exited`, `hoverEntered/Exited`) instead of HVR events - mechanical | **Medium** - hard parts are the same ones as with UXR, but everything else is maintained by Unity |
| **From scratch** | everything incl. input mapping, locomotion, teleport, UI module, simulator, sockets | – | **High** - weeks spent on solved problems (input, UI raycasting, simulator) |
| Meta ISDK | – | Pico/PCVR not supported, proprietary | Disqualified |

## 5. What is worth borrowing from UltimateXR (MIT)

UXR is a poor *base* for us, but a good *quarry* (keep the MIT notice when copying):

- **Hand meshes and skins** (`Art/Avatars/*Hands*`) - replaces HVR's licensed hand models.
- **Hand pose asset + Hand Pose Editor** ideas/code for the few authored grips (backpack, shoulder, ladder).
- `UxrCameraWallFade` → reference for head-in-wall fade.
- `UxrGrabPointShapeAxisAngle` → reference for "slide hand along the blade/handle axis" on weapons.
- `UxrBodyIK` / arm IK - only if we later want a visible body.
