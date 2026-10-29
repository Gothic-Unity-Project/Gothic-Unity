# ADR: HurricaneVR is removed, XR Interaction Toolkit is the only VR backend

- Status: proposed
- Date: 2026-10-28

## Context

VR used to run on HurricaneVR, a paid asset that isn't in the repository. Only people with a licence could build the VR version, and CI had to pull it from a private repository. Since the HurricaneVR replacement (phases 0 to 5), everything VR needs runs on our own interaction layer with the free XR Interaction Toolkit behind it. Both backends lived side by side, chosen by a developer setting.

Keeping both had a cost even for people without HurricaneVR: our prefabs still carried its components. In an editor without it, every chest, door, item and NPC that was spawned logged one warning per missing component. In the Old Camp that was about half of the loading time.

## Decisions

1. **The XR Interaction Toolkit is the only VR backend.** The setting to choose a backend is gone, and so are the scripting define and the HurricaneVR backend code. Not chosen: keeping the HurricaneVR backend as an option. Nobody can test it without a licence, so it would rot.
2. **Our own interaction layer stays.** Game code still talks to our own VR interfaces, and the toolkit stays behind them. A future backend can be added the same way.
3. **The prefabs and scenes only hold what we use.** All HurricaneVR components, its player, its settings, its demo props and the old backpack and slot prefabs are removed. A scan for references to any HurricaneVR file comes back empty.
4. **CI builds without any private assets.** There is one build job, and it fails if HurricaneVR or its define comes back.
5. **Renames and wording follow in their own PRs.** Names like "VR - Player XRI" and comments that still mention HurricaneVR are changed later, so the open PRs that edit these files don't run into conflicts now.

## Consequences

- Everyone can open, build and play the VR version, also without a headset through the simulator.
- Loading a world no longer floods the console with missing-script warnings. Measured in the editor with a save in the Old Camp: everything around the player has appeared after about 8 seconds instead of about 25, the worst frame while things appear takes 0.15 seconds instead of 2.5, and there are no missing-script warnings anymore instead of about 27,000. Builds from CI had HurricaneVR installed and never had these warnings, so players don't get this speed-up.
- Open pull requests that still edit HurricaneVR backend files get a conflict and have to drop those edits.
