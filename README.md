# DisplayRotate

Rotate any display on Windows 10 and 11 from the system tray, a hotkey, or the command line.
Landscape, portrait, flipped either way. Pick it per monitor from the tray menu, bind your own
hotkeys to any display and angle, and have your preferred orientations come back at startup.

A native Windows app (C# / .NET 10, WinForms), using the same Win32 display API the Settings
app does (`ChangeDisplaySettingsEx`).

## Install

Everything is on the [latest release](https://github.com/phoen-ix/displayRotate/releases/latest):

- **Installer.** Run a `DisplayRotate-Setup-<version>*.exe`.
  - By default it installs just for you (`%LOCALAPPDATA%\Programs\DisplayRotate`) and needs
    no administrator rights.
  - Pick "anyone who uses this computer" and it installs into Program Files, asking for
    administrator rights only then.
  - It offers Start Menu and Desktop shortcuts, and uninstalls from Windows' "Installed apps".
  - If DisplayRotate is running, installing or uninstalling asks it to close first.
  - **Uninstalling leaves nothing behind for your account:** the program, your settings,
    update downloads, and the autostart entry.
- **Portable.** Unzip one of the zips and run `DisplayRotate.exe`. Settings go to
  `%APPDATA%\DisplayRotate`. A portable copy can check for updates, but you replace it yourself.

A rotation icon appears in the system tray either way.

**If you're not sure, take `DisplayRotate-Setup-<version>.exe`.** It asks which build you want
and suggests the one that needs nothing.

| Installer | Size | Needs anything installed? |
| --- | --- | --- |
| **`DisplayRotate-Setup-<version>.exe`** | ~43 MB | No. It asks which of the two builds below to install. |
| **`DisplayRotate-Setup-<version>-full.exe`** | ~43 MB | **No.** All-in-one, with the runtime inside. |
| **`DisplayRotate-Setup-<version>-min.exe`** | ~0.3 MB | Yes: the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64), which the installer offers to fetch through `winget`. |

| Portable | Size | Needs anything installed? |
| --- | --- | --- |
| **`DisplayRotate-<version>.zip`** | ~42 MB | **No.** The runtime is bundled. |
| **`DisplayRotate-<version>-min.zip`** | ~0.1 MB | Yes: the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). |

It is the same app in every row.
- The full build is self-contained and compressed.
- The minimal build is a small launcher for a runtime you install once.
- CI builds every download from [`packaging/`](packaging/) and attaches a `SHA256SUMS.txt`.
- Nothing is code-signed yet. SmartScreen may warn on first run (**More info → Run anyway**),
  and an exe from a downloaded zip may need **Properties → Unblock**.

### Silent install

For scripts and deployment tools:

| Switch | Effect |
| --- | --- |
| `/S` | Silent. Every prompt takes its safe default. |
| `/CurrentUser` / `/AllUsers` | Install for you, or for everyone (needs an elevated prompt when silent). Without either, an existing install's scope is kept, and a new one is per-user. |
| `/RESTART` | Start DisplayRotate again once the files are in place (silent installs only; the in-app updater passes it). |
| `/NORUNTIME` | Minimal build: never download the .NET runtime. A silent install that would need it exits with code 3 instead. |
| `/D=<dir>` | Install folder (must be last). |
| `uninstall.exe /S` | Silent uninstall. |

A silent `/AllUsers` install from a shell that isn't elevated exits with code 2 and installs nothing.

## Use

- **Click** the tray icon (left or right) for the menu. It shows:
  - every connected display, with its resolution and its four orientations (the current one is ticked)
  - **Identify**: a big number on each screen, so you know which is which
  - **Settings…**
  - **Check for updates**
  - **Exit**
- **Hotkeys** work anywhere while DisplayRotate runs. The defaults rotate the primary display:

  | Hotkey | Orientation |
  | --- | --- |
  | `Ctrl+Alt+Up` | 0° - Landscape |
  | `Ctrl+Alt+Right` | 90° - Portrait |
  | `Ctrl+Alt+Down` | 180° - Landscape (flipped) |
  | `Ctrl+Alt+Left` | 270° - Portrait (flipped) |

  In **Settings** you can rebind them, add more for any display, or switch them off. If
  another program already owns a combination, a balloon says which ones could not be registered.
- **Settings** also has:
  - start with Windows
  - Identify buttons in the menu
  - how long the Identify overlay stays up
  - **auto-restore**: choose an orientation per display, and DisplayRotate puts it back every
    time it starts
- **Only one copy runs at a time.** Starting it again while it is already in the tray does nothing.

### Command line

```
DisplayRotate list                  List connected displays
DisplayRotate rotate <angle>        Rotate the primary display (0, 90, 180, 270)
DisplayRotate rotate <#> <angle>    Rotate display # (from `list`) to angle
DisplayRotate --version             Print the version
```

## Updates

DisplayRotate can update itself the way it was installed: by running the new release's
installer over the old one. That installer keeps your scope, your folder and your build.

- **Check now** is in Settings → Updates and in the tray menu (**Check for updates**).
- **Check once a day** is off by default. Turn it on in Settings, and a newer release shows up
  as a tray balloon and as **Install DisplayRotate X.Y.Z…** at the top of the tray menu.
- **Installing always needs your click.**
  - The download comes from this repository's GitHub release.
  - It is checked against that release's `SHA256SUMS.txt` before it runs.
  - DisplayRotate then closes, updates, and comes back.
  - A per-machine install asks for administrator rights first.

The details are in [docs/updating.md](docs/updating.md): what is verified, what is not, the policy switch, and
what happens when something goes wrong.

## Settings and where things live

| What | Where |
| --- | --- |
| Settings (hotkeys, preferred orientations, update choice) | `%APPDATA%\DisplayRotate\settings.json` |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `DisplayRotate`, written only when you tick "Start with Windows" |
| Installed program | `%LOCALAPPDATA%\Programs\DisplayRotate`, or `C:\Program Files\DisplayRotate` for everyone |
| Update downloads | `%TEMP%\DisplayRotate-update-*`, removed after a day |

The orientation itself is Windows' own display setting. DisplayRotate changes it the same way
Settings → Display does, and uninstalling leaves your screens however they are.

## Privacy

Out of the box DisplayRotate makes **no network connections at all**: no telemetry, no
background check. The update setting starts at **Only when I ask**.

A check, whether you asked for it or it is the opt-in daily one, does three things:
- It asks `https://github.com/phoen-ix/displayRotate/releases/latest` which release is newest.
  It reads only the redirect and uses no API.
- An install then downloads that release's `SHA256SUMS.txt` and installer from GitHub.
- It sends a `DisplayRotate/<version>` user agent and nothing else.

An administrator can switch checks off machine-wide (see [docs/updating.md](docs/updating.md#policy)).

## Build from source

The .NET 10 SDK builds the solution on Windows or Linux (Linux via `EnableWindowsTargeting`;
the exe runs on Windows only):

```sh
dotnet build DisplayRotate.slnx -c Release
dotnet test --solution DisplayRotate.slnx -c Release --no-build
```

`packaging/build.sh <version>` (or `build.bat` on Windows) publishes both builds and makes all three
installers with `makensis`. They run the same steps as the release job in
[`.github/workflows/ci.yml`](.github/workflows/ci.yml). [`packaging/README.md`](packaging/README.md)
explains the installer.

- **Releases are automatic.** A push to `master` whose commits include a Conventional-Commit
  `feat:` or `fix:` tags and publishes the next version once every check has passed. That
  includes a real install, upgrade and uninstall on a Windows runner.
- Other types (`docs:`, `ci:`, `chore:`, `refactor:`) release nothing.

## License

MIT - see [LICENSE](LICENSE).
