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

## Key commits

`ac3defb3` trade · `27877a13`/`f69422b5` stack split · `dbf74e14`/`0f5feebe`/`56596cc5`/`934b0305` loot backpack ·
`be96097d`/`3f8bcb2a`/`6c9736a9` popup + vacuum · `b1d68e70` script inventory events · `7a682222` UI freeze

2026-10-10/11: `5db3f9fa` lidless containers

2026-10-11/13: `f4439ca6` quick loot

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
