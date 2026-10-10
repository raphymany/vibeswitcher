#!/usr/bin/env bash
# Drift guard for the macOS/Linux app: flags Windows files that changed after being forked into xplat/.
#   scripts/check-forks.sh            → exit 1 and list every fork whose upstream changed
#   scripts/check-forks.sh --update   → record the current upstream hashes (after porting the changes)
# Works from any directory, on Linux, macOS and Git Bash on Windows (git normalizes line endings).
set -euo pipefail

mode="${1:-check}"
case "$mode" in
  check|--update) ;;
  *) echo "usage: $0 [--update]" >&2; exit 2 ;;
esac

root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
list="$root/xplat/forks.txt"
stale=0
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line%$'\r'}"   # tolerate CRLF checkouts
  if [[ -z "${line//[[:space:]]/}" || "$line" == \#* ]]; then
    printf '%s\n' "$line" >> "$tmp"
    continue
  fi
  read -r fork upstream recorded <<< "$line"
  if [[ ! -f "$root/xplat/$fork" ]]; then
    echo "MISSING FORK: xplat/$fork (listed in forks.txt)"; stale=1
  fi
  if [[ ! -f "$root/$upstream" ]]; then
    echo "MISSING UPSTREAM: $upstream (forked into xplat/$fork) — was it moved or deleted?"; stale=1
    printf '%s\n' "$line" >> "$tmp"
    continue
  fi
  current="$(git -C "$root" hash-object "$upstream")"
  if [[ "$current" != "$recorded" ]]; then
    if [[ "$mode" == "--update" ]]; then
      echo "updated: $upstream"
    else
      echo "CHANGED: $upstream  →  review xplat/$fork"
      echo "         see: git log -p -- $upstream"
      stale=1
    fi
  fi
  printf '%s %s %s\n' "$fork" "$upstream" "$current" >> "$tmp"
done < "$list"

if [[ "$mode" == "--update" ]]; then
  awk '/^#/ || NF==0 { print; next } { printf "%-56s %-48s %s\n", $1, $2, $3 }' "$tmp" > "$list"
  if [[ "$stale" -ne 0 ]]; then echo "forks.txt updated, but fix the MISSING entries above."; exit 1; fi
  echo "forks.txt updated."
  exit 0
fi

if [[ "$stale" -ne 0 ]]; then
  echo
  echo "Port the upstream change into the fork (or confirm it doesn't apply), then run: xplat/scripts/check-forks.sh --update"
  exit 1
fi
echo "All forks are in sync with their Windows originals."
