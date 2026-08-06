#!/usr/bin/env bash
# Exports a signed release APK named after the project's own version, keeping
# every place that version number shows up in lockstep instead of hand-edited
# separately:
#   - godot/project.godot's application/config/version is the single source
#     of truth. SplashScreen.cs reads it directly at runtime (the "v1.1.0"
#     text on the splash screen).
#   - godot/export_presets.cfg's version/name (both presets — APK and AAB)
#     is overwritten to match it here, and version/code (Android's required
#     monotonically-increasing build number) is bumped by 1 every run.
#   - The output filename embeds it: builds/android/SlingTTEvolution-vX.Y.Z.apk.
#
# To cut a new version: edit project.godot's config/version, then run this.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT_DIR="$REPO_ROOT/godot"
GODOT_BIN="${GODOT_BIN:-D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe}"
PRESETS="$GODOT_DIR/export_presets.cfg"
OUT_DIR="$REPO_ROOT/builds/android"

VERSION=$(grep -m1 '^config/version=' "$GODOT_DIR/project.godot" | sed -E 's/config\/version="(.*)"/\1/')
if [ -z "$VERSION" ]; then
  echo "error: could not read application/config/version from project.godot" >&2
  exit 1
fi
echo "Exporting version $VERSION"

if [ ! -f "$PRESETS" ]; then
  echo "error: $PRESETS not found — export_presets.cfg is gitignored (carries the release keystore path/password); create it locally from export_presets.cfg.example first" >&2
  exit 1
fi

CURRENT_CODE=$(grep -m1 '^version/code=' "$PRESETS" | sed -E 's/version\/code=([0-9]+)/\1/')
NEXT_CODE=$((CURRENT_CODE + 1))
sed -i -E "s/^version\/code=[0-9]+/version\/code=$NEXT_CODE/" "$PRESETS"
sed -i -E "s/^version\/name=\".*\"/version\/name=\"$VERSION\"/" "$PRESETS"
echo "export_presets.cfg: version/name=\"$VERSION\", version/code=$NEXT_CODE (both presets)"

mkdir -p "$OUT_DIR"

(cd "$GODOT_DIR" && "$GODOT_BIN" --headless --export-release "Android-APK")

RAW_APK="$REPO_ROOT/SlingTTEvolution.apk"
if [ ! -f "$RAW_APK" ]; then
  echo "error: export did not produce $RAW_APK — check the Godot output above" >&2
  exit 1
fi

FINAL_APK="$OUT_DIR/SlingTTEvolution-v$VERSION.apk"
mv "$RAW_APK" "$FINAL_APK"
echo "Built $FINAL_APK"
