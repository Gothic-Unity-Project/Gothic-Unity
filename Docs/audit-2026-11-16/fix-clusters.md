# Fix clusters: where fixes stack on a shared root cause

- Date of analysis: 2026-11-16
- Repository state: `main` at `ffd7f5ec8`
- Range analysed: `git log --since='8 weeks ago' --no-merges`, which is `704ab89d5` (2026-10-08) to `ffd7f5ec8`
  (2026-11-15). The repository has no older commits inside the 8 weeks, so the range is really 5.5 weeks.
- Size: 850 non-merge commits, of which 343 `fix`, 189 `refactor`, 131 `feat`, 102 `docs`, 47 `chore`, 18 `perf`.
- Analysis only. No source file was changed, nothing was run in Unity.

## Short answer

The worry is partly right. Of 343 fix commits, roughly 110 belong to seven threads where several fixes patch
symptoms of one structural cause. Four of those causes are still in the code today:

1. Child VOBs are built by a second, weaker pipeline than root VOBs (9 fixes, last one on 2026-11-14).
2. An NPC that has no built body (the VR hero, a culled NPC, an unspawned NPC) is handled by a guard at every call
   site: 54 `IsHero(` checks and 165 `Go`/`PrefabProps` null checks in Core (about 22 fixes).
3. Hero stats live in two stores (Daedalus instance and VOB) that are synced by hand at 5 call sites plus a private
   copy in the rune caster (5 fixes, and the sync method that one fix removed is back).
4. A held or holstered VR item has its physical state spread over a layer, per-collider ignore pairs, a 1 s timer and
   a shader value, set in two event handlers with early returns (16 fixes).

Three other threads were real ping-pong but are now settled by a proper root fix: player locks, VR swing
measurement, dialog hero hold. The rest of the fix volume is legitimate: review findings on large feature PRs, mod
robustness, save/load coverage, and the HVR to XRI port.

A process finding sits on top: about half of all fixes land within 48 hours of a `feat:` on the same files, and 39
fix subjects name a review. Much of the "fix" count is features being finished after merge, not regressions.

## Method

1. Listed the fix commits with their touched files and grouped them by file path (script in the scratchpad, rules by
   folder and file name). A commit counts once, for the group most of its `.cs` files fall into.
2. For the largest groups, read the per-file history (`git log --numstat -- <file>`) and the diffs of the commits
   that touch the same function or lines repeatedly.
3. Grouped the fixes again by topic thread (the sections below), because the file groups mix unrelated work. Thread
   counts are hand-assigned from subjects and diffs and are approximate.
4. Read the current code at each spot to judge whether the cause is still there.

Where a claim rests on a commit message only, the section says so. "Read" below means the diff was read.

Caveat on dates: many commits carry the same author date because branches were rebased and merged in batches (49
commits on 2026-10-08, 92 on 2026-10-31, 91 on 2026-11-08). Time-based numbers are therefore rough.

## Table 1: fix commits by file group

"Primary" counts each commit once. "Touching" counts every commit that changes at least one file of the group.

| Group | Primary | Touching | Main files |
|---|---|---|---|
| NPC AI (states, perceptions, actions, helpers) | 44 | 69 | `NpcAiService`, `AiHandler`, `NpcHelperService`, `Domain/Npc/Actions` |
| World VOBs (init, movers, triggers, mobs, loading) | 36 | 59 | `VobInitializerDomain`, `VobService`, `MoverAdapter` |
| Assets and docs only (no `.cs`) | 34 | - | prefabs, `Production.asset`, scenes |
| VR items and inventory (items, slots, backpack, loot) | 33 | 58 | `VRVobItem`, `VRBackpack`, `VrSocketSlot`, `VRNpcLoot` |
| VR hands, rig, player | 33 | 65 | `VRPlayerService`, `VrPhysicsHand`, `XriRig`, `VRHeroBody` |
| Fight, knockout, death, VR melee | 21 | 38 | `FightService`, `VrWeaponAttackDomain`, `VrFistDomain` |
| UI and menus | 20 | 28 | `AbstractMenu`, `VRCinema`, `StatusBarAdapter` |
| Other `.cs` | 20 | 62 | mixed |
| NPC lifecycle (spawn, body, culling) | 19 | 43 | `NpcService`, `NpcInitializerDomain`, `NpcMeshCullingDomain` |
| Config and boot | 14 | 45 | `DeveloperConfig`, `ConfigService`, `Constants` |
| Daedalus VM and externals | 14 | 35 | `VmExternalDomain`, `VmExternalGuard` |
| Caches, textures, ZenKit extensions | 13 | 33 | `TextureCacheService`, `ZenKitExtension` |
| VR context services | 10 | 28 | `VRContextInteractionService`, `VRContextMenuService` |
| Dialog and trade | 9 | 24 | `DialogService`, `TradeService` |
| Audio, music, sky | 8 | 17 | `AudioService`, `SkyService` |
| Save game | 6 | 11 | `SaveGameService`, `Domain/SaveGames` |
| Animation | 6 | 14 | `AnimationService` |
| Spells | 3 | 7 | rune caster, transformation |

## Table 2: topic threads analysed

| # | Thread | Fixes (approx.) | Verdict | Cause still in code |
|---|---|---|---|---|
| 1 | Child VOBs below another VOB | 9 | symptom patches | yes |
| 2 | NPC without a built body (hero, culled, unspawned) | 22 | symptom patches | yes |
| 3 | Hero stats in two stores | 5 | symptom patches, one revert | yes |
| 4 | VR item physical state (layers, ignores, slots) | 16 | symptom patches | yes |
| 5 | Dialog hero hold, range and player locks | 13 | ping-pong, now settled | no (two modes remain) |
| 6 | VR melee: swing measurement and equip rules | 15 | 3 patches on one cause, now settled | mostly no |
| 7 | VR UI placement and look tuned blind | 26 | iteration without visual check | process cause |
| 8 | `DeveloperConfig` flags and `Production.asset` | 14 | process cause | reduced (36 flags) |
| 9 | Boot order: ini not loaded yet, DI cycles | 10 | symptom patches | partly |
| 10 | Save/load restore of single states | 22 | mostly legitimate | no shared cause found |
| 11 | Daedalus external robustness for mods | 10 | legitimate, general fix exists | no |
| 12 | Loading screen and loading bar | 7 | one regression pair | not judged |

## 1. Child VOBs below another VOB

**Fixes:** `a3dac1a3b` (10-08), `379691143`, `ea99cb6f1`, `924850d4c`, `4b0c572d3`, `98cd7fab8` (all 10-31),
`249be9b45` (11-11), `df52c48d6` (11-14), part of `af9385d6b` (11-06). All read except `98cd7fab8`.

**What happened, in order:**

- `a3dac1a3b`: doors, chests and items below another VOB had no `VobLoader`. Their adapters called
  `GetComponentInParent<VobLoader>()` and got the parent's VOB: `InvalidCastException`, chests that didn't work.
  Fix: `EnsureOwnVobLoader`, but only for `IInteractiveObject or IItem`.
- `379691143`: children of VOB types we don't build (cameras, fog zones) and of decal or particle VOBs with visuals
  off were dropped. Fix: two new special cases, `IsUnbuiltVobType` and `IsVisualDisabled`, checked in both
  `VobService` and `VobInitializerDomain`, plus a third rule for trigger zones scaled to their bounding box.
- `ea99cb6f1`: logic VOBs below a lazily loaded VOB only existed once the player came near. Fix: `VobService` creates
  them early and the initializer keeps a `HashSet<int> _createdEarlyVobIds` so the parent skips them later.
- `924850d4c`: the previous two fixes created trigger lists twice, because one of the two child loops didn't check
  the set. Fix: one `InitChildren`.
- `4b0c572d3`, `249be9b45`: child position. First movers below movers, then every child below a moved parent, with a
  new `parentVobPose` parameter threaded through `InitVob`.
- `df52c48d6`: the original bug again, three weeks after the first fix, for a VOB type the type filter doesn't cover
  (`SoundDaytimeHandler` read the parent's loader: `InvalidCastException`). Fix: pass the VOB in through `Init`.

**Current code:**

- `Assets/Gothic-Core/Scripts/Domain/Vobs/VobInitializerDomain.cs:77-78`: `InitVob` now takes `isRootVob`,
  `parentVob`, `isRelativeToParent` and `parentVobPose`.
- `:1019-1035`: `EnsureOwnVobLoader` still returns early unless the VOB is `IInteractiveObject`, `IItem` or a type
  looked up by name. Every other child (lights, sounds, plain VOBs) still shares its parent's loader.
- `:55`, `:338`, `:375-383`: the created-early id set.
- `:258-265`, `:279`, `:323-326`, `:364`, `:388`, `:402`: the four child special cases.
- `Assets/Gothic-Core/Scripts/Services/Vobs/VobService.cs:391-403` and `:420-440`: the second copy of the child
  rules.
- 81 `GetComponentInParent<VobLoader>` calls in the code base, 77 of them in Gothic-VR.

**Pattern:** special cases added one VOB type at a time; the same bug fixed again for another type; a flag set
(`_createdEarlyVobIds`) added to undo the double creation an earlier fix introduced; logic duplicated in two classes.

**Likely root cause:** a root VOB and a child VOB go through different pipelines. A root gets a `VobContainer`, a
`VobLoader`, lazy loading and culling in `VobService`. A child is built in place by recursion inside its parent's
GameObject and gets a loader only if its type is on a list. Everything that assumes "one VOB, one container" then
breaks for children, one type at a time.

**Proposed consolidation:** flatten the VOB tree when the world is created. Every VOB, root or child, gets its own
`VobContainer` and loader with `ParentVob` and its offset to the parent stored once. Parenting of GameObjects becomes
a second step that only movers need. Then `EnsureOwnVobLoader`, the created-early set, `IsUnbuiltVobType` child
handling and the `isRootVob` branches go away, and `GetComponentInParent<VobLoader>` is correct by construction.

**Effort:** L (touches culling, lazy loading and save games). **Confidence:** high.

## 2. NPC without a built body: the VR hero, culled NPCs, unspawned NPCs

**Fixes (read):** `5fd4973f0`, `a2aad4c78`, `bb6577572`, `ae95fe9e6`, `a17559c79`, `30cb21773`, `e26fc78dd`,
`cd7c3930a`, `198045b5e`, `228f2ce39`, `79234c7e4`, `af7562c69`, `a2cdf07ea`.
**Fixes (subject only):** `c18acb5a3`, `3caaa3b95`, `96c54c0f7`, `43284afd6`, `5c356224b`, `31582f515`, `9819b8ad2`,
`a9fe15968`, `1631e1475`, `1050dc15a`.

**What happened:** three different situations produce the same kind of bug, and each got its own guards.

- *The VR hero is an NPC without `AiHandler`, animated body or AI queue.* `a2aad4c78`: `AI_WaitTillEnd` waited for
  the hero forever, fixed with a check plus a 30 s timeout (`WaitTillEnd.cs:17`, `:37`). `ae95fe9e6`: drowning didn't
  end the game because the death path returned early for an NPC without animation system
  (`FightService.cs:216`, now `!isHero && ...`). `bb6577572`: the hero body has no hitbox, fixed with `?.`.
- *An NPC that is culled or not spawned has no GameObject.* `5fd4973f0`, `c18acb5a3`, `43284afd6` and parts of
  `e26fc78dd` add null guards to single externals.
- *An NPC's body is built late and rebuilt.* State has to be applied again each time, and each aspect found that out
  separately: dead pose (`a17559c79`), knocked-out pose (`30cb21773`), not running `ZS_Dead` twice (`e26fc78dd`),
  fatness and ghost look (`cd7c3930a`, which removed a "one frame after the culling event" coroutine), hitbox bounds
  (`198045b5e`), highlight materials (`228f2ce39`), save position (`79234c7e4`: the loader stays at the world origin
  once the body exists, so NPCs were saved at height 0).

**Current code:**

- 54 `IsHero(` calls in `Assets/Gothic-Core/Scripts` (11 in `FightService`, 8 in `NpcHelperService`, 6 in
  `DialogService`), and comments like "The VR hero has no ..." in `FightService.cs:216` and `:768`,
  `NpcAiService.cs:966`, `DialogService.cs:471`, `EquipDomain.cs:114-143`, `NpcService.cs:663`,
  `VmModelExternalDomain.cs:148`. Core knows about VR here, against principle 4 of `Docs/architecture.md`.
- 165 null checks on `Go` or `PrefabProps` in 52 Core files (26 in `NpcAiService`, 14 in `FightService`).
- `Assets/Gothic-Core/Scripts/Adapters/Npc/AiHandler.cs:61-84`: `Start()` is the place that restores an NPC from its
  stored state, with one hand-written branch per state (dead, unconscious). `AiHandler` is an adapter with 905 lines.
- Two events announce a body: `NpcMeshCullingChanged` and `NpcVisualBodyChanged`
  (`GlobalEventDispatcher.cs:60`, `:260`). Fatness, hitbox and poses don't use them, they are called directly from
  `NpcInitializerDomain`.

**Pattern:** guard clauses for a state that is normal, not exceptional; special cases per NPC kind; one timing
workaround (removed again); the same "apply it again when the body appears" fix in five places.

**Likely root cause:** `NpcContainer` has no explicit lifecycle. "Exists in the VM", "is in the world but has no
body", "has a body" and "is the player" are told apart by null checks and `IsHero` at each call site, so every new
external or feature has to rediscover which of them it can meet.

**Proposed consolidation:** two steps.

1. Give the hero and the bodiless NPC a real answer instead of a guard. Core defines what it needs from "the body"
   (play an animation, run the AI queue, be at a position, die) as an interface on `NpcContainer`. The NPC prefab
   implements it, VR registers the hero's implementation (no queue: run at once; no dying animation: fall view), and
   an unspawned NPC gets a null object that answers "not here". Most `IsHero` branches and `Go == null` guards in
   services then disappear.
2. One "body built" step in `NpcInitializerDomain` that calls a fixed list (pose from state, look, hitbox, weapons),
   used for first build, return from culling and armor change alike. `AiHandler.Start()` loses its restore branches.

**Effort:** L for step 1, M for step 2. **Confidence:** high for the diagnosis, medium for the size of the payoff
(the guards in single externals were not all read).

## 3. Hero stats in two stores

**Fixes (messages and stats read, diffs not):** `8dda2c649`, `cccc84d22`, `96c54c0f7`, `f8b635d7b` (all 10-08),
`5b52ee405` (10-27). Related, subject only: `d7795dd7e` ("G1 rune charging updates MANA/MAX").

**What happened:** `e34e9107b` added `SyncHeroInstanceToVob` because scripts write `hero.attribute[]` on the
Daedalus instance while the HP bar, fights and the status menu read the VOB. `cccc84d22`: the sync then overwrote
values that C# had written to the VOB only (healed damage came back). `f8b635d7b` removed
`SyncHeroInstanceToVob` for that reason. `fdc2141a0` (refactor, 10-16) made `NpcService.SetAttribute` write both
stores, and the sync method came back.

**Current code:**

- `Assets/Gothic-Core/Scripts/Services/Npc/NpcService.cs:515`: `SyncHeroInstanceToVob` exists again. Its comment
  says it is safe "because C# writes both stores".
- Callers that must remember to sync: `StatusMenu.cs:59`, `NpcDeathDomain.cs:134`, `NpcKnockoutDomain.cs:119`,
  `PickpocketDialogDomain.cs:128`.
- `Assets/Gothic-VR/Scripts/Adapters/Vob/VobItem/VRRuneCaster.cs:525-617`: its own `SyncManaFromInstance`, with a
  comment about "the mana snaps back bug".
- The `daedalus-bridge` skill documents the rule as "decide whether the other must be synced".

**Pattern:** a fix that reverts an earlier one; the same bug fixed separately in several places; a rule that lives
in a skill text instead of the code.

**Likely root cause:** two sources of truth for the same numbers, with no hook when a script writes one of them. A
sync on demand can't be complete: every script path that changes hero stats needs a caller that knows.

**Proposed consolidation:** read hero and NPC stats from the Daedalus instance everywhere and write the VOB only
when saving (`SaveGameNpcDomain.FlushInstanceToVob` already does that for save games). The instance is a ZenKit
object too, so principle 2 holds. `SyncHeroInstanceToVob`, its callers and the rune caster's copy go away.

**Effort:** M. **Confidence:** medium (the readers of `vob.GetAttribute` were counted, not read: 20 `SetAttribute`
call sites).

## 4. VR item physical state: layers, ignore pairs, slots

**Fixes:** `fec54b519` (10-18), `39b0a2d45`, `26ff3aaaa` (10-25), `bbdfed984` (10-26), `5d730efd5`, `1ccdd5bf5`,
`39faa6345` (10-31), `e90753a3e`, `a6d3a60ca` (11-03), `82ba7ae16`, `3fb77e953`, `c172e1512`, `37d7e9fd4` (11-06),
`8b9291b41` (11-08), `4269e2dcd`, `3da8b8fbc` (11-13). Messages and stats read for all; code diffs not read for the
prefab-only ones.

**What happened:**

- `fec54b519`: the player now collides with items, so held and holstered items must ignore the player.
- `39b0a2d45`: that made both hands ignore held items. Fix: only the holding hand, tracked per grabber.
- `bbdfed984`: the backpack is not an item, so it missed that logic and spun in the hand. Fix: the same calls
  copied into `VRBackpack`.
- `e90753a3e`: a coin on the hip counted as floor for the ground check. Fix: a new layer `VobItemSocketed`.
- `82ba7ae16`: the new layer was missing from the hand's far caster, so the line couldn't pull items from slots.
- `3fb77e953`: now the line pulled items out of the player's own holsters too. Fix: a per-slot flag
  `IsLineGrabbable`.
- Slot item size changed three times (`26ff3aaaa`, `1ccdd5bf5`, `a6d3a60ca`), backpack rotation in the right hand
  three times (`bbdfed984`, `4269e2dcd`, `3da8b8fbc`, the last a one-value prefab change a few hours after the
  second).

**Current code:**

- `Assets/Gothic-VR/Scripts/Adapters/Vob/VRVobItem.cs:117-196`: `OnGrabbed` and `OnReleased` set the layer, with an
  early return for sockets. `:458-462` `SetSocketedLayer`. `:480-491`: a fixed 1 s wait puts the item back on
  `VobItem`; the real check is commented out at `:484`.
- `:502-513`: `IsColliderOverlapping` is dead code with a FIXME. Its mask
  `Constants.VobItemNoWorldCollision | Constants.HandLayer` (`:86`, `:441`, `:513`) ORs two layer indices, not two
  masks. It only affects the gizmo today.
- `Assets/Gothic-VR/Scripts/Services/VRPlayerService.cs:482-520`: the per-collider ignore pairs, a second mechanism
  beside the layers.
- `Physics.IgnoreCollision` or layer writes in 11 VR files.

**Pattern:** each fix adds a state (a layer, a flag) and the next fix repairs what the new state broke; a timing
workaround (1 s); the same logic copied for the backpack.

**Likely root cause:** "where is this item" (in the world, in a hand, in a slot, being pulled) is not one value. It
is derived from a layer, a set of ignore pairs, a coroutine and a transparency value, each changed in a different
place. Rule F of the architecture doc asks for layer changes to be a called-out contract; here they arrive fix by
fix.

**Proposed consolidation:** one component on the item with an explicit state (`World`, `Held`, `Socketed`,
`Ghost`) and one method that applies everything for a state: layer, ignore pairs, transparency. Grab and release
events only choose the state. The backpack uses the same component. Write the layer matrix for the four states into
`Docs/` once and keep `SocketedItemLayerTest` as its test.

**Effort:** M. **Confidence:** high for the cause, medium for the slot size and rotation part (prefab values, not
read).

## 5. Dialog hero hold, range and player locks (settled)

**Fixes (read):** `1dac3ed96`, `ec65d1614`, `050f16c20`, `563700ace`, `129677c27`, `021932a09`.
**Subject only:** `982b0ff04`, `7e2365fea`, `49bcaa2bc`, `9819b8ad2`.

**What happened:** free movement in a dialog bubble shipped on 10-08 (`9c118291f`) and was patched three times in
ten days. `ec65d1614` (10-30) replaced it by pinning the hero and opened dialogs up to 20 m "like the engine".
`050f16c20` one day later: 8 m, because in VR an NPC talked from across the yard. `6c221d0f8` (11-04) removed free
movement, `c8bd12aae` the same day added it back as an option. `563700ace` (11-08): the pin had switched the stick
off since `ec65d1614`, so the hero couldn't move at all in the simulator. `500a4e4de` (11-10) moved it all to
`DialogHeroHoldDomain`.

Player locks went the same way and ended well: `129677c27` gave each lock an owner. `021932a09` still added a
`_isPlayerLockedByDialog` boolean a few days later (probably a branch from before the owners), which is gone now.

**Current code:** `Assets/Gothic-Core/Scripts/Domain/Dialog/DialogHeroHoldDomain.cs` (246 lines) holds both modes,
constants at `:38-48`. All 16 `LockPlayer`/`UnlockPlayer` calls name an owner.

**Pattern:** a fix that weakens an earlier fix (20 m to 8 m), a feature removed and re-added on one day, a flag to
suppress a behaviour.

**Likely root cause:** an open product decision (how does a dialog hold a VR player) settled by playtests, and an
engine rule copied before checking it in VR. Not a code structure problem any more.

**Proposed consolidation:** none needed. Decide whether both modes stay; each mode doubles the playtest surface.

**Effort:** S. **Confidence:** high.

## 6. VR melee: swing measurement and equip rules (mostly settled)

**Fixes (read):** `339f1aed0`, `ea1147aad`, `9cf94fa48`. **Not read (empty diff for `.cs`):** `bd2054114`.
**Subject only:** `1b642ca7e`, `6389aec6b`, `affd6ee8d`, `5a1f57505`, `372b1d58d`, `e4bf31809`, `82c235a13`,
`d1453ca10`, `72b263d99`, `25c68f115`, `195b5e6c8`.

**What happened:** running with a sword started an attack. `bd2054114` (11-02) changed a threshold on the prefab.
`339f1aed0` (11-11) subtracted the player's velocity as a vector and added a correction for rig turns.
`ea1147aad` (11-13) found the actual cause (the character controller's velocity only holds the last `Move()` of a
frame) and replaced the measurement by one in tracking space (`VrSwingSampler`). The two earlier patches were
compensation for a wrong frame of reference.

The other eleven are review findings on the melee port `522c31e88`, committed as `wip:` on 11-02. "A punch must not
finish a knocked out NPC" was fixed twice (`1b642ca7e`, `6389aec6b`).

**Current code:** swing and stab use `VrSwingSampler`. One leftover of the old approach:
`Assets/Gothic-VR/Scripts/Adapters/Vob/VRDigSpot.cs:87` still tests `rb.linearVelocity.magnitude` in world space.

**Proposed consolidation:** use `VrSwingSampler` for the dig spot too. **Effort:** S. **Confidence:** high for the
swing part; the equip and punch rules were not read.

## 7. VR UI placement and look, tuned without seeing it

**Fixes:** menu distance and panels `83e62cdda`, `08a4c792c`, `c3491d06f`, `b1d946114`, `6e78c8ad3`, `7effd2b6b`,
`c8067ca9f`, `c177065af`; Marvin panel facing `a8f1658a0`, `6bb6dda8f`; page arrows `358cf567e`, `5c5283cf0`,
`761571a6f`; thumbnails `4ae1a71b0`, `fd505de92`; chapter screen `3e584440b`, `e1f208386`; death view `a85bc2b6e`,
`812789108`, `b71694efc`, `ce6853eaa`, `d9c5fc889`; others by subject (`314a49182`, `8dc2cedb2`, `3f17c58b7`,
`e0d88d658`). Read: the arrow diffs, `ce6853eaa`; stats and messages for the rest.

**Evidence of ping-pong:**

- Page arrows, same line, three values in two days: `arrowWidth * 1.5f`, then `1.85f` (`5c5283cf0`), then `1.15f`
  (`761571a6f`). Now `Assets/Gothic-Core/Scripts/Adapters/UI/Menus/AbstractMenu.cs:221`.
- Main menu help panels: `c8067ca9f` replaced 190 lines of per-panel scene overrides by one rotation override,
  `c177065af` the same day removed that override because it turned the panels away.
- Marvin panel on the hand: turned to the back of the hand (`a8f1658a0`), three weeks later "faces the player
  again (it showed its back)" (`6bb6dda8f`).
- Thumbnail brightness fixed twice on consecutive days, the second time at the real cause (RGB565 texture upload).
- `358cf567e` says in its message "Not yet verified in game". Four commits in the range say so.
- Timing workaround: `Assets/Gothic-VR/Scripts/Adapters/Player/VrKnockoutView.cs:58-59` keeps the veil for 3 frames
  or 0.1 s to hide the hitch of building the death menu on first open.

**Likely root cause:** layout and look are changed by an agent that can't see the headset, so the loop is commit,
playtest, commit. Scene-level overrides on prefab instances (`MainMenu.unity`) make each try a blind edit; rule H of
the architecture doc says scenes should hold prefab instances only.

**Proposed consolidation:** a process change, not code. Hold layout values in the prefab and tune them in the
headset through the Marvin menu before committing (the swing thresholds already work this way since `ea1147aad`).
Commit a layout value once it was seen. Build the death menu at load time instead of hiding its first frames
(principle P3).

**Effort:** S. **Confidence:** medium (most of these diffs are prefab or scene YAML and were not read).

## 8. `DeveloperConfig` flags and `Production.asset`

**Fixes:** 10 commits that only repair `Production.asset`, between 11-02 and 11-06 (`c4dab0525`, `9921e5e9a`,
`3a92ef810`, `5663c6479`, `9a662163f`, `89a7c4702`, `19316d8b6`, `d51934c85`, `cd20ca8a5`, `37d7e9fd4`), two
review fixes that touch it (`aa17ea463`, `443b41c20`), plus `d520c3391` and `f51048361` by their subjects. Subjects
read, asset diffs not.

**What happened:** `DeveloperConfig` had 32 `public bool` fields at the start of the range, 68 after the NPC AI
merge (`4f32e8cc3`, 11-02), 61 on 11-07 and 36 today. `DeveloperConfig.cs` was changed in 62 commits,
`Production.asset` in 41. Fifteen refactor commits remove flags. The Production fixes exist because a new flag's C#
default is not what ships: `9921e5e9a` notes that NPC archers fought with melee weapons only because
`EnableNpcRangedCombat` was off in the asset.

**Likely root cause:** every feature shipped behind its own flag, and the asset value is a second manual step. Rule
I of the architecture doc and the `feature-checklist` skill describe it, but nothing checks it.

**Proposed consolidation:** an EditMode test that fails when `Production.asset` lacks a serialized value for a
`DeveloperConfig` field or carries one for a removed field. Keep the current direction of removing flags.

**Effort:** S. **Confidence:** high.

## 9. Boot order: options read before Gothic.ini exists, DI cycles

**Fixes (read):** `fc388ede4`, `1d9f49f34`, `ee0137340`, `a2cdf07ea`, `f042cfeda`, `5b1ad620e`, `9cf94fa48`,
`fc05dd76f`. **Subject only:** `1ef2d9e89`, `3caaa3b95`.

**Two causes:**

- *The player scene runs before a game is picked, so `ConfigService.Gothic` is null.* The vignette
  (`fc388ede4`), the comfort settings (`1d9f49f34`) and the footsteps option (`ee0137340`) each got their own guard
  and their own "read later" logic. `c7c9ea72b` (11-10) added null-safe helpers
  (`Assets/Gothic-Core/Scripts/Services/Config/ConfigService.cs:29-40`), used by 9 callers; 23 direct
  `.Gothic.Get*` reads and 13 hand-written null guards in 10 files remain.
- *Reflex fails at runtime when two services inject each other.* "The game starts again" (`a2cdf07ea`), "the VR
  scene starts again" (`9cf94fa48`, caused by review fix `1b642ca7e`), and the music service (`5b1ad620e`) are the
  same failure. Each was solved by taking the hero from the VM instead of `NpcService`. `fc05dd76f` is the related
  case of a component added with `AddComponent` and never injected.

**Likely root cause:** no place says "the ini is loaded now", and nothing checks the service graph before play
mode. `NpcService` is the hub most cycles run through because everything asks it for the hero.

**Proposed consolidation:** (a) an event "Gothic.ini loaded" that option consumers apply their value on, and move
the remaining direct reads to the helpers; (b) an EditMode test that builds the Reflex container and resolves every
registered service, so a cycle fails in CI and not at game start; (c) a small `HeroProvider` that services inject
instead of `NpcService` when they only need the hero.

**Effort:** S each. **Confidence:** high.

## 10. Save/load restore of single states

About 22 fixes, 14 of them on 10-27 right after `fb7626c20` ("Saving and loading games works again"): triggers,
movers, rain, dead NPCs, mission log, attacking monsters, mob states. Later ones: `30cb21773`, `79234c7e4`,
`e26fc78dd`, `5d68fad37`, `f538564a5`. Read: `a17559c79`, `30cb21773`, `e26fc78dd`, `ee590d694`, `f538564a5`;
subjects for the rest.

**Verdict:** mostly legitimate. Each fix covers a state the save didn't carry yet, and most follow principle 2 by
writing into the ZenKit field Gothic uses. The NPC part overlaps with section 2 (state applied again when the body
is built). The mover was touched four times, each time for a different case, and ended with an EditMode test. No
shared cause beyond "the feature merged before its states were covered". **Confidence:** medium.

## 11. Daedalus external robustness for mods

`f1032bfa6`, `8dfb18b17`, `fad7b05a0`, `66d081244`, `1f90db7f6`, `18803af3a`, `64b05881b`, `1c9721d11`,
`2d655635b`, `7c5e65461`. Only the stat of `f1032bfa6` was read.

**Verdict:** legitimate, and the general fix exists: `f1032bfa6` guards every external in one place
(`Domain/Vm/VmExternalGuard.cs`, 496 lines). `VmExternalDomain` has 23 fix commits only because it used to be the
2,472-line registration file. Game-version registration was patched twice in opposite directions (`66d081244`
"only for G2", then `fad7b05a0` "when the scripts declare them"), the second being the general rule. Null guards in
single externals kept coming after the guard (`43284afd6`, `e26fc78dd`); they belong to section 2.

## 12. Loading screen and loading bar

`966a7c5e4`, `4f605a631` (10-28), `c94f198d1` (10-31), `102a94ddd`, `23bdd3720`, `1b26aa2ae`, `e2ccec28b` (11-02).
Read: `1b26aa2ae`, `e2ccec28b`.

One regression pair: `1b26aa2ae` added `FinalizePhase()` to complete the bar, which broke level changes and loaded
saves; `e2ccec28b` repaired it and added a guard in `LoadingService` so "a tick without a phase no longer aborts
the load". The guard hides a wrong call order instead of fixing it. The rest was not judged.

## Smaller threads seen, not analysed in depth

- "One rule for dead / down / knocked out" was announced in four fixes (`979ae5e60`, `2a1a92898`, `edaead7a7`,
  `e26fc78dd`) and the rule moved between `NpcProperties`, `NpcHelperService` and `NpcContainerExtension`. About 20
  raw `BodyState == BsDead/BsUnconscious` comparisons remain (`FightService.cs:177`, `:195`, `:211`, `:348`, `:489`,
  `:504`, `AiHandler.cs:93`, `:676`, `:759`, others).
- "One item symbol rule" (`f71bea3e1`, `b637b37f9`, `2c8b646e9`, `372b1d58d`): same shape, not read.
- Trade "can't hang the dialog" in three review rounds on one day (`267ca7400`, `0b80330c5`, `da6690143`) and again
  in `823fc26b0`: not read.
- Fire scheme matching: `92b27ad9e` and `e94f59be9` carry the same subject (diffs not compared).
- Documents in the hand: `3a2d9a5d6` shipped reading with both hands, `213487fab` changed it to one hand two weeks
  later, with four fixes around it. Feature redesign, not a bug cluster.

## Fixes right after a feature on the same files

Measured by author timestamp: a fix counts when a `feat:` commit up to 48 hours older shares at least one `.cs`
file with it.

- 169 of 343 fixes (49 %) match. With a stricter rule (two shared files, or one shared file with a feature of at
  most 15 files) it is still 154.
- 65 of 131 features are followed by at least one such fix.
- 39 fix subjects contain "review". These are review findings committed after the feature commit.

| Feature | Date | Fixes within 48 h |
|---|---|---|
| `4f32e8cc3` NPC and monster AI, wave 2 (93 `.cs` files) | 11-02 | 17 |
| `fb7626c20` saving and loading works again (49 files) | 10-26 | 12 |
| `675ba0845` hits, knockout, death and experience | 10-31 | 10 |
| `da28b82bd` editor keeps playing without headset | 10-29 | 6 |
| `e6354b46e` knockout veil and death menu | 11-04 | 5 |
| `ee5c0263d` UI and Marvin commands | 11-03 | 5 |
| `2dcde7481` reference engine externals | 10-31 | 5 |
| `21e28b9ee` movers, triggers and mobs | 11-11 | 4 |
| `9b45f3bcf` NPCs notice theft | 11-03 | 4 |
| `7ed9736fc` NPC perceptions and AI states | 10-31 | 4 |
| `61af19201` VR controls in the game menu | 10-31 | 4 |
| `e0a85ca6a` trading | 10-30 | 4 |
| `194ef0042` spell target lock | 11-13 | 3 |
| `b65c1f150` comfort vignette and text size | 10-30 | 3 |

The numbers overstate single cases: a 93-file feature shares a file with almost anything, and rebased branches
carry clustered dates. The direction is clear all the same: large feature commits merge first and are completed by
fixes.

## The 10 files with the most fix commits

| Fixes | All commits | Lines today | File | Comment |
|---|---|---|---|---|
| 30 | 54 | 1715 | `Gothic-Core/Scripts/Services/Npc/NpcAiService.cs` | section 2; four times the size limit |
| 23 | not counted | 123 | `Gothic-Core/Scripts/Domain/Vm/VmExternalDomain.cs` | was the registration hub, split since |
| 22 | 39 | 970 | `Gothic-Core/Scripts/Services/Npc/NpcService.cs` | hub for the hero, sections 2, 3, 9 |
| 20 | 62 | 624 | `Gothic-VR/Scripts/Services/Context/VRContextInteractionService.cs` | hub, unrelated fixes |
| 20 | 46 | 1204 | `Gothic-Core/Scripts/Domain/Vobs/VobInitializerDomain.cs` | section 1 |
| 17 | 32 | 740 | `Gothic-Core/Scripts/Services/Player/DialogService.cs` | section 5 |
| 17 | 26 | 700 | `Gothic-Core/Scripts/Services/Npc/NpcHelperService.cs` | section 2 |
| 16 | 31 | 1135 | `Gothic-Core/Scripts/Services/Vobs/VobService.cs` | section 1 |
| 15 | 31 | 905 | `Gothic-Core/Scripts/Adapters/Npc/AiHandler.cs` | section 2; an adapter with logic |
| 14 | 36 | 533 | `Gothic-VR/Scripts/Services/VRPlayerService.cs` | section 4 |

`VRContextInteractionService` and `VRContextMenuService` (12 fixes, 48 commits) are busy because every VR feature
registers something there. Their fixes are unrelated to each other, apart from the dialog and lock thread.

## What to tackle first

1. **Guard tests for the two cheap process causes (S).** A test that resolves the whole Reflex container, and a
   test that compares `DeveloperConfig` fields with `Production.asset`. They end two fix series (sections 8 and 9)
   for a few hours of work.
2. **One explicit state for a VR item (M).** Section 4. It is the area players touch most, the fix chain is the
   clearest "fix breaks the neighbour" case, and it is contained in Gothic-VR.
3. **Hero stats from one store (M).** Section 3. Small surface, removes a rule people have to remember.
4. **One "body built" step for NPCs (M).** Second half of section 2. Takes the restore branches out of
   `AiHandler.Start()` and gives fatness, hitbox, pose and highlight one caller.
5. **Flatten the VOB tree (L).** Section 1. Highest certainty about the cause, but it touches culling and save
   games, so it needs a design and an ADR first.
6. **A body interface for the hero and bodiless NPCs (L).** First half of section 2. Largest payoff in removed
   guards, largest change. Do it after 4.
7. **Tune layout in the headset before committing (S, process).** Section 7.

## Not checked

- Diffs of prefab, scene, material and asset files. All claims about `BackPack XRI.prefab`, `MainMenu.unity`,
  `Production.asset` and `DynamicsManager.asset` rest on commit messages and stats.
- The 34 fix commits without any `.cs` file, apart from their subjects.
- The VR hands and rig group (33 primary fixes: `VrPhysicsHand`, `XriRig`, `VRHeroBody`, the device simulator). The
  history was listed, no diffs were read. It looks like fallout of the HVR to XRI port (10-22 to 10-26). The
  simulator's grip was fixed twice (`03c14e3fc`, `852e3758b`), which may be a regression.
- Favorites ring, spells and transformation, audio and music, sky, animation, caches and textures: counted in
  table 1, not analysed.
- Trade, item symbol rule, most of the VR melee review fixes: subjects only.
- `Gothic-Flat`, `Gothic-Gothic1`, `Gothic-Gothic2`, tests and editor code.
- `refactor:` and `perf:` commits were not searched for hidden fixes, and `feat:` commits not for hidden reverts.
- Whether the proposed consolidations fit open branches. `.claude/review/` holds reports for nine branches that
  were not read.
- Nothing was verified at runtime. Statements like "still in the code" come from reading and `grep`.
- Counts from `grep` (54 `IsHero(`, 165 null checks, 81 `GetComponentInParent<VobLoader>`, 23 direct ini reads) are
  pattern matches, not reviewed one by one.
