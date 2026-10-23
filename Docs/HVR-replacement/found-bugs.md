# Bugs found during the HVR replacement

Bugs that showed up while implementing and testing the phases. None of them is caused by the XRI backend; they
exist in shared code (some on HVR as well). Kept here instead of tickets so they travel with the feature branch.
Move them to GitHub issues when the branch is merged, if they are still open.

## Inventory: picking up a world item adds 0 (`Amount = 0`)

- **Seen:** Phase 2a, again in 3b. Both VR backends.
- **What:** `VRPlayerService.SetGrab` adds `vobItem.Amount` items, and every world item checked has `Amount = 0`. With
  `Amount = 1` set at runtime the counting works (+1 on pickup, two-hand grab counts once, drop removes it).
- **Possible fix:** `Math.Max(1, Amount)` in `VRPlayerService`, or set `Amount` when the vob is created. Check that
  stacks (arrows, gold, ore) still get their real amount.
- **Test:** pick up a torch/beer at the G1 spawn and a stack of ore nuggets; open the backpack; drop them again.

## Speech-to-text throws without a microphone

- **Seen:** Phase 1d (simulator trigger started speech-to-text).
- **What:** `IndexOutOfRangeException` in `SpeechToTextHandler.GetMicrophoneDeviceName` when no microphone exists.
- **Expected:** one `Logger.LogWarning(..., LogCat.VR)` and speech-to-text disabled, no exception.

## Eating a held item throws `MissingReferenceException`

- **Seen:** Phase 2b.
- **What:** `VobMeshCullingDomain.StopTrackVobPositionUpdatesDelayed` (around line 539) still references the vob
  after it was destroyed by eating it while held. Not backend specific.
- **Possible fix:** destroyed-check, or cancel the delayed call when the vob is destroyed.
- **Test:** hold berries to the mouth; no exception, nothing keeps being tracked.

## "Couldn't find object in Culling list" when grabbing runtime-spawned chest/loot items

- **Seen:** Phase 3c.
- **What:** grabbing an item that was spawned at runtime into a chest slot or an NPC loot slot logs
  `Couldn't find object in Culling list ... (Loader)`, from `VRVobItem` -> `StartTrackVobPositionUpdates`. Shared
  code of both backends, so HVR very likely has it too.
- **Related, HVR only so far:** closing NPC loot once logged `Cannot set the parent of 'ITMW_1H_SWORD_04 (Loader)'
  while it is being destroyed`.
- **Maybe related:** the existing GitHub issue "Chest -> Backpack - throwing exception".
- **Test:** take an item out of a chest; loot a dead NPC, take an item, close the panel; no errors.

## Misc
1. Head never sticks into the wall as it is blocked from going into (poking back)
1. Marvin menu is rotated wrong (fixed later, when we have a hand and actually see, where it should be placed).

## Known, not new

- Chests with more than 12 items: pagination is missing (an error is logged, as before).
- Taking items from a chest doesn't change the chest's saved contents.
- Chest slot spheres overlap on small chests (same as with HVR).
