# NPC AI and fights

A map of the code that makes NPCs think, move and fight. The scripts decide (`ZS_*` states, `B_*` reactions,
perceptions), C# runs their externals and the engine rules around them. Paths are under `Assets/Gothic-Core/Scripts/`
unless a module is named. Decisions: `Docs/adr/2026-10-31-npc-ai-port.md`.

## The loop

1. `Adapters/Npc/AiHandler` (one per NPC, Update): runs the state loop (`ZS_*` / `_Loop` / `_End`), the active
   perception tick (`ExecuteActivePerceptions`: ASSESSPLAYER, ASSESSENEMY, ASSESSFIGHTER, ASSESSBODY, ...) and the
   queue of animation actions.
2. The scripts call externals. `Domain/Vm/VmExternalDomain` registers them all and owns one domain per Gothic family:
   `VmAiExternalDomain` (`AI_*`), `VmNpcExternalDomain` (`Npc_*`), `VmNpcInventoryExternalDomain` (items, weapons,
   armor), `VmWorldExternalDomain` (`Wld_*`), `VmModelExternalDomain` (`Mdl_*`), `VmUiExternalDomain` (`Doc_*`,
   `Info_*`, `Log_*`, `Print*`), `VmUtilityExternalDomain` (`Hlp_*`, `Mob_*`, `Perc_*`, ...). Shared helpers:
   `VmExternalSupport`, `VmExternalGuard` (no exception crosses into the VM).
3. The external calls the service of its area, mostly `Services/Npc/NpcAiService`. `AI_*` externals queue an
   action from `Domain/Npc/Actions/AnimationActions/` into `NpcProperties.AnimationQueue`.

## Who owns what

| Class | Owns |
|---|---|
| `Services/Npc/NpcAiService` | the `AI_*`/`Npc_*` externals' logic, perceptions (send, deliver, passive broadcast), AI queue, attitudes, the hero's weapon state perceptions |
| `Domain/Npc/NpcTargetSelectionDomain` (owned by NpcAiService) | who an NPC fights or looks at: `Npc_GetNextTarget`, `UpdateEnemyNpc`, `FindClosestSensedBody` |
| `Domain/Npc/HeroSpellStateDomain` (NpcAiService) | sleep/freeze spells on the hero |
| `Services/Npc/NpcHelperService` | senses (`CanSenseNpc`, `CanSeeNpc`), perception ranges, person/guild attitude |
| `Services/Npc/FightService` | how an NPC goes down: a hit (`FightHit`), hit points gone, knockout, death, finishing moves (the NPCs' and the hero's), the hero's death; the hero's prehit swing |
| `Domain/Npc/MeleeDamageDomain` | melee damage: roll, damage types, protection; the formula comes from `IMeleeDamageRule` (G1/G2) |
| `Domain/Npc/SpellHitDomain` | an NPC casting its spell (`CastNpcSpell`) and spells reaching NPCs: ASSESSMAGIC, area/spread hits, burning, Windfist throw |
| `Domain/Npc/RangedHitDomain` | arrow/bolt damage via `IContextRangedCombatService` (G1/G2) |
| `Domain/Npc/HeroBlockDomain` | parries, jump backs, the hero's block via `IContextHeroBlockService` (VR/Flat), weapon clash |
| `Domain/Npc/DamagePerceptionDomain` | who reacts to a hit: victim, witnesses, party members |
| `Domain/Npc/NpcKnockoutDomain`, `NpcDeathDomain` | unconscious vs dead, XP, `ZS_Dead` |
| `Services/Npc/ProjectileService` | flying arrows/bolts, line of shot, hit chance |
| `Actions/AnimationActions/Attack` | `AI_Attack`: picks the fight move (`FightAiMove`) and queues `AttackPlayAni` |
| `Actions/AnimationActions/AttackPlayAni` | one fight move: hit frame, engine combo, run attack, strafe, ranged follow-up, swimming |
| `Domain/Npc/NpcChaseMovement` (one per AttackPlayAni) | running after the target: NavMesh steering, climb, ladder, drop, give-up timer |
| `Actions/AnimationActions/AbstractWalkAnimationAction2` | walking to WP/FP/NPC/item/mob, incl. ladders on the way |
| `Adapters/Npc/NpcJumpFall` (via `INpcJumpFallFactory`) | falling, climbing, ladders, throws - only the tick; rules in `Domain/Npc/NpcFallDomain` and `Services/Movement/LedgeService` |
| `Services/Npc/NpcNavMeshService` | NavMesh steer points and ladder links |

Platform and game version ports (in `Services/Context/`, registered in the modules' `*ContextBootstrap`):

- `IContextHeroBlockService`: does the hero block now? `Gothic-VR/.../VRContextHeroBlockService`, Flat has its own.
- `IContextRangedCombatService`: ranged damage and hit chance. `Gothic1/.../G1RangedCombatService`, `Gothic2/...`.
- `IMeleeDamageRule`: G1 critical hit vs G2 hit chance. `G1MeleeDamageRule`, `G2MeleeDamageRule`.

## Rules beyond vanilla

Our own additions and the fight features sit behind `DeveloperConfig` flags. The first row is an AI rule the scripts
don't have (on in Production, listed in the ADR). The other AI rules beyond the scripts were removed, see
[#501](https://github.com/Gothic-Unity-Project/Gothic-Unity/issues/501). Check the flag before you assume a behaviour
comes from the scripts:

| Flag | Where |
|---|---|
| `EnableHeroChaseCooldown` | `NpcAiService` |
| `EnableVrChainLightning` | `SpellHitDomain` |
| `EnableRangedKnockoutOption` | `FightService`, `SpellHitDomain`, `MeleeDamageDomain` |
| `EnableVrHeroBlock` | `VRContextHeroBlockService` (Gothic-VR) |
| `EnableNpcNavMesh` | `NpcNavMeshService` (used by `NpcChaseMovement`) |
| `EnableCombatSystem` | `FightService.Init` (no hit listeners without it) |

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change which enemy an NPC picks / switches to | `NpcTargetSelectionDomain` |
| change what an NPC perceives (range, sight, hearing) | `NpcHelperService` (senses), `AiHandler.ExecuteActivePerceptions` (tick) |
| fix an `AI_*` / `Npc_*` external | the family's `Vm*ExternalDomain` registers it, `NpcAiService` implements it |
| change which fight move an NPC chooses | `Attack` |
| change how a swing, combo or hit frame plays | `AttackPlayAni` |
| change how an NPC runs after its target (stuck, ladders, give up) | `NpcChaseMovement` |
| change melee damage | `MeleeDamageDomain`, G1/G2 formula in the module's `*MeleeDamageRule` |
| change spell effects or an NPC's spell cast | `SpellHitDomain` |
| change arrows and bolts | `ProjectileService` (flight), `RangedHitDomain` + `*RangedCombatService` (damage) |
| change parry/block | `HeroBlockDomain`, the VR side in `VRContextHeroBlockService` |
| change who reacts to a hit | `DamagePerceptionDomain` |
| change knockout or death | `NpcKnockoutDomain`, `NpcDeathDomain` |
| change falling, climbing, fall damage | `NpcFallDomain`, `LedgeService`; `NpcJumpFall` only moves |
| add a rule the scripts don't have | the owning class above, behind a new `DeveloperConfig` flag (+ `Production.asset`), listed in the ADR |
