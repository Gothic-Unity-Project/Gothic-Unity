# ADR: No pluggable VR backend any more

- Date: 2026-11-16
- Replaces: decision 2 of [HurricaneVR is removed, XR Interaction Toolkit is the only VR backend](2026-10-28-hvr-removal.md)
- Technical details: Docs/architecture.md (VR interaction)
- Issue: #521

## Context

While HurricaneVR and the XR Interaction Toolkit lived side by side, the VR code could switch between them. Every grabbable, socket, door hinge and chest asked "do I belong to the backend that is running?", and the player was picked by asking the backend whether it owned the rig. Since HurricaneVR is gone, the answer is always yes. The switch was one more concept to read through and kept a promise ("a future backend can be added the same way") that nobody plans to use.

## Decisions

1. **The XR Interaction Toolkit is wired in directly.** There is no exchangeable backend object any more, and no "is this the active backend" checks on grabbables, sockets, hinges and chests. The player is the one with a rig. Not chosen: keeping the switch for a possible future backend. If one is ever needed, it is easier to add it back with a real second implementation than to keep an untested one.
2. **Our own VR interfaces stay.** Game code still talks to our own input, rig, haptics, grab and socket interfaces, never to the toolkit's types. That boundary keeps game code readable and testable, and the audit found it clean. Not chosen: letting game code use the toolkit directly, which would spread toolkit details through every VR feature.
3. **Toolkit classes keep the "Xri" prefix.** It is the abbreviation the toolkit's own documentation uses, and renaming would touch every prefab-facing class for no change in behaviour. Not chosen: "Xrit".

## Consequences

- Less code and one concept less in the VR layer; the runtime gain is small.
- Grabbables and sockets now work right away when they are created, also before dependency injection reached them.
- Adding a second VR backend later means bringing back a way to choose between them.
