#!/usr/bin/env bash
# Build Vitara's iOS app on a Mac and open it in Xcode, ready to Run on a connected iPhone.
#
#   cd vitara/web
#   bash scripts/native-ios.sh http://<server-address>:3100 [http://<address-to-check-from-here>]
#
# The first address is where the PHONE will find Vitara: the same one you open in Safari on the
# phone. It is baked into the app at build time (VITE_API_BASE), so build again if it changes.
#
# The optional second address is where THIS Mac checks the server before building. Needed when
# the Mac is the server itself: the phone reaches it over the mesh, but a machine often cannot
# reach its own mesh address. Then:
#   bash scripts/native-ios.sh http://<mesh-ip>:3100 http://localhost:3100
#
# What it does, in order, stopping with a reason at the first thing that is wrong:
#   1. checks the Mac (Node 22+, Xcode) and that the server answers and allows the native app;
#   2. builds the web app with the server address in it;
#   3. creates the Xcode project the first time (ios/, not tracked in git), then copies the
#      build into it;
#      adds the Apple Health permission text and entitlement;
#   4. for an http:// address, lets this one app talk plain http (Apple blocks it by default);
#   5. opens Xcode. There: pick your iPhone at the top, set your Team once under
#      Signing & Capabilities, press Run.
set -euo pipefail
cd "$(dirname "$0")/.."

SERVER="${1:-}"
CHECK="${2:-$SERVER}"
say()  { printf '\n\033[1m%s\033[0m\n' "$*"; }
fail() { printf '\n\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

[ -n "$SERVER" ] || fail "Usage: bash scripts/native-ios.sh http://<server-address>:3100"
case "$SERVER" in http://*|https://*) ;; *) fail "Give the full address, starting with http:// or https://";; esac
SERVER="${SERVER%/}"
CHECK="${CHECK%/}"

# ── 1. The Mac and the server ────────────────────────────────────────────────
say "1/5  Checking this Mac"
[ "$(uname)" = "Darwin" ] || fail "This needs a Mac: iOS apps are built with Xcode."
command -v node >/dev/null || fail "Node is not installed. Install it with:  brew install node"
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]')"
[ "$NODE_MAJOR" -ge 22 ] || fail "Node $(node -v) is too old; Capacitor needs 22 or newer.  brew upgrade node"
command -v xcodebuild >/dev/null || fail "Xcode is not installed (App Store), or run:  xcode-select --install"
echo "  node $(node -v), $(xcodebuild -version | head -1)"

say "     Checking the server at $CHECK"
CODE="$(curl -s -o /dev/null -m 8 -w '%{http_code}' "$CHECK/svc/vault/api/auth/probe" || true)"
[ "$CODE" = "200" ] || fail "The server did not answer (got '$CODE' from $CHECK/svc/vault/api/auth/probe).
If this Mac IS the server, check through localhost instead:
  bash scripts/native-ios.sh $SERVER http://localhost:3100"
if ! curl -s -D - -o /dev/null -m 8 -H 'Origin: capacitor://localhost' "$CHECK/svc/vault/api/auth/probe" \
     | grep -qi '^access-control-allow-origin: capacitor://localhost'; then
  fail "The server is up but does not allow the native app yet (no CORS for capacitor://localhost).
It is running an older vitara-web. Deploy the current one to the server first, then run this again."
fi
echo "  answers, and allows the native app"
[ "$CHECK" = "$SERVER" ] || echo "  (checked through $CHECK; the app itself will use $SERVER)"

# ── 2. Build ────────────────────────────────────────────────────────────────
say "2/5  Building the app for $SERVER"
npm ci --no-audit --no-fund
VITE_API_BASE="$SERVER" npm run build

# ── 3. The Xcode project ────────────────────────────────────────────────────
say "3/5  Preparing the Xcode project"
[ -d ios ] || npx cap add ios
npx cap sync ios

# Apple Health (native/vitara-health). Two things Apple requires before an app may even ask:
# the sentence shown in the permission sheet, and the HealthKit entitlement. Both are written
# every run, so a project created before this existed picks them up too.
PLIST="ios/App/App/Info.plist"
/usr/libexec/PlistBuddy -c "Delete :NSHealthShareUsageDescription" "$PLIST" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :NSHealthShareUsageDescription string Vitara reads your steps, workouts, sleep, heart rate and weight to compare each day with your own normal. It never writes to Health." "$PLIST"
cat > ios/App/App/App.entitlements <<'ENT'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.developer.healthkit</key>
  <true/>
  <key>com.apple.developer.healthkit.access</key>
  <array/>
</dict>
</plist>
ENT
PBX="ios/App/App.xcodeproj/project.pbxproj"
if ! grep -q "CODE_SIGN_ENTITLEMENTS" "$PBX"; then
  # Beside the bundle id in each build configuration (Debug and Release) of the app target;
  # Xcode reflows the line the next time it saves the project.
  sed -i '' 's|PRODUCT_BUNDLE_IDENTIFIER = app.vitara.mobile;|CODE_SIGN_ENTITLEMENTS = App/App.entitlements; PRODUCT_BUNDLE_IDENTIFIER = app.vitara.mobile;|' "$PBX"
fi
grep -q "CODE_SIGN_ENTITLEMENTS" "$PBX" || fail "Could not add the HealthKit entitlement to the Xcode project.
In Xcode: App > Signing & Capabilities > + Capability > HealthKit, then run this again."
echo "  Apple Health: permission text and entitlement in place"

# ── 4. Plain http, for this app only ────────────────────────────────────────
if [[ "$SERVER" == http://* ]]; then
  say "4/5  Allowing plain http (the server address is not https)"
  /usr/libexec/PlistBuddy -c "Delete :NSAppTransportSecurity" "$PLIST" 2>/dev/null || true
  /usr/libexec/PlistBuddy -c "Add :NSAppTransportSecurity dict" \
                          -c "Add :NSAppTransportSecurity:NSAllowsArbitraryLoads bool true" "$PLIST"
  echo "  Fine for an app on your own phone. An App Store build needs https instead."
else
  say "4/5  https address: nothing to allow"
fi

# ── 5. Xcode ────────────────────────────────────────────────────────────────
say "5/5  Opening Xcode"
cat <<'NEXT'
  In Xcode:
    a. At the top, choose your iPhone as the run destination.
    b. Click "App" in the left column, then Signing & Capabilities: tick
       "Automatically manage signing" and pick your Team (your Apple ID). Once only.
    c. Press Run (the triangle).
  First time on the phone: Settings > General > VPN & Device Management > trust your
  developer certificate, then open Vitara again.
NEXT
npx cap open ios
