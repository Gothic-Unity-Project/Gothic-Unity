# ADR: VR comfort for the swing trail and earthquakes

- Status: proposed
- Date: 2026-11-11
- Technical details: Docs/architecture.md

## Context

Two effects of the original game are made for a monitor. The weapon swing trail is drawn right in front of the eyes on every swing in VR. Earthquakes (summoning, fire rain) shake the camera, which makes people sick in a headset.

## Decisions

1. **The swing trail is half as visible as in Gothic.** A fixed value for VR, no setting. Not chosen: removing the trail (it shows the swing's reach) or a setting (nobody asked to change it).
2. **An earthquake rumbles both controllers instead of shaking the camera,** as long as it lasts and while the hero is within its reach. Its sound plays as before. Not chosen: shaking the camera (motion sickness).

## Consequences

- Flat mode shows the weaker trail too.
- Rumble length and reach come from the effect's own data; effects without it rumble for 3 s within 10 m.
