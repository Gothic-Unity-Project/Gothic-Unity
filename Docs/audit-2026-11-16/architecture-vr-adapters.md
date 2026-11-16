# Architecture audit: `Assets/Gothic-VR/Scripts/Adapters` (Services and Domain as places to move logic to)

Snapshot: `main` @ ffd7f5ec8, 2026-11-16. Read-only. `dotnet` is not installed, so the layer/namespace check did not run. Layer facts below come from grep.

## Summary
1. Adapters still hold a lot of game logic. The worst case is `VRRuneCaster`, which contains the whole hero spell protocol: mana investment, Daedalus `Spell_ProcessMana`, `Spell_ProcessMana_Release` and `Spell_Cast_*`, refunds, damage, scroll use-up. That is Core logic, and a Flat mode would have to copy it. After it come `VRBackpack`, `VRPickpocket`, `VRSwimDive` and `VRSpellTransformPuppet`.
2. The churn in `VRContextInteractionService`, `VRContextMenuService` and `VRConstants` has one structural cause: every Immersion/Accessibility option is written down in 3-5 places. 51 of the 71 commits to the menu service in 8 weeks also changed one of the other two files.
3. No XRI types outside `Interaction/Backends/` and no lateral module usings: that part is clean. But 10 adapters read `Keyboard.current`/`Mouse.current` directly instead of going through the `IVrInput` port, and 16 VR Services/Domain files use VR Adapters.
4. The biggest duplications: the held object → `VobContainer` lookup (78 call sites), "the trigger of the hand holding me" (5 files), the backpack/loot/trade socket refill routine (3 files), reading options (~20 files) and water-level classification (3 files).
5. Quest 2 cost: `VRFocus.LateUpdate` runs on every item, NPC and mob prefab. The transform puppet allocates every frame. The rest is per-player and cheap.

## Ranking: non-adapter logic held by each adapter

| # | Adapter (lines) | What doesn't belong in an adapter | Logic to move (approx. lines) |
|---|---|---|---|
| 1 | `Adapters/Vob/VobItem/VRRuneCaster.cs` (1023) | spell invest/cast protocol, mana rules for G1 vs G2, refunds, damage per level, AoE/summon routing, transform/light special cases, scroll use-up | ~550 |
| 2 | `Adapters/Player/VRBackpack.cs` (1042) | inventory page model (paging, equipped first, minus held items), 3 owner modes (hero/loot/trade) with trade-return rules, its own copy of the PickUp/Drop world-list rule, take-all | ~350 |
| 3 | `Adapters/Player/VRPickpocket.cs` (703) | robbery session state machine, caught/cooldown/rob range rules, victim scan, G1/G2 branch | ~250 |
| 4 | `Adapters/Player/VRSpellTransformPuppet.cs` (903) | monster animation and walk-mode choice, its own swim/wade classification, writes `AiHuman.WaterLevel/WalkMode` | ~300 |
| 5 | `Adapters/Player/VRSwimDive.cs` (659) | hero water level from guild depths, hero body state `BS_SWIM/BS_DIVE` (a copy of `NpcWaterService`) | ~150 |
| 6 | `Adapters/Trade/VRTradeCounter.cs` (744) | "trader can't trade any more" rule, offer-side rules; ~200 lines of hand-built UI tree | ~80 rules + ~200 structure |
| 7 | `Adapters/Vob/VRVobItem.cs` (609) | central switch deciding which behaviour component an item gets, document use-up rule | ~120 |
| 8 | `Adapters/Vob/LockPicking/VRContainerDoorPickingInteraction.cs` (330) | lock combination rule and a placeholder break chance (`Random.value > 0.5f`, FIXME) | ~60 |
| 9 | `Adapters/Vob/VobItem/VRStackSplitter.cs` (723) | spawning a world item: culling + world VOB list, done by hand | ~40 |
| 10 | `Adapters/Player/VRFavoritesRing.cs` (1065) | mostly already in `VrFavoritesService`. What remains: moving stack amounts, spawning the taken spell, zoom persistence. Mostly a too-large UI class | ~100 |
| - | `Interaction/Components/VrPhysicsHand.cs` (1323) | no Gothic logic and follows component rule 2. Only too big (principle 8) | 0 |

## Findings (ranked)

### 1. [high] `VRRuneCaster.cs:212-318, 424-435, 481-618, 624-766`: hero spell casting lives in a VR adapter (principles 1, 3, 4, 8)
- **What's wrong:**
  - `TryInvestMana`, `IsEarlyReleaseCastAllowed`, `InvokeSpellCast`, `RefundInvestedMana`, `IsScriptPaidSpell` and `SyncManaFromInstance` run the Daedalus spell protocol and change hero mana.
  - `FinalizeCast` decides damage per level, AoE vs. target vs. summon, the Light/Transform special cases and scroll use-up (`TryUseUpScroll`).
  - Core already has the NPC side of the same protocol (`Domain/Npc/SpellHitDomain.cs:94` calls `Spell_ProcessMana`).
- **Why it matters:**
  - Flat mode, or any second caster such as the ring spell, has to copy about 550 lines.
  - Two implementations of one Gothic rule will drift apart. The comments already document four "mana snaps back" / "thousands of damage" fixes.
- **Fix:**
  - New `Gothic.Core.Services.Magic.HeroSpellCastService`, with `Domain/Magic/SpellInvestDomain` (invest tick, release check, `Spell_Cast_*`, refund, script-paid detection, tick interval).
  - New `Domain/Magic/SpellCastEffectDomain` (damage per level, AoE/summon/target routing, Light/Transform hooks through an interface the VR transform service implements, scroll use-up).
  - A `Models/Magic/SpellCastSession` holds mana invested, invest level and target.
  - What stays in `VRRuneCaster` (~350 lines): input, hand choice, `VRSpellFx`/`VRSpellThrow`, haptics.
  - Do it in 2 PRs: invest/mana first, then cast effects.
- **Effort:** L

### 2. [high] `Services/Context/VRContextMenuService.cs:301-690`, `VRContextInteractionService.cs:258-292`, `VRConstants.IniNames`, and ~20 adapters: one option is written down in 3-5 places (principles 3, 4)
- **What's wrong:**
  - Every option needs:
    - an ini name and a default value in `VRConstants`;
    - a hand-written `Create*Label`/`Create*Choice` pair in the menu service;
    - an `if key ==` branch in `PlayerPrefsUpdated`;
    - its own reader in each consumer. For example `VRFavoritesRing.cs:146-186` and `VRPickpocket.cs:115-138` each have their own `GothicInisInitialized` + `PlayerPrefUpdated` code.
  - Behaviour is inconsistent:
    - `VRRuneCaster.IsIniOn` (`:381`) returns true when Gothic.ini is missing.
    - `VRFavoritesRing.ReadOption` (`:149`) returns false.
    - `VRRuneCaster.cs:733` uses a literal `1` instead of a default constant.
- **Why it matters:** this explains the 62/48/45 commits on these three files. Every new option touches 4-5 files and invites merge conflicts.
- **Fix:**
  - New `Gothic.VR.Services.Options.VrOptionsService` (PlayerScope) with typed properties and a `Changed(key)` event, backed by one declarative `VrOptionDefinitions` list: ini key, section, default, menu (Immersion/Accessibility), kind (toggle, slider, choice), text key.
  - The menu service builds its rows from that list.
  - Consumers inject the service instead of `ConfigService` + `GlobalEventDispatcher.PlayerPrefUpdated`.
  - Migrate the consumers one PR at a time.
- **Effort:** M

### 3. [high] `VRBackpack.cs:189-209, 644-796, 843-1040`: one adapter runs three owner modes plus the inventory page model (principles 3, 8)
- **What's wrong:**
  - Trade rules: `TryReturnTradeGoods`, `ReturnToHero`, and the `IsTradeBackpack` branches in `OnItemPutIntoBackpack:697-712`.
  - Loot rules: `ShowTakeAllButton` with its body-state check, `OnTakeAllClick`.
  - Page model: `UpdateInventoryView:856-866`, `SubtractItemFromHand`, and the literal `9` slots in 4 places (`:883, 966, 970, 971`).
  - `OnItemPutOutOfBackpack:786-788` writes `SaveGameService.CurrentWorldData.Vobs` directly. Its own comment says "Like PlayerService.DropItem", i.e. a second copy of that rule.
  - 17 injected services.
- **Why it matters:**
  - Every trade, loot or pickpocket change edits the hero's backpack.
  - The world-list rule now exists in `PlayerService`, `VobService:616/1013`, `VRBackpack` and `VRStackSplitter`. If one copy is forgotten, an item is saved twice or not at all.
- **Fix:**
  - (a) `VobService.AddLooseWorldItem(container)` / `RemoveFromWorldList(container)`, used by `VRBackpack`, `VRStackSplitter:619-620` and `VRFavoritesRing:766`.
  - (b) A plain `Gothic.VR.Domain.Inventory.BackpackPage` (category, page, page size, order, held subtraction) owned by a new `VrBackpackService`.
  - (c) Move the trade-goods rules into a `VrTradeGoodsService` that the backpack asks with `TryRedirect(interactable)`.
  - (d) See finding 9 for the shared socket refill.
- **Effort:** M (3 PRs)

### 4. [high] `VRPickpocket.cs:56-70, 307-312, 479-498, 523-694`, `VRPickpocket.cs:110, 469`: robbery session and G1/G2 branch live in the adapter (principles 3, 4)
- **What's wrong:**
  - The adapter decides:
    - caught = the victim can see the hero (`:628-634`);
    - the caught cooldown (`_caughtCooldown = 30f`, `:494`, `:653`);
    - the rob range (`:642`);
    - "no roll while asleep" (`:589`);
    - the robbery's start and end.
  - `_pickpocketService.IsGothic1` branches on the game version inside the VR adapter.
- **Why it matters:**
  - These are Gothic theft rules, and a Flat mode would need them.
  - The game-version `if` is the smell principle 4 forbids.
- **Fix:**
  - Add `StartRobbery(victim)`, `TickRobbery(hero)` → `RobberyResult` and `EndRobbery()` to Core `PickpocketService`, plus a `PickpocketRobbery` model that holds the victim, the caught flag and the cooldowns.
  - Replace the G1/G2 difference with an `IPickpocketMode` registered in `G1ContextBootstrap`/`G2ContextBootstrap` (steal window vs. dialog purse).
  - The adapter keeps purses, hover, backpack spawn and haptics (~400 lines).
- **Effort:** M

### 5. [medium] `VRSwimDive.cs:190-231, 270-376` vs `Core/Services/Npc/NpcWaterService.cs:70-195` vs `VRSpellTransformPuppet.cs:267-287, 418-423`: water level classified three times (principle 3)
- **What's wrong:**
  - Each file maps depth to Normal/Knee/Chest with its own thresholds: guild depths, guild depths, and eye-height shares.
  - Two of them write the body state (`VRSwimDive.UpdateHeroBodyState:359-376`, `NpcWaterService:183-186`).
- **Why it matters:** scripts react to `BS_SWIM` (monsters stop chasing). Three rules can disagree about whether the hero swims.
- **Fix:**
  - New `Gothic.Core.Domain.Npc.WaterLevelDomain.Classify(depth, guild)` → `WaterLevel` and `ApplyBodyState(npc, level)`, used by `NpcWaterService` and `VRSwimDive`.
  - The puppet asks it with the monster's guild.
  - `VRSwimDive` keeps the raycast, rig speeds, bobbing and sound.
  - Cache the guild depths at world load instead of looking them up every `FixedUpdate` (`:202-203`).
- **Effort:** M

### 6. [medium] `VRVobItem.cs:114-174, 237-399`: central switch for item behaviours (principle 4)
- **What's wrong:**
  - `OnGrabbed` decides, item flag by item flag, whether to add `VRTorch`, `VRDocReader`, `VRRuneCaster`, `VRItemUser` or a ranged weapon.
  - `ConsumeDocument:308-332` decides the "sealed letter is used up" rule and changes amounts.
- **Why it matters:**
  - Every new item behaviour edits this file (31 commits in 8 weeks).
  - The item-kind checks are written again in `VRRuneCaster:144, 731` (scroll = `ItemMulti`) instead of in `Extensions/ItemInstanceExtension.cs`.
- **Fix:**
  - Add `IsScroll`, `IsRune`, `IsTorch` and `IsReadableDocument` to `ItemInstanceExtension`.
  - New `Gothic.VR.Services.Items.VrItemBehaviourService` holding registered `IVrItemBehaviour` (`AppliesTo(item, grabArgs)`, `Attach(go)`) entries, bound in `ReflexVRSceneInstaller`.
  - Move the document use-up into `ItemUseService` (Core).
- **Effort:** M

### 7. [medium] `Services/Context/VRContextInteractionService.cs:143, 166-240, 211-229`: a service builds the player's features in code (principle 4; asset rule A)
- **What's wrong:**
  - `SetupPlayerController` creates `VRStackSplitter`, `VRBackpackEquipToggle`, `VRFavoritesRing`, `VRPickpocket`, `VRHeroMovement` and `VRHeroBlock` with `new GameObject().AddComponent<>()`.
  - The class also does comfort settings, controls, teleport, storing held items, view hiding and chapter intros, and depends on 13 adapter types.
- **Why it matters:** every new player feature edits this, the most-changed file. The composition isn't visible in the prefab.
- **Fix:**
  - Put these components on the VR player prefab (or a `PlayerFeatures` child prefab) through Unity MCP.
  - Split the class into `VrComfortSettingsService` (consuming finding 2), `VrHeldItemsService` (`StoreHeldItemsInBackpack`, `GetHeldAmount`) and the remaining `IContextInteractionService` facade.
- **Effort:** M

### 8. [medium] VR Services and Domain use VR Adapters (16 files): dependency direction
- **What's wrong:** for example `Domain/Player/VrFistDomain.cs` → `VRFistHitbox`, `VrFavoritesService.cs:263` → `GetComponentInParent<VRBackpack>`, `VRMobUseService` → 4 adapters, `VRRangedService` → `VRBow`/`VRCrossbow`, `VRCarriedItemsSaveService:149` → `FindObjectsByType<VRVobItem>`.
- **Why it matters:**
  - Core rule 1 forbids this pattern inside Core.
  - The "Inside Gothic-VR" table doesn't say whether it applies to VR, so each PR decides on its own. That is how 16 files got here.
- **Fix:**
  - **Discussion:** state the rule for Gothic-VR in `Docs/architecture.md`.
  - If it is adopted, use factory or registry interfaces next to the service, as in Core (`IVrRangedWeaponFactory`, `IVrBackpackRegistry`), and add a VR check to `CoreArchitectureTest` with a baseline.
- **Effort:** S for the doc, M per service

### 9. [medium] Socket refill duplicated three times
- **Where:** `VRBackpack.cs:843-1000`, `VRNpcLoot.cs:404-494`, `VRTradeCounter.cs:551-633`.
- **What's wrong:** each file does the same thing:
  - clear the sockets;
  - wait a frame;
  - spawn items with `VobService.CreateItem`, set `isKinematic = false` and `TryPlace(silent)`;
  - ignore its own socket events;
  - keep a dirty flag.
- **Related copies:**
  - `isKinematic = false` after `CreateItem` also appears in `VRCarriedItemsSaveService:226`.
  - `DestroyNextFrame` exists 3 times (`VRBackpack:995`, `VRNpcLoot:489`, `VRCarriedItemsSaveService:262`).
  - `AddEquippedLabel` exists twice (`VRBackpack:1005`, `VRNpcLoot:525`).
- **Why it matters:** the comments show the same bugs fixed separately: "two at once doubled the items", "a throw left _refresh set forever".
- **Fix:** a `Gothic.VR.Adapters.Inventory.VrSocketPage` component with `Show(IReadOnlyList<ContentItem>, Func<ContentItem, bool> isEquipped)`, an `IsOwnEvent(interactable)` check and the equipped label, used by all three.
- **Effort:** M

### 10. [medium] Raw keyboard/mouse in adapters bypasses the `IVrInput` port (VR rule 1)
- **What's wrong:** `Keyboard.current`/`Mouse.current` appear in `VRRuneCaster` (4), `VRStackSplitter` (2), `VRSpellTransformPuppet` (2), `VRPlayer` (2), `VRTorch`, `VRItemUser`, `VRDocReader`, `VRCrossbow`, `VRCinema` and `VRFavoritesRing:397`.
- **Why it matters:** the simulator key map (R = cast, T = split, wheel = zoom) is spread across 10 game files. A second backend or simulator has to find them all.
- **Fix:** map the keys in the simulator backend (`XriHvrStyleSimulator`), so `IVrInput.GetButton(side, Trigger)` / `GetThumbstick` already report them. Then delete the `IsSimulator` branches.
- **Effort:** S-M

### 11. [medium] `VRFocus.cs:236-270`: `LateUpdate` on every item, NPC and mob (P1)
- **What's wrong:**
  - `VRFocus` is on `oCItem`, `oCNpc`, `oCMobBed/Door/Container/Fire/Inter/Ladder/Switch/Wheel` (VR prefabs).
  - Its `LateUpdate` runs on every active instance, although it only has work while highlighted or locked.
  - Every NPC subscribes to `HeroSpellTargetLocked` (`:79`).
- **Why it matters:** hundreds of active instances near the player means hundreds of empty native→managed calls per frame on Quest 2.
- **Fix:**
  - `enabled = IsHighlighted || _isLocked`, switched in `OnHoverEnter/Exit`/`SetLocked`.
  - Let `VRPlayerService` tell the one locked NPC instead of broadcasting to all NPCs.
  - Measure on a Quest in the Old Camp before and after.
- **Effort:** S

### 12. [medium] `VRRuneCaster.cs:696, 706`, `VRSpellThrow.cs:229`: `SpellHit` event used as a method call (principle 7)
- **What's wrong:**
  - `SpellHit` has one listener (`FightService.cs:89` → `SpellHitDomain`).
  - The AoE case raises it with placeholders (`hero, hero, hero position`), relying on the listener to "re-resolve".
- **Why it matters:** this is a command with hidden semantics, not a fact. A second listener would see fake targets.
- **Fix:** have `HeroSpellCastService` (finding 1) call `FightService.ApplyHeroSpell(caster, target?, position, damage, isAoe)` directly. Keep `SpellHit` for real hits only.
- **Effort:** S, after finding 1

### 13. [medium] Duplicated small rules and lookups (principles 3, 6; VR building blocks)
- **(a) Held object → `VobContainer`:** `GetComponentInParent<VobLoader>()?.Container` appears 78 times in Gothic-VR. Biggest users: `VRVobItem` 8, `VRBackpack` 8, `VRContainerDoorPickingInteraction` 5. The building block `VRPlayerService.GetHeldContainer` is used only by 3 services. Fix: a `VrInteractable.VobContainer` property cached once, or a `VobContainerExtensions.GetContainer(this Component)` extension.
- **(b) "The trigger of the hand holding me":** `VRTorch.cs:52-64`, `VRDocReader.cs:43-87`, `VRItemUser.cs:64-73`, `VRRuneCaster.cs:102-106, 386-391`, `VRCrossbow.cs:246`. Fix: `VRPlayerService.TryGetHoldingSide(GameObject, out VrHandSide)`, combined with finding 10.
- **(c) Grab state with two owners:** `VRPlayerService.GrabbedItemLeft/Right` are public mutable fields. `VRCarriedItemsSaveService.cs:193-194` writes them. Raw `== gameObject` comparisons appear in 9 files. Fix: private setters plus a `ClearGrabs()` method.
- **(d) No registry of backpacks:** `FindObjectsByType<VRBackpack>` in `VRStackSplitter.cs:270` and `VRBackpackEquipToggle.cs:89`. Fix: an `IVrBackpackRegistry` that backpacks register with on enable.
- **(e) `Resources.Load` outside `ResourceCacheService` (rule D):** `VRBackpack.cs:147`, `VRPickpocket.cs:371`, `VRTradeCounter.cs:438`.
- **Effort:** S each

### 14. [medium] `VRTradeCounter.cs:166-183, 343-443`: polled rule and hand-built UI
- **What's wrong:**
  - "Trader dead/unconscious/drew a weapon → trade ends" is checked every frame in the adapter.
  - Texts, buttons, canvas and the goods table are built from code (~200 lines, rule A).
  - The socket prefab is borrowed from the trader's `VRNpcLoot` (`:227`).
- **Fix:**
  - `TradeService` (Core) ends the session itself on the NPC body-state/fight-mode change, and the counter only listens to `TradeEnded`.
  - Make a `TradeCounter` prefab under `Gothic-VR/Resources/VR/Prefabs/` with serialized references, created through Unity MCP.
- **Effort:** M

### 15. [low] `VRContainerDoorPickingInteraction.cs:206-250`: lock-pick rule in an adapter
- **What's wrong:** the L/R combination and the break chance (`Random.value > 0.5f`, marked FIXME) are decided in the adapter.
- **Fix:** a Core `LockPickDomain.TryStep(lockable, isLeft, heroTalent)` → `LockPickStepResult`, owned by `VobService` or a new `LockPickService`, so the real talent rule has one home.
- **Effort:** S

### 16. [low] `VRSpellTransformPuppet.cs:403-600`: animation selection and per-frame allocation (principle 8, P2)
- **What's wrong:**
  - Choosing idle/turn/move/fall/swim animations and walk modes is NPC locomotion logic.
  - `GetCachedMoveAnimation` builds the key `$"{type}|{WalkMode}"` (`:546`) and allocates lambdas (`:504, 517, 530`) every frame while moving.
  - `HasTrack("S_SWIM")` and `HasTrack("S_SWIMF")` are looked up every frame (`:418`).
- **Fix:**
  - New `Gothic.VR.Domain.Player.VrPuppetAnimationDomain.Choose(PuppetMotion)` → animation name, with the cache keyed by a `(AnimationType, WalkMode)` struct.
  - Resolve `canSwim` once in `TrySetup`.
- **Effort:** M

### 17. [low] Event subscriptions without removal
- **Where:** `VRSwimDive.cs:108, 160` and `VRSpectatorCamera.cs:60, 81` add lambda/method listeners to the ProjectScope dispatcher and never remove them.
- **Why it matters:** harmless only as long as the VR player scene is never rebuilt. Otherwise handlers pile up and hold destroyed objects.
- **Fix:** named handlers, removed in `OnDestroy`.
- **Effort:** S

### 18. [low] `AudioSource.PlayClipAtPoint` in 7 adapters, plus `VrSocketSlot`
- **Where:** `VRBackpack`, `VRPickpocket`, `VRTradeCounter`, `VRBow`, `VRCrossbow`, `VRSpellFx` (3 calls), `VRTorch`.
- **Why it matters:** a GameObject is created per sound, while the `IVrSfxPlayer` port exists. Mostly event-driven (not per frame), so the cost is low.
- **Fix:** use `IVrSfxPlayer.Play`.
- **Effort:** S

### 19. [low] Size only, no wrong-layer logic (principle 8)
- **`VRFavoritesRing.cs` (1065):** seams are carousel and zoom (`:381-475, 615-681`), put-in candidates (`:872-1044`) and stack transfer (`:799-843`). The transfer changes amounts directly (`:838-839`), which should be `VrFavoritesService.TransferOne`.
- **`VrPhysicsHand.cs` (1323):** seams are held-item pose (`:601-814`), active/inactive blend (`:824-922`), body build (`:928-1036`) and rig jump (`:1154-1243`).
- **Fix:** extract plain worker classes, as `VRHeroBody` already does.
- **Effort:** M each, later

### 20. [discussion] Adapter-owned worker classes
- **What:** `VrHeroLocomotion`, `VrHeroDownPose`, `VrHeroArmIk`, `VrHeroSleeves`, `VRHeroBodyAnimator`, `VrHeroBodyMeshCutter`, `VrHeroHands`, `VRSpellFx`, `VRTelekinesisSpell` and `VrBowMeshCalibration` are plain classes in `Adapters/`, owned by MonoBehaviours.
- **The gap:** the doc only allows Domain workers owned by Services.
- **Fix:** add a line to `Docs/architecture.md`, either allowing "adapter workers (Unity-only helpers of one adapter)" or moving them. Otherwise AI agents will keep inventing a pattern each time.

## Layer and dependency status (point 3 of the request)
- XRI types outside `Interaction/Backends/`: none. (`XriRig.cs` mentions `VRPlayer` only in a comment.)
- `using Gothic.Gothic1/Gothic2/Flat/Lab` in VR: none.
- Components → Services/Adapters: `VrPhysicsHand` only raises `PhysicalHandContact`. Clean.
- VR Services/Domain → VR Adapters: 16 files (finding 8).
- Static resolve: `VRTradeGoods.RemoveFromWorld` (`VRTradeGoods.cs:158-161`, `ReflexProjectInstaller.DIContainer.Resolve`) is used as a service from `VRBackpack` and `VRTradeCounter`. Also used in `VirtualKeyboard`.

## DI and event coupling (point 6 of the request)
- Injected services per adapter: `VRBackpack` 17, `VRRuneCaster` 14, `VRVobItem` 13, then `VRStackSplitter`, `VRMouth`, `VRBow`, `VRSwimDive` and `VRFavoritesRingSpell` with 10 each. Each matches a finding above (mixed responsibilities).
- Events used as calls: `SpellHit` (finding 12).
- Events used correctly as facts: `BackpackInHandChanged` (raised by the backpack, listened to by `VRHeroBody`), the `LockPickCombo*` events (VR haptics plus Core listeners) and `HeroManaBarRequested`.

## Suggested order of work (each step is one PR)
1. `VrOptionsService` + `VrOptionDefinitions` (finding 2). Kills the churn in the 3 hottest files. Low risk. **M**
2. Small building blocks: the cached `VobContainer` lookup, `TryGetHoldingSide`, simulator keys moved into the backend, `IsScroll`/`IsRune`/`IsTorch`/`IsReadableDocument` (findings 13a-c, 10, 6 first half). Makes every later move smaller. **S**, 2-3 PRs
3. `VRFocus` enabled only when needed, with a Quest measurement (finding 11). **S**
4. One world-list method in `VobService` (finding 3a). **S**
5. Hero spell protocol to Core, in two PRs (finding 1), then `SpellHit` as a direct call (finding 12). **L**
6. `WaterLevelDomain` shared by `NpcWaterService` and `VRSwimDive` (finding 5). **M**
7. The `VrSocketPage` component, then splitting the backpack's trade/loot/page parts (findings 9, 3b-c). **M + M**
8. Pickpocket robbery session in `PickpocketService` + `IPickpocketMode` (finding 4). **M**
9. `VrItemBehaviourService` registrations (finding 6). **M**
10. Player features into the prefab, and splitting `VRContextInteractionService` (finding 7). **M**
11. Trade counter prefab and the trade-end rule in `TradeService` (finding 14); lock-pick domain (finding 15). **M, S**
12. Later: puppet animation domain (16), seams in the ring and physics hand (19), doc decisions (8, 20), listener cleanup (17), sounds (18).

## Coverage
- **Read fully:** `Adapters/Player/VRFavoritesRing.cs`, `Adapters/Player/VRBackpack.cs`, `Adapters/Vob/VobItem/VRRuneCaster.cs`, `Adapters/Trade/VRTradeCounter.cs`.
- **Read in large part:**
  - `Adapters/Player/VRPickpocket.cs` (lines 40-310, 479-700)
  - `Adapters/Player/VRSpellTransformPuppet.cs` (190-610, plus the method list)
  - `Adapters/Player/VRSwimDive.cs` (100-399)
  - `Adapters/Vob/VRVobItem.cs` (1-405)
  - `Adapters/Vob/VobItem/VRStackSplitter.cs` (100-240, 260-300, 519-640)
- **Skimmed (method lists or specific regions):** `Adapters/VRFocus.cs`, `Adapters/VRNpcLoot.cs`, `Adapters/Vob/VobItem/VRSpellFx.cs`, `Adapters/Player/VRHeroBody.cs`, `Adapters/Vob/LockPicking/VRContainerDoorPickingInteraction.cs` (190-250), `Adapters/Player/VRBackpackEquipToggle.cs` (80-100), `Services/Context/VRContextInteractionService.cs` (outline, 258-294, 455-512), `Services/Context/VRContextMenuService.cs` (outline, 557-605), `Services/VRPlayerService.cs` (public API), `Services/VRCarriedItemsSaveService.cs` (140-155), `Interaction/Components/VrPhysicsHand.cs` (usings and method list), Core `NpcWaterService.cs` and `VobService.cs` (single regions), `Docs/architecture.md` (principles 7-9, layers, "where does my code go", Inside Gothic-VR, Performance, Smells).
- **Seen only through grep (usings, Update methods, listeners, Find/Resources calls, Keyboard input):** every other `.cs` under `Adapters/`, `Services/` and `Domain/`.
- **Not looked at:** `Adapters/UI/VirtualKeyboard.cs`, `VRCinema.cs`, `VRGrabBar.cs`, `VRUiOnTop.cs`, `VRDialog.cs` (except grep hits); `Adapters/Marvin/*`; `VrHeroDownPose`, `VrHeroArmIk`, `VrKnockoutView`, `VRHeroMovement`, `VRBow`/`VRCrossbow` bodies; `Domain/Player/*` bodies (`VrWeaponAttackDomain` 606 lines, `VrSwingSampler` 368); `VRWeaponService`; `ReflexVRSceneInstaller`.

Key paths (repo root `/home/jaxt0r/Gothic-workspace/Gothic-Unity`):
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Adapters/Vob/VobItem/VRRuneCaster.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Adapters/Player/VRBackpack.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Adapters/Player/VRPickpocket.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Adapters/Player/VRSwimDive.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Services/Context/VRContextMenuService.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Services/Context/VRContextInteractionService.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-VR/Scripts/Adapters/VRFocus.cs`
- `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Docs/architecture.md`
