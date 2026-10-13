#!/usr/bin/env bash
# Runs the xplat tests on Linux from a Windows PC, inside WSL (Ubuntu).
#
#   wsl -d Ubuntu -- bash /mnt/c/<path to repo>/xplat/scripts/wsl-test.sh [--audio]
#
# Copies the repo into the Linux filesystem first (~/vs-src) so Windows and Linux never share bin/
# and obj/. --audio also runs the real-sound-server integration tests: it uses the system PulseAudio
# if installed, otherwise downloads one into ~/vs-pa (no sudo needed — apt-get download + dpkg -x;
# the package names below are Ubuntu 24.04's, and apt's package lists must exist).
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
dest="$HOME/vs-src"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
export DOTNET="$dotnet"

command -v rsync >/dev/null || { echo "rsync is needed: sudo apt-get install rsync" >&2; exit 1; }
rsync -a --delete --exclude bin --exclude obj --exclude .git "$repo/" "$dest/"
cd "$dest/xplat"
"$dotnet" test VibeSwitcher.XPlat.sln

if [ "${1:-}" = "--audio" ]; then
  if ! command -v pulseaudio >/dev/null || ! command -v pactl >/dev/null; then
    root="$HOME/vs-pa/root"
    if [ ! -x "$root/usr/bin/pulseaudio" ]; then
      echo "Downloading a private PulseAudio into ~/vs-pa (one time)..."
      mkdir -p "$HOME/vs-pa/debs" && cd "$HOME/vs-pa/debs"
      apt-get download pulseaudio pulseaudio-utils libpulse0 libasound2t64 libasyncns0 libfftw3-single3 \
        libltdl7 liborc-0.4-0t64 libsndfile1 libsoxr0 libspeexdsp1 libtdb1 libwebrtc-audio-processing1 \
        libwrap0 libice6 libsm6 libflac12t64 libvorbis0a libvorbisenc2 libopus0 libmp3lame0 libmpg123-0t64 \
        libogg0 libgomp1 libabsl20220623t64
      for f in *.deb; do dpkg -x "$f" "$root"; done
      cd "$dest/xplat"
    fi
    export VS_PA_ROOT="$root"
  fi
  bash scripts/run-linux-audio-tests.sh pulseaudio
fi
