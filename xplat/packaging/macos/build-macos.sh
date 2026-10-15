#!/usr/bin/env bash
# Builds VibeSwitcher.app and a .dmg for one Mac architecture, ad-hoc signed (runs after "Open
# Anyway" once; notarize.sh signs and notarises it properly when an Apple developer account is set up).
#
#   xplat/packaging/macos/build-macos.sh <osx-arm64|osx-x64> <output folder>
#
# Needs a Mac (sips, iconutil, codesign, hdiutil) and the .NET 10 SDK.
set -euo pipefail
umask 022

if [ $# -ne 2 ]; then echo "usage: $0 <osx-arm64|osx-x64> <output folder>" >&2; exit 2; fi
rid="$1"
out="$(mkdir -p "$2" && cd "$2" && pwd)"
here="$(cd "$(dirname "$0")" && pwd)"
xplat="$(cd "$here/../.." && pwd)"
repo="$(cd "$xplat/.." && pwd)"
cd "$xplat" # so dotnet uses xplat/global.json's SDK, not the Windows app's at the repo root
case "$rid" in osx-arm64) arch=arm64 ;; osx-x64) arch=x64 ;; *) echo "unknown runtime $rid" >&2; exit 2 ;; esac
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="$(dotnet msbuild "$xplat/src/VibeSwitcher.App" -getProperty:Version)"
# (Guards against anything else dotnet printed, like a first-run banner, ending up in file names.)
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "unexpected version '$version'" >&2; exit 1
fi
short="${version%%-*}" # macOS wants plain numbers in the bundle; the pre-release mark ("-rc.1") stays in the file name
echo "Building VibeSwitcher $version for $rid"

app="$work/VibeSwitcher.app"
main="$app/Contents/MacOS/VibeSwitcher"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/Licenses"
dotnet publish "$xplat/src/VibeSwitcher.App/VibeSwitcher.App.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -o "$app/Contents/MacOS" -v q
chmod 755 "$main"

sed -e "s/@SHORT_VERSION@/$short/" "$here/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" >/dev/null

# The icon, every size macOS asks for, from the Windows app's 256 px artwork.
iconset="$work/VibeSwitcher.iconset"
mkdir -p "$iconset"
src="$repo/VibeSwitcher/Resources/Icons/vs-icon-256.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$src" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$src" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/VibeSwitcher.icns"

cp "$repo/LICENSE" "$xplat/src/VibeSwitcher.App/Assets/Fonts/"*LICENSE* "$xplat/src/VibeSwitcher.App/Assets/Fonts/"*OFL* \
  "$app/Contents/Resources/Licenses/"

# Ad-hoc signature (Apple Silicon only runs signed code; this needs no developer account). Every
# file in Contents/MacOS on its own first — codesign counts everything there as code, .NET's .dll
# and .json files included — then the app, whose signature covers the main program.
find "$app/Contents/MacOS" -type f ! -path "$main" -print0 | xargs -0 codesign --force --sign -
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"

# The disk image: the app and a shortcut to Applications, to drag it onto. (ditto keeps the
# signatures codesign stored alongside the .dll files.)
dmgdir="$work/dmg"
mkdir -p "$dmgdir"
ditto "$app" "$dmgdir/VibeSwitcher.app"
ln -s /Applications "$dmgdir/Applications"
dmg="$out/VibeSwitcher-$version-mac-$arch.dmg"
# (hdiutil sometimes fails with "Resource busy" on build machines; it works on a retry.)
for attempt in 1 2 3; do
  hdiutil create -volname "VibeSwitcher" -srcfolder "$dmgdir" -ov -format UDZO "$dmg" >/dev/null && break
  [ "$attempt" = 3 ] && exit 1
  sleep 5
done
rm -rf "$out/VibeSwitcher.app"
ditto "$app" "$out/VibeSwitcher.app"
echo "Built $dmg"
