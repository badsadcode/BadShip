#!/usr/bin/env bash
# Checks an interior in Godot: working doors and a walking path to every room.
#   GODOT=/path/to/godot tools/godot-interior/run.sh out/ship/ship_7.interior.glb
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
godot="${GODOT:-godot}"
"$godot" --headless --path "$here" --import >/dev/null 2>&1 || true
"$godot" --headless --path "$here" --script res://verify.gd -- "$(realpath "$1")"
