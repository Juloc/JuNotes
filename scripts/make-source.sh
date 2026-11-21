#!/usr/bin/env bash
# Writes the source file that SideStore (and AltStore) read to offer the app and its updates.
# usage: make-source.sh <ipa> <version> <owner/repo> <out.json>
set -euo pipefail

ipa="$1" version="$2" repo="$3" out="$4"
size="$(wc -c < "$ipa" | tr -d ' ')"
today="$(date -u +%Y-%m-%d)"
base="https://github.com/$repo/releases/download/latest"

cat > "$out" <<JSON
{
  "name": "JuNotes",
  "identifier": "app.junotes.source",
  "subtitle": "Notizen für die Schule",
  "apps": [
    {
      "name": "JuNotes",
      "bundleIdentifier": "app.junotes.ios",
      "developerName": "Juloc",
      "subtitle": "Hefte, Noten und Lernen auf dem iPad",
      "localizedDescription": "Handschriftliche Hefte mit dem Apple Pencil, Ordner und Fächer, Noten, Stundenplan, Karteikarten und Lernhilfen.",
      "version": "$version",
      "versionDate": "$today",
      "size": $size,
      "downloadURL": "$base/JuNotes.ipa",
      "versions": [
        {
          "version": "$version",
          "date": "$today",
          "size": $size,
          "downloadURL": "$base/JuNotes.ipa",
          "localizedDescription": "Neue Version aus der CI."
        }
      ]
    }
  ],
  "news": []
}
JSON
echo "$out"
