; DisplayRotate installer - NSIS script. One of two defines picks which installer to build:
;
;   makensis /DVERSION=<v> displayrotate.nsi                 -> DisplayRotate-Setup-<v>.exe       (standard)
;   makensis /DVERSION=<v> /DFULL_ONLY displayrotate.nsi     -> DisplayRotate-Setup-<v>-full.exe  (all-in-one)
;   makensis /DVERSION=<v> /DMINIMAL_ONLY displayrotate.nsi  -> DisplayRotate-Setup-<v>-min.exe   (true minimal)
;
; Standard carries BOTH builds and asks which to install; -full carries only the self-contained
; build (runtime bundled, needs nothing); -min carries only the framework-dependent build (needs
; the .NET 10 Desktop Runtime, which it offers to fetch through winget). The in-app updater always
; fetches -full or -min, whichever matches the BuildVariant this script records.
;
; Run from THIS folder with the published exes staged next to it (build.bat / build.sh, or CI):
;   DisplayRotate.exe      - self-contained single-file   (standard and -full)
;   DisplayRotate-min.exe  - framework-dependent single-file (standard and -min)
; The installed exe is always named DisplayRotate.exe.
;
; Switches: /S  /CurrentUser  /AllUsers  /RESTART  /NORUNTIME  /D=<dir> (last)
; Exit codes: 2 = silent /AllUsers without administrator rights (nothing installed)
;             3 = silent /NORUNTIME minimal install with the .NET runtime missing (nothing installed)
; packaging/README.md explains the design; ci.yml's installer-smoke job proves it on Windows.

Unicode true
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define APP "DisplayRotate"
!define PUBLISHER "phoen-ix"
!define EXE "DisplayRotate.exe"
!define ICON_SRC "..\src\DisplayRotate\app.ico"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define DOTNET_URL "https://dotnet.microsoft.com/download/dotnet/10.0"
; Both names are hard-coded in the app too (src/DisplayRotate.Core/Names.cs), and a test pins
; the two files together: renaming one alone fails silently.
!define MUTEX_NAME "Local\DisplayRotate-single-instance-4d9e21"
!define QUIT_EVENT "Local\DisplayRotate-quit-4d9e21"
!define ERROR_ACCESS_DENIED 5

!ifdef MINIMAL_ONLY
  !ifdef FULL_ONLY
    !error "MINIMAL_ONLY and FULL_ONLY are mutually exclusive - build them one at a time."
  !endif
  !define VARIANT " (Minimal)"
  !define OUTSUFFIX "-min"
  !define SINGLE_BUILD
!else
  !ifdef FULL_ONLY
    !define VARIANT " (Full)"
    !define OUTSUFFIX "-full"
    !define SINGLE_BUILD
  !else
    !define VARIANT ""
    !define OUTSUFFIX ""
  !endif
!endif

Name "${APP} ${VERSION}${VARIANT}"
OutFile "DisplayRotate-Setup-${VERSION}${OUTSUFFIX}.exe"
BrandingText "${APP} ${VERSION}${VARIANT}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "FileFunc.nsh"   ; ${GetSize}, ${GetParameters}, ${GetOptions}

; ---- per-user / per-machine ----
!define MULTIUSER_EXECUTIONLEVEL Highest
!define MULTIUSER_MUI
; No MULTIUSER_INSTALLMODE_COMMANDLINE: /CurrentUser and /AllUsers are parsed by hand in
; .onInit, AFTER the privilege fib there. Stock MultiUser parses them inside MULTIUSER_INIT
; and answers an unelevated /AllUsers with an MB_OK box that has no silent default, then
; quits with exit code 0 - so "/S /AllUsers" from a normal shell hung on a dialog nobody
; could see and then reported success having installed nothing. (pawse, 00f102f)
!define MULTIUSER_USE_PROGRAMFILES64
!define MULTIUSER_INSTALLMODE_INSTDIR "${APP}"
; Install for the current user unless asked otherwise - without this MultiUser preselects
; per-machine for anyone holding an admin token. A tray app needs no privileges at all.
!define MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER
!include "MultiUser.nsh"

; Deliberately AFTER the include, to override the "highest" that MULTIUSER_EXECUTIONLEVEL
; Highest emits. "highest" makes Windows elevate an administrator at launch - a UAC prompt
; just to open the installer, before anyone has chosen anything, for what is by default a
; per-user install. asInvoker means no prompt unless per-machine is actually picked, and then
; ElevateForAllUsers asks for exactly that. The Highest define stays: MULTIUSER_PAGE_INSTALLMODE
; refuses to compile without it.
RequestExecutionLevel user

!ifndef SINGLE_BUILD
Var BuildChoice   ; "full" | "min"
Var RbFull
Var RbMin
!endif

Var AppRunning       ; "1" | "0" - set by ${UN}AppIsRunning, shared by both halves
Var RealPrivileges   ; account type as Windows reports it, before we fib to MultiUser
!ifndef FULL_ONLY
Var DotnetFound      ; "1" | "0" - set by DotnetPresent
Var NoRuntime        ; "1" when /NORUNTIME was passed - never fetch .NET unattended
!endif
Var RestartApp       ; "1" when /RESTART was passed - a silent install relaunches DisplayRotate

; ---- UI ----
!define MUI_ICON "${ICON_SRC}"
!define MUI_UNICON "${ICON_SRC}"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Launch DisplayRotate now"
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchApp"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
; Elevate the moment "anyone who uses this computer" is actually chosen - see
; ElevateForAllUsers. The define is consumed by the page macro below.
!define MULTIUSER_PAGE_CUSTOMFUNCTION_LEAVE ElevateForAllUsers
!insertmacro MULTIUSER_PAGE_INSTALLMODE
!ifndef SINGLE_BUILD
Page custom BuildPageCreate BuildPageLeave
!endif
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---- custom "which build" page (standard installer only) ----
!ifndef SINGLE_BUILD
Function BuildPageCreate
  !insertmacro MUI_HEADER_TEXT "Choose build" "Pick which DisplayRotate build to install."
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 34u "DisplayRotate ships as two builds. The full build bundles the .NET runtime and needs nothing installed. The minimal build is tiny but requires the .NET 10 Desktop Runtime (x64)."
  Pop $0
  ${NSD_CreateRadioButton} 0 40u 100% 12u "Full - runtime bundled. Just works, nothing else to install."
  Pop $RbFull
  ${NSD_CreateRadioButton} 0 56u 100% 12u "Minimal - tiny. Needs the .NET 10 Desktop Runtime (installed via winget if missing)."
  Pop $RbMin
  ${If} $BuildChoice == "min"
    ${NSD_Check} $RbMin
  ${Else}
    ${NSD_Check} $RbFull
  ${EndIf}
  nsDialogs::Show
FunctionEnd

Function BuildPageLeave
  ${NSD_GetState} $RbMin $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $BuildChoice "min"
  ${Else}
    StrCpy $BuildChoice "full"
  ${EndIf}
FunctionEnd
!endif

Function un.onInit
  !insertmacro MULTIUSER_UNINIT
  ; Stock MultiUser decides the mode from the account type alone - and with
  ; MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER that is CurrentUser for EVERY token: the
  ; registered uninstall strings carry no /AllUsers. Left like that, a machine-wide uninstall
  ; from "Installed apps" ran as the user, failed every Delete under Program Files and the
  ; HKLM key silently, and reported success. So work out which install this uninstaller
  ; belongs to: an HKLM entry naming our own folder means machine-wide, and the mode (hence
  ; SHCTX and the common-folder shortcuts) has to follow. Assigned directly -
  ; un.MultiUser.InstallMode.AllUsers refuses on a User token, and asInvoker makes us one.
  ; (HKLM is read in this 32-bit exe's redirected view - the view the install wrote to.)
  ReadRegStr $0 HKCU "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != "$INSTDIR"
    ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
    ${If} $0 == "$INSTDIR"
      StrCpy $MultiUser.InstallMode AllUsers
      SetShellVarContext all
    ${EndIf}
  ${EndIf}
  ${If} $MultiUser.InstallMode == "AllUsers"
  ${AndIf} $MultiUser.Privileges != "Admin"
  ${AndIf} $MultiUser.Privileges != "Power"
    ; Hand over to an elevated copy. Target $INSTDIR\uninstall.exe rather than $EXEPATH: NSIS
    ; runs uninstallers from a copy in $TEMP, and that copy is not what we want to re-launch.
    ; Forward /S: the QuietUninstallString runs this silently, and a handoff that dropped the
    ; flag would turn that "quiet" uninstall into a full wizard behind the UAC prompt.
    ClearErrors
    ${If} ${Silent}
      ExecShell "runas" "$INSTDIR\uninstall.exe" "/S"
    ${Else}
      ExecShell "runas" "$INSTDIR\uninstall.exe"
    ${EndIf}
    ${IfNot} ${Errors}
      Quit
    ${EndIf}
    MessageBox MB_OK|MB_ICONSTOP|MB_TOPMOST|MB_SETFOREGROUND "DisplayRotate was installed for everyone on this computer, so removing it needs administrator rights.$\n$\nRight-click uninstall.exe in the DisplayRotate folder and choose 'Run as administrator'." /SD IDOK
    Quit
  ${EndIf}
FunctionEnd

Function LaunchApp
  ; Through Explorer, so a per-machine (elevated) install still starts the app as the normal,
  ; non-elevated user - a tray app started by an elevated process inherits its token.
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXE}"'
FunctionEnd

; ---- ensure .NET 10 Desktop Runtime for the minimal build ----
; Skipped entirely in a FULL_ONLY build: nothing there can call these, and makensis -WX
; treats an unreferenced function as an error.
!ifndef FULL_ONLY
; Sets $DotnetFound. Asks the .NET host where it lives rather than assuming: a runtime
; installed anywhere but the default folder would otherwise read as "missing".
Function DotnetPresent
  Push $0
  Push $1
  Push $2
  StrCpy $DotnetFound "0"
  ; The x64 host records its location in the 32-bit registry view (WOW6432Node) - where
  ; hostfxr itself looks. SetRegView 64 reads the wrong one and falls back to the default
  ; folder every time (WinLogRotate, b4233b7).
  SetRegView 32
  ReadRegStr $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64" "InstallLocation"
  SetRegView default
  ${If} $0 == ""
    StrCpy $0 "$PROGRAMFILES64\dotnet"   ; nothing recorded - fall back to the usual spot
  ${EndIf}
  ; Exactly 10.*, not "10 or later": the app's default roll-forward policy is Minor, so an
  ; 11.x runtime alone would not start it.
  FindFirst $1 $2 "$0\shared\Microsoft.WindowsDesktop.App\10.*"
  FindClose $1
  ${If} $2 != ""
    StrCpy $DotnetFound "1"
    DetailPrint ".NET 10 Desktop Runtime found ($2 in $0)."
  ${EndIf}
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

Function EnsureDotnet
  Call DotnetPresent
  ${If} $DotnetFound == "1"
    Return
  ${EndIf}
  DetailPrint ".NET 10 Desktop Runtime (x64) not found."
  ${If} $NoRuntime == "1"
    DetailPrint "Skipping the runtime download (/NORUNTIME)."
    Call DotnetManual
    Return
  ${EndIf}
  ; Ask first: it is a machine-wide install of about 57 MB that nobody chose yet. /SD IDYES so
  ; a scripted /S deploy still provisions the runtime without a prompt.
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_TOPMOST|MB_SETFOREGROUND "The minimal DisplayRotate build needs the .NET 10 Desktop Runtime (x64), which isn't installed on this PC.$\n$\nDownload and install it now? That's about 57 MB, fetched and installed machine-wide by winget.$\n$\nChoose No to handle it yourself - DisplayRotate won't start until the runtime is present." /SD IDYES IDNO dn_manual
  ; Resolve winget's real path via System32's where.exe - both fully qualified so a planted
  ; where.exe / winget.exe in the (possibly elevated) installer's folder can't run.
  nsExec::ExecToStack '"$SYSDIR\where.exe" winget'
  Pop $0
  Pop $2
  ${If} $0 != 0
    Call DotnetManual
    Return
  ${EndIf}
  Push $2
  Call FirstLine
  Pop $3
  ${If} $3 == ""
    Call DotnetManual
    Return
  ${EndIf}
  DetailPrint "Installing .NET 10 Desktop Runtime via winget..."
  nsExec::ExecToLog '"$3" install --id Microsoft.DotNet.DesktopRuntime.10 -e --silent --accept-package-agreements --accept-source-agreements'
  Pop $0
  ${If} $0 != 0
    Call DotnetManual
    Return
  ${EndIf}
  ; Trust but verify - winget can report success without the runtime we actually need.
  Call DotnetPresent
  ${If} $DotnetFound != "1"
    DetailPrint "winget reported success but no .NET 10 Desktop Runtime is present."
    Call DotnetManual
  ${EndIf}
  Return
 dn_manual:
  Call DotnetManual
FunctionEnd

; First line of the string on the stack (where.exe prints one path per line).
Function FirstLine
  Exch $0
  Push $1
  Push $2
  Push $3
  StrCpy $1 ""
  StrCpy $2 0
  fl_loop:
    StrCpy $3 $0 1 $2
    StrCmp $3 "" fl_done
    StrCmp $3 "$\r" fl_done
    StrCmp $3 "$\n" fl_done
    StrCpy $1 "$1$3"
    IntOp $2 $2 + 1
    Goto fl_loop
  fl_done:
  StrCpy $0 $1
  Pop $3
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

Function DotnetManual
  ; /SD IDNO: NSIS does NOT suppress message boxes in silent mode, so without a silent default
  ; a /S install on a machine with no winget would block here forever on a dialog nobody can
  ; see - and an unattended run must never open a browser either.
  MessageBox MB_YESNO|MB_ICONEXCLAMATION|MB_TOPMOST|MB_SETFOREGROUND "The minimal DisplayRotate build needs the .NET 10 Desktop Runtime (x64), and it isn't installed on this PC.$\n$\nOpen the download page now? DisplayRotate will finish installing either way, but it won't start until the runtime is there." /SD IDNO IDNO +2
  ExecShell "open" "${DOTNET_URL}"
FunctionEnd
!endif

; ---- closing a running DisplayRotate ----
; One macro, instantiated for the installer and the uninstaller: NSIS keeps their functions
; apart, and both halves need the whole set.
!macro APP_CLOSE_FUNCS UN ACTION

Function ${UN}AppIsRunning
  Push $0
  Push $1
  StrCpy $AppRunning "0"
  ; 1) The tray instance's single-instance mutex. "Access denied" means the mutex is there but
  ;    owned by a token we can't open (DisplayRotate running elevated while we aren't) - still
  ;    a running DisplayRotate.
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${MUTEX_NAME}") p .r0 ?e'
  Pop $1
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    StrCpy $AppRunning "1"
    Goto air_done
  ${ElseIf} $1 = ${ERROR_ACCESS_DENIED}
    StrCpy $AppRunning "1"
    Goto air_done
  ${EndIf}
  ; 2) The image name. Catches an instance in another user's session (a Local\ mutex is
  ;    per-session), and a command-line run, either of which still locks the exe.
  ;    Fully qualified so a planted tasklist.exe beside an elevated installer can't run.
  nsExec::ExecToStack /TIMEOUT=10000 '"$SYSDIR\tasklist.exe" /NH /FO CSV /FI "IMAGENAME eq ${EXE}"'
  Pop $0
  Pop $1
  ; A match is a CSV row: "DisplayRotate.exe","1234",... The no-match line ("INFO: No tasks...")
  ; is translated on localised Windows but is never quoted - so test the first character.
  StrCpy $1 $1 1
  ${If} $0 == 0
  ${AndIf} $1 == '"'
    StrCpy $AppRunning "1"
  ${EndIf}
 air_done:
  Pop $1
  Pop $0
FunctionEnd

Function ${UN}AppRequestQuit
  Push $0
  Push $1
  System::Call 'kernel32::OpenEventW(i 0x0002, i 0, w "${QUIT_EVENT}") p .r0 ?e'
  Pop $1
  ${If} $0 != 0
    System::Call 'kernel32::SetEvent(p r0)'
    System::Call 'kernel32::CloseHandle(p r0)'
    DetailPrint "Asked DisplayRotate to close..."
  ${ElseIf} $1 = ${ERROR_ACCESS_DENIED}
    DetailPrint "DisplayRotate is running with higher privileges - it cannot be asked to close."
  ${Else}
    DetailPrint "DisplayRotate offers no quit channel here (another session, or the command line)."
  ${EndIf}
  ; Wait on the mutex, not the clock: it exists exactly as long as the tray instance does.
  ; Ten seconds is the ceiling; a healthy instance is gone in well under one.
  StrCpy $1 0
 arq_loop:
  Call ${UN}AppIsRunning
  ${If} $AppRunning == "0"
    ; The mutex goes a moment before the process does, and the exe stays locked until then.
    Sleep 500
    DetailPrint "DisplayRotate closed."
    Goto arq_done
  ${EndIf}
  ${If} $1 >= 20
    Goto arq_done
  ${EndIf}
  Sleep 500
  IntOp $1 $1 + 1
  Goto arq_loop
 arq_done:
  Pop $1
  Pop $0
FunctionEnd

Function ${UN}AppForceClose
  Push $0
  DetailPrint "Force-closing DisplayRotate..."
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM ${EXE}'
  Pop $0
  Pop $0
FunctionEnd

Function ${UN}EnsureAppClosed
  Call ${UN}AppIsRunning
  ${If} $AppRunning == "0"
    Return
  ${EndIf}
  MessageBox MB_YESNOCANCEL|MB_ICONEXCLAMATION|MB_TOPMOST|MB_SETFOREGROUND "DisplayRotate is running and has to close before ${ACTION} can continue.$\n$\nYes - close DisplayRotate now.$\nNo - leave it to me; I'll quit it from the tray.$\nCancel - stop and change nothing." /SD IDYES IDYES eac_ask IDNO eac_retry
  Abort "Cancelled - DisplayRotate is still running."
 eac_ask:
  Call ${UN}AppRequestQuit
  ${If} $AppRunning == "0"
    Return
  ${EndIf}
 eac_retry:
  Call ${UN}AppIsRunning
  ${If} $AppRunning == "0"
    Return
  ${EndIf}
  ; /SD IDIGNORE: unattended, force it rather than fail - DisplayRotate holds nothing that a
  ; forced close would lose (settings are written the moment they are saved).
  MessageBox MB_ABORTRETRYIGNORE|MB_ICONEXCLAMATION|MB_TOPMOST|MB_SETFOREGROUND "DisplayRotate is still running.$\n$\nRetry - I've quit it from the tray (right-click the icon, then Exit); check again.$\nIgnore - force it closed.$\nAbort - stop and change nothing." /SD IDIGNORE IDRETRY eac_retry IDIGNORE eac_force
  Abort "Cancelled - DisplayRotate is still running."
 eac_force:
  Call ${UN}AppForceClose
  Call ${UN}AppIsRunning
  ${If} $AppRunning == "0"
    Return
  ${EndIf}
  ; Still there: it runs elevated, or in another account. /SD IDNO so a silent run never
  ; raises a UAC prompt nobody is there to answer.
  MessageBox MB_YESNO|MB_ICONEXCLAMATION|MB_TOPMOST|MB_SETFOREGROUND "DisplayRotate is running as administrator or in another account, so it can't be closed from here.$\n$\nClose it using administrator rights?" /SD IDNO IDNO eac_stuck
  ClearErrors
  ExecShellWait "runas" "$SYSDIR\taskkill.exe" "/F /IM ${EXE}" SW_HIDE
  Call ${UN}AppIsRunning
  ${If} $AppRunning == "0"
    Return
  ${EndIf}
 eac_stuck:
  MessageBox MB_RETRYCANCEL|MB_ICONSTOP|MB_TOPMOST|MB_SETFOREGROUND "DisplayRotate could not be closed.$\n$\nQuit it from the tray, or re-run this as administrator, then Retry." /SD IDCANCEL IDRETRY eac_retry
  Abort "DisplayRotate could not be closed."
FunctionEnd
!macroend

!insertmacro APP_CLOSE_FUNCS ""    "Setup"
!insertmacro APP_CLOSE_FUNCS "un." "the uninstaller"

; ---- sections ----
Section "-Core" SEC_CORE
  SectionIn RO
  Call EnsureAppClosed

  SetOutPath "$INSTDIR"
  File /oname=displayrotate.ico "${ICON_SRC}"
  File /oname=LICENSE.txt "..\LICENSE"
!ifdef MINIMAL_ONLY
  File /oname=${EXE} "DisplayRotate-min.exe"
!else
  !ifdef FULL_ONLY
  File /oname=${EXE} "DisplayRotate.exe"
  !else
  ${If} $BuildChoice == "min"
    File /oname=${EXE} "DisplayRotate-min.exe"
  ${Else}
    File /oname=${EXE} "DisplayRotate.exe"
  ${EndIf}
  !endif
!endif

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayName"     "${APP}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\displayrotate.ico"
  WriteRegStr   SHCTX "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   SHCTX "${UNINST_KEY}" "URLInfoAbout"    "https://github.com/phoen-ix/displayRotate"
  ; Which build this is, so the in-app updater fetches the same kind (Names.BuildVariantValue).
!ifdef MINIMAL_ONLY
  WriteRegStr   SHCTX "${UNINST_KEY}" "BuildVariant"    "min"
!else
  !ifdef FULL_ONLY
  WriteRegStr   SHCTX "${UNINST_KEY}" "BuildVariant"    "full"
  !else
  WriteRegStr   SHCTX "${UNINST_KEY}" "BuildVariant"    "$BuildChoice"
  !endif
!endif
  WriteRegStr   SHCTX "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr   SHCTX "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD SHCTX "${UNINST_KEY}" "EstimatedSize" $0
  WriteRegDWORD SHCTX "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD SHCTX "${UNINST_KEY}" "NoRepair" 1

!ifdef MINIMAL_ONLY
  Call EnsureDotnet
!else
  !ifndef FULL_ONLY
  ${If} $BuildChoice == "min"
    Call EnsureDotnet
  ${EndIf}
  !endif
!endif
SectionEnd

Section "Start Menu shortcut" SEC_SM
  CreateShortcut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\displayrotate.ico" 0
SectionEnd

Section /o "Desktop shortcut" SEC_DESK
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\displayrotate.ico" 0
SectionEnd

; No "start at login" section: under an elevated per-machine install it would write the
; *admin's* HKCU Run key, not the user's (the same reason LaunchApp shells through Explorer),
; and a silent upgrade would switch autostart back on for anyone who had turned it off. The app
; owns autostart - Settings -> "Start with Windows" writes the Run value as the signed-in user.

Function .onInit
!ifndef SINGLE_BUILD
  StrCpy $BuildChoice "full"
!endif
  !insertmacro MULTIUSER_INIT
  ; Stock MultiUser doesn't just disable the per-machine option for non-admins, it skips the
  ; whole page - so a standard user who knows an admin password could never install
  ; machine-wide. Remember what we really are, then claim Admin purely so the choice renders;
  ; ElevateForAllUsers does the real elevating if it's picked. We run asInvoker, so even an
  ; administrator arrives here UNELEVATED with a filtered token that MultiUser reads as
  ; non-admin - the fib matters for admins too.
  StrCpy $RealPrivileges $MultiUser.Privileges
  ${If} $RealPrivileges != "Admin"
  ${AndIf} $RealPrivileges != "Power"
    StrCpy $MultiUser.Privileges "Admin"
  ${EndIf}

  Push $R0
  Push $R1
  Push $R2
  ${GetParameters} $R0

  ; /CurrentUser and /AllUsers, by hand and after the fib (see the MultiUser defines above).
  ; /AllUsers wins when both are given, as in stock MultiUser.
  StrCpy $R2 "0"
  ClearErrors
  ${GetOptions} $R0 "/CurrentUser" $R1
  ${IfNot} ${Errors}
    Call MultiUser.InstallMode.CurrentUser
    StrCpy $R2 "1"
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/AllUsers" $R1
  ${IfNot} ${Errors}
    Call MultiUser.InstallMode.AllUsers
    StrCpy $R2 "1"
  ${EndIf}

  ; Upgrade in place. Without a scope switch, follow an existing install - per-user first,
  ; the same order the app's InstallRecord reads them - rather than putting a second copy
  ; beside it. An explicit switch is obeyed: the in-app updater always passes the scope of
  ; the copy it is updating, which may be the machine-wide one on a PC that has both.
  ; After the fib: MultiUser.InstallMode.AllUsers checks $MultiUser.Privileges.
  ${If} $R2 == "0"
    ReadRegStr $0 HKCU "${UNINST_KEY}" "UninstallString"
    ${If} $0 == ""
      ReadRegStr $0 HKLM "${UNINST_KEY}" "UninstallString"
      ${If} $0 != ""
        Call MultiUser.InstallMode.AllUsers
      ${EndIf}
    ${EndIf}
  ${EndIf}

  ; Silent runs never see the mode page, so nothing would ever elevate them. Fail loudly with
  ; a distinct exit code rather than half-install into Program Files with a token that can't.
  ${If} ${Silent}
  ${AndIf} $MultiUser.InstallMode == "AllUsers"
  ${AndIf} $RealPrivileges != "Admin"
  ${AndIf} $RealPrivileges != "Power"
    SetErrorLevel 2
    Quit
  ${EndIf}

  ; Reuse the previous install's folder, so a custom directory is upgraded rather than
  ; orphaned - a silent upgrade has no directory page to pick it again.
  ReadRegStr $0 SHCTX "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != ""
  ${AndIf} ${FileExists} "$0\${EXE}"
    StrCpy $INSTDIR $0
  ${EndIf}

  ; /RESTART - relaunch DisplayRotate when a silent install finishes (what the in-app updater
  ; passes: it was asked to quit, and nothing else would bring it back). Honoured only under /S;
  ; an interactive run has the finish page's checkbox.
  StrCpy $RestartApp "0"
  ClearErrors
  ${GetOptions} $R0 "/RESTART" $R1
  ${IfNot} ${Errors}
    StrCpy $RestartApp "1"
  ${EndIf}
!ifndef FULL_ONLY
  ; /NORUNTIME - never provision the .NET runtime (the updater passes it: the runtime it is
  ; running on is still there).
  StrCpy $NoRuntime "0"
  ClearErrors
  ${GetOptions} $R0 "/NORUNTIME" $R1
  ${IfNot} ${Errors}
    StrCpy $NoRuntime "1"
  ${EndIf}
!endif
  ClearErrors
  Pop $R2
  Pop $R1
  Pop $R0

!ifdef MINIMAL_ONLY
  ; Refuse a silent /NORUNTIME install that would land a build whose runtime isn't here, before
  ; the running copy is asked to quit or a byte is written - the copy already installed keeps
  ; working. (The standard installer defaults to the full build under /S, so it never needs this.)
  ${If} ${Silent}
  ${AndIf} $NoRuntime == "1"
    Call DotnetPresent
    ${If} $DotnetFound != "1"
      SetErrorLevel 3
      Quit
    ${EndIf}
  ${EndIf}
!endif
FunctionEnd

; NSIS calls this after a successful install - Abort and Quit skip it - including a silent
; one, which is the whole point: /S never reaches the finish page, so MUI_FINISHPAGE_RUN
; never fires and the app the updater just closed would simply stay closed.
Function .onInstSuccess
  ${If} ${Silent}
  ${AndIf} $RestartApp == "1"
    DetailPrint "Relaunching DisplayRotate..."
    Call LaunchApp
  ${EndIf}
FunctionEnd

; Called by MULTIUSER_PAGE_INSTALLMODE's leave handler, after MultiUser has applied the
; choice. If a non-admin picked all-users, hand the install to an elevated copy of ourselves
; rather than marching on toward Program Files with a token that can't write it.
Function ElevateForAllUsers
  ${If} $MultiUser.InstallMode != "AllUsers"
    Return
  ${EndIf}
  ${If} $RealPrivileges == "Admin"
  ${OrIf} $RealPrivileges == "Power"
    Return                        ; already elevated at launch - nothing to do
  ${EndIf}

  ; The elevated instance really is an admin, so it never comes back through here.
  ClearErrors
  ExecShell "runas" "$EXEPATH" "/AllUsers"
  ${IfNot} ${Errors}
    Quit                          ; the elevated copy takes over
  ${EndIf}

  ; UAC declined, or no admin account available.
  Call MultiUser.InstallMode.CurrentUser
  MessageBox MB_OK|MB_ICONINFORMATION|MB_TOPMOST|MB_SETFOREGROUND "Administrator rights weren't granted, so DisplayRotate will be installed for you only." /SD IDOK
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SM}   "Add a DisplayRotate shortcut to the Start Menu."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESK} "Add a DisplayRotate shortcut to the Desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---- uninstall ----
; Uninstalling leaves nothing of DisplayRotate behind for this account: its files, its registry
; entries, its settings and any update download. Display orientations are Windows' settings,
; not DisplayRotate's, and stay as they are.
Section "Uninstall"
  Call un.EnsureAppClosed
  SetOutPath "$TEMP"   ; move CWD out of $INSTDIR so the folder can be removed

  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\displayrotate.ico"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"

  ; SetShellVarContext current: a machine-wide uninstall runs in the "all" context (the common
  ; Start Menu above), where $APPDATA would be ProgramData instead of this account's folder.
  SetShellVarContext current
  RMDir /r "$APPDATA\${APP}"
  FindFirst $0 $1 "$TEMP\DisplayRotate-update-*"
  ${DoWhile} $1 != ""
    RMDir /r "$TEMP\$1"
    FindNext $0 $1
  ${Loop}
  FindClose $0

  ; The autostart entry only if it starts THIS install's exe - a second copy elsewhere may own it.
  ReadRegStr $0 HKCU "${RUN_KEY}" "${APP}"
  ${If} $0 == '"$INSTDIR\${EXE}"'
  ${OrIf} $0 == "$INSTDIR\${EXE}"
    DeleteRegValue HKCU "${RUN_KEY}" "${APP}"
  ${EndIf}

  ; Remove THIS install's Add/Remove Programs entry - and only this one. Both hives use the same
  ; key name, so deleting both would orphan a per-user copy installed beside a machine-wide one.
  ReadRegStr $0 HKCU "${UNINST_KEY}" "InstallLocation"
  ${If} $0 == "$INSTDIR"
    DeleteRegKey HKCU "${UNINST_KEY}"
  ${EndIf}
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${If} $0 == "$INSTDIR"
    DeleteRegKey HKLM "${UNINST_KEY}"
  ${EndIf}

  Delete "$INSTDIR\uninstall.exe"
  RMDir  "$INSTDIR"
  ; NSIS runs uninstallers from a %TEMP% copy, so the folder may still be held. Sweep it up from
  ; a detached shell a moment later.
  IfFileExists "$INSTDIR\uninstall.exe" 0 +2
    Exec '"$SYSDIR\cmd.exe" /c ping 127.0.0.1 -n 3 >nul & del /f /q "$INSTDIR\uninstall.exe" & rmdir "$INSTDIR"'
SectionEnd
