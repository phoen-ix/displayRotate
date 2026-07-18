@echo off
rem Build RotateIt installer. Run from this folder.
rem Usage:  build.bat <version>    e.g.  build.bat 1.0.0
setlocal
if "%~1"=="" (
  echo Usage: build.bat ^<version^>   e.g.  build.bat 1.0.0
  exit /b 1
)

rem Copy required files into this folder
copy /y "..\publish\RotateIt.exe" . >nul || exit /b 1
copy /y "..\RotateIt\app.ico" "rotateit.ico" >nul || exit /b 1
copy /y "..\LICENSE" . >nul || exit /b 1

makensis /DVERSION=%~1 rotateit.nsi || exit /b 1

echo.
echo Built RotateIt-Setup-%~1.exe
