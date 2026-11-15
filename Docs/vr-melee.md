# VR melee

How the hero fights with his real hands in VR: sword swings, fists, parrying and the stab finish. Gothic's rules
(damage, finishing moves, who reacts) stay in Core; VR only turns hand movement into Gothic's events.
Why it's built this way: Docs/adr/2026-11-02-vr-melee.md. The NPC side of fights is in `Docs/npc-ai.md`.

## Who owns what

| Class | Owns |
|---|---|
| `VRWeaponService` (VR) | the hero's weapons: two `VrWeaponAttackDomain` (swing, attack window, combo), `VrFistDomain`, `VrStabFinishDomain`; folds them into the hero's weapon state (`UpdateHeroWeaponState`) |
| `VrWeaponAttackDomain` | one held melee weapon: the attack window, the combo, the swing sound, the hero's prehit (`HeroMeleeSwing`) |
| `VrSwingSampler` | owned by `VrWeaponAttackDomain`: measures the held weapon in tracking space and decides when a swing counts; blade axis and hand velocity for the stab finish |
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

1. The player swings. `VrSwingSampler` sees a swing (see below), `VrWeaponAttackDomain` opens the attack window.
2. The weapon's trigger enters an NPC (`VRWeaponAttackAdapter`; a fist: `VRFistHitbox` -> `VrFistDomain`). In the
   attack window it raises `FightHit(attacker, target, position, weapon)`. The weapon is the held item's
   `ItemInstance`, null for a fist.
3. `FightService` takes the hit. It checks blocks (the NPC's parry/jump back, the hero's block via
   `VRContextHeroBlockService`), then the finishing-move rule, then `MeleeDamageDomain` with the weapon's damage.
4. `VRWeaponService` hears `FightHit` and moves the swinging weapon's attack state on (waiting for a combo).
5. `VrHapticsService` hears the fact events (`FistSwung`, `FistPunchLanded`, `HeroBlockStarted`, `HeroBlockedHit`,
   `StabStuck`, `StabHeld`, `StabFinished`) and vibrates the hand.

## When a swing counts

A swing is measured in the rig's tracking space (`IVrRig.TrackingSpace`), from the tracked controllers - not from the
physics weapon and not in world space. Walking, sprinting, gravity, ground snapping, turning and teleports move the
tracking space itself, so they never count. (Before, the run speed alone passed the threshold: the character
controller's velocity is only its last `Move()` of a frame.)

`VrSwingSampler` samples every rendered frame (`VRUnityLifecycleEvents.Update` -> `VRWeaponService.Update`) into a
fixed ring buffer; the attack window logic stays on `FixedUpdate` and reads the latest values. A swing needs, all at
once:

- hand (handle) speed and blade tip speed over their thresholds - hand speed from `IVrInput.GetControllerVelocity`
  (the faster holding hand), the tip from the tracked hand pose plus the tip's offset in that hand,
- both held for `MinTimeOver`,
- the tip travelled `MinTipPath` within `PathWindow`.

A thrust counts too: hand speed along the blade over `ThrustSpeed` and nearly all of the hand's speed
(`Melee.StabThrustAlignment`); its path is the hand's. The tip (the far end of the weapon mesh along its longest axis,
away from the hand) is fixed in the hand 0.15 s after the grab - the weapon flies into the hand first - and again when
the first holding hand lets go of a two-handed hold. Until then no swing counts. Samples are dropped on grab, release
and hand change. The combo (drop below and back over) runs on the tip speed.

Defaults (prefab `VR - Player XRI`, `VRPlayerWeaponInteraction`, tunable in Marvin): 1H hand 1.2 / tip 3.0 m/s, 2H hand
1.0 / tip 2.5 m/s, 0.06 s over, 0.35 m tip path in 0.2 s, thrust 2.5 m/s. Marvin changes apply on the next grab. The
`[VrSwing]` editor log shows the values that started each attack. Fists keep their own rule (`VrFistDomain`,
`Melee.FistPunchVelocity`).

## Stab finish and blocks

A stab finish: the blade enters an unconscious NPC, `VRWeaponService.TryStabFinish` hands it to
`VrStabFinishDomain`. It reads the held weapon's `VrSwingSampler` (`VRWeaponService.TryGetSwing`): a thrust
(`IsThrust(Melee.StabThrustSpeed)`, slower than a thrust attack on purpose) sticks the blade, the sampler's blade axis
tells pulling out from pushing in. Kept in for 2 s it raises `FightFinishingMove`, the same event as an NPC's `AI_FinishingMove`.

A block: `VrHeroBlockDomain` sets the hero's block props every frame and calls `FightService.ReportBlockContact` for
each NPC weapon touching the held one; an NPC's hit then asks `VRContextHeroBlockService` whether it is blocked.

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change when a swing counts | `VrSwingSampler` (thresholds: `VRPlayerWeaponInteraction` prefab fields -> `WeaponPhysicsConfig` -> `VrSwingThresholds`) |
| change the attack window or combo | `VrWeaponAttackDomain` |
| change the fist rules | `VrFistDomain`, tunables in `VRConstants.Melee` |
| change the stab finish | `VrStabFinishDomain`, `VRConstants.Melee`; Gothic.ini `stabFinish` switches it |
| change what a block is | `VrHeroBlockDomain` (props, contacts), `VRContextHeroBlockService` (the rule) |
| change damage or finish decisions | `FightService`, `MeleeDamageDomain` (Core, not VR) |
| change which hit vibrates how | `VrHapticsService` |
| change a weapon's hitboxes | `VobItemFittedColliderCacheCreatorDomain` (then bump the cache version) |
