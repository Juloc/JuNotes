#!/usr/bin/env bash
# Starts the app in the iPad simulator with made-up school data (the -JuNotesDemo launch argument) and saves a screenshot of each main screen.
# This is how the real SwiftUI app can be looked at without a Mac of one's own: the CI runs it and uploads the pictures.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
app="$root/clients/ios/App"
out="${1:-$root/build/screenshots}"
derived="${RUNNER_TEMP:-$root/build}/sim-derived"

command -v xcodegen >/dev/null || { echo "xcodegen is missing (brew install xcodegen)" >&2; exit 1; }

# The simulator: the iPad Air 13-inch, as the real device.
device_id="$(xcrun simctl list devices available | grep -E 'iPad Air 13-inch' | head -1 | grep -oE '[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}' || true)"
if [ -z "$device_id" ]; then
  echo "No iPad Air 13-inch simulator found; available devices:" >&2
  xcrun simctl list devices available >&2
  exit 1
fi

(cd "$app" && xcodegen generate)
rm -rf "$out"
mkdir -p "$out"

xcodebuild \
  -project "$app/JuNotes.xcodeproj" \
  -scheme JuNotes \
  -configuration Debug \
  -destination "id=$device_id" \
  -derivedDataPath "$derived" \
  CODE_SIGNING_ALLOWED=NO \
  build

xcrun simctl boot "$device_id" 2>/dev/null || true
xcrun simctl bootstatus "$device_id" -b
# The headless simulator always delivers the upright picture, so these are all portrait.
# A fixed time and full battery, so the pictures do not differ by when they were taken.
xcrun simctl status_bar "$device_id" override --time "9:41" --batteryState charged --batteryLevel 100 --wifiBars 3 || true
xcrun simctl install "$device_id" "$derived/Build/Products/Debug-iphonesimulator/JuNotes.app"

shoot() { # name, screen, extra launch arguments...
  local name="$1" screen="$2"
  shift 2
  xcrun simctl terminate "$device_id" app.junotes.ios 2>/dev/null || true
  xcrun simctl launch "$device_id" app.junotes.ios -AppleLanguages "(de)" -AppleLocale de_DE -JuNotesDemo -JuNotesScreen "$screen" "$@"
  sleep 10
  xcrun simctl io "$device_id" screenshot "$out/$name.png"
}

shoot 1-heute today
shoot 2-bibliothek library
shoot 3-ordner folder
shoot 4-fach subject
shoot 5-kindi kindi
shoot 6-heft notebook
shoot 7-cover-editor coverEditor
shoot 8-karteikarten cards
shoot 9-noten grades

ls -l "$out"
