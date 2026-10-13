#!/usr/bin/env bash
# Runs the Linux audio integration tests against a private, throwaway sound server, so nothing on
# the machine's real audio setup is touched.
#
#   scripts/run-linux-audio-tests.sh [pulseaudio|pipewire]
#
# Needs pactl/pacat/parec (pulseaudio-utils) plus the chosen server: `pulseaudio`, or `pipewire`,
# `pipewire-pulse` and `wireplumber`. Without sudo (e.g. WSL), point VS_PA_ROOT at an extracted
# PulseAudio package tree (see scripts/wsl-test.sh) and it's used instead of the system one.
# DOTNET overrides the dotnet executable.
set -euo pipefail

server="${1:-pulseaudio}"
xplat="$(cd "$(dirname "$0")/.." && pwd)"
dotnet="${DOTNET:-dotnet}"

runtime="$(mktemp -d)"
export XDG_RUNTIME_DIR="$runtime"
unset PULSE_SERVER || true
# Never let a client (pactl in the wait loop below) autospawn a second, fully configured PulseAudio.
echo "autospawn = no" > "$runtime/client.conf"
export PULSE_CLIENTCONFIG="$runtime/client.conf"
pids=()
cleanup() {
  for p in "${pids[@]:-}"; do [ -n "$p" ] && kill "$p" 2>/dev/null || true; done
  wait 2>/dev/null || true
  rm -rf "$runtime"
}
trap cleanup EXIT

dlpath=()
if [ -n "${VS_PA_ROOT:-}" ]; then
  lib="$(ls -d "$VS_PA_ROOT"/usr/lib/*-linux-gnu | head -1)"
  mods="$(ls -d "$VS_PA_ROOT"/usr/lib/pulse-*/modules)"
  export PATH="$VS_PA_ROOT/usr/bin:$PATH"
  export LD_LIBRARY_PATH="$lib:$lib/pulseaudio:$mods${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  dlpath=(--dl-search-path="$mods")
fi

case "$server" in
  pulseaudio)
    # -n: skip the default startup script (no hardware probing); only what the tests need.
    pulseaudio -n --daemonize=no --exit-idle-time=-1 --use-pid-file=no "${dlpath[@]}" \
      -L module-native-protocol-unix --log-target=stderr --log-level=error &
    pids+=($!)
    ;;
  pipewire)
    pipewire & pids+=($!)
    sleep 0.5
    wireplumber & pids+=($!)
    pipewire-pulse & pids+=($!)
    ;;
  *)
    echo "usage: $0 [pulseaudio|pipewire]" >&2
    exit 2
    ;;
esac

for _ in $(seq 1 50); do
  pactl info >/dev/null 2>&1 && break
  sleep 0.2
done
pactl info | grep -E '^(Server Name|Server Version):' || { echo "sound server did not start" >&2; exit 1; }
pactl --version | head -1

cd "$xplat"
VS_LINUX_AUDIO_IT=1 "$dotnet" test tests/VibeSwitcher.Platform.Linux.Tests/VibeSwitcher.Platform.Linux.Tests.csproj \
  --filter "Category=LinuxAudio" --logger "console;verbosity=normal"
