"""Converts the meshes of an old (2.4x-2.6x, pre-BMesh) .blend into GLB files, keeping its 20 layers as groups.

    python3 blend2glb.py Shipyard.blend out/shipyard_kit [--names "2=Hull sections,3=Components,..."]

Writes one GLB per used layer (layer_NN_name.glb), an all-in-one kit.glb with a node per layer, and parts.json
(every object: layer, triangles, size, modifiers). Mirror modifiers are applied; EdgeSplit is ignored (flat shading
anyway); other modifiers are reported in parts.json. Blender's Z-up becomes glTF's Y-up; object origins are kept.
"""
import json
import math
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from blendfile import BlendFile  # noqa: E402

MOD_NAMES = {5: "mirror", 12: "array", 13: "edge_split", 1: "subsurf", 11: "boolean"}
DEFAULT_LAYER_NAMES = {1: "Workspace", 2: "Hull sections", 3: "Components", 4: "Engine builder", 5: "Hardpoints", 6: "Greebles", 20: "Example ships"}


def blender_to_gltf(v):
    x, y, z = v
    return (x, z, -y)


def read_objects(bf):
    ob_s, me_s, id_s = bf.sdna_index("Object"), bf.sdna_index("Mesh"), bf.sdna_index("ID")
    lb_s, md_s = bf.sdna_index("ListBase"), bf.sdna_index("ModifierData")
    mir_s = bf.sdna_index("MirrorModifierData")
    ma_s = bf.sdna_index("Material")
    objects = []
    for b in bf.blocks:
        if b.code != b"OB\0\0" or bf.get(ob_s, b.offset, "type") != 1:
            continue
        name = bf.get(id_s, bf.get(ob_s, b.offset, "id"), "name")[2:]
        lay = bf.get(ob_s, b.offset, "lay")
        m = bf.get(ob_s, b.offset, "obmat")  # 16 floats, obmat[i] is column i (translation in the last)
        cols = [m[0:4], m[4:8], m[8:12], m[12:16]]
        me_blk = bf.deref(bf.get(ob_s, b.offset, "data"))
        if me_blk is None:
            continue
        mb = me_blk.offset
        verts = []
        vs, voffs = bf.items(bf.get(me_s, mb, "mvert"))
        for o in voffs:
            verts.append(tuple(bf.get(vs, o, "co")))
        faces = []
        fs, foffs = bf.items(bf.get(me_s, mb, "mface"))
        for o in foffs:
            v1, v2, v3, v4 = (bf.get(fs, o, k) for k in ("v1", "v2", "v3", "v4"))
            faces.append(([v1, v2, v3] if v4 == 0 else [v1, v2, v3, v4], bf.get(fs, o, "mat_nr")))
        # Material names and colours (mesh materials).
        mats = []
        ms, moffs = bf.items(bf.get(me_s, mb, "mat"))
        for o in moffs:
            ptr = struct.unpack_from(bf.end + "Q", bf.data, o)[0] if bf.ptr == 8 else struct.unpack_from(bf.end + "I", bf.data, o)[0]
            mblk = bf.deref(ptr)
            if mblk is None:
                mats.append(("default", (0.6, 0.6, 0.6)))
            else:
                mats.append((bf.get(id_s, bf.get(ma_s, mblk.offset, "id"), "name")[2:],
                             (bf.get(ma_s, mblk.offset, "r"), bf.get(ma_s, mblk.offset, "g"), bf.get(ma_s, mblk.offset, "b"))))
        # Modifiers.
        mods, mirror_axes = [], []
        ptr = bf.get(lb_s, bf.get(ob_s, b.offset, "modifiers"), "first")
        while ptr:
            mblk = bf.deref(ptr)
            if mblk is None:
                break
            mtype = bf.get(md_s, mblk.offset, "type")
            mods.append(MOD_NAMES.get(mtype, f"type{mtype}"))
            if mtype == 5:
                flag = bf.get(mir_s, mblk.offset, "flag")
                mirror_axes = [i for i, bit in enumerate((8, 16, 32)) if flag & bit] or [0]
            ptr = bf.get(md_s, mblk.offset, "next")
        # Apply Mirror in object space: a mirrored copy per axis, winding flipped.
        for axis in mirror_axes:
            n = len(verts)
            verts += [tuple(-c if i == axis else c for i, c in enumerate(v)) for v in verts]
            faces += [([i + n for i in reversed(f)], mat) for f, mat in faces]
        objects.append({"name": name, "lay": lay, "cols": cols, "verts": verts, "faces": faces, "mats": mats, "mods": mods})
    return objects


def layers_of(lay):
    return [i + 1 for i in range(20) if lay & (1 << i)]


class Glb:
    def __init__(self):
        self.bin = bytearray()
        self.views, self.accessors, self.meshes, self.nodes, self.materials = [], [], [], [], []
        self.mat_index = {}

    def view(self, data, target):
        while len(self.bin) % 4:
            self.bin.append(0)
        self.views.append({"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data), "target": target})
        self.bin += data
        return len(self.views) - 1

    def accessor(self, view, ctype, count, typ, mn=None, mx=None):
        a = {"bufferView": view, "componentType": ctype, "count": count, "type": typ}
        if mn is not None:
            a["min"], a["max"] = mn, mx
        self.accessors.append(a)
        return len(self.accessors) - 1

    def material(self, name, color):
        key = (name, tuple(round(c, 3) for c in color))
        if key not in self.mat_index:
            lin = [c ** 2.2 for c in color]  # Blender 2.5 colours were authored without colour management
            self.materials.append({"name": name, "pbrMetallicRoughness": {"baseColorFactor": lin + [1], "metallicFactor": 0.3, "roughnessFactor": 0.6}, "doubleSided": True})  # old meshes have mixed winding; Blender draws both sides
            self.mat_index[key] = len(self.materials) - 1
        return self.mat_index[key]

    def mesh(self, ob):
        # Flat-shaded: every face gets its own vertices and normal, grouped by material.
        groups = {}
        V = [blender_to_gltf(v) for v in ob["verts"]]
        for f, mat in ob["faces"]:
            pts = [V[i] for i in f if i < len(V)]
            if len(pts) < 3:
                continue
            a, b, c = pts[0], pts[1], pts[2]
            n = cross(sub(b, a), sub(c, a))
            if len(pts) == 4:
                n = add(n, cross(sub(pts[2], pts[0]), sub(pts[3], pts[0])))
            ln = math.sqrt(dot(n, n))
            if ln < 1e-12:
                continue
            n = tuple(x / ln for x in n)
            g = groups.setdefault(mat, ([], [], []))
            base = len(g[0])
            g[0].extend(pts)
            g[1].extend([n] * len(pts))
            g[2].extend([base, base + 1, base + 2] + ([base, base + 2, base + 3] if len(pts) == 4 else []))
        prims = []
        for mat, (pos, nor, idx) in sorted(groups.items()):
            flat = [c for p in pos for c in p]
            mn = [min(flat[i::3]) for i in range(3)]
            mx = [max(flat[i::3]) for i in range(3)]
            pa = self.accessor(self.view(struct.pack(f"<{len(flat)}f", *flat), 34962), 5126, len(pos), "VEC3", mn, mx)
            na = self.accessor(self.view(struct.pack(f"<{len(nor) * 3}f", *[c for p in nor for c in p]), 34962), 5126, len(nor), "VEC3")
            big = len(pos) > 65535
            ia = self.accessor(self.view(struct.pack(f"<{len(idx)}{'I' if big else 'H'}", *idx), 34963), 5125 if big else 5123, len(idx), "SCALAR")
            mname, mcol = ob["mats"][mat] if mat < len(ob["mats"]) else ("default", (0.6, 0.6, 0.6))
            prims.append({"attributes": {"POSITION": pa, "NORMAL": na}, "indices": ia, "material": self.material(mname, mcol)})
        if not prims:
            return None
        self.meshes.append({"name": ob["name"], "primitives": prims})
        return len(self.meshes) - 1

    def node(self, d):
        self.nodes.append(d)
        return len(self.nodes) - 1

    def object_node(self, ob):
        # Node matrix: Blender's object matrix moved into glTF axes (C · M · C⁻¹), column-major.
        c = ob["cols"]
        conv = lambda v: blender_to_gltf(v)
        x, y, z, t = (conv(col[:3]) for col in c)
        # Columns of C·M·C⁻¹: the images of glTF's X, Y, Z axes.
        gx, gy, gz = x, z, tuple(-v for v in y)
        matrix = [*gx, 0, *gy, 0, *gz, 0, *t, 1]
        d = {"name": ob["name"], "matrix": matrix, "extras": {"layers": layers_of(ob["lay"]), "modifiers": ob["mods"]}}
        mi = self.mesh(ob)
        if mi is not None:
            d["mesh"] = mi
        return self.node(d)

    def write(self, path, roots, generator="BadShip blend2glb"):
        while len(self.bin) % 4:
            self.bin.append(0)
        g = {"asset": {"version": "2.0", "generator": generator}, "scene": 0, "scenes": [{"nodes": roots}], "nodes": self.nodes,
             "meshes": self.meshes, "materials": self.materials, "accessors": self.accessors, "bufferViews": self.views,
             "buffers": [{"byteLength": len(self.bin)}]}
        j = json.dumps(g, separators=(",", ":")).encode()
        j += b" " * ((4 - len(j) % 4) % 4)
        total = 12 + 8 + len(j) + 8 + len(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(j), 0x4E4F534A) + j)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin))


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    names = dict(DEFAULT_LAYER_NAMES)
    if "--names" in sys.argv:
        for kv in sys.argv[sys.argv.index("--names") + 1].split(","):
            k, v = kv.split("=", 1)
            names[int(k)] = v
    bf = BlendFile(src)
    objects = read_objects(bf)
    by_layer = {}
    for ob in objects:
        by_layer.setdefault(layers_of(ob["lay"])[0] if ob["lay"] else 0, []).append(ob)
    slug = lambda s: "".join(ch if ch.isalnum() else "_" for ch in s.lower()).strip("_")
    parts = []
    kit = Glb()
    kit_roots = []
    for layer in sorted(by_layer):
        label = f"Layer {layer} - {names.get(layer, 'unnamed')}"
        obs = sorted(by_layer[layer], key=lambda o: o["name"].lower())
        g = Glb()
        children = [g.object_node(ob) for ob in obs]
        g.write(out / f"layer_{layer:02d}_{slug(names.get(layer, 'unnamed'))}.glb", [g.node({"name": label, "children": children})])
        kit_roots.append(kit.node({"name": label, "children": [kit.object_node(ob) for ob in obs]}))
        for ob in obs:
            V = [blender_to_gltf(v) for v in ob["verts"]] or [(0, 0, 0)]
            size = [round(max(v[i] for v in V) - min(v[i] for v in V), 3) for i in range(3)]
            tris = sum(1 if len(f) == 3 else 2 for f, _ in ob["faces"])
            parts.append({"name": ob["name"], "layer": layer, "layers": layers_of(ob["lay"]), "triangles": tris, "size_local": size,
                          "materials": [m[0] for m in ob["mats"]], "modifiers": ob["mods"]})
    kit.write(out / "kit.glb", kit_roots)
    (out / "parts.json").write_text(json.dumps({"source": src.name, "blender_version": bf.version, "layers": {str(k): names.get(k, "unnamed") for k in sorted(by_layer)}, "parts": parts}, indent=1))
    print(f"{len(objects)} mesh objects in {len(by_layer)} layers -> {out}")
    for layer in sorted(by_layer):
        print(f"  layer {layer:2d} {names.get(layer, 'unnamed'):16s} {len(by_layer[layer]):3d} objects")


if __name__ == "__main__":
    main()
