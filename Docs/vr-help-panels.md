# VR help panels

Clicking the Gothic logo in the VR menu swaps the menu ring for three help panels at eye level; clicking it again
brings the menus back (`GothicLogoHandler` raises `GlobalEventDispatcher.GothicLogoClicked`, `TutorialHandler`
switches). Decision records: [2026-11-13-vr-help-panels](adr/2026-11-13-vr-help-panels.md),
[2026-11-22-vr-help-panels-swap](adr/2026-11-22-vr-help-panels-swap.md).

| Panel | Where | Content |
|---|---|---|
| `Quest2` (controls) | in place of the status menu | controller drawing, labels follow the control layout |
| `Hero Slots` | in place of the open Gothic menu | hero outline, the 4 body slots (both hips, backpack left shoulder, holster right shoulder) |
| `Help` | in place of the quest log | tabs left (`VRHelpTabs`), one page per topic right: controller drawing + text |

## Files

- `Gothic-VR/Resources/VR/Prefabs/UI/Tutorials.prefab` - the three panels and their placement. Instances: player
  prefab `MenuHandler/Tutorials` (in game) and `MainMenu.unity` (main menu, same placement and swap).
- `Gothic-VR/Resources/VR/Prefabs/UI/ControllerHelpPage.prefab` - the controller drawing with one label per stick,
  stick click and button of both hands (`VRControllerHelpPage`). Used by `Quest2` and every help tab.
- Texts: string table `Gothic-UI` (`Gothic-Core/Localizations/`), keys `vr.controls.*` and `vr.help.*`, EN and DE.

## Placement

`MenuHandler` is scaled (0.35, 0.35, 1). The `Tutorials` root scales z by 0.35 so the panels have a uniform world
scale - otherwise yaw shears the panels. Hence the panels' local z is divided by 0.35 (-2.857 = 1 m towards the
player) and the side panels use the world yaw of the menus they replace (±49.8°, which is what their ±22.5° become
under the non-uniform scale). All three stand at y 0 like the menus, no tilt.

## Swap

`TutorialHandler` owns the state; it reacts only to clicks on its own `_logo` (main menu and player logo may
coexist). The `Tutorials` root stays active, `_panels` are switched.

- Show: `_panels` on; `_hiddenWhileShown` (player prefab: `StatusMenu`, `QuestMenu`) and the open Gothic menu
  (`MenuHandler.CurrentMenu`; asked on click, as `MenuHandler` opens `MENU_MAIN` before the panels' `Awake`) off.
  The logo stays visible and is the only way back.
- Hide (logo again): panels off, the hidden menus on.
- The next `MenuOpened` while shown (back button in a sub menu, or the menu reopened after a close, which opens
  `MENU_MAIN`): panels off, `_hiddenWhileShown` on; `MenuHandler` already activated the new menu. Nothing listens
  to `MenuClosed`.
- Main menu scene: the same swap; `_hiddenWhileShown` is empty there (no side menus).

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
3. Append the button to `VRHelpTabs._tabs` and the page to `_pages` (same order). The buttons are 1.1 units apart
   from y 3.0 down (5 tabs: swim, trade, magic, ranged, ring end at y -1.4); the headline starts at y -4.4, so up to 7
   tabs fit before they need to move closer.
