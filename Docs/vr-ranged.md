# VR ranged

How the hero shoots bows and crossbows with his real hands in VR: a bow is drawn by its string, a crossbow is aimed
with the weapon and reloaded by pulling its string back. Gothic's rules (munition, flight, damage, missed arrows lying
in the world) stay in Core and are the same for NPC archers; VR only turns hand movement into a shot.
Why it's built this way: Docs/adr/2026-11-02-vr-ranged.md. The melee side is in `Docs/vr-melee.md`.

## Who owns what

| Class | Owns |
|---|---|
| `VRRangedService` (VR, player scope) | the ranged rules of the hero in VR: who shoots a crossbow one-handed (`IsOneHandedCrossbow`), access to munition and shots |
| `ProjectileService` (Core) | munition of a weapon (1 item per shot), the flying arrow/bolt, what happens when it lands (`RangedHit`, or it lies in the world) |
| `FightService` / `RangedHitDomain` (Core) | the damage of a hit, the weapon requirement (`CheckHeroWeaponRequirement`) |
| `VRVobItem` (VR adapter) | adds `VRBow`/`VRCrossbow` on the first grab of a ranged item, switches them with `SetHeld` while any hand holds it |
| `VRBow` (VR adapter) | a held bow: drawing with the free hand's grip, the nocked arrow, the shot (speed and damage grow with the draw), our string |
| `VrBowMeshCalibration` | once per bow: limb tips, grip, max draw, the mesh copy without Gothic's string, the `S_SHOOT` draw frames |
| `VRCrossbow` (VR adapter) | a held crossbow: trigger shot, the bolt on the stock, manual or automatic reload |
| `VRCrossbowString` (VR adapter) | stays on a crossbow: its cocked state and shape (`S_RELAX` blended to the rest mesh) |
| `VRWeaponService` (VR) | folds a held bow/crossbow into the hero's weapon state (`UpdateHeroWeaponState`: melee > ranged > fist) |

Tunables: the one-handed crossbow rule in `FightConst`, sounds and morph animation names in `Constants`, the
Immersion option "Crossbow" (Gothic.ini `crossbowReload`) in `VRConstants`. Bow and crossbow colliders are fitted while
pre-caching (`VobItemFittedColliderCacheCreatorDomain`), like melee weapons.

## Data flow of one shot

1. A hand grabs a bow. `VRVobItem` asks `ItemInstanceExtension.GetRangedWeaponState`, adds `VRBow` once and calls `SetHeld(true)`.
   The first time, `VrBowMeshCalibration` reads the mesh; the bow shows the copy without the Gothic string and ours.
2. `VRWeaponService.UpdateHeroWeaponState` reports Bow (CBow) to the NPCs (`PERC_DRAWWEAPON` and friends).
3. The free hand's grip at the string starts the draw; an arrow is nocked if the hero has one. The limbs bend with
   the `S_SHOOT` frames.
4. Let go: `VRBow` checks tension and requirements, consumes the arrow and calls `VRRangedService.Shoot` ->
   `ProjectileService`. A hit raises `RangedHit`, `FightService` applies the damage; a miss lies in the world.
5. The last hand lets go: `SetHeld(false)` drops a nocked arrow and brings back the Gothic mesh.

A crossbow: the trigger of a holding hand fires (both hands needed below the one-hand rule). Then the string sits at
the prod; the free hand's trigger pulls it back along the stock until it locks. Out of a holster or the backpack a
crossbow is cocked again. With "Crossbow" off it reloads by itself after 1.5 s.

## Where does my change go?

| You want to ... | Go to |
|---|---|
| change which items are bows/crossbows | `ItemInstanceExtension.GetRangedWeaponState` (Core, item flags `ITEM_KAT_FF`, `ITEM_CROSSBOW`; NPCs, colliders and the VR hero use it) |
| change drawing, the arrow, the bow's shot | `VRBow` |
| change how a bow mesh is read (tips, grip, Gothic string) | `VrBowMeshCalibration` |
| change crossbow shot or reload | `VRCrossbow`, its shape in `VRCrossbowString` |
| change who shoots a crossbow one-handed | `VRRangedService.IsOneHandedCrossbow`, `FightConst` |
| change munition, flight or damage | `ProjectileService`, `RangedHitDomain` (Core, not VR) |
| change a bow's hitboxes | `VobItemFittedColliderCacheCreatorDomain` (then bump the cache version) |
