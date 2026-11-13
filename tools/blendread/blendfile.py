"""A small reader for uncompressed .blend files (the format Blender has used since the 2.4x days).

It parses the file header, the file blocks and the SDNA (the struct catalogue every .blend carries), so struct
fields can be read by name: enough to pull out objects, meshes (pre-BMesh MVert/MFace and BMesh MPoly/MLoop),
transforms and material names for reference work. No Blender needed."""
import struct
from dataclasses import dataclass


@dataclass
class Block:
    code: bytes
    size: int
    old: int
    sdna: int
    count: int
    offset: int  # file offset of the data


class BlendFile:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        head = self.data[:12]
        if head[:7] != b"BLENDER":
            raise ValueError("not an uncompressed .blend file")
        self.ptr = 8 if head[7:8] == b"-" else 4
        self.end = "<" if head[8:9] == b"v" else ">"
        self.version = head[9:12].decode()
        self.blocks = []
        pos = 12
        hfmt = self.end + ("4sIQII" if self.ptr == 8 else "4sIIII")
        hsize = struct.calcsize(hfmt)
        while pos + hsize <= len(self.data):
            code, size, old, sdna, count = struct.unpack_from(hfmt, self.data, pos)
            pos += hsize
            self.blocks.append(Block(code, size, old, sdna, count, pos))
            pos += size
            if code == b"ENDB":
                break
        self.by_old = {b.old: b for b in self.blocks if b.old}
        self._parse_sdna(next(b for b in self.blocks if b.code == b"DNA1"))

    # ------------------------------------------------------------------ SDNA
    def _parse_sdna(self, blk):
        d, e = self.data, self.end
        p = blk.offset + 4  # "SDNA"

        def tag(name):
            nonlocal p
            assert d[p:p + 4] == name, name
            p += 4

        def strings(n):
            nonlocal p
            out = []
            for _ in range(n):
                q = d.index(b"\0", p)
                out.append(d[p:q].decode("latin-1"))
                p = q + 1
            return out

        def align():
            nonlocal p
            p = (p + 3) & ~3

        tag(b"NAME"); n = struct.unpack_from(e + "I", d, p)[0]; p += 4; self.names = strings(n); align()
        tag(b"TYPE"); n = struct.unpack_from(e + "I", d, p)[0]; p += 4; self.types = strings(n); align()
        tag(b"TLEN"); self.tlen = list(struct.unpack_from(e + f"{n}H", d, p)); p += 2 * n; align()
        tag(b"STRC"); n = struct.unpack_from(e + "I", d, p)[0]; p += 4
        self.structs = []
        for _ in range(n):
            t, nf = struct.unpack_from(e + "HH", d, p); p += 4
            fields = []
            for _ in range(nf):
                ft, fn = struct.unpack_from(e + "HH", d, p); p += 4
                fields.append((ft, fn))
            self.structs.append((t, fields))
        self.struct_of_type = {t: i for i, (t, _) in enumerate(self.structs)}
        # Field layouts: name -> (offset, type name, raw field name, size)
        self.layout = []
        for t, fields in self.structs:
            off, lay = 0, {}
            for ft, fn in fields:
                raw = self.names[fn]
                size = self._field_size(ft, raw)
                key = raw.lstrip("*").split("[")[0].replace("(", "").replace(")", "")
                lay[key] = (off, self.types[ft], raw, size)
                off += size
            self.layout.append(lay)

    def _field_size(self, ft, raw):
        mult = 1
        for part in raw.split("[")[1:]:
            mult *= int(part.split("]")[0])
        if raw.startswith("*") or raw.startswith("(*"):
            return self.ptr * mult
        return self.tlen[ft] * mult

    def struct_name(self, sdna):
        return self.types[self.structs[sdna][0]]

    def sdna_index(self, type_name):
        return self.struct_of_type[self.types.index(type_name)]

    # ------------------------------------------------------------------ reading fields
    def get(self, sdna, base, field):
        off, tname, raw, size = self.layout[sdna][field]
        pos = base + off
        e, d = self.end, self.data
        if raw.startswith("*") or raw.startswith("(*"):
            return struct.unpack_from(e + ("Q" if self.ptr == 8 else "I"), d, pos)[0]
        n = size // max(1, self.tlen[self.types.index(tname)]) if tname in self.types else 1
        fmt = {"float": "f", "double": "d", "int": "i", "short": "h", "char": "b", "uchar": "B", "ushort": "H", "uint": "I"}.get(tname)
        if tname == "char" and "[" in raw:
            return d[pos:pos + size].split(b"\0")[0].decode("latin-1")
        if fmt is None:
            return pos  # a nested struct: return its file offset
        vals = struct.unpack_from(e + fmt * n, d, pos)
        return vals[0] if n == 1 else list(vals)

    def has(self, sdna, field):
        return field in self.layout[sdna]

    def deref(self, ptr):
        return self.by_old.get(ptr)

    def items(self, ptr):
        """The struct array a pointer points at: (sdna, offsets...)."""
        b = self.deref(ptr)
        if b is None:
            return None, []
        size = self.tlen[self.structs[b.sdna][0]]
        return b.sdna, [b.offset + i * size for i in range(b.count)]
