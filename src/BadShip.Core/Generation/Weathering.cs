using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Age on the surfaces, for ships that are not new ("worn", and under the damage of "damaged" and "derelict"): grime
/// running down from windows and hatches, rust runs, soot blown forward from the engines, mismatched replacement panels
/// in primer and bare metal, and scraped edges. Flat decals just off the hull (under the raised details), in their own
/// material roles (grime, rust, primer, bare_metal) so a game can restyle them, on their own random stream, in each
/// part's ".details" (so the LODs drop them). Stronger the older the ship.
/// </summary>
public static class Weathering
{
    public const string Grime = "grime", Rust = "rust", Primer = "primer", BareMetal = "bare_metal";

    public static void Apply(BuildContext c)
    {
        var age = c.Plan.Condition switch { "worn" => 1f, "damaged" => 1.3f, "derelict" => 1.8f, _ => 0f };
        if (age == 0 || c.DetailLevel < 1 || c.Plan.Family == "fighter") return;
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("condition") is { } cs and > 0 ? cs : c.Recipe.Seed, "weathering");
        AddMaterials(c);
        var k = c.DetailScale;
        foreach (var part in c.Lofts.Where(p => p.Kind is "hull" or "tier" or "pod" or "spine" && c.Ship.Modules.Contains(p.Module)).ToList())
        {
            var loft = part.Loft;
            var mesh = c.DetailsOf(part.Module).Mesh;
            var engine = part.Module.Type is "engine" or "engineering_bay" or "engineering_block";
            for (var s = 0; s < loft.Segments; s++)
            {
                if (loft.IsStep(s)) continue;
                for (var f = 0; f < 8; f++)
                {
                    var (along, across) = loft.FaceSize(s, f);
                    if (along < 2 * k || across < 1 * k) continue;
                    var face = new Face(c, loft, s, f, BuildContext.FaceKey(part.Module, s, f), along, across, k);
                    var bottom = f is Profile.FaceBottom;
                    if (!bottom) RunsUnderPanes(face, r, mesh, age);
                    if (!bottom) Runs(face, r, mesh, age);
                    if (engine || s == 0 && part.Kind is "hull" or "spine") Soot(face, r, mesh, age, engine);
                    if (f is Profile.FacePosX or Profile.FaceNegX or Profile.FaceTop or Profile.FaceBottom) Replacements(face, r, mesh, age);
                    if (f is Profile.FaceTopPosX or Profile.FaceTopNegX) Scrapes(face, r, mesh, age);
                }
            }
        }
    }

    private static void AddMaterials(BuildContext c)
    {
        var m = c.Ship.Materials;
        var hull = m.TryGetValue(Roles.HullMain, out var h) ? h.Color : Palettes.Linear("#6c7378");
        void Add(string role, Vector3 color, float rough, float metal) => m[role] = new MaterialDef(role, color, rough, metal, Vector3.Zero, 1);
        // Grime: the hull's own colour darkened and browned; on a dark hull, pale dust instead, so it shows either way.
        var lum = 0.2126f * hull.X + 0.7152f * hull.Y + 0.0722f * hull.Z;
        Add(Grime, lum < 0.05f ? hull * 1.5f + Palettes.Linear("#6a6052") * 0.45f : hull * 0.35f + Palettes.Linear("#2a2016") * 0.3f, 0.95f, 0.1f);
        Add(Rust, Palettes.Linear("#6e4230"), 0.95f, 0.2f);
        Add(Primer, Palettes.Linear(c.Plan.Palette is "pirate" or "military" ? "#6b7356" : "#7d8a7a"), 0.85f, 0.2f);
        Add(BareMetal, Palettes.Linear("#9da3a8"), 0.35f, 0.85f);
    }

    private sealed record Face(BuildContext C, Loft Loft, int S, int F, string Key, float Along, float Across, float K)
    {
        public Vector3 Normal => Loft.FaceNormal(S, F);
        public Vector3 At(float u, float v) => Loft.FacePoint(S, F, Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1));

        /// <summary>Which way is down on this face, in v: -1 when v grows upward, +1 when it grows downward, 0 when level.</summary>
        public float DownV()
        {
            var dy = At(0.5f, 1).Y - At(0.5f, 0).Y;
            return MathF.Abs(dy) < 0.05f * Across ? 0 : dy > 0 ? -1 : 1;
        }

        /// <summary>A tapered streak as a flat quad just off the face, from (u0, v0) to (u1, v1), w0 wide at its head and
        /// w1 at its tail (widths in metres, across the streak).</summary>
        public void Streak(Mesh m, string mat, float u0, float v0, float u1, float v1, float w0, float w1)
        {
            var a = At(u0, v0);
            var b = At(u1, v1);
            var along = b - a;
            if (along.LengthSquared() < 1e-4f) return;
            var n = Normal;
            var side = Vector3.Normalize(Vector3.Cross(n, along));
            var o = n * (0.015f * K);
            m.AddQuad(mat, a - side * (w0 / 2) + o, a + side * (w0 / 2) + o, b + side * (w1 / 2) + o, b - side * (w1 / 2) + o, n);
        }
    }

    /// <summary>Grime running down from the windows (painted or real).</summary>
    private static void RunsUnderPanes(Face face, Rng r, Mesh m, float age)
    {
        var down = face.DownV();
        if (down == 0) return;
        foreach (var p in face.Loft.Panes.Where(p => p.S == face.S && p.F == face.F))
        {
            if (!r.Chance(0.5 * age)) continue;
            var u = r.Range(p.U0, p.U1);
            var v0 = down > 0 ? MathF.Max(p.V0, p.V1) : MathF.Min(p.V0, p.V1);
            var len = r.Range(2f, 6f) * face.K * age / face.Across;
            face.Streak(m, Grime, u, v0, u, v0 + down * len, r.Range(0.6f, 1.4f) * face.K, r.Range(0.25f, 0.5f) * face.K);
        }
    }

    /// <summary>Streaks running down the face from seams and fittings: grime, and rust on older ships.</summary>
    private static void Runs(Face face, Rng r, Mesh m, float age)
    {
        var down = face.DownV();
        var count = (int)(face.Along * face.Across / (60f * face.K * face.K) * age * r.Range(0.6f, 1.2f));
        for (var i = 0; i < count; i++)
        {
            var mat = r.Chance(0.12 * age * age) ? Rust : Grime;
            var u = r.Range(0.03f, 0.97f);
            if (down == 0)
            {
                // A level face: grime blown back along the hull instead.
                var v = r.Range(0.1f, 0.9f);
                var len = r.Range(4f, 14f) * face.K / face.Along;
                face.Streak(m, mat, u, v, u - len, v, r.Range(0.6f, 1.5f) * face.K, r.Range(0.2f, 0.45f) * face.K);
                continue;
            }
            var vs = down > 0 ? r.Range(0.05f, 0.6f) : r.Range(0.4f, 0.95f);
            var l = r.Range(1.5f, 6f) * face.K * age / face.Across;
            face.Streak(m, mat, u, vs, u, vs + down * l, r.Range(0.5f, 1.3f) * face.K, r.Range(0.2f, 0.45f) * face.K);
        }
    }

    /// <summary>Soot blown forward from the engines over the engine housings and the stern.</summary>
    private static void Soot(Face face, Rng r, Mesh m, float age, bool engine)
    {
        // The face's rear edge: the end of u nearest the stern.
        var rear = face.At(0, 0.5f).Z < face.At(1, 0.5f).Z ? 0f : 1f;
        var dir = rear == 0 ? 1f : -1f;
        var n = (int)(face.Across / (2f * face.K) * (engine ? 0.9f : 0.4f) * age);
        for (var i = 0; i < n; i++)
        {
            var v = r.Range(0.08f, 0.92f);
            var len = MathF.Min(0.7f, r.Range(4f, engine ? 14f : 8f) * face.K * age / face.Along);
            face.Streak(m, Grime, rear, v, rear + dir * len, v + r.Range(-0.03f, 0.03f), r.Range(1f, 2.4f) * face.K, r.Range(0.3f, 0.8f) * face.K);
        }
    }

    /// <summary>Replacement panels that never got painted: primer, bare metal, sometimes the wrong hull colour.</summary>
    private static void Replacements(Face face, Rng r, Mesh m, float age)
    {
        var count = (int)(face.Along * face.Across / (350f * face.K * face.K) * age);
        for (var i = 0; i < count; i++)
        {
            var w = r.Range(3f, 7f) * face.K / face.Along;
            var h = MathF.Min(0.5f, r.Range(2f, 4f) * face.K / face.Across);
            var u0 = r.Range(0.03f, 0.97f - w);
            var v0 = r.Range(0.05f, 0.95f - h);
            // Not over windows (anything raised sits above it anyway),
            if (face.Loft.Panes.Any(p => p.S == face.S && p.F == face.F && p.U0 < u0 + w && u0 < p.U1 && p.V0 < v0 + h && v0 < p.V1)) continue;
            // Nor over the ship's name and markings.
            if (face.C.IsProtected(face.Key, u0, u0 + w, v0, v0 + h)) continue;
            var mat = r.Weighted<string>([(Primer, 0.5), (BareMetal, 0.25), (Roles.HullPanel, 0.25)]);
            var o = face.Normal * (0.04f * face.K);
            m.AddQuad(mat, face.At(u0, v0) + o, face.At(u0 + w, v0) + o, face.At(u0 + w, v0 + h) + o, face.At(u0, v0 + h) + o, face.Normal);
        }
    }

    /// <summary>Scraped paint along the top edges, showing bare metal.</summary>
    private static void Scrapes(Face face, Rng r, Mesh m, float age)
    {
        var upper = face.At(0.5f, 1).Y > face.At(0.5f, 0).Y ? 1f : 0f;
        var count = (int)(face.Along / (12f * face.K) * age);
        for (var i = 0; i < count; i++)
        {
            var u = r.Range(0.02f, 0.9f);
            var len = r.Range(1.5f, 5f) * face.K / face.Along;
            var v = upper > 0.5f ? 1 - r.Range(0.02f, 0.12f) : r.Range(0.02f, 0.12f);
            face.Streak(m, BareMetal, u, v, u + len, v, 0.2f * face.K, r.Range(0.05f, 0.15f) * face.K);
        }
    }
}
