#!/usr/bin/env bash
# Builds the BadShip Shipyard page: the generator compiled to WebAssembly plus the page, into one folder.
#   tools/shipyard/build.sh [out-dir]      (default: out/shipyard)
# Serve the folder with any static web server (it needs application/wasm for .wasm files), e.g.
#   cd out/shipyard && python3 -m http.server
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$here/../.."
out="${1:-$repo/out/shipyard}"
dotnet publish "$repo/src/BadShip.Web" -c Release -nologo -v q
rm -rf "$out" && mkdir -p "$out"
# The artifact host reserves names starting with "_", so the runtime goes in framework/ (dotnet.js loads relative to itself).
cp -r "$repo/src/BadShip.Web/bin/Release/net10.0/publish/wwwroot/_framework" "$out/framework"
rm -f "$out"/framework/*.map
{ echo '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>[hidden]{display:none!important}</style></head><body>'
  cat "$here/shipyard.html"
  echo '</body></html>'; } > "$out/index.html"
echo "built $out ($(du -sh "$out" | cut -f1))"
