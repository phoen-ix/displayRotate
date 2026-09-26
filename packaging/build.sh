#!/usr/bin/env sh
# Build all three DisplayRotate installers. Run from this folder.
# Usage:  ./build.sh <version>    e.g.  ./build.sh 0.1.0
# Needs the .NET 10 SDK and makensis (Linux: apt install nsis). The same steps as ci.yml's
# release job, which is what actually ships.
set -e
[ -n "$1" ] || { echo "Usage: ./build.sh <version>  (e.g. ./build.sh 0.1.0)"; exit 1; }

proj=../src/DisplayRotate/DisplayRotate.csproj

# Full: self-contained, needs nothing installed.
dotnet publish "$proj" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version="$1" -o ../publish-full
# Minimal: framework-dependent, needs the .NET 10 Desktop Runtime.
dotnet publish "$proj" -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:DebugType=none -p:Version="$1" -o ../publish-min

cp ../publish-full/DisplayRotate.exe DisplayRotate.exe
cp ../publish-min/DisplayRotate.exe DisplayRotate-min.exe

makensis -WX -V2 -DVERSION="$1" displayrotate.nsi
makensis -WX -V2 -DVERSION="$1" -DFULL_ONLY displayrotate.nsi
makensis -WX -V2 -DVERSION="$1" -DMINIMAL_ONLY displayrotate.nsi
echo "Built DisplayRotate-Setup-$1.exe, DisplayRotate-Setup-$1-full.exe and DisplayRotate-Setup-$1-min.exe"
