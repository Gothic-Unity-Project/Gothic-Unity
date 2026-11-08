# ADR: NPC AI - fights, real projectiles, NavMesh paths and adapter ports

- Status: proposed
- Date: 2026-10-31
- Technical details: FightService, NpcAiService, NpcInventoryService, ProjectileService, NpcNavMeshService, NpcWaterService, GroundSoundService, ParticleService, VmExternalDomain, Domain/Npc/Actions (Core)

## Context

On main, NPCs fought only the hero and only with melee. Archers chased forever, monsters ignored each other, and mobs could be used by two NPCs at once. The author's experimental branch fixed this over many commits, each behind its own DeveloperConfig flag. This PR ports that final state. The fix-only flags are dropped and their "on" path is kept; the feature switches stay.

## Decisions

1. **The source branch's behaviour is the reference.** Each flag the PR drops is `1` in the branch's Production.asset, and its "on" path is what this PR runs. Engine features of the vanilla game (hero spell states, Windfist throw, animation PFX, VisualFx lights and earthquake sound, routine perception reset, ranged combat, VOB world light) have no flag and are always on. The feature switches stay flags.
2. **Arrows and bolts are real flying objects** (`Projectile`). They hit via sphere casts and stick in the body. A miss drops a real item that can be picked up again. Not chosen: an instant hit roll, because cover and dodging wouldn't exist.
3. **NPC chase paths use a NavMesh** (`NpcNavMeshService`), built per world, with ladders as links. The Gothic waynet still drives routines.
4. **Monster and orc guilds are friendly to their own.** Only the human block of the guild table comes from the scripts. The rest was all hostile, so wolves fought wolves.
5. **The hero gets the Daedalus spell states** (sleep, freeze) in `HeroSpellStateDomain`. The hero runs no states of its own, so casters kept recasting.
6. **Core code reaches adapters only through interfaces.** `IProjectile`, `IVisualFxLight`, `INpcJumpFall` and `INpcBurnEffect` live in Core, each with a factory interface (`IProjectileFactory`, ...). The factory classes live in `Adapters/` and `ReflexProjectInstaller` binds them; services and actions inject the interface. Burning goes through the `NpcIgnited` event: `NpcBurnService` plays the victim animation and lights the `NpcBurnEffect` adapter. The NPC spell flight is a `ParticleService` coroutine. Not chosen: static delegates set in `RuntimeInitializeOnLoadMethod` (an invisible, startup-order dependent second DI), and prefabs per adapter, which would mean new assets for components that are created on demand.
7. **AI rules beyond the scripts were removed.** The port had six rules of our own (NPCs reacting to an armed NPC nearby, a fighting NPC turning to a second attacker, an NPC hit by another NPC targeting it, guards knocking out a berzerk NPC, NPCs seeing the hero's summons like the hero, the proclaiming NPC stopping its chase from afar). They are not what the scripts or the engine do, more like a mod, so the maintainer decided in the review to drop them and their flags: the game does what the scripts do. What each did, how, and how to bring it back is in issue #501 (https://github.com/Gothic-Unity-Project/Gothic-Unity/issues/501), for discussion. The 5-second cooldown after a given-up chase (`EnableHeroChaseCooldown`) went too, with the guard's own stop of its chase after `HAI_TIME_PURSUIT`: vanilla has neither. The scripts keep an NPC from coming back endlessly themselves - the gap between the start and the abort distance (`HAI_DIST_ABORT_*`), the `AI_Wait(self, 5)` before `ZS_HealSelf` when the hero ran too long, and `ZS_Attack` not enabling `PERC_ASSESSENEMY` (only the next state does). The "same guild is never a target" checks stay unconditional safety nets in the target selection.

## Consequences

- `FightService` (~2000 lines) and `NpcAiService` need splitting: hero block, spell hit, ranged hit, damage perception, knockout/death.
- The G1/G2 ranged rules are spread over two services.
- Some events have only one side here. The other side comes with the VR melee, magic, Marvin and UI PRs.
- Not in this PR: the hero's footstep perception (movement PR), theft (trade PR), dialog range by body size (dialog PR), model scale and runtime armor mesh swap (meshes PR).
- `NpcJumpFall` and the NavMesh haven't been measured on Quest yet.
