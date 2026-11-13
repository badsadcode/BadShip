# blendread

A small, dependency-free reader for old uncompressed `.blend` files (Blender 2.4x–2.6x, before BMesh), and a
converter that turns their meshes into GLB while keeping the old 20 **layers** as named groups (Blender 2.8+ replaced
layers with collections, and very old files don't always come across).

```bash
python3 tools/blendread/blend2glb.py Kit.blend out/kit --names "2=Hull sections,3=Components"
node tools/render/kitview.mjs out/kit      # <layer>_parts.png contact sheets and <layer>_overview.png
```

Output: `layer_NN_<name>.glb` per used layer, `kit.glb` with one node per layer, and `parts.json` (every object with
its layer(s), triangles, local size, materials and modifiers). Mirror modifiers are applied; EdgeSplit is ignored
(output is flat-shaded); other modifiers are listed but not applied. Materials are exported double-sided because old
meshes often have mixed face winding (Blender draws both sides). Blender's Z-up becomes glTF's Y-up; object origins
and transforms are kept, so the GLBs open in current Blender with each part where it was.

The reader parses the file's own SDNA (struct catalogue), so fields are read by name rather than fixed offsets.
