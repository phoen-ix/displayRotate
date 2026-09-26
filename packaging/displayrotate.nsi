; DisplayRotate installer - NSIS script
;
;   makensis /DVERSION=<v> displayrotate.nsi  ->  DisplayRotate-Setup-<v>.exe
;
; Run from THIS folder after placing DisplayRotate.exe (published single-file) here.

Unicode true

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!define APP "DisplayRotate"
!define PUBLISHER "DisplayRotate"
!define EXE "DisplayRotate.exe"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define DOTNET_URL "https://dotnet.microsoft.com/download/dotnet/10.0"

Name "${APP} ${VERSION}"
OutFile "DisplayRotate-Setup-${VERSION}.exe"
BrandingText "${APP} ${VERSION}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; ---- per-user / per-machine ----
!define MULTIUSER_EXECUTIONLEVEL Highest
!define MULTIUSER_MUI
!define MULTIUSER_INSTALLMODE_COMMANDLINE
!define MULTIUSER_USE_PROGRAMFILES64
!define MULTIUSER_INSTALLMODE_INSTDIR "${APP}"
!define MULTIUSER_INSTALLMODE_INSTALL_REGISTRY_KEY "${APP}"
!define MULTIUSER_INSTALLMODE_INSTALL_REGISTRY_VALUENAME "UninstallString"
!include "MultiUser.nsh"

; ---- UI ----
!define MUI_ICON "displayrotate.ico"
!define MUI_UNICON "displayrotate.ico"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Launch DisplayRotate now"
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchApp"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MULTIUSER_PAGE_INSTALLMODE
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function un.onInit
  !insertmacro MULTIUSER_UNINIT
FunctionEnd

Function LaunchApp
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXE}"'
FunctionEnd

; ---- ensure .NET 10 Desktop Runtime ----
Function EnsureDotnet
  FindFirst $0 $1 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
  FindClose $0
  ${If} $1 != ""
    DetailPrint ".NET 10 Desktop Runtime found ($1)."
    Return
  ${EndIf}
  DetailPrint ".NET 10 Desktop Runtime (x64) not found."

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
  ${EndIf}
FunctionEnd

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
  MessageBox MB_YESNO|MB_ICONEXCLAMATION "${APP} requires the .NET 10 Desktop Runtime (x64), which isn't installed and couldn't be installed automatically.$\n$\nOpen the download page now?" IDNO +2
  ExecShell "open" "${DOTNET_URL}"
FunctionEnd

; ---- sections ----
Section "-Core" SEC_CORE
  SectionIn RO
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM DisplayRotate.exe'

  SetOutPath "$INSTDIR"
  File "${EXE}"
  File "displayrotate.ico"
  File "LICENSE"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayName"     "${APP}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   SHCTX "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\displayrotate.ico"
  WriteRegStr   SHCTX "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   SHCTX "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr   SHCTX "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD SHCTX "${UNINST_KEY}" "EstimatedSize" $0
  WriteRegDWORD SHCTX "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD SHCTX "${UNINST_KEY}" "NoRepair" 1

  Call EnsureDotnet
SectionEnd

Section "Start Menu shortcut" SEC_SM
  CreateShortcut "$SMPROGRAMS\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\displayrotate.ico" 0
SectionEnd

Section "Desktop shortcut" SEC_DESK
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}" "" "$INSTDIR\displayrotate.ico" 0
SectionEnd

Section "Start DisplayRotate at login" SEC_AUTO
  WriteRegStr HKCU "${RUN_KEY}" "${APP}" '"$INSTDIR\${EXE}"'
SectionEnd

Function .onInit
  !insertmacro MULTIUSER_INIT
  SectionSetFlags ${SEC_DESK} 0
FunctionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SM}   "Add a DisplayRotate shortcut to the Start Menu."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESK} "Add a DisplayRotate shortcut to the Desktop."
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AUTO} "Start DisplayRotate automatically when you sign in."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---- uninstall ----
Section "Uninstall"
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM DisplayRotate.exe'
  SetOutPath "$TEMP"

  Delete "$INSTDIR\${EXE}"
  Delete "$INSTDIR\displayrotate.ico"
  Delete "$INSTDIR\LICENSE"
  Delete "$SMPROGRAMS\${APP}.lnk"
  Delete "$DESKTOP\${APP}.lnk"

  DeleteRegValue HKCU "${RUN_KEY}" "${APP}"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKLM "${UNINST_KEY}"

  Delete "$INSTDIR\uninstall.exe"
  RMDir  "$INSTDIR"
  IfFileExists "$INSTDIR\uninstall.exe" 0 +2
    Exec '"$SYSDIR\cmd.exe" /c ping 127.0.0.1 -n 3 >nul & del /f /q "$INSTDIR\uninstall.exe" & rmdir "$INSTDIR"'
SectionEnd
