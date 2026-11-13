# VR magic

How the hero casts runes and scrolls with his real hands in VR: aimed spells, charged spells, thrown spells,
transformations and telekinesis. Gothic's rules (the Daedalus spell protocol, damage per level, who reacts) stay in
the scripts and Core and are the same for NPC casters; VR turns triggers and hand movement into the engine's calls.
Why it's built this way: Docs/adr/2026-11-03-vr-magic.md. Melee and ranged are in `Docs/vr-melee.md` and
`Docs/vr-ranged.md`.

## Who owns what

| Class | Owns |
|---|---|
| `VRVobItem` (VR adapter) | adds `VRRuneCaster` to a grabbed rune/scroll (and to the favorites ring's invisible rune, `RingHand`) |
| `VRRuneCaster` (VR adapter) | one readied rune/scroll: the trigger as cast key, the Daedalus protocol (`Spell_ProcessMana`, `Spell_ProcessMana_Release`, `Spell_Cast_<name>`), mana ticks, the cast's effect (`SpellHit`), scroll use-up, the charge state machine of throwable spells |
| `VRSpellFx` (worker of the caster) | what a spell looks and sounds like: the effect in the hand, charging effects and light at the body, cast effects and light, collision effects, invest/cast sounds, the Light spell |
| `VRSpellThrow` (worker of the caster) | throwable spells: hand velocity, the swing's direction/speed/gravity/homing, the flying `Projectile` and its hits |
| `VRSpellLock` (worker of the caster) | lock-on for throwable spells held in a hand: the rune hand's trigger locks the NPC it points at, keeps it while held, breaks it (down, out of `VRSpellThrow.Range`, out of sight); the only owner of the throw's target |
| `VRTelekinesisSpell` (worker of the caster) | Telekinesis: trigger 1 extends the distance grab, trigger 2 pulls (no Daedalus, no mana) |
| `VRChainLightning` (VR adapter) | G1's chain lightning while invested |
| `VRSpellTransformService` (VR) / `VRSpellTransformPuppet` (VR adapter) | transformation scrolls: the monster puppet, camera and guild swap, transforming back |
| `VRPlayerService` (VR) | spell targeting with the rune hand's distance grab (`GetSpellTarget`), `IsSpellLockActive` (blocks dialogs like targeting), telekinesis range |
| `VRFocus` (VR adapter) | an NPC's highlight and name while hovered or locked (`SetLocked`: brighter, name readable at range) |
| `VRWeaponService` (VR) | a readied rune is `WeaponState.Mage` for the NPCs (`SetRuneReadied`) |
| `VmCacheService` (Core) | spell data, `spellFXInstanceNames` (`TryGetSpellMfxName`), VISUALFX keys, damage per level |
| `HeroSpellStateDomain` (Core) | sleep/freeze on the hero; `IsCastBlocked` (knocked out, asleep, frozen) |
| `FightService` (Core) | what a spell hit does: damage, AOE fan-out, `PERC_ASSESSMAGIC` on the target |
| `StatusBarAdapter` (Core adapter) | the hero's mana bar: shows on `HeroManaBarRequested`, follows `NpcAttributeChanged` |

Constants: `DaedalusConst` (`SplSendCast`/`SplSendStop`/`SplNextLevel`, `ZsMagicFreeze`, `SpellFxInstanceNames`),
`SpellConst` (AOE, summon, charge and spread spell names). Tunables: `VRConstants.Magic` (`MinSpellCastSeconds`,
`SpellThrowSpeedMultiplier`, `SpellThrowFullSwingSpeed`, plus the throw and
homing values `MaxSwingFactor`, `HandVelocityWindow`, `MinHomingSwingFactor`, `MinThrowDirectionSpeed`,
`DroppedSpellSpeed`, `HomingConeDegrees`, `HomingDegreesPerSecond`, `DefaultThrowRange`, and the lock-on values
`MinLockedSwingFactor`, `LockSightCheckSeconds`, `LockLostSightSeconds`, `LockRangeHysteresis`,
`LockFocusBrightness`, `LockNameFullSizeDistance`), the Immersion options "Single-use scrolls" (Gothic.ini
`scrollsSingleUse`), "Thrown spells" (`throwableSpells`) and "Two-hand charge" (`twoHandSpellCharge`), names in
`VRConstants.IniNames`.

## Data flow of one cast

1. A rune is grabbed: `VRVobItem` adds `VRRuneCaster`. `Start` sets the hero's active spell, readies the rune
   (`SetRuneReadied`), creates its workers and raises `HeroManaBarRequested(true)`.
2. The trigger is pressed: aiming starts (`ActivateSpellTargeting`), `VRSpellFx` puts the effect in the hand and the
   charging effects at the body, NPCs notice the caster (`SendHeroCasterPerception`).
3. Every `time_per_mana`: one mana (G1; G2 scripts pay on the cast) and `Spell_ProcessMana(level)` with self = hero,
   other = target (`VmGlobalsScope`). `SPL_NEXTLEVEL` grows the charge, `SPL_SENDCAST` casts, `SPL_SENDSTOP` stops.
4. Let go early: `Spell_ProcessMana_Release` decides whether it casts at the level reached.
5. The cast: `Spell_Cast_<name>`, the cast effects, then `SpellHit` (target, AOE or none) -> `FightService`. A
   scroll is used up (`InventoryTransferService` hero owner or the held stack).
6. The rune is let go: the caster is destroyed, effects end, `HeroManaBarRequested(false)`.

A thrown spell (Gothic.ini `throwableSpells`, VISUALFX CAST key with `TARGET` trajectory): the other hand charges with its
trigger; let go, `VRSpellThrow.Aim` reads the swing and `Throw` sends the hand's effect as a `Projectile`. Its hit
raises `SpellHit`/`SpellSpreadHit` like a cast.

Lock-on (rune or scroll in a hand, not ring spells, not the simulator; decision record
`Docs/adr/2026-11-13-vr-magic-lock-on.md`):

1. The rune hand holds its trigger: `VRSpellLock` extends that hand's distance grab to `VRSpellThrow.Range` and locks
   the first standing NPC it points at (`IsDown()` = dead or knocked out, a frozen NPC stays a target). The far reach
   is dropped again; `VRFocus.SetLocked` keeps the highlight and name until the lock ends, wherever the hands point:
   brighter than a hover (`LockFocusBrightness`), the name growing beyond `LockNameFullSizeDistance` so it reads at
   the spell's range. `PlayerService.FocusNpc` is the target (hovering other NPCs doesn't take it meanwhile),
   `TMAG_INIT` plays, `HeroSpellTargetLocked` makes `VrHapticsService` tick the rune hand.
2. While locked the caster copies `VRSpellLock.Target` into its spell target every frame, so `Spell_ProcessMana`
   gets it as `other`. Pointing elsewhere keeps the lock.
3. It breaks by itself when the target is down or gone, farther than `Range * LockRangeHysteresis`, or out of sight
   for `LockLostSightSeconds` (`NpcHelperService.HasFreeSight`, Gothic's sight rule: leaves and bushes don't block;
   checked every `LockSightCheckSeconds`). Then `HeroSpellTargetLockLost` vibrates, and the trigger must be let go
   before it locks again. Letting go of the trigger or the rune unlocks quietly.
4. A release while locked: `Aim` points at the target's `GetAimPoint()`, no gravity, the swing sets the speed (at
   least `MinLockedSwingFactor`), and `Throw` homes on the target at `HomingDegreesPerSecond` instead of searching
   the homing cone.

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change when a spell charges, casts or stops | `VRRuneCaster` (`Update`, `UpdateHoldToCast`, `TryInvestMana`, `FinalizeCast`) |
| change how a spell looks or sounds | `VRSpellFx` (VISUALFX keys, lights, sounds) |
| change throwing (swing, homing, flight) | `VRSpellThrow` |
| change the lock-on (acquire, break, feedback) | `VRSpellLock`, its highlight in `VRFocus`, its vibrations in `VrHapticsService` |
| change telekinesis | `VRTelekinesisSpell`, its grab in `VRPlayerService` |
| change what a hit does (damage, AOE, reactions) | `FightService`, `VmCacheService.GetSpellDamagePerLevel` (Core, not VR) |
| change when the hero can't cast | `HeroSpellStateDomain.IsCastBlocked` (Core) |
| change the mana bar | `StatusBarAdapter` (Core) |
| change transformations | `VRSpellTransformService`, `VRSpellTransformPuppet` |
