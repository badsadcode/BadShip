#!/usr/bin/env bash
# Imports a gallery's ships into this Godot project and verifies them.
#   GODOT=/path/to/godot tools/godot-test/run.sh out/gallery
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
godot="${GODOT:-godot}"
python3 "$here/prepare.py" "$1"
rm -rf "$here/.godot"
"$godot" --headless --path "$here" --import 2>&1 | grep -iE "error|warn" | grep -v "^$" | head -40 || true
"$godot" --headless --path "$here" --script res://verify.gd
