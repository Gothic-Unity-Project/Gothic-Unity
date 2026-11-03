# ADR: VR movement, trade and theft

- Status: proposed
- Date: 2026-11-03
- Technical details: VRHeroMovement, VRSwimDive, VRTradeCounter, VRTradeGoods, VRPickpocket (VR); HeroPersonalSpace, NpcBodyBounds (Core adapters); HeroPerceptionService, TradeService, PickpocketService, TradePricing (Core); DeveloperConfig EnableVrTrade, EnableVrPickpocket, EnableG2VrPickpocket, EnableHeroPersonalSpace, EnableSneakSlowSprint

## Context

The scripts read the hero's body state (BS_WALK/RUN/SNEAK/SWIM), hear his steps and watch what he takes. In VR the hero moves with sticks and hands, trades without Gothic's trade screen and steals without its steal window.

## Decisions

1. **The hero's body state comes from his movement,** like the engine:
   - walking and running come from the speed;
   - crouching is sneaking, but only with the sneak talent (OpenGothic `Npc::canSneak`);
   - running stays on for 1.5 s, and a teleport counts as running, so `ZS_Attack_Loop` can give up a chase;
   - swimming and diving are BS_SWIM and BS_DIVE.

   VR decides it for now (`VRHeroMovement`, `VRSwimDive`). A Core owner, for Flat too, is a follow-up.
2. **Footsteps are quiet sounds** (PERC_ASSESSQUIETSOUND), except while sneaking. Sleepers in G2 wake up from them, like in the engine.
3. **Taking an item lying in the world can be theft** (PERC_ASSESSTHEFT with no victim, like the engine). Items made at runtime (out of a backpack or chest) and pieces of the hero's own stacks aren't.
4. **Trade is a counter between the hero and the trader:**
   - prices like OpenGothic (G2 `TRADE_VALUE_MULTIPLIER`, currency `TRADE_CURRENCY_INSTANCE` or ore), and the trader never runs out of currency;
   - unpaid goods stay the trader's until OK;
   - a trade ends with the dialog, with a save and with leaving the world, and the offers go back to their owners;
   - currency held in a hand doesn't pay.
5. **Pickpocketing in VR:**
   - A purse on the hip of a human who can't see the sneaking hero opens his pockets.
   - The chance to get caught is G1's talent value, and in G2 100 - dexterity (5-90).
   - Sleepers can't catch you.
   - G2 gets the purse too, on top of its dialog (`EnableG2VrPickpocket`). It skips the dialog's one-time check and its XP, which is a deliberate choice, and the flag switches it off.
6. **NPC bodies stay out of the camera** (`EnableHeroPersonalSpace`, 1 m), position only, not for humans who can be talked to. Only NPCs within 12 m are checked, from a list rebuilt every 0.25 s.
7. **The speed potion and the slow sneak sprint are factors on one base speed.** Swimming or a transformation setting speeds in between becomes the new base.

## Consequences

- `NpcBodyBounds` also shapes the combat hitbox, and it's attached by the personal space. Giving it to the NPC's own setup is a follow-up.
- `IsTakenWithoutTheft` isn't saved: after a load, the hero's own dropped items can be theft once.
- The speed potion and an open trade aren't saved.
- `VRTradeCounter` (719 lines) and `VRPickpocket` (552 lines) build their objects in code. Prefabs are a follow-up.
