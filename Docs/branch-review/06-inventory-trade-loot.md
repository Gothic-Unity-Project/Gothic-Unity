# 6. Inventory, trade, stacks, NPC loot

[← Overview](README.md)

<!-- toc -->
## Contents

- [What changed](#what-changed)
  - [Added 2026-10-10 – 2026-10-11](#added-2026-10-10--2026-10-11)
  - [Added 2026-10-11 – 2026-10-13](#added-2026-10-11--2026-10-13)
  - [Added 2026-10-18](#added-2026-10-18)
  - [Added 2026-10-25](#added-2026-10-25)
  - [Added 2026-10-25 (afternoon)](#added-2026-10-25-afternoon)
  - [Added 2026-10-25 (evening)](#added-2026-10-25-evening)
  - [Added 2026-10-26](#added-2026-10-26)
- [Key commits](#key-commits)
- [Main files](#main-files)
- [Config flags](#config-flags)
- [Review notes](#review-notes)
- [How to test](#how-to-test)
<!-- /toc -->

## What changed

- **Trading in VR:** "Show me your goods" puts a counter between hero and trader: his goods, both offers, the
  price difference in gold (G2) / ore (G1), OK/X. Engine prices, reserved offers, anti-theft (trader goods dropped
  or bagged go back to him and can't be eaten). The dialog continues during trading.
  An invisible table under the goods backpack catches it at hand height (only that backpack touches it).
  Each side of the counter is a view of its offer: the same items stack into one, three slots show the offer
  (arrows move it by one), and an always empty slot next to the middle takes new items.
- **Stack splitting:** hold the trigger to take pieces into the empty hand or into a socket, at an accelerating
  pace; join equal stacks; live amount labels.
- **NPC loot backpack:** a tinted copy of the hero's backpack next to the downed NPC, opening on its first non-empty
  category, closing after a timeout when untouched. `NpcLootMode`: sockets, backpack or both.
- **Inventory owners:** `IInventoryOwner` with `PlayerInventoryOwner`/`NpcInventoryOwner`, so every inventory view
  works for hero and NPC alike.
- Backpack extras behind "TODO TEST ME" flags: item details popup, backpack vacuum.
- Fixes: backpack UI froze after a page click during a refresh; food in the backpack isn't eaten; quick slots no
  longer resize items; script inventory changes reach VR.

### Added 2026-10-10 – 2026-10-11

- **Lidless containers** (no `.MDS`, e.g. the orc mummies in the Sleeper's temple) are mobs: highlight and click like a
  switch, the click shows a loot backpack with their items on the hero's side.

### Added 2026-10-11 – 2026-10-13

- **Quick loot:** the loot backpack of a dead NPC or a lidless container (mummy) gets a green **X** above the right page
  arrow (a copy of the arrow, Gothic font) - it moves everything into the hero's inventory at once. Not for knocked out
  NPCs (robbed item by item) nor traders.

### Added 2026-10-18

- **Plundering** (`60fb9190`, `EnableInvItemBySlot`): an NPC who knocked out the hero takes half his ore and says so
  (G1 `B_Plunder`). `Npc_GetInvItemBySlot` only found the equipped weapon/armor - with 1000 ore he said "not even
  ore". Like OpenGothic's `Inventory::findByFlags` it returns the slot-th item of the `INV_*` category from the packed
  inventory.

### Added 2026-10-25

- **Pickpocketing, G1** (`0ecdf3d7`, `EnableVrPickpocket`): G1 steals through the engine's steal window, G2 in
  dialogs (`DIA_*_PICKPOCKET`, `C_Beklauen`) - only G1 gets this; OpenGothic has no G1 pickpocketing. `PickpocketService`
  (Core): the hero has `NPC_TALENT_PICKPOCKET` skill > 0 and is in `BS_SNEAK`; the victim is human (guild <
  `GIL_SEPERATOR_HUM`), up, without a drawn weapon and can't see the hero (FOV). Each item taken rolls the talent value
  as the chance to get caught (`Npc_Default` 90, `B_GiveSkill` -40 per level: 50 / 10) - Fingers: "only as a master
  you take something without him noticing". Caught: `PERC_CATCHTHIEF` to the victim (`ZS_CatchThief`: whirl around,
  `ZS_AssessEnemy`) via `NpcAiService.ExecuteSinglePerception`, the passive `PERC_ASSESSTHEFT` from the hero with the
  victim set to witnesses within `HAI_DIST_ASSESSTHEFT` (`B_AssessTheft`: "$BEHINDYOU", `PERC_ASSESSWARN` to the
  victim). `VRPickpocket` (VR): up to 3 purses within 3 m - the backpack's `BackPack-Body` mesh made 14 cm, at
  `ZS_SWORD` mirrored on the NPC's middle plane (right hip), pulsing emission; a free hand within 20 cm grows it and
  its grip opens `PickpocketInventoryOwner` (the NPC's items without worn/wielded ones, reports every taken item) in a
  `VRBackpack` copy forced into that hand. The victim seeing the hero is a catch too; letting go of the backpack or
  2.5 m away closes it; a caught victim has no purse for 30 s. `VRNpc.OnGrabbed` doesn't talk while a hand is at the
  purse (+1 s) or the NPC is robbed - HVR grabs the NPC with the same grip. Runtime only; the items are in the saved
  inventories.

### Added 2026-10-25 (afternoon)

- **Equip from the backpack** (`cdb58a50`, `EnableBackpackEquipToggle`): `VRBackpackEquipToggle` - an empty hand at a
  wearable (armor, amulet, ring, belt) in the hero's backpack and a trigger click (under 0.5 s, under 3 cm) equips or
  unequips it with effects; `VRBackpack.RefreshInventoryView` updates [E].
- **Pickpocketing in menus** (`4a199637`): `PickpocketService.IsEnabled` checks the Gothic config and the VM before the
  game version - it threw every frame in the profile menu.

### Added 2026-10-25 (evening)

- **Favorites ring** (`ceeee724`, `d644e599`, `EnableRingWeaponSlots`, `EnableRingItemSlots`,
  `EnableRingWeaponLeavesOnDrop`): the spell ring ([Magic](02-magic.md)) also holds the equipped melee weapon and
  bow/crossbow (the last equipped of each kind - Gothic has one of each), up to 3 favorite items
  (`NpcProperties.FavoriteItems`, saved as `HeroFavoriteItems` in UNITYSAVE.json) and the trade currency
  (`TRADE_CURRENCY_INSTANCE`, G1 `ItMiNugget`) - at most 8 + 2 + 3 + 1; the empty slot shows while something is
  missing. Weapons and items are the real items (`VobService.CreateItem`), kinematic and shrunk into their slot, with no
  hand/body collisions, no force grab and without `VRFocus`'s name: HVR grabs them like out of a socket (at the grip,
  `VRWeaponGripGrab`), full size again in `BeforeGrabbed`; one is taken out of the inventory (the grab counted it in,
  `VRPlayerService.SetGrab`). Grip held at a stack for 1.5 s grows the held stack (2/s, doubling every second); moving
  the hand 15 cm away closes the ring. Only what is in the backpack shows - inventory minus `IsHeldByPlayer` items
  outside the backpack's own sockets (an equipped weapon in a hand, holster or on the ground made a copy with every
  grab). A held item let go within 0.2 s of touching a slot (any collider, a sword by its blade) goes in: a weapon is
  equipped (all others of its kind come off - several bows stayed equipped, `[E]` moved from one to the next), anything
  else becomes a favorite, at its own slot it just goes back. A trigger click unequips / takes it off the ring; loading
  a game closes the ring; the backpack vacuum leaves ring items alone. `VRPlayerWeaponInteraction` (grabbing a melee
  weapon equips it while held) gives back the weapon equipped before the grab - before, any weapon let go left no
  equipped melee weapon at all. A weapon taken out of the ring stays equipped if it ends up in the backpack (opened or
  over the shoulder), dropped or in another quick slot it comes off (checked 0.5 s after letting go).
- **EquipItem gives the item** (`71c4d8dc`, `EnableEquipItemCreatesItem`): externals.d - "gibt dem Nsc direkt das
  angegebene Item an den Guertel"; orc shamans equip their staff without `CreateInvItem`. We only equipped it:
  Mroczne Tajemnice's PC_Rockefeller had his warhammer equipped but not in the inventory. Loading a save clears the
  hero's inventory first, nothing doubles.

### Added 2026-10-26

- **The favorites ring around the player** (`17faa5dd`): at eye level, 2.2x, the head selects the slot; a
  carousel turned by the hand around the player (spins on, a trigger press stops it), the right stick zooms it; runes
  stand upright, the front spell's effect after 1 s over its half transparent rune.

## Key commits

`ac3defb3` trade · `27877a13`/`f69422b5` stack split · `dbf74e14`/`0f5feebe`/`56596cc5`/`934b0305` loot backpack ·
`be96097d`/`3f8bcb2a`/`6c9736a9` popup + vacuum · `b1d68e70` script inventory events · `7a682222` UI freeze

2026-10-10/11: `5db3f9fa` lidless containers

2026-10-11/13: `f4439ca6` quick loot

2026-10-18: `60fb9190` plundering takes ore

2026-10-25: `0ecdf3d7` pickpocketing (G1)

2026-10-25: `cdb58a50` equip from the backpack · `4a199637` pickpocketing menu fix

2026-10-25: `ceeee724` favorites ring · `d644e599` ring weapon back into the backpack · `71c4d8dc` EquipItem gives the item
2026-10-26: `17faa5dd` ring around the player

## Main files

Core: `Services/Trade/TradeService.cs`, `Domain/Trade/TradePricing.cs`, `Models/Trade/*`,
`Services/Inventory/StackSplitService.cs`, `Domain/Inventory/*InventoryOwner.cs`, `Models/Inventory/IInventoryOwner.cs`,
`Services/Npc/NpcInventoryService.cs` (+496) · VR: `Trade/VRTradeCounter.cs`, `Trade/VRTradeGoods.cs`,
`VobItem/VRStackSplitter.cs` (+670), `Player/VRBackpack.cs`, `VRNpcLoot.cs`, `VRBackpackVacuum.cs`,
`VRItemDetailsPopup.cs` · Tests: `Gothic-Tests/PlayMode/TradePricingTests.cs`

## Config flags

`EnableVrTrade`, `EnableTradeGoodsTable`, `EnableStackSplit`, `StackSplitRepeatDelay`, `StackSplitAcceleration`, `NpcLootMode`,
`NpcLootBackpackTimeout`, `EnableBackpackVacuum`, `BackpackVacuumRadius`, `BackpackVacuumStoreSeconds`,
`EnableItemDetailsPopup`, `EnableScriptRemovesHeldItems`.

2026-10-10/11: —

2026-10-11/13: `EnableLootTakeAll`

2026-10-18: `EnableInvItemBySlot`

2026-10-25: `EnableVrPickpocket`

2026-10-25: `EnableBackpackEquipToggle`

2026-10-25: `EnableRingWeaponSlots`, `EnableRingItemSlots`, `EnableRingWeaponLeavesOnDrop`, `EnableEquipItemCreatesItem`
2026-10-26: `EnableRingAroundPlayer`, `EnableRingSpellPreviewAlpha`, `EnableRingFreeSpin`, `EnableRingZoom`

## Review notes

- ✅ Good layering here: pricing is pure domain code with **unit tests**, trade state is a model, VR only presents.
- ✅ Trade events go through `GlobalEventDispatcher` (`TradeStarted/OfferChanged/Committed/Closed`).
- ⚠️ `VRStackSplitter.cs` (670 lines) holds the hold-to-split timing and socket logic; `StackSplitService` is thin.
- ℹ️ `TradeService`/`StackSplitService` are registered with fully qualified names in `ReflexProjectInstaller`
  (missing `using`) — cosmetic.

## How to test

- G2 Constantino / G1 Fisk: buy 3 of 10 arrows (split onto the counter), sell something, check the balance.
- Knock out an NPC: his backpack appears, opens on a category with items, disappears after 20 s.
- G1 Sleeper temple: click an orc mummy, take its items; they stay gone after save/load.

- Kill an NPC, open his loot: the green X takes everything; a knocked out NPC and a trader have no X.
- G1/MT: get knocked out by a guard - he says he took your ore, half of it is gone.
- Open the ring: it's around you; turn it with a hand and let go - it spins, a trigger press stops it.
