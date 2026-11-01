# ADR: Behaviour scenarios check the game's rules without a headset

- Status: proposed
- Date: 2026-11-01
- Technical details: Docs/behaviour-tests.md

Changed in v2 after the architecture review: grabs go through the VR layer's own events instead of copying its chain (decision 3); the game version is a property of a run, not of a scenario (decision 2); the test settings are a copy made at start, not a stored settings file, and the start-up seam is named honestly (decision 8); settings and saves a run touches are restored even after a crash (decision 4); the theft fix ships on its own before this.

Changed while building: scenarios can be marked as showing a known bug (decision 10); the scenarios that play the desktop simulator with keyboard and mouse run in a play session of their own, and every run started from the menu reloads the editor's scripts first (decision 11).

## Context

The game has grown to around ninety features, and every decision record in this folder is a rule a player can see broken. Each rule was checked by hand: start the game in the editor, teleport, spawn things, poke the services, look. A headset session per feature doesn't scale, and a regression in a merged branch is found by whoever plays next.

The original game had no such thing; its rules lived in the scripts and were tested by playing. We keep that for how a feature feels, but the rules themselves can be checked by the machine.

## Decisions

1. **Rules are written as readable scenarios: given a situation, when something happens, then this is true.** One scenario file per decision record, in plain sentences from a fixed catalog, so a developer, a reviewer and a tester read the same thing. Not chosen: tests written only in code, which only developers read and which each re-invent the world start and the waiting.
2. **The scenarios run inside the editor, in the game itself, without a headset.** A run is for one game version; it starts the game once with its own settings, loads a world once, and runs the scenarios one after another, each undoing what it changed (held things, spawned people, open dialogs, settings). Not chosen: a separate driver program outside the game, which can't see the game's own state, and tools for recording and replaying headset input, which replay a hand but can't check a rule.
3. **Scenarios talk to the game the way the platforms do:** through the services the VR layer itself calls, and through the VR layer's own grab events. Holding a torch in a scenario means the torch is put into the hand and the same grab event is raised that a controller would raise; the game's own chain does the rest. The real grab through the desktop simulator is one smoke scenario per area, not every scenario. Not chosen: driving everything through simulated hands, which is slow and makes every rule an input test; copying the grab chain in the tests, which drifts from it.
4. **Settings and saves the run changes are restored, even after a crash.** The developer's own game settings file and the two save slots the scenarios use (14 and 15) are backed up before the first scenario changes anything, put back at the end, and put back first thing at the next start if a backup is still there.
5. **Waiting is for a condition, never a fixed time,** with a clear message when it doesn't come. Time of day is fixed per scenario, dice are seeded, and scenarios bring their own NPC in front of the hero (one the world doesn't place itself) instead of relying on where a routine put somebody.
6. **The sentence catalog is small and grows with the features:** world and setup, hero, items and hands, NPCs, dialogs, time and saving, checks including pictures. A check reads what the game shows or what the owning part of the game answers, never a copy of its rule; a check on a picture compares the same spot with and without the feature, not an absolute value. The catalog in the technical doc is written from the code, and a check fails when the two differ.
7. **What can't be automated is still written as a scenario and marked manual.** Each run writes a report next to its results and screenshots: per feature what passed, what failed and why, with a picture of the moment it failed, and a headset checklist of the manual scenarios and of those the run skipped (the other game version, the simulator scenarios outside their session). Not chosen: a separate test plan document that drifts away from the features.
8. **The scenario reader is the official one for this scenario language; the runner is our own and thin,** on the editor's test framework, so the Test Runner, the editor assistant and the editor menu all run the same scenarios. Not chosen: the one existing scenario framework for Unity (unmaintained, no waiting across frames), and writing a reader ourselves.
9. **The game offers what a scenario needs as small ordinary features, plus one named seam:** an item put into the world counts as lying there from then on (which also fixes that things the scripts put into the world later could be stolen unseen), a spawned person can be removed again, and the game can be started with settings handed in by whoever starts it - a seam meant for tests and tools, not hidden test code.
10. **A scenario that shows a known, unfixed bug is marked as such and stays in the run.** Its failure doesn't turn the run red, the report lists it apart; when it passes, the report says the bug seems gone. Not chosen: deleting or skipping such scenarios, which hides the bug; leaving them as plain failures, which makes every run red and new failures easy to miss.
11. **The simulator scenarios have their own play session.** They replace the editor's keyboard and mouse with test devices for the whole session, which can only happen before the game starts, so a run is either plain or a simulator session; the menu's full run does both one after the other. Every menu run first reloads the editor's scripts, so nothing of the last session's game is left behind. Not chosen: switching the input inside a running game, which loses the simulated headset and controllers.

## Consequences

- A rule is checked in seconds instead of a headset session; the fast run of the first features takes under three minutes, the whole surface would take under an hour on one machine.
- Writing a scenario needs no code when the sentences exist; a new sentence is a small piece of test code with a product reason.
- Runs need a Gothic installation and the editor, so they run on developers' machines, not in the build pipeline.
- How a feature feels in the headset, comfort, hands and frame time on the Quest stay manual, listed on the checklist the run writes.
- Gothic 2 is checked in a second run with its own settings; the scenarios say which version they are for.
- The first scenarios found bugs that are now marked as known: a spawned person stands a metre beside his spot, guards don't react to theft yet in Gothic 1, and the desktop simulator's grip never starts a grab.
- Still open: the flat-screen mode once it works, whether the command line run starts the VR layer cleanly without a headset (not tried yet: it needs the editor closed), a fresh world before a single scenario, and the page on the development site that shows the runs.
