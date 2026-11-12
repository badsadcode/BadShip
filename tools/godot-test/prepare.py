"""Copies a gallery's ships into ships/ and writes ships/manifest.json with what each file should contain."""
import json, shutil, sys
from pathlib import Path

src = Path(sys.argv[1])
dst = Path(__file__).parent / "ships"
shutil.rmtree(dst, ignore_errors=True)
dst.mkdir()
manifest = []
for entry in json.loads((src / "index.json").read_text()):
    name = entry["name"]
    meta = json.loads((src / f"{name}.metadata.json").read_text())
    for suffix in ["", ".lod1", ".lod2", ".collision"]:
        shutil.copy(src / f"{name}{suffix}.glb", dst / f"{name}{suffix}.glb")
    manifest.append({
        "name": name,
        "size": meta["bounds"]["size"],
        "triangles": meta["triangles"],
        "lod_triangles": [l["triangles"] for l in meta["lods"]],
        "collision_pieces": meta["collision"]["pieces"],
        "modules": [m["id"] for m in meta["modules"]],
        "mesh_modules": [m["id"] for m in meta["modules"] if m.get("bounds")],
        "markers": len(meta["markers"]),
        "materials": sorted({mat["role"] for mat in meta["materials"]}),
    })
(dst / "manifest.json").write_text(json.dumps(manifest, indent=1))
print(f"prepared {len(manifest)} ships in {dst}")
