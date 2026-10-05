# 5. VR hero: body, IK, items in hand, docs and maps

[← Overview](README.md)

## What changed

**Hero body**
- The hero's armor (torso, legs, arms) is drawn under the VR head; arms reach the HVR hands with a stretching
  two-bone IK, the wrist rolls with the hand; legs/torso play Gothic's walk/run/strafe/turn animations.
- Hands are cut out of the mesh, sleeves rolled up over the bare forearm. Armor changes swap the body.
- Body states for scripts: `BS_WALK`/`BS_RUN` from movement, `BS_SNEAK` when crouched.

**Items in hand**
- Equip armor, amulets and rings in VR (both hands), protection and effects from the scripts.
- Pouches open with both hands; food eaten at the VR mouth (stacks decrement); swampweed joints smoked at the mouth
  (puffs, smoke); torches lit/put out with the trigger, with a moving light.
- Speed potions speed up VR movement; sealed letters are used up when read.
- Teleports put the held rune/items into the backpack. The unconscious hero can't attack or cast; he drops melee
  weapons when knocked out.

**Docs and maps**
- `Doc_*` externals: maps, letters and books in a VR viewer in the hand. Pages are no longer upside down.
- Hero arrow on the world map, turning with the hero: G2 from `Doc_SetLevelCoords`, G1 from bounds calibrated in VR
  (`G1WorldMapBounds`), because G1 maps carry no coordinates.

## Key commits

`7227eb60`/`1e7389ac`/`05dfa9f7`/`434432cd` hero body · `84a84753` equip · `c62882ff` pouches · `567fc20d`/`bdda2d2a`
food · `f49f47e6`/`b0703e75`/`9edce695` smoking · `ea038cb0` torch · `adc2067a` quick-win externals ·
`4df98e0a` Doc_* · `1fb8180a`/`76f2eace` maps · `980feedf`/`2fa62aa7` knocked-out hero

## Main files

`Gothic-VR/Adapters/Player/VRHeroBody.cs` (+768) · `VRHeroBodyAnimator.cs` · `VRSwimDive.cs` ·
`VobItem/VRItemUser.cs` · `VRMouth.cs` · `VRTorch.cs` · `VRDocViewer.cs` · Core `Services/DocService.cs`,
`Models/Doc/DocModel.cs` · `HVROverrides/VRPlayerController.cs`

## Config flags

`EnableVrHeroBody`, `EnableHeroMoveBodyState`, `EnableHeroSneakBodyState`, `EnableEquipItems`,
`EnableScriptEquipEffects`, `EnableSmoking`/`SmokePuffs`/`SmokeStartDelay`, `EnableVrTorch`,
`EnableQuickWinExternals`, `EnableDocConsumeOnClose`, `EnableMapPlayerMarker`, `G1WorldMapBounds`.

## Review notes

- ✅ Body animation reuses the hero's Gothic animations through the existing animation system.
- ⚠️ `G1WorldMapBounds` is a hand-calibrated constant (two sample points). Correct for the vanilla G1 map; a mod with
  its own map would need its own value.
- ⚠️ `VRHeroBody.cs` (768 lines) mixes mesh cutting, IK and slot handling — candidate for splitting.

## How to test

- Look down: armor body, arms follow the controllers. Change armor.
- G1: open the colony map at the start and in the Swamp Camp — the arrow sits on you and turns with you.
- Light a torch in a cave; smoke a joint.
