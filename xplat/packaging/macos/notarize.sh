#!/usr/bin/env bash
# Signs VibeSwitcher.app with a Developer ID, notarises it with Apple and staples the ticket, then
# builds a signed .dmg — so it opens without the "Open Anyway" step. Needs an Apple developer
# account (not set up yet); nothing runs this until then.
#
#   xplat/packaging/macos/notarize.sh <path to VibeSwitcher.app> <output .dmg>
#
# Environment:
#   SIGN_IDENTITY   "Developer ID Application: <name> (<team id>)" in the keychain
#   NOTARY_PROFILE  the notarytool keychain profile, made once with
#                   `xcrun notarytool store-credentials <name> --apple-id … --team-id …`
#                   (keeps the password out of command lines)
set -euo pipefail
umask 022

if [ $# -ne 2 ]; then echo "usage: $0 <path to VibeSwitcher.app> <output .dmg>" >&2; exit 2; fi
app="$1"
dmg="$2"
here="$(cd "$(dirname "$0")" && pwd)"
: "${SIGN_IDENTITY:?}" "${NOTARY_PROFILE:?}"
main="$app/Contents/MacOS/VibeSwitcher"

# Every file in Contents/MacOS on its own first (codesign counts everything there as code, .NET's
# .dll and .json files included), then the app — whose signature covers the main program, the only
# one that gets the entitlements. All with the hardened runtime notarisation requires.
find "$app/Contents/MacOS" -type f ! -path "$main" -print0 |
  xargs -0 codesign --force --timestamp --options runtime --sign "$SIGN_IDENTITY"
codesign --force --timestamp --options runtime --entitlements "$here/entitlements.plist" --sign "$SIGN_IDENTITY" "$app"
codesign --verify --deep --strict "$app"

# notarytool exits 0 even when Apple rejects the upload, so check the verdict (and show why).
notarise() {
  local result id
  result="$(xcrun notarytool submit "$1" --keychain-profile "$NOTARY_PROFILE" --wait --output-format json)"
  id="$(grep -o '"id" *: *"[^"]*"' <<<"$result" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')"
  if ! grep -q '"status" *: *"Accepted"' <<<"$result"; then
    echo "Notarisation failed: $result" >&2
    [ -n "$id" ] && xcrun notarytool log "$id" --keychain-profile "$NOTARY_PROFILE" >&2
    exit 1
  fi
}

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
ditto -c -k --keepParent "$app" "$work/VibeSwitcher.zip"
notarise "$work/VibeSwitcher.zip"
xcrun stapler staple "$app"

mkdir -p "$work/dmg"
ditto "$app" "$work/dmg/VibeSwitcher.app"
ln -s /Applications "$work/dmg/Applications"
# (hdiutil sometimes fails with "Resource busy" on build machines; it works on a retry.)
for attempt in 1 2 3; do
  hdiutil create -volname "VibeSwitcher" -srcfolder "$work/dmg" -ov -format UDZO "$dmg" >/dev/null && break
  [ "$attempt" = 3 ] && exit 1
  sleep 5
done
codesign --force --timestamp --sign "$SIGN_IDENTITY" "$dmg"
notarise "$dmg"
xcrun stapler staple "$dmg"
echo "Notarised $dmg"
