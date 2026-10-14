#!/usr/bin/env bash
# Builds the Linux packages: a .deb and an AppImage, from a self-contained publish of the app.
#
#   xplat/packaging/linux/build-linux.sh <output folder>
#
# Needs: the .NET 10 SDK, dpkg-deb, and for the AppImage `appimagetool` (set APPIMAGETOOL to it; the
# AppImage is skipped when it isn't available). 64-bit PCs (linux-x64) for now.
set -euo pipefail
umask 022 # the package's folders must be readable by everyone, whatever the builder's umask

if [ $# -ne 1 ]; then echo "usage: $0 <output folder>" >&2; exit 2; fi
out="$(mkdir -p "$1" && cd "$1" && pwd)"
here="$(cd "$(dirname "$0")" && pwd)"
xplat="$(cd "$here/../.." && pwd)"
repo="$(cd "$xplat/.." && pwd)"
cd "$xplat" # so dotnet uses xplat/global.json's SDK, not the Windows app's at the repo root
rid=linux-x64
id=com.raphymany.vibeswitcher
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="$(dotnet msbuild "$xplat/src/VibeSwitcher.App" -getProperty:Version)"
# (Guards against anything else dotnet printed, like a first-run banner, ending up in file names.)
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "unexpected version '$version'" >&2; exit 1
fi
# Debian sorts "~" before anything, so 2.0.1~preview comes before the final 2.0.1.
debversion="${version//-/\~}"
echo "Building VibeSwitcher $version for $rid"

dotnet publish "$xplat/src/VibeSwitcher.App/VibeSwitcher.App.csproj" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -o "$work/app" -v q
# (dotnet copies some files with odd modes, like rwxr--r--: only the programs are executable.)
find "$work/app" -type f -exec chmod 644 {} +
chmod 755 "$work/app/VibeSwitcher"
if [ -f "$work/app/createdump" ]; then chmod 755 "$work/app/createdump"; fi

icons="$repo/VibeSwitcher/Resources/Icons"

# Licences: the app's, and the bundled fonts'.
licences() {
  mkdir -p "$1"
  install -m 644 "$repo/LICENSE" "$1/LICENSE"
  install -m 644 "$xplat/src/VibeSwitcher.App/Assets/Fonts/"*LICENSE* "$xplat/src/VibeSwitcher.App/Assets/Fonts/"*OFL* "$1/"
}

# ── .deb ─────────────────────────────────────────────────────────────────────────────────────────
deb="$work/deb"
mkdir -p "$deb/DEBIAN" "$deb/opt/vibeswitcher" "$deb/usr/bin" "$deb/usr/share/applications" \
  "$deb/usr/lib/udev/rules.d"
cp -a "$work/app/." "$deb/opt/vibeswitcher/"
ln -s /opt/vibeswitcher/VibeSwitcher "$deb/usr/bin/vibeswitcher"
install -m 644 "$here/$id.desktop" "$deb/usr/share/applications/$id.desktop"
for size in 16 32 64 256; do
  install -D -m 644 "$icons/vs-icon-$size.png" "$deb/usr/share/icons/hicolor/${size}x${size}/apps/$id.png"
done
install -m 644 "$here/70-vibeswitcher-headsets.rules" "$deb/usr/lib/udev/rules.d/70-vibeswitcher-headsets.rules"
licences "$deb/usr/share/doc/vibeswitcher"
install -m 644 "$repo/LICENSE" "$deb/usr/share/doc/vibeswitcher/copyright" # where Debian looks for it
install -m 755 "$here/postinst" "$deb/DEBIAN/postinst"
install -m 755 "$here/postrm" "$deb/DEBIAN/postrm"
cat > "$deb/DEBIAN/control" <<EOF
Package: vibeswitcher
Version: $debversion
Architecture: amd64
Maintainer: VibeSwitcher <raphymany@users.noreply.github.com>
Installed-Size: $(du -sk "$deb" | cut -f1)
Depends: libc6, libstdc++6, libgcc-s1, zlib1g, libfontconfig1, libx11-6, libx11-xcb1, libxcb1, libice6, libsm6, libxi6, libxrandr2, libxcursor1, libxext6, libxfixes3, pulseaudio-utils, libicu80 | libicu79 | libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu72 | libicu71 | libicu70
Recommends: libgl1, xdg-utils, libglib2.0-bin
Section: sound
Priority: optional
Homepage: https://github.com/raphymany/vibeswitcher
Description: Switch audio devices with profiles and hotkeys
 VibeSwitcher switches your speakers, headset and microphone together with one
 click or a global hotkey, on a schedule, when an app starts, or when a
 wireless headset turns on.
EOF
(cd "$deb" && find . -type f ! -path './DEBIAN/*' -printf '%P\0' | sort -z | xargs -0 md5sum > DEBIAN/md5sums)
# xz rather than the newer zstd, which older dpkg versions can't read.
dpkg-deb -Zxz --root-owner-group --build "$deb" "$out/vibeswitcher_${debversion}_amd64.deb" >/dev/null
echo "Built $out/vibeswitcher_${debversion}_amd64.deb"

# ── AppImage ─────────────────────────────────────────────────────────────────────────────────────
if [ -z "${APPIMAGETOOL:-}" ] || [ ! -x "$APPIMAGETOOL" ]; then
  echo "APPIMAGETOOL not set: skipping the AppImage"
  exit 0
fi
appdir="$work/AppDir"
mkdir -p "$appdir/usr/lib/vibeswitcher"
cp -a "$work/app/." "$appdir/usr/lib/vibeswitcher/"
cat > "$appdir/AppRun" <<'EOF'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/lib/vibeswitcher/VibeSwitcher" "$@"
EOF
chmod 755 "$appdir/AppRun"
install -m 644 "$here/$id.desktop" "$appdir/$id.desktop"
install -m 644 "$icons/vs-icon-256.png" "$appdir/$id.png"
cp "$appdir/$id.png" "$appdir/.DirIcon"
install -D -m 644 "$here/70-vibeswitcher-headsets.rules" "$appdir/usr/share/vibeswitcher/70-vibeswitcher-headsets.rules"
licences "$appdir/usr/share/doc/vibeswitcher"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" --no-appstream "$appdir" "$out/VibeSwitcher-${version}-x86_64.AppImage" >/dev/null
echo "Built $out/VibeSwitcher-${version}-x86_64.AppImage"
