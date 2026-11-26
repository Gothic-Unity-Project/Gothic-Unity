# Mobs

How mobs (Gothic's `oCMobInter` family: anvils, forges, grindstones, cauldrons, benches, levers, wheels, beds,
doors, chests) keep their state and play their own animations. Why the animations are built this way:
Docs/adr/2026-11-25-mob-animations.md. The NPC AI in general is mapped in `Docs/npc-ai.md`.

## The mob state

A mob has a current state: 0 at rest, 1 (or higher) while used or switched, -1 while an NPC leaves it. It lives in
`VobContainer.MobState`, not in the VOB's `State` (that is Gothic's `stateNum`, the number of states, which the save
format keeps). Scripts read it with `Wld_GetMobState` (never below 0).

| Who | Writes `MobState` when |
|---|---|
| `UseMob` (`AI_UseMob`) | each finished transition of its user moves the state one step towards the target (`UpdateState`), a linked mover is opened/closed |
| `NpcService.ReleaseMob` | the last NPC still in one of the mob's states leaves it without `AI_UseMob(-1)` (`AI_StandUp`, death, starting over when culled or re-enabled): -1 |
| `MobInteractionService` | the hero uses a lever/wheel with a target: 0 -> 1 triggers, 1 -> 0 untriggers (the trigger branch) |
| `VRMobHinge` (VR) | the hand opens (1) or closes (0) a door or chest lid |
| `MobStatesSaveSection` | a loaded game restores the states > 0 (section `core.mobStates`, per world, by type and position) |

Saving: only the state is saved. Nothing about a mob's animation is: it starts again with the next use after a load.

## Who uses a mob

An NPC uses a mob while `NpcPrefabProperties.CurrentInteractable` is the mob and
`NpcProperties.CurrentInteractableStateId` is >= 0 (`NpcContainerExtension.IsUsingMob`). `UseMob` reserves the mob
before walking there, so two NPCs don't pick the same one (`VobService.GetOtherMobUsers`, a mob with several slots
takes several).

The usage ends in one place: `NpcService.ReleaseMob` clears mob, slot, state and body state, stops the mob's
animation and, for the last user, sets the mob's state to -1. It runs after `AI_UseMob(-1)`'s last back transition,
at `AI_StandUp(Quick)` (`StandUp`), at death (`NpcDeathDomain.Die`: a killed smith frees his anvil) and when the NPC
starts over (`AiHandler`).

The hero doesn't set `CurrentInteractable`. He uses a mob through `MobInteractionService` (the VR grab,
`VRMobUseService`): its condition function, then either the item swap of crafting mobs, the mob's dialog
(`[onStateFunc]_S1`) or the trigger branch.

## A mob's own animations

Like the engine (OpenGothic `Interactive::setState`), a mob with an `.MDS` visual plays animations of its own model
script while its state changes: `T_S0_2_S1`, then the loop `S_S1`, and `T_S1_2_S0` back. They carry their own
events: G2's smithy has `BSANVIL_OC.MDS` `S_S1` with `FORGE_ANVIL` and `ANVIL_SPARKS`, `BSFIRE_OC.MDS` `T_S0_2_S1`
with `FIRE_HOT`, `BSSHARP_OC.MDS` turning its wheel with `SHARPSTONE_SPARKS`, `BSCOOL_OC.MDS` with `WATERVAPOUR`.
State -1 is called `STAND` in the names (`T_STAND_2_S0` - most mobs don't have it).

| Class | Owns |
|---|---|
| `MobAnimationService` (Services/Vobs) | which mobs animate (`IsAnimated`: an `.MDS` visual, no door, chest, bed or fireplace), the track names, users, the hero's dialog use, the safety net rule (`IsStillUsing`), baking the world's mob tracks at load |
| `IMobAnimator` / `IMobAnimatorFactory` (Services/Vobs) | the port; `MobAnimatorFactory` (Adapters/Vob) adds a `MobAnimator` on first use, bound in `ReflexProjectInstaller` |
| `MobAnimator` (Adapters/Vob) | plays: maps the model's nodes once, samples them on the CPU, transition -> loop -> way back, the users, the per-loop safety net |
| `AnimationEffectPlayer` (Adapters/Animations) | the sound and particle events of a track instance - shared with `AnimationSystem` (NPCs) |
| `AnimationSampling` (Models/Animations) | the frame interpolation of a baked track - shared with `AnimationPoseJob` (NPCs, Burst) |

### Data flow

1. World load: `MobAnimationService` bakes `T_S0_2_S1`, `S_S1`, `T_S1_2_S0` of every animated mob model in
   `GameStateService.VobsInteractable` and decodes their sounds (P3). The Editor log lists the tracks found per model.
2. An NPC's `UseMob` plays its own transition and calls `OnStateChanged(mob, npc, from, to)`. The mob's transition
   starts with it, then its loop runs while the NPC stays.
3. A second NPC on the same mob (another slot) joins: the mob doesn't restart; a user leaving while others stay
   doesn't stop it.
4. The last user leaves (`to == -1`, or `ReleaseMob` -> `Stop`): a running transition ends, the transition back plays
   (a lever swings back), the particles fade out, `MobAnimator` turns itself off.
5. Safety net: once per loop round (never per frame) `MobAnimator` asks `IsStillUsing` for each user. A user that is
   gone, culled, dead, knocked out or at another mob is stopped - a path that ends a usage without `ReleaseMob`
   can't leave an anvil sparking forever.

The hero: a lever or wheel plays only its transition into the new state and rests there (no user). A mob with a
dialog (anvil, grindstone, rune table) works like for a smith while its dialog is open: `StartHeroUse` before
`[onStateFunc]_S1`, `StopHeroUse` on `GlobalEventDispatcher.MobDialogEnded` (DialogService raises it where it drops
the mob dialog anchor - also when `_S1` opened no dialog: the mob works once, there and back). The forge's item swap
without a dialog works once too.

### The light player (MobAnimator)

`AnimationSystem` is built for skinned NPCs: a PlayableGraph, an Animator and a Burst job per NPC, layers, blending,
root motion. A mob moves a few rigid nodes (a grindstone's wheel, a lever's arm), so `MobAnimator` samples them on
the main thread with `AnimationSampling` and writes the node transforms, no blending. It reuses Gothic's clock and
event timeline (`AnimationTrackInstance`) and the baked tracks of `AnimationService` (`GetTrack(name, mds, null)`,
`GetSkeleton(mds)`, which also reads a hierarchy from an `.MDL`).

- Node mapping: the skeleton's paths (MDH order, `AnimationTrack.BoneToNode`) below the root node's parent; the root
  node is found once by its Gothic name (architecture.md rule C exception).
- Root node: tracks pin the root's X/Z to zero and store Y relative to the rest height. That matches mobs: the mesh
  builder rests their root node at zero.
- Sounds: one 3D `AudioSource` on the mob, linear fall-off to the event's range (`FORGE_ANVIL` R:2500 = 25 m), like an
  NPC's sound.
- Particles: built once per effect on its first event and replayed (an anvil hit twice per loop doesn't rebuild its
  sparks), destroyed after fading when the mob rests. Effects not attached to a node are parented to the mob, so they
  are culled with it.
- Per frame: `Update` only while a transition or loop plays, nothing allocates. A mob culled with its VOB pauses and
  goes on when visible again.

Not done (follow-ups): a lever's or wheel's pose after a load or a stop in the middle of a transition (it shows its
rest pose until used again), idle loops of unused mobs (`S_S0`), a light from the forge (the original has none),
doors, chests and beds (the VR hand moves them), `oCMobFire` (FireService).

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change which mobs play their own animations | `MobAnimationService.IsAnimated` |
| change which tracks a state change plays, or how users join and leave | `MobAnimationService.OnStateChanged` / `Stop` |
| change playback, node mapping or the safety net round | `MobAnimator` |
| change mob sounds or particles (also for NPCs) | `AnimationEffectPlayer` |
| end an NPC's mob usage on a new path | call `NpcService.ReleaseMob` - never clear `CurrentInteractable` by hand |
| change what the hero's grab of a mob does | `MobInteractionService.HandleMobGrab` |
| save more mob state | `MobStatesSaveSection` (`Docs/save-game.md`) |
