#!/usr/bin/env bash
# Builds an unsigned JuNotes.ipa on macOS. Signing happens later: with a free Apple ID through iloader or Impactor, or with a developer account through TestFlight.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
app="$root/clients/ios/App"
out="${1:-$root/build}"

command -v xcodegen >/dev/null || { echo "xcodegen is missing (brew install xcodegen)" >&2; exit 1; }

(cd "$app" && xcodegen generate)
rm -rf "$out"
mkdir -p "$out"

xcodebuild \
  -project "$app/JuNotes.xcodeproj" \
  -scheme JuNotes \
  -configuration Release \
  -destination 'generic/platform=iOS' \
  -derivedDataPath "$out/derived" \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" \
  build

mkdir -p "$out/Payload"
cp -R "$out/derived/Build/Products/Release-iphoneos/JuNotes.app" "$out/Payload/"
(cd "$out" && zip -qry JuNotes.ipa Payload)
echo "$out/JuNotes.ipa"
