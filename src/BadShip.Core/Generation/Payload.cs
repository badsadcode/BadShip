using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>What a spine ship carries between its command and engine blocks: container racks, tank clusters or pods.</summary>
public static class Payload
{
    /// <summary>
    /// Containers stacked in a cage either side of the keel (and sometimes on top). Missing boxes in the outer column
    /// show the frame; <paramref name="oneSided"/> (±1) loads only one side for an uneven, working look.
    /// </summary>
    public static void ContainerRack(BuildContext c, Module spine, int k, float zs, float ze, float sw, float sh, float oneSided)
    {
        var r = c.Shape;
        var m = c.Add($"payload_{k}", "container_rack", ModuleTier.Secondary, spine.Id);
        var cw = Num.Clamp(c.Plan.Beam * 0.1f, 2.4f, 4.6f);
        var chh = cw;
        var nx = r.Int(2, 3);
        var ny = r.Int(2, 4);
        var len = ze - zs;
        var nz = Math.Max(1, (int)((len - 1) / (cw * 3.6f)));
        var cl = (len - 1) / nz - 0.5f;
        var gap = 0.3f;
        var rackH = ny * (chh + gap) - gap;
        var y0 = -rackH / 2;
        var outer = sw / 2 + 0.4f + nx * (cw + gap);
        var sides = oneSided != 0 ? new[] { oneSided } : new[] { 1f, -1f };
        foreach (var side in sides)
        {
            for (var ix = 0; ix < nx; ix++)
                for (var iz = 0; iz < nz; iz++)
                {
                    // Each column is stacked from the bottom; the outer column may be shorter.
                    var height = ix == nx - 1 && r.Chance(0.35) ? r.Int(0, ny - 1) : ny;
                    for (var iy = 0; iy < height; iy++)
                    {
                        var x = side * (sw / 2 + 0.4f + ix * (cw + gap) + cw / 2);
                        var y = y0 + iy * (chh + gap) + chh / 2;
                        var z = zs + 0.5f + iz * (cl + 0.5f) + cl / 2;
                        Container(m.Mesh, r, new Vector3(x, y, z), new Vector3(cw, chh, cl));
                    }
                }
            // The cage: end frames and longitudinal rails at the outer corners.
            var xo = side * outer;
            var xi = side * (sw / 2 - 0.2f);
            foreach (var z in new[] { zs + 0.2f, ze - 0.2f })
            {
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(xi, y0 - 0.4f, z), new Vector3(xo, y0 - 0.4f, z), 0.5f);
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(xi, -y0 + 0.4f, z), new Vector3(xo, -y0 + 0.4f, z), 0.5f);
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(xo, y0 - 0.4f, z), new Vector3(xo, -y0 + 0.4f, z), 0.5f);
            }
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(xo, y0 - 0.4f, zs), new Vector3(xo, y0 - 0.4f, ze), 0.4f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(xo, -y0 + 0.4f, zs), new Vector3(xo, -y0 + 0.4f, ze), 0.4f);
        }
        if (r.Chance(0.5))
        {
            var top = sh / 2;
            var n = r.Int(1, 2);
            for (var ix = 0; ix < n; ix++)
                for (var iz = 0; iz < nz; iz++)
                {
                    var x = (ix - (n - 1) * 0.5f) * (cw + gap);
                    Container(m.Mesh, r, new Vector3(x, top + chh / 2 - 0.1f, zs + 0.5f + iz * (cl + 0.5f) + cl / 2), new Vector3(cw, chh, cl));
                }
        }
        c.AddMarker("cargo_hardpoint", m, new Vector3(0, -rackH / 2, (zs + ze) / 2), -Vector3.UnitY, cw);
    }

    private static void Container(Mesh m, Rng r, Vector3 center, Vector3 size)
    {
        var mat = r.Chance(0.08) ? Roles.Accent : r.Pick(Roles.Cargo);
        Shapes.Box(m, mat, center, size);
        // Corner posts read as container frames at a distance.
        var h = size * 0.5f;
        foreach (var sz in new[] { -1f, 1f })
            Shapes.Box(m, Roles.DarkMechanics, center + new Vector3(0, 0, sz * (h.Z - 0.1f)), new Vector3(size.X + 0.08f, size.Y + 0.08f, 0.3f));
    }

    /// <summary>Two to four long tanks around the keel, with domed ends, bands and saddles.</summary>
    public static void Tanks(BuildContext c, Module spine, int k, float zs, float ze, float sw, float sh)
    {
        var r = c.Shape;
        var m = c.Add($"payload_{k}", "tank_cluster", ModuleTier.Secondary, spine.Id);
        var count = r.Int(2, 4);
        var rt = Num.Clamp(c.Plan.Beam * r.Range(0.13f, 0.2f), 2f, 9f);
        var mat = r.Pick([Roles.Accent, Roles.HullSecondary, Roles.CargoA, Roles.Radome]);
        var dirs = new List<Vector2> { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) }.Take(count);
        foreach (var d in dirs)
        {
            var off = d.X != 0 ? sw / 2 + rt * 0.92f : sh / 2 + rt * 0.92f;
            var cx = d.X * off;
            var cy = d.Y * off;
            var a = new Vector3(cx, cy, zs + rt * 0.5f);
            var b = new Vector3(cx, cy, ze - rt * 0.5f);
            Shapes.Prism(m.Mesh, mat, a, b, rt, rt, 12, MathF.PI / 12, capA: false, capB: false);
            Shapes.Prism(m.Mesh, mat, b, b + new Vector3(0, 0, rt * 0.45f), rt, rt * 0.45f, 12, MathF.PI / 12, capA: false);
            Shapes.Prism(m.Mesh, mat, a, a - new Vector3(0, 0, rt * 0.45f), rt, rt * 0.45f, 12, MathF.PI / 12, capA: false);
            var bands = Math.Max(1, (int)((ze - zs) / (rt * 2.2f)));
            for (var i = 0; i <= bands; i++)
            {
                var z = float.Lerp(a.Z + 0.6f, b.Z - 0.6f, i / (float)bands);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(cx, cy, z - 0.35f), new Vector3(cx, cy, z + 0.35f), rt * 1.04f, rt * 1.04f, 12, MathF.PI / 12);
            }
            // Saddles to the keel.
            foreach (var z in new[] { a.Z + rt * 0.4f, b.Z - rt * 0.4f })
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(0, 0, z), new Vector3(cx, cy, z), MathF.Min(rt * 0.5f, 2.2f));
        }
    }

    /// <summary>Chamfered pods either side of the keel on short necks: habitation or labs (windows) or cargo.</summary>
    public static void Pods(BuildContext c, Module spine, int k, float zs, float ze, float sw, float sh, float oneSided)
    {
        var r = c.Shape;
        var p = c.Plan;
        var pw = p.Beam * r.Range(0.24f, 0.34f);
        var ph = p.Height * r.Range(0.55f, 0.85f);
        var neck = r.Range(1.2f, 3.5f);
        var py = r.Range(-0.15f, 0.2f) * p.Height;
        var sides = oneSided != 0 ? new[] { oneSided } : new[] { 1f, -1f };
        foreach (var side in sides)
        {
            var id = $"payload_{k}_{(side > 0 ? "port" : "starboard")}";
            var cx = side * (sw / 2 + neck + pw / 2);
            var pp = new Profile(cx, py, pw * 0.88f, pw, ph, pw * r.Range(0.1f, 0.2f), ph * r.Range(0.15f, 0.3f));
            var (m, loft) = c.AddLoft(id, p.Archetype == "research" ? "lab_pod" : "cargo_pod", ModuleTier.Secondary, spine.Id,
                [(zs, pp.Scaled(0.9f, 0.88f)), (zs + MathF.Min(ph * 0.25f, (ze - zs) * 0.3f), pp), (ze - MathF.Min(ph * 0.25f, (ze - zs) * 0.3f), pp), (ze, pp.Scaled(0.9f, 0.88f))],
                p.Archetype == "freighter" ? Roles.HullSecondary : Roles.HullMain, "pod");
            // Necks: boxed tunnels to the keel (they make the pod's attachment obvious).
            var nz = ze - zs;
            foreach (var t in nz > 14 ? new[] { 0.25f, 0.75f } : new[] { 0.5f })
            {
                var z = float.Lerp(zs, ze, t);
                var x0 = side * (sw / 2 - 0.3f);
                var x1 = side * (sw / 2 + neck + 0.6f);
                Shapes.BoxMinMax(m.Mesh, Roles.DarkMechanics, new Vector3(MathF.Min(x0, x1), py - 1.2f, z - 1.4f), new Vector3(MathF.Max(x0, x1), py + 1.2f, z + 1.4f));
            }
            c.AddSides(m, loft, 1, side > 0 ? Profile.FacePosX : Profile.FaceNegX);
            if (p.Archetype == "research") Parts.SideWindows(c, m, loft, 1, 0.55f);
            c.AddEnvelope(p.Archetype == "research" ? "habitable" : "cargo", m, Aabb.FromCenterSize(new Vector3(cx, py, (zs + ze) / 2), new Vector3(pw * 0.75f, ph * 0.7f, (ze - zs) * 0.85f)));
        }
    }
}
