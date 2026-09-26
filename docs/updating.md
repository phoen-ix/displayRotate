# Updating

DisplayRotate updates itself the way it was installed: by running the newer release's installer
over the old one.
- The installer keeps your scope (just you, or everyone) and your folder.
- It keeps your build (full or minimal), and your settings are untouched.
- It asks the running DisplayRotate to close, replaces it, and starts it again.

There are two ways to get there, and they do the same thing.

## By hand

Download the installer from the [latest release](../../../releases/latest) and run it. It finds the
existing install and upgrades it in place. Silent: `DisplayRotate-Setup-<version>.exe /S`.

A portable copy is updated by hand too: download the new zip and replace `DisplayRotate.exe`
while DisplayRotate is not running.

## From the app

- **Check now.** Use Settings → Updates → **Check now**, or **Check for updates** in the tray menu.
  This asks GitHub which release is newest and says whether it is newer than yours. It makes no
  changes and needs no rights.
- **Check once a day.** This is off by default. When on, DisplayRotate checks a minute after it
  starts and then whenever a day has passed since the last check. That includes a check you made
  yourself, and one that failed: an offline laptop does not retry every hour.
  A newer release shows a tray balloon and an **Install DisplayRotate X.Y.Z…** item at the top of
  the tray menu. It never installs by itself.
- **Update.** Use **Update to X.Y.Z** in Settings, the tray menu item, or the balloon. It goes like this:
  1. It reads what the installer recorded in the Add/Remove Programs entry for this copy: where it
     is, for whom it was installed, and which build.
     - A copy that no installer put where it runs is portable. For a portable copy, the button opens
       the download page instead.
  2. It downloads the release's `SHA256SUMS.txt` **first**. A release whose files are still
     uploading is refused in a second, before anything large is fetched.
  3. It downloads `DisplayRotate-Setup-<version>-full.exe` or `-min.exe`, whichever matches your
     build, into `%TEMP%\DisplayRotate-update-<random>\`. Redirects are followed only to GitHub's
     own hosts, over https.
  4. It checks the download's SHA-256 against the checksum file. On a mismatch the download is
     deleted and nothing runs.
  5. It runs the installer with `/S /CurrentUser /NORUNTIME /RESTART`, or `/S /AllUsers …` for a
     per-machine install. The installer asks DisplayRotate to quit and replaces it, and
     `/RESTART` starts it again, as you rather than elevated.

A per-machine install (Program Files) needs administrator rights to update:
- The button carries the UAC shield, and Windows asks when you press it.
- A per-user install asks nothing.

If DisplayRotate is still running three minutes after it handed over to the installer, the
update did not install. A balloon says so, and the old version keeps working.

The choice and the time of the last check are stored with your other settings, in
`%APPDATA%\DisplayRotate\settings.json` (`updateCheck`, `lastUpdateCheckUtc`).

## What is verified, and what is not

- Every download is checked against the release's `SHA256SUMS.txt` before it runs. That catches a
  corrupted or truncated download and a mix-up between assets.
- It does **not** protect against a compromised GitHub account or repository, because the
  checksum file lives in the same release as the installer.
- Nothing is code-signed yet. When a certificate exists, a signature check is added at the same
  point.
- The version comes from the tag GitHub's `releases/latest` redirect names. A development
  build (`0.0.0`, anything not built by CI) reports the newest release but never offers to
  install it.

## Policy

An administrator can switch every update check off machine-wide:

```
reg add HKLM\SOFTWARE\Policies\DisplayRotate /v DisableUpdateCheck /t REG_DWORD /d 1 /f
```

With it set:
- **Check now** and **Check for updates** are disabled.
- The daily check never runs.
- Nothing goes online.

Deploy updates with the installer's silent switches instead (see the README).

## Proxies

DisplayRotate uses the system proxy settings, like any .NET app.

## When it refuses

| Message | What it means |
| --- | --- |
| GitHub could not be reached | Offline, a proxy or firewall in the way, or no release published yet. Nothing was changed. |
| The release's SHA256SUMS.txt could not be downloaded | The release was published moments ago and is still uploading, or GitHub is having a moment. Try again in a few minutes. |
| The release does not list DisplayRotate-Setup-…exe | The release is missing your build's installer. Nothing was downloaded. |
| The download did not match the release's checksum | Something altered the download in transit, or it was cut short. It was deleted; try again. |
| The administrator prompt was declined | A per-machine install needs administrator rights to update. Nothing was changed. |
| This copy was not installed | A portable copy. Download the new zip from the release page. |
