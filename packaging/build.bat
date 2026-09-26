@echo off
rem Build all three DisplayRotate installers. Run from this folder.
rem Usage:  build.bat <version>    e.g.  build.bat 0.1.0
rem Needs the .NET 10 SDK and NSIS (makensis on PATH). The same steps as ci.ymls
rem release job, which is what actually ships.
setlocal
if "%~1"=="" (
  echo Usage: build.bat ^<version^>   e.g.  build.bat 0.1.0
  exit /b 1
)
set PROJ=..\src\DisplayRotate\DisplayRotate.csproj

rem Full: self-contained, needs nothing installed.
dotnet publish %PROJ% -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=%~1 -o ..\publish-full || exit /b 1
rem Minimal: framework-dependent, needs the .NET 10 Desktop Runtime.
dotnet publish %PROJ% -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:Version=%~1 -o ..\publish-min || exit /b 1

copy /y "..\publish-full\DisplayRotate.exe" "DisplayRotate.exe" >nul || exit /b 1
copy /y "..\publish-min\DisplayRotate.exe" "DisplayRotate-min.exe" >nul || exit /b 1

makensis /WX /V2 /DVERSION=%~1 displayrotate.nsi || exit /b 1
makensis /WX /V2 /DVERSION=%~1 /DFULL_ONLY displayrotate.nsi || exit /b 1
makensis /WX /V2 /DVERSION=%~1 /DMINIMAL_ONLY displayrotate.nsi || exit /b 1

echo.
echo Built DisplayRotate-Setup-%~1.exe, DisplayRotate-Setup-%~1-full.exe and DisplayRotate-Setup-%~1-min.exe
