# ADR: NPCs at a bench, stove or workbench stay there instead of starting over

- Status: proposed
- Date: 2026-10-28

## Context

In Gothic, an NPC's routine runs its state loop over and over while the NPC has nothing else to do. The loops of NPCs that sit on a bench or work at a saw, an anvil or an alchemy lab only send the NPC to the mob when it isn't using it yet. They find that out from the NPC's body state: sitting, or working at a mob.

Our NPCs got that body state while setting off towards the mob, and the walk there replaced it with "standing" on arrival. So every NPC at a mob looked like it had never arrived, and its loop sent it to the mob again on every pass. In Khorinis that meant hundreds of restarts per NPC per minute, the random idle animations of sitting NPCs never played, and the busy town cost far more than needed. An NPC whose mob has no spot to use it from (Fellan's hut) tried again every frame.

## Decisions

1. **The body state of using a mob is set when the NPC is at the mob,** after the walk. It stays until the NPC has left the mob again.
2. **A mob without a free spot isn't available.** Like in Gothic, the scripts' check for an available mob says no when there is nowhere to use it from, so the routine doesn't keep asking.
3. **Warnings that routines would repeat are logged once.** A missing animation and the not yet built head animations used to be logged thousands of times per minute, and every log records a stack trace, also in builds.

## Consequences

- Measured in the editor at the Khorinis market place: NPCs started using a mob about 3,300 times in a bit over a minute, now 9 times per minute. The garbage the game produces per frame dropped from about 590 to 390 kilobytes.
- Sitting and working NPCs stay at their mob, and sitting NPCs play their random idle animations again.
- Many guards, merchants and citizens still align to their spot on every loop pass, as the Gothic scripts ask for. That costs a script call per NPC every few frames and is the next thing to make cheaper.
