# ADR: VR movement, trade and theft

- Status: proposed
- Date: 2026-11-03
- Technical details: VRHeroMovement, VRSwimDive, VRTradeCounter, VRTradeGoods, VRPickpocket (VR); NpcBodyBounds (Core adapter); HeroPerceptionService, TradeService, PickpocketService, PickpocketDialogDomain, TradePricing (Core); Gothic.ini option pickpocketPurse

## Context

The scripts read the hero's body state (BS_WALK/RUN/SNEAK/SWIM), hear his steps and watch what he takes. In VR the hero moves with sticks and hands, trades without Gothic's trade screen and steals without its steal window.

## Decisions

1. **The hero's body state comes from his movement,** like the engine:
   - walking and running come from the speed;
   - crouching is sneaking, but only with the sneak talent (OpenGothic `Npc::canSneak`). Like vanilla Gothic there is no sprint while sneaking: the sprint input does nothing, the hero moves at sneak speed and stays BS_SNEAK;
   - running stays on for 1.5 s, and a teleport counts as running, so `ZS_Attack_Loop` can give up a chase. The steps NPCs hear only come with real movement (like the engine's step events), not from a lingering run;
   - swimming and diving are BS_SWIM and BS_DIVE.

   VR decides it for now (`VRHeroMovement`, `VRSwimDive`). A Core owner, for Flat too, is a follow-up.
2. **Footsteps are quiet sounds** (PERC_ASSESSQUIETSOUND), except while sneaking. Sleepers in G2 wake up from them, like in the engine.
   The VR hero body's animations make the steps (always on, like an NPC's); a body without animations falls back to a step every stride walked.
3. **Taking an item lying in the world can be theft** through main's world item list (`HeroPerceptionService.OnHeroTookItem`, called by `PlayerService.PickUpItem`). Our own theft check (`IsTakenWithoutTheft`) is gone. A piece split off a world stack counts as taking that stack (once per stack).
4. **In VR, trade is a counter between the hero and the trader.** It replaces the trade window of [2026-10-30-trading](2026-10-30-trading.md), as that record foresaw ("can replace the panel later on top of the same trading rules"), and its author agreed to drop the window:
   - one `TradeService` with that record's rules: the trade starts after the trader's lines. Unlike the window, the counter doesn't hold the dialog: its choices come back at once (talking while trading, like with a cashier), and the dialog's end ends the trade. The counter opens through main's trade port (VR: the counter; a mode without one, Flat, ends the trade at once);
   - the counter's offers are a `TradeSession` in it: reserved out of their owner's inventory, swapped on OK, and the difference is paid in currency; the trader never runs out of currency;
   - the sell price rounds `value x TRADE_VALUE_MULTIPLIER` after cutting float noise (0.15 x 100 is 15, not 16);
   - the offers go back when the trade ends: closed, the dialog ends, or the world is left (saving is blocked in a dialog);
   - unpaid goods stay the trader's until OK, and currency held in a hand doesn't pay.
5. **Pickpocketing in VR:**
   - A purse hangs on the hip of a human who can't see the sneaking hero.
   - G1 (always on, no option): the purse is the engine's steal window. It opens his pockets, each item taken can get the hero caught with the talent value's chance. Sleepers can't catch you.
   - G2: the purse is only a VR view of G2's own pickpocket dialog, not a theft system of its own. It shows exactly when the NPC's `DIA_*_PICKPOCKET` option would show (its condition with the NPC as self and the hero as other). No purse means already robbed, or not skilled enough (talent, dexterity). Grabbing it is choosing "pickpocket": the script's `B_Beklauen` runs and gives the gold, XP and sound, or the NPC attacks. Gold only, once per NPC, like the dialog. Vanilla's 10 dexterity "trap window" stays: the purse shows, the grab fails.
   - G2's purse is a player immersion option in Gothic.ini (`pickpocketPurse`, Immersion menu, on by default). Off, G2 only has its dialog. It replaces the DeveloperConfig flag `EnableG2VrPickpocket`.
   - Why: the first G2 purse was a separate theft system next to the dialog. It took any item with its own dexterity roll, bypassed G2's balance (gold amounts, XP, dexterity per NPC) and could rob an NPC twice (purse and dialog).
6. **No personal space: NPC bodies may come into the camera.** A personal space (`HeroPersonalSpace`, NPCs pushed 1 m away from the hero's head) was tried and removed again:
   - in a fight the NPC hits first anyway, before its body reaches the camera;
   - leaning into an NPC is the player's choice;
   - moving NPC transforms behind the AI's back fought its movement.

   It can come back as a Gothic.ini comfort option if playtests show monster heads in the camera.
7. **The speed potion is a factor on one base speed (and sneaking caps the run speed at the move speed).** Swimming or a transformation setting speeds in between becomes the new base.

## Consequences

- `NpcBodyBounds` shapes the combat hitbox of every NPC with one. It's added when the NPC's body is built and measured there in the rest pose (the mesh's own bounds, no BakeMesh). It follows the animation within 12 m of the camera; further away the last box stays.
- After a load, the world item list is built from the saved world: items the hero dropped can be theft once.
- The speed potion and an open trade aren't saved.
- Unpaid goods in the hero's hand count as his inventory while held (Daedalus `Npc_HasItems` sees them); letting go anywhere but the counter or the trader's goods gives them back.
- `VRTradeCounter` (719 lines) and `VRPickpocket` (552 lines) build their objects in code. Prefabs are a follow-up.
