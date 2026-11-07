# ADR: NPC AI - fights, real projectiles, NavMesh paths and adapter ports

- Status: proposed
- Date: 2026-10-31
- Technical details: FightService, NpcAiService, NpcInventoryService, ProjectileService, NpcNavMeshService, NpcWaterService, GroundSoundService, ParticleService, VmExternalDomain, Domain/Npc/Actions (Core); DeveloperConfig "NPC AI and Fight"

## Context

On main, NPCs fought only the hero and only with melee. Archers chased forever, monsters ignored each other, and mobs could be used by two NPCs at once. The author's experimental branch fixed this over many commits, each behind its own DeveloperConfig flag. This PR ports that final state. The fix-only flags are dropped and their "on" path is kept; the feature switches stay.

## Decisions

1. **The source branch's behaviour is the reference.** Each flag the PR drops is `1` in the branch's Production.asset, and its "on" path is what this PR runs. The feature switches stay flags. Example: `EnableNpcRangedCombat`, off as on the branch.
2. **Arrows and bolts are real flying objects** (`Projectile`). They hit via sphere casts and stick in the body. A miss drops a real item that can be picked up again. Not chosen: an instant hit roll, because cover and dodging wouldn't exist.
3. **NPC chase paths use a NavMesh** (`NpcNavMeshService`), built per world, with ladders as links. The Gothic waynet still drives routines.
4. **Monster and orc guilds are friendly to their own.** Only the human block of the guild table comes from the scripts. The rest was all hostile, so wolves fought wolves.
5. **The hero gets the Daedalus spell states** (sleep, freeze) in `HeroSpellStateDomain`. The hero runs no states of its own, so casters kept recasting.
6. **Core code reaches adapters only through interfaces.** `IProjectile`, `IVisualFxLight`, `INpcJumpFall` and `INpcBurnEffect` live in Core, each with a factory interface (`IProjectileFactory`, ...). The factory classes live in `Adapters/` and `ReflexProjectInstaller` binds them; services and actions inject the interface. Burning goes through the `NpcIgnited` event: `NpcBurnService` plays the victim animation and lights the `NpcBurnEffect` adapter. The NPC spell flight is a `ParticleService` coroutine. Not chosen: static delegates set in `RuntimeInitializeOnLoadMethod` (an invisible, startup-order dependent second DI), and prefabs per adapter, which would mean new assets for components that are created on demand.
7. **Some AI rules go beyond the scripts, and each has its own flag.** They are our additions, not engine behaviour, so a mod that relies on the vanilla perceptions can switch them off. All are on by default (the author's branch behaviour):
   - `EnableNpcArmedThreatDetection`: an NPC in routine reacts (ZS_AssessFighter) to a nearby NPC fighting with a drawn weapon. B_AssessFighter only looks at the hero, so NPC fights went unnoticed.
   - `EnableActiveAttackerRetarget`: an NPC already in a fight turns to a second NPC that attacks it. Combat states register no PERC_ASSESSENEMY, so it never noticed.
   - `EnableNpcHitRetaliation`: an NPC hit by another NPC makes the attacker its target. B_CombatReactToDamage does this for the hero only.
   - `EnableHeroChaseCooldown`: after giving up the chase, an NPC ignores the hero for 5 seconds. The scripts restart the fight at once and the NPC came back endlessly.
   - `EnableGuardsAgainstBerzerk`: guards (G1) knock out a berzerk NPC. Vanilla lets it rage.
   - `EnableHeroSummonAttitude`: NPCs see the hero's summons the way they see the hero. In G2 the scripts make guards attack them.
   Checked against OpenGothic: none of these exist there. The engine behaviours we copied (combo continue, hero block, finishing move, ...) are not listed, they have their own feature flags already. The "same guild is never a target" checks stay unconditional safety nets inside these rules.

## Consequences

- `FightService` (~2000 lines) and `NpcAiService` need splitting: hero block, spell hit, ranged hit, damage perception, knockout/death.
- The G1/G2 ranged rules are spread over two services.
- Some events have only one side here. The other side comes with the VR melee, magic, Marvin and UI PRs.
- Not in this PR: the hero's footstep perception (movement PR), theft (trade PR), dialog range by body size (dialog PR), model scale and runtime armor mesh swap (meshes PR).
- `NpcJumpFall` and the NavMesh haven't been measured on Quest yet.
