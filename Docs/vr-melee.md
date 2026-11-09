# VR melee

How the hero fights with his real hands in VR: sword swings, fists, parrying and the stab finish. Gothic's rules
(damage, finishing moves, who reacts) stay in Core; VR only turns hand movement into Gothic's events.
Why it's built this way: Docs/adr/2026-11-02-vr-melee.md. The NPC side of fights is in `Docs/npc-ai.md`.

## Who owns what

| Class | Owns |
|---|---|
| `VRWeaponService` (VR) | the hero's weapons: two `VrWeaponAttackDomain` (swing velocity, attack window, combo), `VrFistDomain`, `VrStabFinishDomain`; folds them into the hero's weapon state (`UpdateHeroWeaponState`) |
| `VrWeaponAttackDomain` | one held melee weapon: when a swing counts, the attack window, the swing sound, the hero's prehit (`HeroMeleeSwing`) |
| `VrFistDomain` | fists: an empty hand with grip + trigger held is a fist; punch speed, cooldown, the fist hit |
| `VrStabFinishDomain` | a blade thrust into an unconscious NPC and kept there; raises the finish |
| `VRHeroBlockService` (VR, player scope) | owns `VrHeroBlockDomain` and ticks it (`VRHeroBlock` adapter) |
| `VrHeroBlockDomain` | blocking: both hands on one melee weapon + both triggers; sets the hero's block props and reports the NPC weapons touching the held one |
| `VRContextHeroBlockService` (VR) | the block rule: blocked only if the NPC weapon touched the held one within 0.5 s (waits for it if the hit reaches the body first) |
| `FightService` (Core) | decides what a hit does: damage, finishing move, block (`HeroBlockDomain`), the NPCs' reaction to the hero's prehit |
| `VrHapticsService` (VR, scene scope) | listens to fact events and maps them to haptic presets and hands; the domains never pick a preset |
| `VRHeldItemSyncService` (VR) | keeps the physical items in hands and holsters in step with the hero's inventory when a script takes items |

Adapters are thin: `VRWeaponAttackAdapter` (weapon trigger), `VRFistHitbox` (knuckle trigger), `VRHeroBlock` (tick).
Item hitboxes come from pre-caching: `VobItemFittedColliderCacheCreatorDomain` fits boxes to a melee weapon's parts
(`ItemColliderFittingDomain`), `VobItemColliderBuilder` builds them (bump `Constants.StaticCacheVersion` when the
fitting changes).

## Data flow of one hit

1. The player swings. `VrWeaponAttackDomain` sees the velocity over the threshold and opens the attack window.
2. The weapon's trigger enters an NPC (`VRWeaponAttackAdapter`; a fist: `VRFistHitbox` -> `VrFistDomain`). In the
   attack window it raises `FightHit(attacker, target, position, weapon)`. The weapon is the held item's
   `ItemInstance`, null for a fist.
3. `FightService` takes the hit. It checks blocks (the NPC's parry/jump back, the hero's block via
   `VRContextHeroBlockService`), then the finishing-move rule, then `MeleeDamageDomain` with the weapon's damage.
4. `VRWeaponService` hears `FightHit` and moves the swinging weapon's attack state on (waiting for a combo).
5. `VrHapticsService` hears the fact events (`FistSwung`, `FistPunchLanded`, `HeroBlockStarted`, `HeroBlockedHit`,
   `StabStuck`, `StabHeld`, `StabFinished`) and vibrates the hand.

A stab finish: the blade enters an unconscious NPC, `VRWeaponService.TryStabFinish` hands it to
`VrStabFinishDomain`. Kept in for 2 s it raises `FightFinishingMove`, the same event as an NPC's `AI_FinishingMove`.

A block: `VrHeroBlockDomain` sets the hero's block props every frame and calls `FightService.ReportBlockContact` for
each NPC weapon touching the held one; an NPC's hit then asks `VRContextHeroBlockService` whether it is blocked.

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change when a swing counts | `VrWeaponAttackDomain` (thresholds: prefab fields, `WeaponPhysicsConfig`) |
| change the fist rules | `VrFistDomain`, tunables in `VRConstants.Melee` |
| change the stab finish | `VrStabFinishDomain`, `VRConstants.Melee`; Gothic.ini `stabFinish` switches it |
| change what a block is | `VrHeroBlockDomain` (props, contacts), `VRContextHeroBlockService` (the rule) |
| change damage or finish decisions | `FightService`, `MeleeDamageDomain` (Core, not VR) |
| change which hit vibrates how | `VrHapticsService` |
| change a weapon's hitboxes | `VobItemFittedColliderCacheCreatorDomain` (then bump the cache version) |
