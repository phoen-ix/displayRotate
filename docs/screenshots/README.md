# Screenshots

Every window DisplayRotate shows, made by the docs tour (`tests/DisplayRotate.DocsTests`). The
README's [Screenshots](../../README.md#screenshots) section says how to make them again.

## Tray menu

Click the tray icon, left or right. Each display is listed with its resolution and its four
orientations, and the current one is ticked. Below them are **Identify Displays**,
**Settings...**, **Check for updates** and **Exit**.

<img src="tray-menu.png" alt="The tray menu: display 1 with its resolution, the four orientations with 0° ticked, Identify, Identify Displays, Settings..., Check for updates and Exit">

## Tray menu with an update

Once a check has found a newer release, **Install DisplayRotate X.Y.Z…** heads the menu.

<img src="tray-menu-update.png" alt="The tray menu with Install DisplayRotate 0.2.0… in bold at the top">

## Settings

**Settings...** in the tray menu: general options, the hotkeys (click a hotkey's box and press
the new combination), and the update choice. **Save** keeps the changes to the options and
hotkeys, **Cancel** drops them.

<img src="settings.png" alt="The Settings dialog: General options, four Ctrl+Alt+arrow hotkeys for the primary display, and Updates set to Only when I ask, never checked">

## Settings with auto-restore

With **Auto-restore display orientations on startup** ticked, each display gets a row: its name,
an **Identify** button, and the orientation DisplayRotate puts back every time it starts.

<img src="settings-auto-restore.png" alt="The Settings dialog with auto-restore ticked: a row for display 1 with an Identify button and 0° (Landscape) chosen, above the hotkeys">

## Settings with an update

After **Check now** has found a newer release, **Update to X.Y.Z** downloads, verifies and
installs it ([docs/updating.md](../updating.md)). A portable copy shows **Open download page**
instead.

<img src="settings-update.png" alt="The Settings dialog with Check once a day selected, an Update to 0.2.0 button and the status DisplayRotate 0.2.0 is available (you have 0.1.1)">

## Identify overlay

**Identify** (one display) or **Identify Displays** (all of them) covers the screen with a
75%-opaque black layer showing the display's number, for the time set in Settings. A click or a
key closes it early. The picture shows the overlay without the desktop behind it.

<img src="identify-overlay.png" alt="The Identify overlay: a large white 1 on black" width="480">
