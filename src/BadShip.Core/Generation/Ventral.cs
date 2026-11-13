using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// What hangs below the main hull. One structure per ship, chosen in the plan: a keel (inverted stepped terraces
/// buried into the hull's lower chamfers), a gondola (a separate pod on pylons, with a visible gap above it) or slung
/// pods (tanks or cargo pods on short pylons). It has its own random stream, so the topside of a ship is unchanged
/// when this changes. Every structure registers its own underside deck for hanging equipment.
/// </summary>
public static class Ventral
{
    public static void Build(BuildContext c)
    {
        var span = c.Bellies.FirstOrDefault(b => b.Main);
        if (span is null) return;
        switch (c.Plan.Ventral)
        {
            case "keel": Keel(c, span); break;
            case "gondola": Gondola(c, span); break;
            case "pods": Pods(c, span); break;
        }
    }

    private static Deck? BellyDeck(BuildContext c, Module m) => c.Decks.FirstOrDefault(d => d.Underside && d.ModuleId == m.Id);

    /// <summary>
    /// Inverted terraces: a block hanging under the hull with sloped ends, its top buried in the hull's lower
    /// chamfers so the joint never shows; sometimes a second, smaller one below it.
    /// </summary>
    private static void Keel(BuildContext c, BellySpan span, int depth = 0)
    {
        var r = c.VentralRng;
        var p = span.Profile;
        var (_, cb) = p.ClampedChamfers();
        var spanLen = span.Z1 - span.Z0;
        var len = spanLen * (depth == 0 ? r.Range(0.6f, 0.9f) : r.Range(0.45f, 0.75f));
        if (len < 6) return;
        var zc = r.Range(span.Z0 + len / 2, span.Z1 - len / 2);
        var top = p.Bottom + MathF.Max(cb * 0.8f, 0.4f);
        var maxW = 2 * p.HalfWidthAt(top) - 0.8f;
        var w = maxW * r.Range(0.65f, 0.95f);
        if (w < 3) return;
        var drop = c.Plan.Height * (depth == 0 ? r.Range(0.32f, 0.55f) : r.Range(0.2f, 0.35f));
        var h = top - p.Bottom + drop;
        var kp = new Profile(p.Cx, top - h / 2, w * r.Range(0.7f, 0.88f), w, h, 0, MathF.Min(h * r.Range(0.3f, 0.5f), drop * 0.9f));
        var slope = MathF.Min(len * 0.2f, drop * r.Range(1.2f, 2.2f));
        var z0 = zc - len / 2;
        var z1 = zc + len / 2;
        var rear = kp.ScaledOnTop(0.94f, 1 - drop / h * r.Range(0.5f, 0.8f));
        var front = kp.ScaledOnTop(0.9f, 1 - drop / h * r.Range(0.55f, 0.9f));
        var (keel, loft) = c.AddLoft(depth == 0 ? "keel" : "keel_lower", "keel", ModuleTier.Primary, span.Module.Id,
            [(z0, rear), (z0 + slope, kp), (z1 - slope, kp), (z1, front)], depth == 0 ? Roles.HullMain : Roles.HullSecondary, "hull");
        BellyDeck(c, span.Module)?.Take(p.Cx - w / 2, p.Cx + w / 2, z0, z1);
        if (c.Plan.Archetype != "freighter" && r.Chance(0.6)) Parts.SideWindows(c, keel, loft, 1, 0.45f);
        c.AddSides(keel, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddEnvelope(c.Plan.Archetype == "freighter" ? "cargo" : "habitable", keel,
            new Aabb(new Vector3(p.Cx - w * 0.35f, kp.Bottom + 0.5f, z0 + slope), new Vector3(p.Cx + w * 0.35f, p.Bottom, z1 - slope)));
        var below = new BellySpan(keel, kp, z0 + slope + 0.5f, z1 - slope - 0.5f, false);
        c.AddBelly(keel, kp, below.Z0, below.Z1, main: false);
        if (depth == 0 && r.Chance(0.5)) Keel(c, below, 1);
    }

    /// <summary>
    /// A pod hanging on two or three pylons with open space above it (negative space under the hull). Research ships
    /// get an observation gondola with a glass band; freighters a detachable cargo gondola.
    /// </summary>
    private static void Gondola(BuildContext c, BellySpan span)
    {
        var r = c.VentralRng;
        var p = span.Profile;
        var a = c.Plan.Archetype;
        var spanLen = span.Z1 - span.Z0;
        var len = spanLen * (spanLen < 30 ? r.Range(0.6f, 0.9f) : r.Range(0.35f, 0.6f));
        // A gondola people walk into needs a few rooms' length between its tapered ends.
        if (c.Recipe.Interior) len = MathF.Max(len, MathF.Min(spanLen * 0.95f, 16f));
        if (len < 5) return;
        var zc = r.Range(span.Z0 + len / 2, span.Z1 - len / 2);
        var usable = MathF.Max(p.BottomFlatHalf, p.WBottom * 0.3f);
        var gw = Num.Clamp(usable * 2 * r.Range(0.7f, 1.1f), 4f, p.WBottom * 0.9f);
        var gh = c.Plan.Height * r.Range(0.3f, 0.48f);
        // With an interior, the gondola is a room people walk into: tall and wide enough for a deck.
        if (c.Recipe.Interior)
        {
            gh = MathF.Max(gh, 4.8f);
            gw = MathF.Max(gw, MathF.Min(7f, p.WBottom * 0.95f));
        }
        var gap = c.Plan.Height * r.Range(0.16f, 0.3f);
        var gTop = p.Bottom - gap;
        var gp = new Profile(p.Cx, gTop - gh / 2, gw * 0.72f, gw, gh, gh * r.Range(0.12f, 0.22f), gh * r.Range(0.35f, 0.5f));
        var z0 = zc - len / 2;
        var z1 = zc + len / 2;
        var mat = a == "freighter" ? Roles.Accent : Roles.HullMain;
        var (g, loft) = c.AddLoft("gondola", "gondola", ModuleTier.Primary, span.Module.Id,
            [(z0, gp.ScaledOnTop(0.75f, 0.6f)), (z0 + MathF.Min(gh * 0.6f, len * 0.25f), gp), (z1 - MathF.Min(gh * 0.9f, len * 0.3f), gp), (z1, gp.ScaledOnTop(0.62f, 0.5f))], mat, "pod");
        g.Detachable = a == "freighter";

        // Pylons: boxed struts buried a little into the hull bottom and into the gondola top.
        var pylons = len > 24 ? 3 : 2;
        var pw = Num.Clamp(2 * p.HalfWidthAt(p.Bottom + 0.5f) - 0.6f, 0.8f, gw * 0.35f);
        var pd = Num.Clamp(len * 0.08f, 1.2f, 4f);
        for (var i = 0; i < pylons; i++)
        {
            var z = float.Lerp(z0 + len * 0.22f, z1 - len * 0.28f, pylons == 1 ? 0.5f : i / (float)(pylons - 1));
            Shapes.BoxMinMax(g.Mesh, Roles.DarkMechanics, new Vector3(p.Cx - pw / 2, gTop - 0.4f, z - pd / 2), new Vector3(p.Cx + pw / 2, p.Bottom + 0.5f, z + pd / 2));
        }
        BellyDeck(c, span.Module)?.Take(p.Cx - gw / 2, p.Cx + gw / 2, z0, z1);

        if (a == "research")
        {
            // An observation gondola: glass all round its lower facets; with an interior, round its sides at eye level,
            // where the lounge inside can look out through it.
            var faces = c.Recipe.Interior ? new[] { Profile.FacePosX, Profile.FaceNegX } : new[] { Profile.FaceBottomPosX, Profile.FaceBottomNegX };
            foreach (var f in faces)
            {
                var lo = Parts.UpV(f, c.Recipe.Interior ? 0.3f : 0.18f);
                var hi = Parts.UpV(f, c.Recipe.Interior ? 0.8f : 0.85f);
                c.UseSurface(BuildContext.FaceKey(g, 1, f), 0, 1, 0, 1);
                var (along, _) = loft.FaceSize(1, f);
                Parts.PaneRow(g.Mesh, loft, 1, f, 0.03f, 0.97f, MathF.Min(lo, hi), MathF.Max(lo, hi), Math.Max(3, (int)(along / 2.4f)), alongU: true, gap: 0.16f);
            }
            c.AddMarker("observation_deck", g, new Vector3(p.Cx, gp.Bottom, zc), -Vector3.UnitY, gw * 0.4f);
        }
        else if (r.Chance(0.6)) Parts.SideWindows(c, g, loft, 1, 0.55f);
        c.AddSides(g, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddEnvelope(a == "freighter" ? "cargo" : "habitable", g, Aabb.FromCenterSize(new Vector3(p.Cx, gp.Cy, zc), new Vector3(gw * 0.7f, gh * 0.6f, len * 0.75f)));
        c.AddBelly(g, gp, z0 + MathF.Min(gh * 0.6f, len * 0.25f) + 0.3f, z1 - MathF.Min(gh * 0.9f, len * 0.3f) - 0.3f, main: false);
    }

    /// <summary>Tanks or cargo pods slung under the belly on short pylons, in a row (pairs when the belly is wide).</summary>
    private static void Pods(BuildContext c, BellySpan span)
    {
        var r = c.VentralRng;
        var p = span.Profile;
        var spanLen = span.Z1 - span.Z0;
        var count = r.Int(2, 4);
        var total = spanLen * r.Range(0.5f, 0.85f);
        var gap = r.Range(1.5f, 4f);
        var len = (total - gap * (count - 1)) / count;
        if (len < 5) return;
        var start = r.Range(span.Z0, span.Z1 - total);
        var rp = Num.Clamp(c.Plan.Height * r.Range(0.13f, 0.2f), 1.5f, 6f);
        var usable = MathF.Max(p.BottomFlatHalf, p.WBottom * 0.25f);
        var columns = usable > rp * 2.4f ? new[] { -1f, 1f } : new[] { 0f };
        var tanks = r.Chance(0.5);
        var mat = tanks ? r.Pick([Roles.Accent, Roles.CargoA, Roles.HullSecondary]) : r.Pick([Roles.CargoA, Roles.CargoB, Roles.Accent]);
        var drop = r.Range(0.6f, 1.6f);
        var deck = BellyDeck(c, span.Module);
        for (var k = 0; k < count; k++)
        {
            var zs = start + k * (len + gap);
            var ze = zs + len;
            foreach (var col in columns)
            {
                var x = p.Cx + col * MathF.Min(usable - rp * 0.4f, rp * 1.25f);
                var cy = p.Bottom - drop - rp;
                var m = c.Add("slung_pod", tanks ? "slung_tank" : "slung_pod", ModuleTier.Secondary, span.Module.Id);
                m.Detachable = true;
                if (tanks)
                {
                    var a0 = new Vector3(x, cy, zs + rp * 0.4f);
                    var b0 = new Vector3(x, cy, ze - rp * 0.4f);
                    Shapes.Prism(m.Mesh, mat, a0, b0, rp, rp, 8, MathF.PI / 8, capA: false, capB: false);
                    Shapes.Prism(m.Mesh, mat, b0, b0 + new Vector3(0, 0, rp * 0.4f), rp, rp * 0.5f, 8, MathF.PI / 8, capA: false);
                    Shapes.Prism(m.Mesh, mat, a0, a0 - new Vector3(0, 0, rp * 0.4f), rp, rp * 0.5f, 8, MathF.PI / 8, capA: false);
                }
                else
                {
                    var pp = Profile.Box(x, cy, rp * 2, rp * 2, rp * 0.35f);
                    new Loft([(zs, pp.Scaled(0.85f, 0.85f)), (zs + rp * 0.3f, pp), (ze - rp * 0.3f, pp), (ze, pp.Scaled(0.85f, 0.85f))]).Build(m.Mesh, mat);
                }
                // Clamp bands and pylons up into the belly.
                foreach (var t in new[] { 0.25f, 0.75f })
                {
                    var z = float.Lerp(zs, ze, t);
                    Shapes.BoxMinMax(m.Mesh, Roles.DarkMechanics, new Vector3(x - rp * 0.3f, cy + rp * 0.6f, z - 0.6f), new Vector3(x + rp * 0.3f, p.Bottom + MathF.Max(0.4f, p.ClampedChamfers().cb * 0.7f), z + 0.6f));
                }
                deck?.Take(x - rp, x + rp, zs, ze);
            }
        }
    }
}
