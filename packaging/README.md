# Packaging

`displayrotate.nsi` is the NSIS script behind all three installers. It is adapted from
[pawse](https://github.com/phoen-ix/pawse)'s, a per-user tray app with the same needs, and
carries the fixes that project found the hard way.

## Building

```sh
./build.sh 0.1.0        # Linux (apt install nsis), or build.bat 0.1.0 on Windows
```

Both scripts publish the two builds into `../publish-full` and `../publish-min` and stage them
here as `DisplayRotate.exe` and `DisplayRotate-min.exe`. Then they run:

```
makensis -WX -V2 -DVERSION=<v> displayrotate.nsi                 -> DisplayRotate-Setup-<v>.exe       (asks which build)
makensis -WX -V2 -DVERSION=<v> -DFULL_ONLY displayrotate.nsi     -> DisplayRotate-Setup-<v>-full.exe  (self-contained)
makensis -WX -V2 -DVERSION=<v> -DMINIMAL_ONLY displayrotate.nsi  -> DisplayRotate-Setup-<v>-min.exe   (needs .NET 10)
```

`-WX` turns every warning into an error, and an unreferenced function counts as one. That is
why the runtime functions are compiled out of `FULL_ONLY` with `!ifndef`.

The icon and the licence are taken from `../src/DisplayRotate/app.ico` and `../LICENSE`.

What ships is built by the `release` job in `../.github/workflows/ci.yml`.
- The `installer` job compiles all three variants on every push.
- `installer-smoke` installs, upgrades and uninstalls for real on Windows, per-user and
  per-machine, full and minimal.

## Switches

| Switch | Effect |
| --- | --- |
| `/S` | Silent. Every MessageBox carries a silent default (`/SD`), and a test fails the build if one does not. |
| `/CurrentUser`, `/AllUsers` | Scope. Parsed by hand in `.onInit`, not by MultiUser.nsh; see below. An explicit switch is obeyed. Without one, an existing install's scope is kept (per-user first), and a new install is per-user. |
| `/RESTART` | After a silent install, start DisplayRotate again through `explorer.exe`, so it runs as the desktop user rather than elevated. |
| `/NORUNTIME` | Never provision the .NET runtime. In `-min`, a silent install with the runtime missing exits 3 before touching anything. |
| `/D=<dir>` | Install folder (NSIS standard, must be last). |

Exit codes: `2` means a silent `/AllUsers` without administrator rights; `3` is described above.

The in-app updater passes `/S /CurrentUser|/AllUsers /NORUNTIME /RESTART`
(`src/DisplayRotate.Core/Updates/InstallerArguments.cs`). `${GetOptions}` ignores switches it is not
asked about, so a test checks that each one is read here.

## Design notes

- **No UAC prompt to open it.**
  - `MULTIUSER_EXECUTIONLEVEL Highest` is kept, because the install-mode page needs it.
  - `RequestExecutionLevel user` after the include overrides the manifest it emits.
  - `MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER` makes per-user the default.
  - Picking "anyone who uses this computer" re-launches the installer elevated
    (`ElevateForAllUsers`). The privilege fib in `.onInit` is what lets a non-admin see that
    choice at all.
- **Silent `/AllUsers` never hangs.** Stock MultiUser answers an unelevated `/AllUsers` with a box
  that has no silent default, then reports success having installed nothing. Hence the
  hand-parsing and exit code 2.
- **Closing the running app.**
  - Detection is the tray instance's mutex, or the image name for another session.
  - The installer signals the app's quit event, then waits on the mutex for up to 10 s.
  - Only then does it offer Retry / force close (silent: force). If force-closing fails, it
    offers an elevated `taskkill`.
  - The mutex and event names live in `src/DisplayRotate.Core/Names.cs` as well, and a test pins
    the two together.
- **Upgrades in place.** The previous `InstallLocation` is reused, so a custom folder isn't
  orphaned. `BuildVariant` (`full`/`min`) is recorded for the updater.
- **The .NET check** reads `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64` in the 32-bit view.
  That is where the host records it, and it is WinLogRotate's fix; pawse reads the 64-bit view.
  The check asks before running `winget` (about 57 MB, machine-wide) and verifies the runtime
  is there afterwards.
- **No autostart section.** In an elevated per-machine install it would write the admin's HKCU,
  and a silent upgrade would turn autostart back on for anyone who had turned it off. The app
  owns it (Settings → Start with Windows).
- **The uninstaller finds its own scope.**
  - An HKLM entry naming its folder means machine-wide. It then hands over to an elevated copy
    of itself, forwarding `/S`.
  - It removes only the Add/Remove entry and the Run value that point at *this* install, so a
    per-user copy beside a machine-wide one survives either being removed.
  - It also removes `%APPDATA%\DisplayRotate` and `%TEMP%\DisplayRotate-update-*` for the account
    running it.
- **Every external tool runs by full path** (`$SYSDIR\tasklist.exe`, `taskkill.exe`,
  `where.exe`, and winget as resolved by `where`). A planted binary beside an elevated
  installer can't run.
