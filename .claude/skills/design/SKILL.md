---
name: design
description: Designs a feature or non-trivial fix in Gothic-Unity before coding - asks the open decisions, writes the
    plan (types, layers, dependencies, events, per-frame cost), drafts the ADR and has architecture-reviewer check
    the plan. Use when asked to plan, design or spec a feature, or before implementing anything that adds types,
    events, registrations or settings.
---

# Design

The plan is written here, checked by `architecture-reviewer`, and approved by the human through the ADR. Don't
write code in this phase.

## 1. Understand

- Read `Docs/architecture.md` (principles, "Where does my code go?", "Inside Gothic-VR", "Performance") and the
  system being changed, end to end (adapter → service → domain → model). Search inside one module folder only.
- Gothic behaviour (externals, AI, items, XP, fights): run the `daedalus-scout` agent first. The scripts decide,
  C# only implements the engine side (principle 1).
- A pure bug fix without new types, events, registrations or settings needs no design: say so and stop.

## 2. Ask the decisions

Ask the human the open decisions, one at a time, with your recommendation first. Typical ones:
- Behaviour as in the original game, or deliberately different?
- G1, G2, both? VR, Flat, both? Mods affected?
- Fixed, a `DeveloperConfig` flag, a `GameSettings.json` setting or a Marvin setting (`marvin-cheats` skill)?
- Saved and restored on load?

Don't decide these yourself. A decision the human didn't make doesn't go into the ADR.

## 3. Write the plan

Save it to `.claude/review/<branch>/plan.md` (git-ignored, `/` in the branch name becomes `-`). Short, per planned
type:

| Type | Layer + folder | Depends on | Raises / listens to | Per frame, how many instances |
|---|---|---|---|---|

Then the steps in build order, and what is left out on purpose. Every step fits into this PR (principle 9).

## 4. Draft the ADR

Read `Docs/adr/README.md` first and follow it: "When" decides if a record is needed, "How" and "Template" decide
its form. Existing records in `Docs/adr/` show the tone.

- File `Docs/adr/<date>-<topic>.md` with `Status: proposed`.
- Context: the problem and how the original game handles it (from step 1 / `daedalus-scout`).
- Decisions: the ones the human made in step 2, each with what wasn't chosen and why.
- Plain words, no class names or code - those stay in the plan and the `Docs/<area>.md` the record links.
- One line per paragraph or list item, a page at most.

## 5. Review

Run the `architecture-reviewer` agent on the plan text. For each finding, change the plan or note why not. On
`needs redesign`, go back to step 2 or 3.

## 6. Hand over

Show the human: the reviewer's verdict, the plan's type table and the ADR path. Wait for their approval before
building. The plan stays in the session (and in `plan.md`) for the build.
