# VR help panels

Clicking the Gothic logo in the VR menu toggles three panels above the menu ring (`GothicLogoHandler` ->
`Tutorials` GameObject). Decision record: [2026-11-13-vr-help-panels](adr/2026-11-13-vr-help-panels.md).

| Panel | Where | Content |
|---|---|---|
| `Quest2` (controls) | above the status menu | controller drawing, labels follow the control layout |
| `Hero Slots` | above the main menu | hero outline, the 4 body slots (both hips, backpack left shoulder, holster right shoulder) |
| `Help` | above the quest log | tabs left (`VRHelpTabs`), one page per topic right: controller drawing + text |

## Files

- `Gothic-VR/Resources/VR/Prefabs/UI/Tutorials.prefab` - the three panels and their placement. Instances: player
  prefab `MenuHandler/Tutorials` (in game) and `MainMenu.unity` (main menu: panels beside the menu at eye level,
  `Help` inactive, placement overridden there).
- `Gothic-VR/Resources/VR/Prefabs/UI/ControllerHelpPage.prefab` - the controller drawing with one label per stick,
  stick click and button of both hands (`VRControllerHelpPage`). Used by `Quest2` and every help tab.
- Texts: string table `Gothic-UI` (`Gothic-Core/Localizations/`), keys `vr.controls.*` and `vr.help.*`, EN and DE.

## Placement

`MenuHandler` is scaled (0.35, 0.35, 1). The `Tutorials` root scales z by 0.35 so the panels have a uniform world
scale - otherwise yaw and tilt shear the panels. Hence the panels' local z is divided by 0.35 (-2.857 = 1 m towards
the player) and the side panels use the world yaw of the menus below (±49.8°, which is what their ±22.5° become
under the non-uniform scale). All three tilt -15° around x: the top edge leans towards the player.

## Labels follow the control layout

`VRControllerHelpPage.OnEnable` reads `IVrInput.ControlLayout` (default layout when not injected) and asks
`VrControlLayout.GetRole(side, slot)` for each stick and face-button label:

- The role's text from `_roleTexts` is shown; on a stick click the entry's `ClickText` ("Click: Jump") if set.
- `VrControlRole.None` or no entry: the label and its line are hidden.
- Trigger and grip labels show `_triggerText` / `_gripText` on both hands; empty hides them.

Changing a button while the panels are open shows on the next open.

## Adding a help tab

1. Add keys `vr.help.<topic>.tab`, `.text` and, if needed, `.trigger`, `.grip` (EN + DE) to `Gothic-UI`.
2. In `Tutorials.prefab` under `Help`: duplicate a `Tabs/Tab <topic>` button and a `Pages/Page <topic>`, point
   their `LocalizeStringEvent`s and the page's `ControllerHelpPage` texts at the new keys.
3. Append the button to `VRHelpTabs._tabs` and the page to `_pages` (same order). With more than 4 tabs, move the
   buttons closer (they are 1.1 units apart).
