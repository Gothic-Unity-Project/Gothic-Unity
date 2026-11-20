# Architecture audit: `Services/Npc` (NpcAiService, NpcService, FightService and their domains)

## Summary
- **Verdict: needs redesign, in steps.** `NpcAiService` (1715 lines, 87 commits and +2074/-879 lines in 8 weeks) does about 11 jobs. The biggest are the perception bus, the AI-queue clear policy, state start, state-time tuning and ~30 thin `AI_*` wrappers. It is both the script façade (`Ext*`) and the engine-internal API.
- `NpcService` (970) holds seven unrelated jobs. Four of them have an obvious existing home: `NpcRoutineService`, `NpcLookService`, `DialogService`, `HeroPerceptionService` / `PlayerService`. `FightService` (820) is mostly well split into domains, but also does hero item-requirement messages and hand-mesh cleanup.
- The biggest drift the test can't see: the services reach the adapters through `NpcContainer.PrefabProps` (32 uses across the 3 services: `AiHandler.ClearState`, `AnimationSystem`, `AudioSource`, `Object.Destroy`). The using-based check never catches this.
- Rules written more than once: hero identity (4 different checks, ~40 places), weapon-state classes (≥6 places), "in the current dialog" (3), saving and restoring the VM globals (5 copies, with different null rules), the hero's voice source (3 copies in FightService plus Output.cs), and two passive-perception paths with different range rules.
- Two baseline lines go away cheaply (50, 51). The root cause of ~8 more is that `NpcProperties` lives in `Adapters/` and holds a Domain type.

---

## 1. `NpcAiService.cs`: responsibilities and split

### Responsibility map (line ranges)
| # | Responsibility | Lines | Size |
|---|---|---|---|
| A | Fields, init, clearing the perception lists | 30-78 | 50 |
| B | Perception execution: enable/disable, `ExecutePerception` (+int-return cache), `ExecuteAssessEnemy` + log de-dup, `CallVmFunctionWithNpcGlobals`, `ExtNpcSetPerceptionTime`, `ExtNpcSendSinglePerc` | 80-240, 677-688 | ~175 |
| C | AI-queue clear policy (`ExtNpcClearAiQueue`/`ClearAiQueue`: keeping the whirl and dialog lines, combo preload, mage cast, mob, walk, undraw, idle, swim) | 284-417 | 135 |
| D | Thin `AI_*` enqueue wrappers (GoTo*, Align*, Wait, PlayAni, LookAt*, PointAt*, Turn*, Whirl, UseMob, UseItemToState, PlayAniBs, Draw/Undraw/ReadyRanged, Attack, PlayAttackAni) | 242-266, 419-441, 477-499, 606-666, 748-766, 807-819, 861-948, 1111-1124, 1202-1227 | ~300 |
| E | Composite `AI_*` commands with rules: FinishingMove + `GetFinishDistanceMeters`, `ExtAiStartState` + `IsTalking`, Dodge, StandUp/Quick, armor change, UseItem, ReadySpell, TurnToNpc + guard-sneak | 443-475, 501-604, 768-788, 821-855, 950-980, 1094-1109, 1294-1318, 908-930, 1060-1087 | ~330 |
| F | State time with VR chase tuning (`ExtNpcGetStateTime`, `IsChasingRunningHero`, `IsHeroCloseInAttackLoop`, three hand-made symbol-index caches) | 982-1092 | 110 |
| G | Thin `Npc_*` queries and setters (HasBodyFlag, PlayAni/StopAni, Knows/SetKnowsPlayer, HasNews, IsInCutscene, WasInState, BodyState, IsDead, IsInState, IsPlayer, armor, IsInFightMode, OwnedByNpc, TrueGuild, RefuseTalk, SetStateTime) | 668-742, 790-805, 1126-1134, 1229-1292, 1320-1328, 1588-1600, 1705-1713, 1089-1092 | ~200 |
| H | Distances (`ExtNpcCanSeeNpc`, DistToNpc/Player, HeightToNpc, hero = `Camera.main`) | 268-282, 726-730, 1136-1200 | ~80 |
| I | Attitude and target (pass-throughs to `NpcHelperService` / `NpcTargetSelectionDomain`) | 1330-1411, 1603-1617 | ~100 |
| J | Passive perception bus (send, deliver per receiver per frame, single, down/murder, warner rewrite, state perceptions) | 52-57, 1413-1586 | ~180 |
| K | Hero-originated perceptions (`ExtSetHeroWeaponState`, `SendHeroCasterPerception`, `BroadcastHeroPassivePerception`, `IsArmedWeaponState`) | 1619-1703 | 85 |
| L | Hero spell state (`ApplyHeroSpellState`, the redirect inside StartState) | 501-509, 519-540 | 30 |

Unused injections: `_multiTypeCacheService`, `_physicsService`, `_configService` (lines 36-38). Line 21 is a duplicate `using`.

### Proposed split (each step its own PR)
| New / target type | Layer | Owns | Methods moving | Callers affected |
|---|---|---|---|---|
| **`NpcPerceptionService`** (`Services/Npc/`, ProjectScope) | Service: owns state (sent and delivered lists, `_isIntPerception`, AssessEnemy log table) | B + J | `ExecutePerception`, `ExecuteAssessEnemy`, `ExecuteSinglePerception`, `ExtNpcPerceptionEnable/Disable`, `ExtNpcSetPerceptionTime`, `ExtNpcSendSinglePerc`, `Npc_SendPassivePerc`, `BroadcastPassivePerception`, `BroadcastDownPerception`, `DeliverPassivePerceptions` | AiHandler, DamagePerceptionDomain, SpellHitDomain, NpcDeathDomain, NpcKnockoutDomain, PickpocketService, HeroPerceptionService, VmNpcExternalDomain, VRNpc, VRChainLightning |
| **`HeroPerceptionService`** (exists: "what the engine tells NPCs about the hero") | Service | K | `ExtSetHeroWeaponState`, `SendHeroCasterPerception`, `BroadcastHeroPassivePerception` (sent through the deferred bus, see finding 4) | AiHandler, VRWeaponService, VRRuneCaster |
| **`AiQueueClearDomain`** (`Domain/Npc/`, `new AiQueueClearDomain().Inject()` in NpcAiService) | Domain, no state | C | `Clear(NpcContainer)`; `ExtNpcClearAiQueue` stays a one-line façade | none outside (same public method) |
| **`AiStartStateDomain`** (`Domain/Npc/`, owned by NpcAiService) | Domain | E (StartState) + L redirect | `StartState(container, action, stopCurrent, wp, isScriptCall)`, `IsTalking`; uses `HeroSpellStateDomain` | DamagePerceptionDomain and NpcKnockoutDomain call the engine API (finding 2) |
| **`NpcStateTimeDomain`** (`Domain/Npc/`, owned by NpcAiService) | Domain | F + guard sneak (1060-1087) | `GetStateTime`, `IsHeroSneakingBehindGuard`; the tunables (`_chaseStateTimeScale`, `_caughtHeroDistance`) go to `AiConst`/`DeveloperConfig` | none outside |
| Extensions / `Vm*ExternalDomain` directly | Extensions | G | `IsDead`, `IsPlayer` (→ `ZenKitExtension.IsHero`), `IsInFightMode` (→ `WeaponState` extension), `GetEquippedArmor` (→ `NpcProperties`/extension), KnowsPlayer, RefuseTalk, TrueGuild... | VmNpcExternalDomain, VmNpcInventoryExternalDomain. Needs a change to the "where does my code go" row (discussion below) |
| `NpcHelperService` → rename later to `NpcSensesService` | Service | H | `GetDistanceCm` and `GetHeightCm` next to `CanSeeNpc` | VmNpcExternalDomain |

What stays in `NpcAiService`: D and E minus StartState, about 550-650 lines of `AI_*` commands. If it is still too big, split the weapon commands (Draw/Undraw/ReadySpell/ReadyRanged/FinishingMove/PlayAttackAni/Attack, ~130 lines) into `NpcFightAiDomain`.

Logical cycle to watch: `NpcPerceptionService` → `NpcAiService.ExtAiStartState` (state perceptions, 1584) is fine one-way. Nothing in the queue code should call back into perception. Today `ExtNpcSendSinglePerc` sits in the same class; once it moves, the arrow points one way.

## 2. `NpcService.cs` and `FightService.cs`

### NpcService (970 lines, 14 injections)
| Responsibility | Lines | Proposed home |
|---|---|---|
| Lazy-init / re-enable queues, coroutines, culling event | 63-177, 693-720, 876-895 | **stays** (NPC lifetime in the world) |
| Spawn / insert / remove / despawn / detach | 179-242, 309-392 | stays |
| Attribute store, written to both Vob and Instance (`SetAttribute`, `ReduceHitPoints`, `ExtNpcChangeAttribute`, talents, `SetLevel`/`SetLp`, `SyncHeroInstanceToVob`) | 244-267, 409-490, 511-525 | later `NpcAttributeService` (11+ callers of `SetAttribute`; this is the "one place" for the two stores, so keep it in one class) |
| Hero bootstrap (`CacheHero`: `FindWithTag`, `GetComponentInChildren<NpcPrefabProperties>`, `Camera.main`), `TransformHero*`, `GetHeroGameObject` | 492-618 | `PlayerService`, with the hero rig supplied by an `IHeroRigProvider` registered by VR/Flat (removes the tag search, part of baseline 53) |
| `Mdl_*` visuals (`ExtMdlSetVisual`, `ExtSetVisualBody`, scale, fatness, overlay) | 269-307, 620-645 | `NpcLookService` (already does fatness and the ghost look). Callers: VmModelExternalDomain |
| `TA_Min` routine entry | 674-691 | `NpcRoutineService`. Caller: VmNpcExternalDomain |
| FP/WP queries | 722-784 | same family as `NpcHelperService.ExtIsNpcOnFp`/`ExtNpcGetDistToWp`/`ExtIsMobAvailable`: one `NpcWayNetDomain` (or `WayNetService` methods) |
| Dialog assignment + ambient C_INFO workaround | 786-874 | `Domain/Dialog/AmbientInfoDomain` owned by `DialogService` (only caller) |
| Hand item mesh (`RemoveUsedItemFromHand`, `DestroyUsedItemMesh`, `InsertItem`, Fist/FightMode) | 647-672, 897-968 | `Domain/Npc/NpcHandSlotDomain`, also replacing the copy in `FightService.DropReadiedWeapon` (713-718) |

### FightService (820 lines, 14 injections, 8 owned domains)
The core hit pipeline (277-477) and down/kill (172-275, 735-765) fit the class doc. What doesn't belong:
- **Hero weapon/rune requirements + `G_CanNotUse`/`G_CanNotCast` prints** (595-684): an equip/use rule with VR-only callers (VRBow, VRCrossbow, VRVobItem, VrFavoritesPutInDomain, VRRuneCaster). Move to `HeroItemRequirementDomain`, owned by the item/equip service (`ItemUseService` or wherever `EquipDomain` lives).
- **Hero prehit swing** (107-161): reaches into `AnimationQueue`, `Wait`, `AttackPlayAni` internals. Move to `HeroBlockDomain` (it already owns hero block/parry) or a new `HeroPrehitDomain`.
- **Voice / pain / death sound** (514-540, 767-818): three copies of "the hero's AudioSource via `GetComponent`". Move to one `NpcVoiceDomain.PlaySvm(npc, "DEAD"|"AARGH")`.
- **`DropReadiedWeapon`** (686-724): inventory + hand mesh + FightMode reset. Move to `NpcHandSlotDomain` (above).
- Pass-through façades (`GetPreferredAttackDistance`, `GetFightDistance`, `CastNpcSpell`, `ReportBlockContact`, `RunZsDead`): fine as an API for the actions, but the actions could inject the domains' owner less broadly. Low priority.

## 3. Service-to-service coupling

| Service | Injects (NPC area and key others) | Injected / used by |
|---|---|---|
| NpcAiService | GameState, NpcHelper, WayNet, NpcMeshCulling (+3 unused) | Vm{Ai,Npc,NpcInventory}ExternalDomain, FightService, HeroPerceptionService, PickpocketService, AiHandler, actions (Attack, GoToNpc, ContinueRoutine), domains (DamagePerception, Knockout, Death, SpellHit), VR (VRWeaponService, VRRuneCaster, VRNpc, VRChainLightning) |
| NpcService | GameState, Vm, Config, UnityMono, FrameSkipper, MultiTypeCache, VmCache, SaveGame, Player, WayNet, NpcMeshCulling, Vob, NpcInventory, NpcLook | FightService, 4 fight domains, AiHandler, ~30 files for `GetHeroContainer` |
| FightService | Audio, Animation, Physics, NpcHelper, GameState, **NpcService, NpcAiService**, NpcMeshCulling, ContextInteraction, Vob, NpcInventory, Player, Config, INpcJumpFallFactory | Attack, AttackPlayAni, GoToNpc, AiHandler, VR items, block services |
| NpcHelperService | GameState, MultiTypeCache, VmCache (unused), WayNet, Vob, ContextGameVersion | NpcAiService, FightService, ProjectileService, PickpocketService, NpcTargetSelectionDomain, ext domains, VR |
| HeroPerceptionService / PickpocketService | → NpcAiService (perception bus) | VmNpcExternalDomain / VR |

- **No service-level cycle.** Logical loops:
  - `NpcAiService` creates `Attack`/`GoToNpc`, which inject `NpcAiService` and `FightService` and call `ExtAi*` back.
  - `NpcTargetSelectionDomain` gets its owner passed in (line 63) and calls back `ExtGetAttitude`.
  - `FightService` → its domains → `NpcAiService`.
- **God objects:** NpcAiService (above), NpcService (7 jobs).
- **Pass-throughs:** `NpcAiService.ExtGetAttitude`/`ExtGetPermAttitude` → `NpcHelperService.GetPersonAttitude`/`GetPermanentAttitude`. The helper methods have exactly one caller (NpcAiService), so it's two hops. In the externals path, `VmAiExternalDomain.AI_Wait` → `NpcAiService.ExtAiWait` → `Enqueue` (two thin layers for ~40 externals).
- `NpcHelperService` is a grab-bag by name and content (principle 6): senses, attitudes, talents, FP/mob, weapons, world items, plus public mutable state (`PerceptionRanges`, `HeroHolsteredMeleeWeapons`, lines 31/122).

## 4. Layer violations and baseline

Baseline lines in this area:
| Line | Entry | Cost to remove |
|---|---|---|
| 50 | `Services/Npc/NpcAiService.cs \| Adapters.Properties` | **S.** Only the explicit `NpcProperties` type in signatures at 100, 116, 149, 1019, 1032. Take an `NpcContainer` instead. `ExecutePerception` callers: AiHandler, DamagePerceptionDomain, SpellHitDomain, VRChainLightning, VRNpc. Caveat: the dependency stays, hidden behind `NpcContainer.Props`. |
| 51 | `Services/Npc/NpcHelperService.cs \| Adapters.Properties` | **S.** The private `GetProperties()` at 425 returns `NpcProperties`; inline `npc?.GetUserData()?.Props`. |
| 52 | `NpcHelperService.cs \| Adapters.Properties.Vobs` | M. `mob?.Props is not InteractiveProperties` (106). Needs an "is interactive" fact on `VobContainer`, or `InteractiveProperties` moved to `Models/` as the doc says. |
| 53 | `Services/Npc/NpcService.cs \| Adapters.Npc` | M. `NpcLoader` (MonoBehaviour) as the queue element (63, 695, 876, plus the `NpcMeshCullingChanged` event signature) and `NpcPrefabProperties`/`INpcSubtitles` in `CacheHero` (575-579). Fix with a hero-rig provider and a plain model queued instead of `NpcLoader`. |
| 18-20, 22 | animation actions → `Adapters.Npc`/`Adapters.Properties`/`Adapters.Vob` | via `PrefabProps`/`NpcProperties` |
| 23-25 | `NpcInitializerDomain` | not examined |
| 36-37 | `Models/Container/NpcContainer.cs` → Adapters | root cause |
| 43-44 | `NpcProxy` → Services | not examined |

- **Root cause of lines 19, 24, 37, 50, 51 (and 54, DialogService):** `NpcProperties` is a plain C# class that the doc says belongs in `Models/`. But it holds `Queue<AbstractAnimationAction>` and `CurrentAction` (Domain types, NpcProperties.cs:3, 74, 155), so moving it as is would add a Models→Domain violation. Smallest correct step: an `INpcAiAction` interface (or an abstract base without services) in `Models/Npc`, then move `NpcProperties` to `Models/Npc`. Effort M, mostly mechanical.
- **Not in the baseline, but the same violation in substance:** the 3 services call adapters through `PrefabProps`:
  - NpcAiService: `AiHandler.ClearState`/`IsInComboPreload` (363, 567, 588), `AnimationSystem.StopAllAnimations`/`IsPlayingOnlyIdle`/`PlayAnimation`/`StopAnimation`, `AnimationHeadHandler.StopLookAt`, `CurrentInteractable.Props`.
  - FightService: `GetComponent<AudioSource>`, `FindChildRecursively`, `Object.Destroy`.
  - NpcService: `FindWithTag`, `GetComponentInChildren`, `Object.Destroy`.

  Worst of these: the Daedalus state machine (`ClearState`, `CallStateEnd`, `StartRoutine`, `ExecuteStates`) lives in the 905-line adapter `AiHandler`, so "the AI of one NPC" is spread over AiHandler (state machine), NpcProperties (queue/state) and NpcAiService (queue rules).
- Domain created by Adapters: none found in this folder; all domains are created by their services.
- Models holding logic: `NpcProperties.HasQueued`/`AreAllQueued` are fine (rule next to its data).

## 5. Duplicated logic
- **Hero identity, 4 variants:** `PrefabProps.IsHero()` (13 places in Services/Npc + Domain/Npc), `Instance.IsHero(vm)` (13), `Vob.Player` (5), `npc.Index == GlobalHero.Index` (9). They already drift within one file: `ExtNpcGetDistToNpc` (1159) uses the index, `ExtNpcGetHeightToNpc` (1192) uses `IsHero(vm)` with a comment saying the other way was a bug. Also `GlobalHero as NpcInstance` (10) next to `NpcService.GetHeroContainer()`.
- **Weapon-state classes:** NpcAiService 459-460, 782-783, 1283-1291, 1700-1703; FightService 690-697; plus 7 `W1H or/||` and 15 `Bow or/||` sites in Services/Domain/Adapters Npc. `ItemInstanceExtension.IsMeleeWeapon` exists for items but nothing exists for `WeaponState`. Fix: `WeaponStateExtension.IsMelee/IsRanged/IsArmed/Matches(FightMode)`.
- **"In the current dialog":** `FightService.IsInCurrentDialog` (586-593), `NpcAiService.ExtNpcIsInCutscene` (735-742), `ClearAiQueue` (334-335). One method on the dialog state (`GameStateService.Dialogs`).
- **VM globals save/restore:**
  - `NpcAiService.CallVmFunctionWithNpcGlobals` (210-230) keeps the old other/victim when given null. `VmGlobalsScope` (Domain/Vm) says explicitly that null must be set "like the engine". Two contradicting rules.
  - FightService 652-661 and 674-683 (copies of each other).
  - `VmService.CallAsHero` (no-arg only), ItemUseService, TradeService, NpcInventoryService, SpellHitDomain, NpcDeathDomain, NpcKnockoutDomain, UseItemEffect, AiHandler: 13 hand-written swaps in total.
- **Daedalus constant read** `GetSymbolByName(X)?.GetInt(0) ?? default`: NpcAiService 130-131, 473-474, 717-718; FightService 639. **Lazy symbol-index sets:** NpcAiService 1021-1023, 1034-1044, 1067-1077, never reset per VM. Fix: one `DaedalusSymbolSet`/`VmCacheService.GetIntConst` helper.
- **Two passive-perception paths with different range rules:** the deferred bus uses the perception range, else senses (1509-1511). The hero broadcast uses `Min(range, senses)` and runs immediately (1685). Also `ExecutePerception` vs `ExecutePassivePerception` vs `ExecuteSinglePerception` vs `ExtNpcSendSinglePerc`: four entry points.
- **Hand slot mesh destroy:** `NpcService.DestroyUsedItemMesh` (928-949) vs `FightService.DropReadiedWeapon` (712-718).
- **Hero voice AudioSource:** FightService 530-535, 772, 806-807; plus `Output.cs` via `GetHeroGameObject`.
- **FP/WP queries** split between NpcService (722-784) and NpcHelperService (`ExtIsNpcOnFp` 168, `ExtNpcGetDistToWp` 237).
- **"Keep these queued actions" filters** in the clear policy, written twice with LINQ: whirl (298-326) and dialog outputs (337-345).

## 6. Public API with zero or one caller / special cases
- Zero external callers: `NpcService.CreateVobNpc` (event listener only; make it private).
- One special caller:
  - NpcAiService: `ApplyHeroSpellState` (SpellHitDomain), `CallVmFunctionWithNpcGlobals` (DamagePerceptionDomain; a VM helper on the AI service), `GetFinishDistanceMeters` (FightService; a script constant), `PlayAttackAni` (Attack), `IsArmedWeaponState` (AiHandler; a pure rule), `SendHeroCasterPerception` (VRRuneCaster).
  - NpcService: `GetClosestNpcInFront` and `DetachFromWorldNpcs` (both only VRSpellTransformPuppet: a VR use case in a Core service), `SpawnNpcByName`/`SetLp` (Marvin), `SetDialogs` (DialogService), `DestroyUsedItemMesh`/`InsertItem` (AnimationSystem), `ClearQueues` (BootstrapService, while `Init` also clears on `LoadGameStart`: two clear paths).
  - FightService: `OnScriptDamageDepleted` and `ApplySelfSpellDamage` (only VRChainLightning; an `On*` name for a direct call), `CheckHeroRuneCircle`/`CheckHeroWeaponRequirement` (VR only).
  - NpcHelperService: `GetPersonAttitude`/`GetPermanentAttitude` (only NpcAiService), `HasFreeSight` (VrSpellLockDomain).
- **The `Ext*` façade is also the engine API.** `ExtAiStartState(..., isScriptCall)` (511-512) has a flag only so engine callers (NpcKnockoutDomain, DamagePerceptionDomain) can skip script behaviour. `Attack.cs` calls `ExtAiDrawWeapon`/`ExtAiWait`/`ExtAiTurnToNpc`/`ExtAiStandUp`; the domains call `ExtNpcClearAiQueue` and `ExtGetAttitude`. A script-compatibility tweak to an `Ext*` method silently changes engine behaviour.

## 7. Per-frame cost
- `DeliverPassivePerceptions` (1476-1545): called from `AiHandler.Update` per awake NPC per frame. O(1) when idle. When perceptions are pending it is O(receivers × perceptions), and each delivery in range calls `ExecutePassivePerception` (1576-1578): `GetSymbolByIndex(...).Name` (marshalled string), `symbolName + "_LOOP"` and `GetSymbolByName` per receiver, all allocating. It also relies on whichever NPC calls first in a frame to swap the lists (`Time.frameCount`, 1478): hidden temporal coupling and Unity time in a service.
- `NpcService.ExtHlpGetNpc` (394-407) and `ExtWldRemoveNpc` (320): a linear scan of the whole world's `NpcCache` with a capturing lambda. `Hlp_GetNpc` is one of the most-called externals (dialog conditions, B_ functions). The cost grows with world size on a script path.
- `ExtNpcClearAiQueue` (298, 320, 337): 2-3 `ToList()` per call. ZS_Attack_Loop calls it every ~2 s per fighter, summons every 0.5 s (B_FullStop). Low frequency, but it scales with fighters.
- `ExtGetEquippedArmor` (1269): LINQ `FirstOrDefault` on a List through `IEnumerable`; scripts ask it in ZS_Attack_Loop (C_PlayerIsFakeBandit). Low.
- Event-driven and fine: `OnHeroMeleeSwing` loops awake NPCs per swing; `BroadcastHeroPassivePerception` builds strings per notified NPC (per draw/cast); `GetComponent<AudioSource>` per hero hit.
- `NpcService` coroutines (111-177) run forever and poll empty queues every frame. Cheap, but it's the P1 pattern.

---

## Ranked findings

1. **[high]** `Assets/Gothic-Core/Scripts/Services/Npc/NpcAiService.cs:30-1715`: god object with 11 responsibilities (map above); the area with the most churn in the repo.
   - Why it matters: every AI fix needs the whole 1.7 k-line file read, and principle 8 is badly broken. Per-PR diffs can't see that the perception, queue and state-time rules are tangled in one class.
   - Fix: split as in section 1, in this order: `NpcPerceptionService` (+K to `HeroPerceptionService`), `AiQueueClearDomain`, `AiStartStateDomain`, `NpcStateTimeDomain`. Update the map in `Docs/npc-ai.md` and add an ADR.
   - Effort: L in total (4 M-sized PRs).

2. **[high]** `NpcAiService.cs:363,567,588,292,408,416,840` (+ FightService 13 and NpcService 8 `PrefabProps` uses): services drive adapters (`AiHandler.ClearState`, `AnimationSystem`, `AudioSource`, `Destroy`) through `NpcContainer.PrefabProps`, and the Daedalus state machine lives in the `AiHandler` adapter (905 lines).
   - Why it matters: the layer rules hold only on paper. The using-based test passes while Core logic depends on MonoBehaviours, and the "NPC AI" is spread over adapter + model + service.
   - Fix (later): first, ports for what the services need (`INpcAnimator`: Play/Stop/StopAll/IsPlayingOnlyIdle, `INpcVoice`) implemented by the adapters. Then move `ClearState`/`CallStateEnd`/`StartRoutine` from AiHandler into `Domain/Npc/NpcStateMachineDomain` owned by NpcAiService, and AiHandler only ticks it.
   - Effort: L.

3. **[medium]** `NpcAiService.cs:511-512`, `Attack.cs`, `NpcKnockoutDomain.cs`, `DamagePerceptionDomain.cs`: the `Ext*` methods serve both the scripts and the engine (the `isScriptCall` flag proves the drift).
   - Why it matters: an engine change goes into a script-facing method and vice versa, and behaviour differences hide behind flags.
   - Fix: engine-facing names without the `Ext` prefix (`StartState(container, …)`, `ClearAiQueue(container)`, `GetAttitude`). `Ext*` stays as a 1-line script adapter, or moves into the `Vm*ExternalDomain`.
   - Effort: M.

4. **[medium]** `NpcAiService.cs:1509-1511` vs `1685`; `210-230` vs `Domain/Vm/VmGlobalsScope.cs:8-10`: the same rule written twice with different behaviour, once for the perception range and once for which VM globals stay set (null handling).
   - Why it matters: principle 3; mods and scripts see different engine behaviour depending on the path, and the hero's broadcast runs immediately, which the deferred bus was built to avoid (comment 1430-1432).
   - Fix: one `GetPassivePerceptionRange(type, receiver)` in the new `NpcPerceptionService`, the hero broadcast going through `BroadcastPassivePerception`, and `CallVmFunctionWithNpcGlobals` replaced by `VmGlobalsScope`. Add `VmGlobalsScope.Self(vm, npc)` for the 13 hand-written swaps.
   - Effort: S-M.

5. **[medium]** `NpcService.cs:674-691, 269-307, 620-645, 786-874`: misplaced jobs.
   - `TA_Min` belongs to `NpcRoutineService`, `Mdl_*` to `NpcLookService`.
   - The ambient-dialog assignment is a ZenKit workaround (principle 5), a re-implementation of the script function `B_AssignAmbientInfos` (principle 1), and has a guild table with G1 guilds only (862-874, principle 4: G2's MIL/PAL/... get no ambient infos), all inside the NPC service.
   - Why it matters: three people working on routines, looks or dialogs all touch NpcService; G2 and mods silently miss a feature.
   - Fix: move the first two (S each). Then an `AmbientInfoDomain` owned by `DialogService`, with the guild prefixes supplied through `IContextGameVersionService` (or derived from the symbol names only, without the table) and a line in the dialog doc's pitfalls.
   - Effort: S + S + M.

6. **[medium]** `FightService.cs:595-684`: hero weapon/rune requirement checks and the script prints live in the "how an NPC goes down" service, called only by VR item code.
   - Why it matters: the class doc stops being true, and the FightService API grows for equip rules.
   - Fix: `HeroItemRequirementDomain`, owned by the item/equip service. Use `VmGlobalsScope` for the two G_CanNot* calls.
   - Effort: S.

7. **[medium]** `NpcService.cs:394-407` (`ExtHlpGetNpc`) and `320`: a whole-world scan with a closure on a frequent script path.
   - Why it matters: the cost grows with the number of NPCs in the world on Quest; GC from the closure.
   - Fix: an `int instanceIndex → NpcContainer` index maintained in `MultiTypeCacheService` next to `NpcCache` add/remove.
   - Effort: S.

8. **[medium]** Hero identity and weapon-state rules written 40+ and 20+ times (section 5).
   - Why it matters: principle 3; they already disagree inside one file (1159 vs 1192).
   - Fix: one hero check (`NpcContainer.IsHero()` delegating to `ZenKitExtension.IsHero(vm)`, or a cached flag set in `CacheHero`) and a `WeaponStateExtension`; replace the uses as files get touched.
   - Effort: S to create, M to roll out.

9. **[medium]** `NpcAiService.cs:1476-1586` (`DeliverPassivePerceptions` / `ExecutePassivePerception`): per-receiver symbol-name lookups and string concatenation on delivery, and the list swap depends on whichever NPC updates first in the frame.
   - Fix: cache "is state perception" per function index (like `_isIntPerception`); swap the lists in an explicit once-per-frame tick (`UnityMonoService` / an `AiTickAdapter`) instead of the first caller.
   - Effort: S.

10. **[low]** VR tuning inside Core NPC services: `NpcAiService.cs:995-1013` (state time ×2 "feels right in VR", 4 m caught distance), `1157-1161`, plus "VR hero" assumptions at NpcService 602-605, 663-666 and NpcAiService 965-979.
    - Why it matters: principle 4; Flat inherits VR tuning, and the tunables are local `private const`s.
    - Fix: the tunables go to `AiConst`/`DeveloperConfig` or an `IContextNpcAiTuning` registered by VR/Flat. State the "the hero has no Gothic body" assumption once in `Docs/npc-ai.md` instead of at every call site.
    - Effort: S.

11. **[low]** Hand-slot mesh handling in 3 places (`NpcService` 647-672 and 897-968, `FightService.DropReadiedWeapon` 686-724, `DrawWeapon.GetHandSlotName`).
    - Fix: `Domain/Npc/NpcHandSlotDomain` owned by `NpcInventoryService`.
    - Effort: M.

12. **[low]** Unused injections: `NpcAiService.cs:36-38` (`_multiTypeCacheService`, `_physicsService`, `_configService`), `NpcHelperService.cs:35` (`_vmCacheService`); `CreateVobNpc` public; `NpcHelperService` public mutable fields (31, 122).
    - Fix: remove the injections and make these members private.
    - Effort: S.

**Discussion (rule friction, `Docs/architecture.md` "where does my code go", first row):** "implement a Daedalus external → the service of its area implements it (e.g. `NpcAiService`)" pushes even one-line getters and enqueues into the service, giving two pass-through layers for ~40 externals and most of NpcAiService's size. Proposal: externals that only read or write one `NpcContainer` field, or enqueue one action, may live in the `Vm*ExternalDomain` directly (or call an extension). Services keep only externals with rules or state. This needs an ADR.

## Suggested order of work
1. S cleanups: finding 12; remove baseline lines 50 and 51 (signatures / inline `GetProperties`); move `TA_Min` → `NpcRoutineService` and `Mdl_*` → `NpcLookService`.
2. Shared rules: `WeaponStateExtension`, the one hero check, dialog-state `IsInCurrentDialog`, `VmGlobalsScope` used everywhere (decide the null rule), a Daedalus constant/symbol-set helper.
3. Extract `NpcPerceptionService` and move the hero broadcasts to `HeroPerceptionService`, with one range rule (findings 1, 4, 9).
4. Extract `AiQueueClearDomain`, `AiStartStateDomain`, `NpcStateTimeDomain` from NpcAiService; separate the engine API from `Ext*` (finding 3); write the ADR and update `Docs/npc-ai.md`.
5. Decide the discussion item (ADR), then move the thin `Npc_*` queries out.
6. NpcService: `AmbientInfoDomain` → DialogService (finding 5); the `Hlp_GetNpc` index (finding 7); `NpcHandSlotDomain` (finding 11); the hero rig provider (baseline 53).
7. FightService: requirement checks out (finding 6), voice helper, prehit to `HeroBlockDomain`.
8. Later: an `INpcAiAction` model type plus moving `NpcProperties` to `Models/` (baseline 19, 24, 37, 54); adapter ports and extracting the state machine from AiHandler (finding 2).

## Coverage
- **Read fully:** `Assets/Gothic-Core/Scripts/Services/Npc/NpcAiService.cs`, `Assets/Gothic-Core/Scripts/Services/Npc/NpcService.cs`, `Assets/Gothic-Core/Scripts/Services/Npc/FightService.cs`, `Assets/Gothic-Core/Scripts/Domain/Vm/VmExternalDomain.cs` (123 lines; now only a registrar, so it doesn't call into the NPC services itself), `Assets/Gothic-Core/Scripts/Domain/Vm/VmGlobalsScope.cs`, `Docs/architecture.md`.
- **Skimmed (grep or partial):**
  - `NpcHelperService.cs` (public API, usings)
  - `Adapters/Npc/AiHandler.cs` (method list, Update 133-160)
  - `Adapters/Properties/NpcProperties.cs` (usings, queue helpers)
  - `Domain/Npc/Actions/AnimationActions/AbstractAnimationAction.cs` (lines 1-60)
  - `Domain/Vm/VmAiExternalDomain.cs` and `VmNpcExternalDomain.cs` (injections, call sites)
  - `Services/Vm/VmService.cs` (`CallAsHero`)
  - `Domain/Culling/NearestFirstQueue.cs` (reorder)
  - `HeroPerceptionService.cs` (header)
  - `Docs/npc-ai.md` (grep)
  - `Assets/Gothic-Tests/EditMode/Architecture/ArchitectureBaseline.txt` (grep)
  - the `[Inject]` lists of every file in `Services/Npc` and the main `Domain/Npc` domains
  - repo-wide caller searches for every public method of NpcAiService, NpcService, FightService and NpcHelperService
- **Not looked at:**
  - Services: AnimationService, NpcInventoryService, ProjectileService, NpcNavMeshService, NpcWaterService, NpcRoutineService, NpcLookService, PickpocketService, NpcRandomAniService, NpcBurnService (only their injections were checked)
  - the bodies of all `Domain/Npc/*Domain` classes and of the animation actions
  - `Services/Player/DialogService.cs`
- **Not run:** the architecture check (`dotnet run --project .github/architecture-check`); the baseline lines above are from the file, not from a fresh run.
