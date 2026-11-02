# 5. VR hero: body, IK, items in hand, docs and maps

[← Overview](README.md)

<!-- toc -->
## Contents

- [What changed](#what-changed)
  - [Hero body](#hero-body)
  - [Items in hand](#items-in-hand)
  - [Docs and maps](#docs-and-maps)
  - [Added 2026-10-10 – 2026-10-11](#added-2026-10-10--2026-10-11)
  - [Added 2026-10-11 – 2026-10-13](#added-2026-10-11--2026-10-13)
  - [Added 2026-10-15](#added-2026-10-15)
  - [Added 2026-10-24](#added-2026-10-24)
  - [Added 2026-10-25 (evening)](#added-2026-10-25-evening)
  - [Added 2026-10-26](#added-2026-10-26)
- [Key commits](#key-commits)
- [Main files](#main-files)
- [Config flags](#config-flags)
- [Review notes](#review-notes)
- [How to test](#how-to-test)
<!-- /toc -->

## What changed

### Hero body

- The hero's armor (torso, legs, arms) is drawn under the VR head; arms reach the HVR hands with a stretching
  two-bone IK, the wrist rolls with the hand; legs/torso play Gothic's walk/run/strafe/turn animations.
- Hands are cut out of the mesh, sleeves rolled up over the bare forearm. Armor changes swap the body.
- Body states for scripts: `BS_WALK`/`BS_RUN` from movement, `BS_SNEAK` when crouched.

### Items in hand

- Equip armor, amulets and rings in VR (both hands), protection and effects from the scripts.
- Pouches open with both hands; food eaten at the VR mouth (stacks decrement); swampweed joints smoked at the mouth
  (puffs, smoke); torches lit/put out with the trigger, with a moving light.
- Speed potions speed up VR movement; sealed letters are used up when read.
- Teleports put the held rune/items into the backpack. The unconscious hero can't attack or cast; he drops melee
  weapons when knocked out.

### Docs and maps

- `Doc_*` externals: maps, letters and books in a VR viewer in the hand. Pages are no longer upside down.
- Hero arrow on the world map, turning with the hero: G2 from `Doc_SetLevelCoords`, G1 from bounds calibrated in VR
  (`G1WorldMapBounds`), because G1 maps carry no coordinates.

### Added 2026-10-10 – 2026-10-11

- **Personal space:** NPC bodies are pushed out to 1 m from the VR hero's camera (position only, AI unchanged) - orc
  attack animations no longer go into the camera and push the hands.
- **Touchplates and other mobs with the mesh on a child bone** can be hovered and used: their grab colliders are
  collected again once the mesh exists, child colliders get `HVRGrabbableChild`.
- Items without a `VobLoader` (backpack/hot slot items after a world change) can be grabbed again.

### Added 2026-10-11 – 2026-10-13

- **Talking works again:** the personal space around the VR hero (NPC bodies 1 m from the head) leaves out humans with
  their weapon away - NPCs were out of the hands' reach and none could be grabbed to talk. Monsters and anyone with a
  drawn weapon keep their distance.
- **Turning while knocked out:** the hero can't walk but can still turn with the stick (snap/smooth turn) - seated
  players couldn't look around while down.
- **Fists** (see [Combat](01-combat.md)): grip + trigger on an empty hand.

### Added 2026-10-15

- **Skin colored hands:** the `VRHands` material gets the hero's arm skin tint (`HandSkinTint`); lights and the sky
  colored ambient tint it only 30% (`HandLightInfluence`), emission with the skin texture carries the rest, no
  highlights or reflections (matte, not waxy). Fully lit they turned navy at night, red in the evening.

### Added 2026-10-24

- **Slower sprint while sneaking** (`0ca89b3b`, `EnableSneakSlowSprint`, opinion): `VRPlayerController.
  HandleHorizontalMovement` halves HVR's `RunSpeed` for the frame while the hero sprints in `BS_SNEAK` (crouched).

### Added 2026-10-25 (evening)

- **Grab at the grip** (`82f127cd`, `EnableWeaponGripGrab`): our items have no grab points, so HVR's physics poser
  (`DynamicGrab` -> `FindClosestPoint` over the enabled colliders) closed the hand on the collider nearest to the palm -
  out of a holster, the backpack or a force grab often the blade or the guard. Gothic's weapon meshes have their origin
  where NPCs hold them (`ZS_RIGHTHAND`). `VRWeaponGripGrab` (`BeforeGrabbed` of the first hand on an `ITEM_KAT_NF`/`FF`):
  the fitted collider the origin lies deepest in (local bounds, no physics query) stays, the others are disabled until
  `Grabbed` (`LateUpdate` restores a cancelled grab). The second hand of a two-handed weapon grabs where it is.
- **Items pass through backpacks** (`878b276f`, `6c941ebb`, `EnableBackpackItemPassThrough`): a held potion or sword
  pushed the hero's backpack (and NPCs' loot backpacks). Items share the `VobItem` layer with the mobs' interaction
  parts (a torch holder's torch, beds, anvils), so no layer can split them: `VRBackpackItemPassThrough` (added by
  `VRBackpack`) lets every item collider near the backpack ignore its solid colliders - hull and the items shown in its
  sockets, collected again every 0.5 s - pair by pair (`Physics.IgnoreCollision`; a pair is forgotten once a collider is
  disabled, so it's checked again). The world and vobs still collide, the socket triggers stay.

### Added 2026-10-26

- **Torch holders** (`df649780`): the grip grab's item cast broke grabbing a torch from its holder.

## Key commits

`7227eb60`/`1e7389ac`/`05dfa9f7`/`434432cd` hero body · `84a84753` equip · `c62882ff` pouches · `567fc20d`/`bdda2d2a`
food · `f49f47e6`/`b0703e75`/`9edce695` smoking · `ea038cb0` torch · `adc2067a` quick-win externals ·
`4df98e0a` Doc_* · `1fb8180a`/`76f2eace` maps · `980feedf`/`2fa62aa7` knocked-out hero

2026-10-10/11: `a3a7209b` personal space · `8a8e717e` mob child colliders · `a2df3346` grab after world change

2026-10-11/13: `51f2be43` personal space vs talking · `d5731bdb` turning while knocked out · `87665888` fists

2026-10-15: `367d7f78` skin colored hands

2026-10-24: `0ca89b3b` slower sprint while sneaking

2026-10-25: `82f127cd` grab at the grip · `878b276f` `6c941ebb` items pass through backpacks
2026-10-26: `df649780` torch holder grab

## Main files

`Gothic-VR/Adapters/Player/VRHeroBody.cs` (+768) · `VRHeroBodyAnimator.cs` · `VRSwimDive.cs` ·
`VobItem/VRItemUser.cs` · `VRMouth.cs` · `VRTorch.cs` · `VRDocViewer.cs` · Core `Services/DocService.cs`,
`Models/Doc/DocModel.cs` · `HVROverrides/VRPlayerController.cs`

## Config flags

`EnableVrHeroBody`, `EnableHeroMoveBodyState`, `EnableHeroSneakBodyState`, `EnableEquipItems`,
`EnableScriptEquipEffects`, `EnableSmoking`/`SmokePuffs`/`SmokeStartDelay`, `EnableVrTorch`,
`EnableQuickWinExternals`, `EnableDocConsumeOnClose`, `EnableMapPlayerMarker`, `G1WorldMapBounds`.

2026-10-10/11: `EnableHeroPersonalSpace`, `EnableMobColliderRefresh`

2026-10-11/13: `EnableKnockoutTurning`, `EnableVrFists`

2026-10-15: `HandLightInfluence`, `HandSkinTint`

2026-10-24: `EnableSneakSlowSprint`

2026-10-25: `EnableWeaponGripGrab`, `EnableBackpackItemPassThrough`

## Review notes

- ✅ Body animation reuses the hero's Gothic animations through the existing animation system.
- ⚠️ `G1WorldMapBounds` is a hand-calibrated constant (two sample points). Correct for the vanilla G1 map; a mod with
  its own map would need its own value.
- ⚠️ `VRHeroBody.cs` (768 lines) mixes mesh cutting, IK and slot handling — candidate for splitting.

## How to test

- Look down: armor body, arms follow the controllers. Change armor.
- G1: open the colony map at the start and in the Swamp Camp — the arrow sits on you and turns with you.
- Light a torch in a cave; smoke a joint.
- Let an orc attack you up close: its body stays 1 m away, hits still land.
- G2 Irdorath: the stone touchplates in the side rooms and on the pillar highlight and react.

- Walk up to any citizen and grab him to talk; orcs/wolves and NPCs with drawn weapons still keep 1 m away.
- Get knocked out (VR): the right stick turns you, walking/teleport is locked until you stand up.
- Skip time to night and evening, enter a cave: the hands stay skin colored and matte, only slightly tinted.
