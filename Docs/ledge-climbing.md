# Ledge climbing

The VR player climbs exactly the ledges Gothic's hero can climb: by gripping the lip and pulling up, by a comfort
fade, or by teleporting onto the top. Behind the DeveloperConfig flag `EnableVrLedgeClimbing`, XRI backend only.
Why it's built this way: Docs/adr/2026-10-28-vr-ledge-climbing.md.

## Gothic's rules (Core)

No world data marks a ledge. ZenGin decides from the geometry and the hero's guild values (OpenGothic
`Npc::tryJump`, `MoveAlgo::climbMove`). The values are in the `GIL_HUMAN` slot of `GIL_VALUES` (cm, degrees):

| Value | G1 / G2 | Use |
|---|---|---|
| `STEP_HEIGHT` | 60 | below it: no ledge, a step |
| `JUMPLOW_HEIGHT` | 105 | below it: band Low (a big step) |
| `JUMPUP_HEIGHT` | 200 | below it: band Mid (pull up); up to `JUMPUP + JUMPLOW` (305): band High (jump up, hang) |
| `JUMPMID_HEIGHT` | 205 | never decides for humans, `JUMPUP_HEIGHT` is checked first |
| `SLIDE_ANGLE` | 45 / 50 | a steeper top isn't a ledge |
| `CLIMB_HEADING_ANGLE` | 45 / 50 | the player must look at the wall (jump) |

A ledge is climbable if the top is walkable, the body fits on top (capsule check) and has headroom on the way up.
Gothic probes 55 cm in front of the body and stands there after the climb. NPCs and loose items are never ledges, but
they block room and headroom.

- `Models/Movement/*`: `LedgeRules` (values in m), `LedgeBody` (capsule + layers), `LedgeInfo` (result), `LedgeBand`.
- `Domain/Movement/LedgeProbeDomain`: the probes - `ProbeAhead` (jump), `ProbeEdge` (a point near a lip, a hand),
  `ProbeTop` (a teleport target, walks back to the lip).
- `Services/Movement/LedgeService`: reads the hero's values (Gothic's values if the scripts have none) and runs the
  probes. Not VR-specific: Flat jumps and NPCs can use it later.

## VR

All on the XR Origin of `VR - Player XRI.prefab`:

- `VrLedgeReach`: owns whether climbing is on (`Climber`: null if the flag is off, no rig or no anchor prefab - then
  the other two stay off as well). Moves one `VrLedgeAnchor` per empty hand along the climbable lip it reaches, plus
  the hints (hand half closes, haptic tick, glow strip). Lips count from the floor the player last stood on, so a jump
  doesn't lower them.
- `VrLedgeClimber` (plain C#, `Domain/Player/`): the player's feet and body (standing height, a crouch doesn't count)
  and the probes from the player's view. The one place the VR side builds a `LedgeBody`.
- `VrLedgeClimb`: what a grip does. Physical: XRI's `ClimbProvider` moves the rig, once pulled up 25 cm and the head
  15 cm above the top the player is put on top (a table's edge alone does nothing). Comfort: fade out, teleport onto
  the top, fade in.
- `VrLedgeLocomotion` (`IVrLedgeHandler`, set with `IVrRig.SetLedgeHandler`): answers the backend's jump
  (`GothicJumpProvider`: comfort climb, or a rise that lifts the hands 15 cm above a High lip) and teleport
  (`XriTeleportAimer`: a target on top of a climbable ledge is valid above the 1.5 m jump limit). The aimer's slope
  limit is `LedgeService.HeroRules.SlideAngle` too, so a teleport onto a ledge checks one Gothic value.

`VR Ledge Anchor XRI.prefab`: `ClimbInteractable` + `XriInteractableBridge` + `VrLedgeAnchor`, on layer 13
`ClimbAnchor` (`Constants.ClimbAnchorLayer`). That layer collides with nothing (collision matrix), only the hands'
near-grab `SphereInteractionCaster` mask sees it: the player's own ground, teleport and room checks never hit an
anchor. Queries without a mask do see its box: leave the layer out (`NpcInitializerDomain`'s spawn check does).

## Settings

Gothic.ini, in the VR menus: `ledgeClimbMode` (Accessibility: pull up / fade) and `ledgeHints` (Immersion: off /
touch / near / assist; assist also makes the anchors bigger).

## Pitfalls

- XRI reads `JumpProvider.jumpHeight` on every frame of a jump and it isn't metres: `GothicJumpProvider` sets it once
  at take-off from the wanted rise (`GetJumpHeightForRise`, inverse of XRI's force curve, ~5% frame loss covered by
  the 15 cm margin).
- A climb grab mid-air must end the jump (`GothicJumpProvider.CancelJump`), otherwise XRI replays its rest after the
  climb. Also fixes the same pop on ladders.
- `Physics.autoSyncTransforms` is off: `VrLedgeReach` syncs after moving an anchor, before XRI's interaction manager.
- Saving while hanging stores a mid-air position.

## Tests

PlayMode `LedgeProbeTest` (bands, room, headroom, slope, hand and top probes on primitives) and
`GothicJumpProviderTest` (jump height for a rise).
