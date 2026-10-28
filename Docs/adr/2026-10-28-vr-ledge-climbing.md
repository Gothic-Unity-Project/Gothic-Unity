# ADR: VR ledge climbing

- Status: accepted (PR #456), tested in the headset
- Date: 2026-10-28
- Technical details: Docs/ledge-climbing.md

## Context

In the original game the hero pulls himself up onto ledges with the jump key. In VR, the player could only climb ladders. Gothic doesn't mark ledges in its worlds: the engine decides from the shape of the world and a few values in the game scripts, such as how high the hero can step, jump and pull himself up.

## Decisions

1. **Gothic's own rules decide what can be climbed.** We do what the original engine does, with the hero's values from the game scripts. Not chosen: marking ledges by hand (every world and every mod would need it), or letting the player climb any edge (walls and palisades that are meant to block the way would no longer do that).
2. **The rules belong to the game, not to VR.** The flat-screen version and the NPCs can use the same rules later.
3. **Three ways up:** grab the edge and pull yourself up (default), a comfort option that fades out and puts you on top (for players who get motion sick), and teleporting onto a ledge you could climb.
4. **The jump reaches the ledge instead of copying Gothic's jump.** In front of a high ledge, a jump lifts your hands just above the edge. All other jumps stay as they are. Not chosen: Gothic's full jump of about two metres, which is too much for VR.
5. **Hints only close to your hands.** Near a climbable edge the hand half closes and vibrates once, and a short strip on the edge can glow. Players choose: off, touch, near or assist. Not chosen: markers you can see from far away, which break the immersion.
6. **Only your hands notice the edge you grab.** It can't push you or get in the way of walking and teleporting.
7. **No climbing by accident.** You only end up on top after really pulling yourself up, with your head above the edge. Grabbing the edge of a table does nothing.
8. **Only for the new VR setup** (XR Interaction Toolkit), behind a developer switch. The old HurricaneVR setup stays as it is.

## Consequences

- Grabbing something in mid-air now ends the jump. This also fixes a jump that went on after climbing a ladder.
- Nothing new is saved. Saving while hanging on a ledge saves you in mid-air, so saving should be blocked while climbing once the save game PR (#447) is merged.
- Open: the "Climbing" option doesn't fit into its menu yet and has to move to another one.
