# ADR: Fires burn when the scripts say so

- Status: proposed
- Date: 2026-10-31

## Context

Gothic's scripts decide when fireplaces burn: Gothic 1 lights its camp fires from ten in the evening to five in the morning, and they are out by day. Our game stored these schedules but never used them, so every fire burned all day. A fireplace without its own fire also lost the objects placed on it, and the parts of a fire (smoke under its light) stood at the wrong place.

## Decisions

1. **Fires follow the scripts' schedule, like in Gothic.** At each time of day every fire of a kind takes the state the schedule gives; a fire that is out shows no flame, smoke, light or sound. Fires the scripts don't schedule keep burning.
2. **Found on BoroBongo's branch, taken over as fixes:** a fireplace without a fire keeps its objects, and a fire's parts are placed relative to the fire.
3. **Not taken over:** fires indoors always burning and lighting fires by touching them. Gothic doesn't do either; they can come later as an own decision.

## Consequences

- Camps look like Gothic by day and by night.
- After loading a saved game the schedule is only known again once the scripts set it; until then fires burn.
- The flames and smoke themselves need the particle effects, which are still switched off in the game's settings.
