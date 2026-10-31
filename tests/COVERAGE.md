# E2E coverage: displayRotate

Extracted 2026-10-31 from `src/DisplayRotate` (68 items: windows, menu items, labelled controls,
event handlers, balloons, message boxes, CLI commands, update outcomes):

```sh
grep -rnoE --include=*.cs --exclude-dir=obj 'class \w+ : (Form|ApplicationContext|NativeWindow)|new ToolStripMenuItem\(\$?"[^"]*"|ShowBalloon\("[^"]*"|MessageBox\.Show\(|Text = "[^"]+"|case "[^"]+"|CheckOutcome\.\w+ =>|(Click|CheckedChanged|KeyDown|Tick|MouseClick|BalloonTipClicked) \+=' src/DisplayRotate
```

Every row below names the source lines (file:line, at the commit that added this file) it
covers. Tests live in `tests/DisplayRotate.DocsTests` (Windows only); `scripts/e2e.sh` runs all
of them except the docs tour (`Category=Docs`). There is one role (the signed-in user) and no
language switch or theme. Reasons for ❌: (a) needs hardware, (b) irreversible external effect,
(c) covered at another layer, (d) cannot be driven, (e) time box.

**Count: 57 rows: ✅ 46 · ❌ 11 (a 4, b 1, c 4, d 2, e 0).**

## Breadth

| Row | Source | Test |
| --- | --- | --- |
| Every window in every state opens, says what it is, throws nothing: tray menu, tray menu with an update, Settings (plain, auto-restore, update available, checks off by policy, installing), Identify overlay | SettingsForm.cs:7, OverlayForm.cs:3, TrayApp.cs:8 | ✅ `Sweep › EveryWindowOpensAndSaysWhatItIs` (8 cases) |
| Every visible Settings input has a name for screen readers | SettingsForm.cs:57-160 | ✅ `Sweep › EveryWindowOpensAndSaysWhatItIs` (Settings cases) |

## Tray app (TrayApp)

| Row | Source | Test |
| --- | --- | --- |
| Menu: each display (number, name, Primary, resolution), its four orientations with the current one ticked, Identify, Identify Displays, Settings..., Check for updates, Exit | TrayApp.cs:346, 356, 360, 364, 373 | ✅ `TrayJourneys › TheMenuListsEveryDisplayWithItsOrientationsAndTicksTheCurrentOne` |
| Left click on the icon opens the menu; any click rebuilds it | TrayApp.cs:40 | ✅ `TrayJourneys › ALeftClickOnTheIconOpensTheMenu`, `CheckForUpdatesAnswersInABalloon` (right click) |
| Orientation item: picking the ticked one changes nothing, menu rebuilt | TrayApp.cs:334 | ✅ `TrayJourneys › ClickingTheTickedOrientationKeepsItAndRebuildsTheMenu` |
| Orientation item: picking another one turns the display | TrayApp.cs:334 | ❌ (a) turns a real screen; a CI runner's virtual display is the only one there, and DisplayManager talks to the driver directly (no simulator) |
| Rotation the driver refuses: message box | TrayApp.cs:338 | ❌ (a) needs a display that rejects an orientation |
| Identify (one display): its number on its screen, gone after the set duration | TrayApp.cs:349, OverlayForm.cs:22 | ✅ `TrayJourneys › IdentifyShowsTheDisplaysNumberOnItsScreenForTheSetDuration` |
| Identify Displays: every screen numbered; a click or a key closes an overlay | TrayApp.cs:357, OverlayForm.cs:25, 26 | ✅ `TrayJourneys › IdentifyDisplaysNumbersEveryScreenAndAClickOrAKeyClosesIt` |
| "Show Identify buttons" off: no per-display Identify | TrayApp.cs:346 | ✅ `TrayJourneys › WithoutIdentifyButtonsOnlyIdentifyDisplaysIsLeft` |
| Settings...: saved changes apply at once, hotkeys off while open and back after | TrayApp.cs:361 | ✅ `TrayJourneys › SettingsSavedFromTheMenuTakeEffectAtOnce` |
| Settings...: Cancel changes nothing | TrayApp.cs:361 | ✅ `TrayJourneys › CancelledSettingsChangeNothing` |
| Check for updates: up to date / unreachable / newer, as a balloon; settings stamped | TrayApp.cs:368, Updater.cs:131-132 | ✅ `TrayJourneys › CheckForUpdatesAnswersInABalloon` (3 cases) |
| Install DisplayRotate X… item (bold, top) after a check found one | TrayApp.cs:299 | ✅ `TrayJourneys › CheckForUpdatesAnswersInABalloon` (newer) |
| Check for updates greyed out when policy turns checks off | TrayApp.cs:364, Updater.cs:134 | ✅ `TrayJourneys › ChecksTurnedOffByPolicyGreyOutCheckForUpdates` |
| Daily check: runs when chosen and due, balloon, once a day; never in manual mode | TrayApp.cs:76 | ✅ `TrayJourneys › TheScheduledCheckRunsOnlyWhenChosenAndOnlyOnceADay` (2 cases) |
| Install item / balloon click: portable copy asks to open the download page; installed copy asks to install | TrayApp.cs:51, 179, 189, 304 | ❌ (d) native MessageBox (Yes opens a browser or the elevation prompt); the engine behind it is (c) `UpdateEngineTests` |
| Install outcome balloons/box: Updating, Update cancelled, failure | TrayApp.cs:206-215 | ❌ (c) outcomes: `UpdateEngineTests › APerUserInstallRunsTheVerifiedInstallerSilentlyWithoutElevation`, `ADeclinedPromptChangesNothing`, `AnInstallerThatWillNotStartIsReported`; the real install: ci.yml installer-smoke |
| Watchdog: an install that never closed the app is reported | Updater.cs:24 | ✅ `TrayJourneys › AnInstallThatNeverClosedTheAppIsReported` |
| Hotkeys: enabled ones held while running, switched-off ones not, released on exit | TrayApp.cs:416 | ✅ `TrayJourneys › TheEnabledHotkeysAreHeldWhileTheAppRuns` |
| Hotkey another program holds: named in a balloon | TrayApp.cs:228 | ✅ `TrayJourneys › AHotkeyAnotherProgramHoldsIsNamedInABalloon` |
| Pressing a hotkey turns its display | TrayApp.cs:416 (WndProc) | ❌ (a) same as an orientation item: turns a real screen |
| Auto-restore at startup turns displays back | Settings.cs RestoreOrientations | ❌ (a) turns a real screen (the dialog side is ✅ below) |
| Missing or unreadable settings file: starts with the defaults | Settings.cs Load | ✅ `TrayJourneys › MissingOrUnreadableSettingsStartWithTheDefaults` (2 cases) |
| Start with Windows: autostart re-pointed at this copy at startup | TrayApp.cs ctor | ✅ `TrayJourneys › StartWithWindowsIsPointedAtThisCopyAtStartup` |
| Exit ends the app, icon gone | TrayApp.cs:374 | ✅ `TrayJourneys › ExitEndsTheApp` |
| Installer's quit event ends the app | TrayApp.cs ListenForQuit | ✅ `TrayJourneys › TheInstallersQuitRequestEndsTheApp`, `CommandLineJourneys › ASecondCopyLeavesAtOnceAndTheInstallersQuitEventEndsTheFirst` |
| Tray balloons as the shell draws them | TrayApp.cs ShowBalloon | ❌ (d) drawn by the shell; their title, text and icon are asserted above |

## Settings dialog (SettingsForm)

| Row | Source | Test |
| --- | --- | --- |
| Save writes every choice: Start with Windows (+ Run value, and its removal), Show Identify, auto-restore + per-display orientation, overlay duration, daily checks, hotkey on/off, display, orientation, keys; no temp file left | SettingsForm.cs:53-65, 98, 121-128, 156, 157 | ✅ `SettingsJourneys › SaveWritesEveryChoice` |
| Cancel discards hotkey edits (Add, Remove, Reset, a captured key) | SettingsForm.cs:160 | ✅ `SettingsFormTests › CancelKeepsTheHotkeys` |
| Enter saves, Escape cancels | SettingsForm.cs:156, 160 | ✅ `SettingsJourneys › EnterSavesAndEscapeCancels` |
| Auto-restore on/off moves everything below and the window edge | SettingsForm.cs:87 | ✅ `SettingsFormTests › TickingAutoRestoreMakesRoomForTheDisplayRows` |
| Per-display rows list every display with its current orientation; hotkey display choices list Primary + every display | SettingsForm.cs:254 | ✅ `SettingsJourneys › TheChoicesListEveryDisplay` |
| Identify next to a display: its number on it | SettingsForm.cs:258 | ✅ `SettingsJourneys › IdentifyNextToADisplayShowsItsNumberOnIt` |
| Overlay duration 1–5 s, out-of-range values clamped | SettingsForm.cs:69 | ✅ `SettingsJourneys › TheOverlayDurationIsOneToFiveSeconds` |
| Hotkey box: combination with Ctrl/Alt/Shift taken, Backspace clears, no modifier or a modifier alone ignored | SettingsForm.cs:389, 418 | ✅ `SettingsJourneys › AHotkeyBoxTakesTheCombinationPressedInIt` (7 cases) |
| Escape in a hotkey box clears it, Settings stays open | SettingsForm.cs:410 | ✅ `SettingsJourneys › EscapeInAHotkeyBoxClearsItAndKeepsSettingsOpen` (fixed in this wave) |
| Add: empty hotkey for Primary at 0°, saved | SettingsForm.cs:107, 108 | ✅ `SettingsJourneys › AddAppendsAnEmptyHotkeyForThePrimaryDisplay` |
| Add after scrolling the list: row lands under the last one | SettingsForm.cs:108 | ✅ `SettingsJourneys › AddPutsTheNewRowUnderTheLastOneWhenTheListIsScrolled` (fixed in this wave) |
| Remove: the row last focused | SettingsForm.cs:111, 112 | ✅ `SettingsFormTests › RemoveDeletesTheRowLastFocused` |
| Remove keeps the other rows' edits | SettingsForm.cs:112 | ✅ `SettingsFormTests › RemoveKeepsTheOtherRowsEdits` |
| Remove with no row chosen: nothing | SettingsForm.cs:112 | ✅ `SettingsJourneys › RemoveWithNoRowChosenRemovesNothing` |
| Reset to Defaults: the four Ctrl+Alt+arrow keys, saved | SettingsForm.cs:115, 116 | ✅ `SettingsJourneys › ResetToDefaultsBringsBackTheFourArrowKeys` |
| Check now: up to date / unreachable / newer (status line, "Open download page" for a portable copy), stamps the time | SettingsForm.cs:134, 135, 207, Updater.cs:131-132 | ✅ `SettingsJourneys › CheckNowShowsTheAnswerAndStampsTheTime` (3 cases) |
| Installed copy: "Update to X" | SettingsForm.cs:139 | ✅ `SettingsJourneys › AnInstalledCopyOffersToUpdateItself` |
| Update to X pressed: download, verify, run the installer; progress text | SettingsForm.cs:139, 221 | ❌ (c) `UpdateEngineTests › APerUserInstallRunsTheVerifiedInstallerSilentlyWithoutElevation`, `ADownloadThatDoesNotMatchIsDiscardedAndNeverRun`; progress wording ✅ `SettingsJourneys › DownloadProgressReadsInPercentOrMegabytes` |
| Open download page pressed (portable copy) | SettingsForm.cs:139 | ❌ (b) opens the browser on github.com; no sink |
| Update failure / policy message box | SettingsForm.cs:240 | ❌ (c) `UpdateEngineTests › ThePolicyStopsTheCheckBeforeAnythingGoesOnline`, `AnInstallerThatWillNotStartIsReported` (the box itself: native MessageBox) |
| Status line: never checked / last checked at | SettingsForm.cs InitialUpdateStatus | ✅ `SettingsJourneys › TheStatusLineShowsTheLastCheck`, `Sweep` (Settings) |
| Status line + Check now off when policy turns checks off | Updater.cs:134 | ✅ `SettingsJourneys › ChecksTurnedOffByPolicyAreSaidSoAndCannotRun` |
| Status line + Check now off while an update installs | SettingsForm.cs InitialUpdateStatus | ✅ `SettingsJourneys › WhileAnUpdateInstallsNothingElseCanStart` |
| Development build: "the newest release is X", never offered | Updater.cs:133 | ❌ (c) `UpdateEngineTests › ACheckComparesAgainstTheLatestRelease` (0.0.0 case); e2e builds 9.9.9 so the update flows can run |

## Command line and process (Program)

| Row | Source | Test |
| --- | --- | --- |
| `--version` | Program.cs:43 | ✅ `CommandLineJourneys › VersionPrintsTheBuildsVersion` (and ci.yml installer-smoke for the published exe) |
| `list` (any case): header, every display, resolution, primary marked | Program.cs:39 | ✅ `CommandLineJourneys › ListShowsEveryDisplayAndMarksThePrimary` |
| `rotate <angle>` / `rotate <#> <angle>` to the current angle | Program.cs:47 | ✅ `CommandLineJourneys › RotateToTheCurrentAngleSaysSoAndChangesNothing` |
| Bad angle, bad display number, unknown display, `rotate` alone, unknown command → usage | Program.cs:47 | ✅ `CommandLineJourneys › BadInputIsExplained` (6 cases) |
| No arguments: tray; a second copy leaves at once | Program.cs Main | ✅ `CommandLineJourneys › ASecondCopyLeavesAtOnceAndTheInstallersQuitEventEndsTheFirst` |

Rotating to a different angle from the command line is the (a) row above. Installing,
upgrading and uninstalling are covered by ci.yml's installer-smoke job and
`InstallerContractTests` (c).
