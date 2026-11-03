# ADR: VR movement, trade and theft

- Status: proposed
- Date: 2026-11-03
- Technical details: VRHeroMovement, VRSwimDive, VRTradeCounter, VRTradeGoods, VRPickpocket (VR); HeroPersonalSpace, NpcBodyBounds (Core adapters); HeroPerceptionService, TradeService, PickpocketService, TradePricing (Core); DeveloperConfig EnableVrPickpocket, EnableG2VrPickpocket, EnableHeroPersonalSpace, EnableSneakSlowSprint

## Context

The scripts read the hero's body state (BS_WALK/RUN/SNEAK/SWIM), hear his steps and watch what he takes. In VR the hero moves with sticks and hands, trades without Gothic's trade screen and steals without its steal window.

## Decisions

1. **The hero's body state comes from his movement,** like the engine:
   - walking and running come from the speed;
   - crouching is sneaking, but only with the sneak talent (OpenGothic `Npc::canSneak`);
   - running stays on for 1.5 s, and a teleport counts as running, so `ZS_Attack_Loop` can give up a chase. The steps NPCs hear only come with real movement (like the engine's step events), not from a lingering run;
   - swimming and diving are BS_SWIM and BS_DIVE.

   VR decides it for now (`VRHeroMovement`, `VRSwimDive`). A Core owner, for Flat too, is a follow-up.
2. **Footsteps are quiet sounds** (PERC_ASSESSQUIETSOUND), except while sneaking. Sleepers in G2 wake up from them, like in the engine.
3. **Taking an item lying in the world can be theft** through main's world item list (`HeroPerceptionService.OnHeroTookItem`, called by `PlayerService.PickUpItem`). Our own theft check (`IsTakenWithoutTheft`) is gone. A piece split off a world stack counts as taking that stack (once per stack).
4. **In VR, trade is a counter between the hero and the trader.** It replaces the trade window of [2026-10-30-trading](2026-10-30-trading.md), as that record foresaw ("can replace the panel later on top of the same trading rules"), and its author agreed to drop the window:
   - one `TradeService` with that record's rules: the trade starts after the trader's lines. Unlike the window, the counter doesn't hold the dialog: its choices come back at once (talking while trading, like with a cashier), and the dialog's end ends the trade;
   - the counter's offers are a `TradeSession` in it: reserved out of their owner's inventory, swapped on OK, and the difference is paid in currency; the trader never runs out of currency;
   - the sell price rounds `value x TRADE_VALUE_MULTIPLIER` after cutting float noise (0.15 x 100 is 15, not 16);
   - the offers go back when the trade ends: closed, the dialog ends, or the world is left (saving is blocked in a dialog);
   - unpaid goods stay the trader's until OK, and currency held in a hand doesn't pay.
5. **Pickpocketing in VR:**
   - A purse on the hip of a human who can't see the sneaking hero opens his pockets.
   - The chance to get caught is G1's talent value, and in G2 100 - dexterity (5-90).
   - Sleepers can't catch you.
   - G2 gets the purse too, on top of its dialog (`EnableG2VrPickpocket`). It skips the dialog's one-time check and its XP, which is a deliberate choice, and the flag switches it off.
6. **NPC bodies stay out of the camera** (`EnableHeroPersonalSpace`, 1 m), position only, not for humans who can be talked to. Only NPCs within 12 m are checked, from a list rebuilt every 0.25 s.
7. **The speed potion and the slow sneak sprint are factors on one base speed.** Swimming or a transformation setting speeds in between becomes the new base.

## Consequences

- `NpcBodyBounds` also shapes the combat hitbox, and it's attached by the personal space. Giving it to the NPC's own setup is a follow-up.
- After a load, the world item list is built from the saved world: items the hero dropped can be theft once.
- The speed potion and an open trade aren't saved.
- Unpaid goods in the hero's hand count as his inventory while held (Daedalus `Npc_HasItems` sees them); letting go anywhere but the counter or the trader's goods gives them back.
- `VRTradeCounter` (719 lines) and `VRPickpocket` (552 lines) build their objects in code. Prefabs are a follow-up.
