# 6. Inventory, trade, stacks, NPC loot

[← Overview](README.md)

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

**Added 2026-10-10 – 2026-10-11**

- **Lidless containers** (no `.MDS`, e.g. the orc mummies in the Sleeper's temple) are mobs: highlight and click like a
  switch, the click shows a loot backpack with their items on the hero's side.

**Added 2026-10-11 – 2026-10-13**

- **Quick loot:** the loot backpack of a dead NPC or a lidless container (mummy) gets a green **X** above the right page
  arrow (a copy of the arrow, Gothic font) - it moves everything into the hero's inventory at once. Not for knocked out
  NPCs (robbed item by item) nor traders.

**Added 2026-10-18**

- **Plundering** (`60fb9190`, `EnableInvItemBySlot`): an NPC who knocked out the hero takes half his ore and says so
  (G1 `B_Plunder`). `Npc_GetInvItemBySlot` only found the equipped weapon/armor - with 1000 ore he said "not even
  ore". Like OpenGothic's `Inventory::findByFlags` it returns the slot-th item of the `INV_*` category from the packed
  inventory.

**Added 2026-10-25**

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

## Key commits

`ac3defb3` trade · `27877a13`/`f69422b5` stack split · `dbf74e14`/`0f5feebe`/`56596cc5`/`934b0305` loot backpack ·
`be96097d`/`3f8bcb2a`/`6c9736a9` popup + vacuum · `b1d68e70` script inventory events · `7a682222` UI freeze

2026-10-10/11: `5db3f9fa` lidless containers

2026-10-11/13: `f4439ca6` quick loot

2026-10-18: `60fb9190` plundering takes ore

2026-10-25: `0ecdf3d7` pickpocketing (G1)

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
