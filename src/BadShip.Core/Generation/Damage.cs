using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;
using BadShip.Validation;

namespace BadShip.Generation;

/// <summary>
/// Condition variants that keep a ship's identity: the same hull and layout, then damage on top.
/// damaged: hull breaches (jagged hole, exposed ribs, peeled plates, scorch, sometimes embers), one engine cold,
/// some snapped or leaning masts and antennas, some sections dark. derelict: more and bigger breaches, every light
/// out, engines cold, faded paint and livery, detachable cargo and container stacks gone, and drifting debris.
/// Runs after livery and before the surface plates (which then avoid the breaches), on its own random stream.
/// </summary>
public static class Damage
{
    public const string Scorch = "scorch", Breach = "breach", Embers = "embers", GlassDark = "glass_dark", LightOff = "light_off", EngineCold = "engine_cold", Heat = "heat";

    public static void Apply(BuildContext c)
    {
        var condition = c.Plan.Condition;
        if (condition is not ("damaged" or "derelict")) return;
        var derelict = condition == "derelict";
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("condition") is { } cs and > 0 ? cs : c.Recipe.Seed, "condition");
        AddMaterials(c, derelict);

        if (derelict) RemoveCargo(c, r);
        BreakEquipment(c, r, derelict);
        Breaches(c, r, derelict);
        Lights(c, r, derelict);
        if (derelict) Fade(c);
    }

    /// <summary>After the surface details: a derelict's running lights (added by that pass) go dark too.</summary>
    public static void AfterDetails(BuildContext c)
    {
        if (c.Plan.Condition == "derelict") Lights(c, new Rng(0), derelict: true);
    }

    private static void AddMaterials(BuildContext c, bool derelict)
    {
        var m = c.Ship.Materials;
        void Add(string role, string hex, float rough, float metal, string? emissive = null, float strength = 1) =>
            m[role] = new MaterialDef(role, Palettes.Linear(hex), rough, metal, emissive == null ? Vector3.Zero : Palettes.Linear(emissive), strength);
        Add(Scorch, "#1a1714", 0.95f, 0.1f);
        Add(Heat, "#7a4524", 0.9f, 0.3f);
        Add(Breach, "#060606", 1f, 0f);
        Add(Embers, "#3a1608", 0.9f, 0f, "#ff6a1a", 2.2f);
        Add(GlassDark, "#121619", 0.35f, 0.3f);
        Add(LightOff, "#3b3c3a", 0.7f, 0.1f);
        Add(EngineCold, "#252b31", 0.6f, 0.4f);
    }

    // ------------------------------------------------------------------ breaches

    private static void Breaches(BuildContext c, Rng r, bool derelict)
    {
        var L = c.Plan.Length;
        var count = derelict ? r.Int(7, 12) : r.Int(3, 6);
        if (c.Plan.Size == "small") count = Math.Max(1, count / 2);
        var faces = new List<(LoftPart part, int s, int f, float along, float across)>();
        var alive = c.Ship.Modules.Select(m => m.Id).ToHashSet();
        foreach (var part in c.Lofts.Where(l => l.Kind is "hull" or "tier" or "pod" && alive.Contains(l.Module.Id)))
            for (var s = 0; s < part.Loft.Segments; s++)
            {
                if (part.Loft.IsStep(s)) continue;
                foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX, Profile.FaceTop, Profile.FaceTopPosX, Profile.FaceTopNegX, Profile.FaceBottomPosX, Profile.FaceBottomNegX })
                {
                    var (along, across) = part.Loft.FaceSize(s, f);
                    if (along > 4 && across > 2.5f) faces.Add((part, s, f, along, across));
                }
            }
        if (faces.Count == 0) return;
        for (var k = 0; k < count; k++)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                // Bigger faces are hit more often.
                var (part, s, f, along, across) = r.Weighted(faces.Select(x => (x, (double)(x.along * x.across))).ToList());
                var R = MathF.Min(Num.Clamp(L * (derelict ? r.Range(0.028f, 0.065f) : r.Range(0.022f, 0.045f)), 1.4f, 13f), MathF.Min(along, across) * 0.3f);
                if (R < 0.8f) continue;
                var du = R * 2.2f / along;
                var dv = R * 2.2f / across;
                var u = r.Range(du + 0.03f, 1 - du - 0.03f);
                var v = r.Range(dv + 0.03f, 1 - dv - 0.03f);
                var key = BuildContext.FaceKey(part.Module, s, f);
                if (!c.TryUseSurface(key, u - du, u + du, v - dv, v + dv)) continue;
                var n = part.Loft.FaceNormal(s, f);
                var center = part.Loft.FacePoint(s, f, u, v);
                Hole(c, r, c.DamageOf(part.Module), center, n, R, embers: !derelict && r.Chance(0.6), debris: derelict);
                break;
            }
        }
        // Stripped sections: plating gone, the ribbed frame showing.
        var stripped = derelict ? r.Int(2, 5) : r.Int(1, 2);
        for (var k = 0; k < stripped; k++)
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var (part, s, f, along, across) = r.Weighted(faces.Select(x => (x, (double)(x.along * x.across))).ToList());
                var w = MathF.Min(along * r.Range(0.12f, 0.25f), L * 0.08f);
                var h = across * r.Range(0.4f, 0.75f);
                if (w < 2 || h < 1.5f) continue;
                var du = w / along;
                var dv = h / across;
                var u0 = r.Range(0.05f, 0.95f - du);
                var v0 = r.Range(0.08f, 0.92f - dv);
                var key = BuildContext.FaceKey(part.Module, s, f);
                if (!c.TryUseSurface(key, u0, u0 + du, v0, v0 + dv)) continue;
                Frame(c.DamageOf(part.Module).Mesh, part.Loft, s, f, u0, u0 + du, v0, v0 + dv, along, across);
                break;
            }
    }

    /// <summary>A rectangle of missing plating: dark cavity, a grid of frame ribs, scorched edge.</summary>
    private static void Frame(Mesh m, Loft loft, int s, int f, float u0, float u1, float v0, float v1, float along, float across)
    {
        var eu = 0.4f / along;
        var ev = 0.4f / across;
        Shapes.Patch(m, Scorch, loft, s, f, u0 - eu, u1 + eu, v0 - ev, v1 + ev, 0.015f);
        Shapes.Patch(m, Breach, loft, s, f, u0, u1, v0, v1, 0.03f);
        var n = loft.FaceNormal(s, f);
        var ribsU = Math.Max(2, (int)((u1 - u0) * along / 1.6f));
        for (var i = 0; i <= ribsU; i++)
        {
            var u = float.Lerp(u0, u1, i / (float)ribsU);
            Shapes.Beam(m, Roles.HullSecondary, loft.FacePoint(s, f, u, v0) + n * 0.12f, loft.FacePoint(s, f, u, v1) + n * 0.12f, 0.16f);
        }
        foreach (var v in new[] { v0 + (v1 - v0) * 0.33f, v0 + (v1 - v0) * 0.66f })
            Shapes.Beam(m, Roles.DarkMechanics, loft.FacePoint(s, f, u0, v) + n * 0.2f, loft.FacePoint(s, f, u1, v) + n * 0.2f, 0.12f);
    }

    /// <summary>One breach: scorched surround, a jagged black hole, ribs across it, plates peeled back around the rim.</summary>
    private static void Hole(BuildContext c, Rng r, Module m, Vector3 center, Vector3 n, float R, bool embers, bool debris)
    {
        var up = MathF.Abs(n.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(n, up));
        up = Vector3.Cross(right, n);
        float[] Jag(int pts, float spread) => Enumerable.Range(0, pts).Select(_ => r.Range(1 - spread, 1 + spread * 0.4f)).ToArray();
        // Rings out from the hole: heat-discoloured metal, scorch, the breach itself.
        Star(m.Mesh, Heat, center, right, up, n, R * 2.1f, Jag(16, 0.3f), 0.015f, -0.005f);
        Star(m.Mesh, Scorch, center, right, up, n, R * 1.55f, Jag(14, 0.35f), 0.02f, 0.005f);
        Star(m.Mesh, Breach, center, right, up, n, R, Jag(12, 0.45f), 0.03f, 0.02f);
        if (embers) Star(m.Mesh, Embers, center, right, up, n, R * 0.42f, Jag(9, 0.5f), 0.02f, 0.05f);
        // Ribs: the frame showing through.
        var ribs = r.Int(2, 3);
        for (var i = 0; i < ribs; i++)
        {
            var off = (i - (ribs - 1) / 2f) * R * 0.55f;
            var tilt = r.Range(-0.25f, 0.25f);
            var a = center + up * (off + tilt * R) - right * R * 0.9f + n * 0.12f;
            var b = center + up * (off - tilt * R) + right * R * 0.9f + n * 0.12f;
            Shapes.Beam(m.Mesh, Roles.HullSecondary, a, b, MathF.Max(0.12f, R * 0.07f));
        }
        // Torn plates bent outwards around the rim.
        var plates = r.Int(4, 7);
        for (var i = 0; i < plates; i++)
        {
            var ang = r.Range(0, MathF.Tau);
            var dir = right * MathF.Cos(ang) + up * MathF.Sin(ang);
            var tangent = Vector3.Cross(n, dir);
            var rim = center + dir * R * r.Range(0.85f, 1.05f);
            var bend = r.Range(0.35f, 1.0f);
            var outward = Vector3.Normalize(dir * MathF.Cos(bend) + n * MathF.Sin(bend));
            var len = R * r.Range(0.35f, 0.7f);
            var wid = R * r.Range(0.3f, 0.6f);
            var thick = MathF.Max(0.08f, R * 0.04f);
            Shapes.OBox(m.Mesh, r.Chance(0.7) ? Roles.HullMain : Roles.HullSecondary, rim + outward * (len / 2), tangent, outward, Vector3.Cross(tangent, outward), new Vector3(wid / 2, len / 2, thick / 2));
        }
        if (debris)
        {
            // Drifting plates: intentionally detached, flagged so tools (and the validator) know.
            var d = c.Add("debris", "debris", ModuleTier.Tertiary, m.Id);
            d.Detachable = true;
            for (var i = 0; i < r.Int(1, 3); i++)
            {
                var p = center + n * r.Range(3f, 10f) + right * r.Range(-R * 2, R * 2) + up * r.Range(-R, R);
                var ax = Vector3.Normalize(new Vector3(r.Range(-1, 1), r.Range(-1, 1), r.Range(-1, 1)) + Vector3.UnitX * 0.01f);
                var ay = Vector3.Normalize(Vector3.Cross(ax, Vector3.Normalize(new Vector3(r.Range(-1, 1), 1, r.Range(-1, 1)))));
                var az = Vector3.Cross(ax, ay);
                Shapes.OBox(d.Mesh, Roles.HullMain, p, ax, ay, az, new Vector3(R * r.Range(0.2f, 0.45f), R * r.Range(0.15f, 0.35f), 0.06f));
            }
        }
    }

    /// <summary>A jagged, star-shaped slab on a surface (fanned from its centre, so concave outlines are fine).</summary>
    private static void Star(Mesh mesh, string mat, Vector3 c, Vector3 right, Vector3 up, Vector3 n, float R, float[] jag, float depth, float lift)
    {
        var k = jag.Length;
        var top = new Vector3[k];
        var bottom = new Vector3[k];
        for (var i = 0; i < k; i++)
        {
            var a = i * MathF.Tau / k;
            var p = c + (right * MathF.Cos(a) + up * MathF.Sin(a)) * (R * jag[i]);
            bottom[i] = p + n * (lift - 0.03f);
            top[i] = p + n * (lift + depth);
        }
        var ct = c + n * (lift + depth);
        for (var i = 0; i < k; i++)
        {
            var j = (i + 1) % k;
            mesh.AddPolygon(mat, [ct, top[i], top[j]], n);
            mesh.AddPolygon(mat, [bottom[i], bottom[j], top[j], top[i]], (bottom[i] + bottom[j]) * 0.5f - c);
        }
    }

    // ------------------------------------------------------------------ broken equipment

    private static readonly HashSet<string> Breakable = ["antenna", "lattice_mast", "sensor_dish", "radome", "crane"];

    private static void BreakEquipment(BuildContext c, Rng r, bool derelict)
    {
        var volumes = c.Ship.Markers.Where(k => k.Kind == "engine_exhaust").Select(ShipValidator.ExhaustVolume)
            .Concat(c.Ship.Markers.Where(k => k.Kind == "docking_port").Select(ShipValidator.DockingVolume)).ToList();
        foreach (var m in c.Ship.Modules.Where(x => Breakable.Contains(x.Type)).ToList())
        {
            m.UpdateBounds();
            var b = m.Bounds;
            // The foot: every breakable item's first shape is its base (pedestal, base box, first mast leg).
            var baseBox = Aabb.Empty;
            for (var i = 0; i < Math.Min(24, m.Mesh.Positions.Count); i++) baseBox = baseBox.Include(m.Mesh.Positions[i]);
            var foot = new Vector3(baseBox.Center.X, baseBox.Min.Y, baseBox.Center.Z);
            var roll = r.NextDouble();
            if (roll < (derelict ? 0.45 : 0.25))
            {
                // Snapped off: a jagged stump where it stood.
                var parent = m.ParentId;
                Remove(c, m);
                var stump = c.Add(m.Id + "_stump", "wreckage", ModuleTier.Secondary, parent);
                var w = MathF.Max(0.6f, MathF.Min(baseBox.Size.X, baseBox.Size.Z) * 0.8f);
                var h = MathF.Min(b.Size.Y * 0.25f, r.Range(0.8f, 2.2f)) + 0.3f;
                var baseC = foot;
                Shapes.Box(stump.Mesh, Roles.DarkMechanics, baseC + Vector3.UnitY * (h / 2), new Vector3(w, h, w));
                stump.Mesh.AddPolygon(Scorch, [baseC + new Vector3(-w / 2, h, -w / 2), baseC + new Vector3(w / 2, h * 1.4f, w / 2), baseC + new Vector3(-w / 2, h * 1.2f, w / 2)], Vector3.UnitY);
            }
            else if (roll < (derelict ? 0.75 : 0.45) && m.Type is "antenna" or "lattice_mast" or "crane")
            {
                // Leaning: rotated about its foot; put back upright if that would block a corridor or exhaust.
                var axis = Vector3.Normalize(new Vector3(r.Range(-1, 1), 0, r.Range(-1, 1)) + new Vector3(0.001f, 0, 0));
                var q = Quaternion.CreateFromAxisAngle(axis, r.Range(0.2f, 0.5f) * r.Sign());
                var oldP = m.Mesh.Positions.ToArray();
                var oldN = m.Mesh.Normals.ToArray();
                for (var i = 0; i < oldP.Length; i++)
                {
                    m.Mesh.Positions[i] = foot + Vector3.Transform(oldP[i] - foot, q);
                    m.Mesh.Normals[i] = Vector3.Normalize(Vector3.Transform(oldN[i], q));
                }
                m.UpdateBounds();
                if (volumes.Any(v => m.Bounds.Penetration(v) > 0.05f))
                {
                    for (var i = 0; i < oldP.Length; i++) { m.Mesh.Positions[i] = oldP[i]; m.Mesh.Normals[i] = oldN[i]; }
                    m.UpdateBounds();
                }
                else
                    foreach (var mk in c.Ship.Markers.Where(k => k.ModuleId == m.Id).ToList())
                    {
                        var i = c.Ship.Markers.IndexOf(mk);
                        c.Ship.Markers[i] = mk with { Position = foot + Vector3.Transform(mk.Position - foot, q), Direction = Vector3.Transform(mk.Direction, q) };
                    }
            }
        }
    }

    // ------------------------------------------------------------------ lights, cargo, paint

    private static void Lights(BuildContext c, Rng r, bool derelict)
    {
        void Swap(Module m, string from, string to)
        {
            if (!m.Mesh.Groups.Remove(from, out var idx)) return;
            if (m.Mesh.Groups.TryGetValue(to, out var existing)) existing.AddRange(idx);
            else m.Mesh.Groups[to] = idx;
        }
        if (derelict)
        {
            foreach (var m in c.Ship.Modules)
            {
                Swap(m, Roles.Glass, GlassDark);
                Swap(m, Roles.Light, LightOff);
                Swap(m, Roles.NavRed, LightOff);
                Swap(m, Roles.EngineGlow, EngineCold);
            }
            return;
        }
        // Damaged: one engine cold, some sections dark.
        var engines = c.Ship.Modules.Where(m => m.Type == "engine").ToList();
        if (engines.Count > 1) Swap(r.Pick(engines), Roles.EngineGlow, EngineCold);
        foreach (var m in c.Ship.Modules.Where(m => m.Mesh.Groups.ContainsKey(Roles.Glass)))
            if (r.Chance(0.3)) Swap(m, Roles.Glass, GlassDark);
    }

    private static void RemoveCargo(BuildContext c, Rng r)
    {
        foreach (var m in c.Ship.Modules.Where(m => m.Detachable && m.Type != "debris" || m.Type is "container_stack" or "container_rack").ToList())
            if (r.Chance(m.Detachable ? 0.7 : 0.5)) Remove(c, m);
    }

    /// <summary>Removes a module, its children, and their markers and envelopes.</summary>
    private static void Remove(BuildContext c, Module m)
    {
        foreach (var child in c.Ship.Modules.Where(x => x.ParentId == m.Id).ToList()) Remove(c, child);
        c.Ship.Modules.Remove(m);
        c.Ship.Markers.RemoveAll(k => k.ModuleId == m.Id);
        c.Ship.Envelopes.RemoveAll(e => e.ModuleId == m.Id);
        // Later passes (surface detail) must not decorate what is gone.
        c.Lofts.RemoveAll(l => l.Module == m);
        c.Sides.RemoveAll(sd => sd.Module == m);
        c.Decks.RemoveAll(d => d.ModuleId == m.Id);
        c.Bellies.RemoveAll(b => b.Module == m);
    }

    private static void Fade(BuildContext c)
    {
        var mats = c.Ship.Materials;
        var grime = Palettes.Linear("#4a443c");
        foreach (var role in new[] { Roles.HullMain, Roles.HullSecondary, Roles.Accent, Roles.Hazard, Roles.Radome, Roles.CargoA, Roles.CargoB, Roles.CargoC })
            if (mats.TryGetValue(role, out var d))
                mats[role] = d with { Color = Vector3.Lerp(d.Color, grime, 0.35f) * 0.85f, Roughness = MathF.Min(1, d.Roughness + 0.15f) };
        if (mats.TryGetValue(Roles.Livery, out var liv))
            mats[Roles.Livery] = liv with { Color = new Vector3(0.62f, 0.6f, 0.55f) };
    }
}
