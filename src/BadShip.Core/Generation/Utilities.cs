using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Utility craft, 14–36 m: the vans and pickups of space. A boxy chamfered cab with big wraparound windows on a
/// sloped windscreen, a body behind it, landing legs with pads, headlights and a roof mast, and one of four jobs:
/// runabout — compact, engine pods on short pylons, a roof light bar;
/// mule — a low open cargo bed behind the cab with strapped containers and rails, an engine block at the back;
/// shuttle — a long passenger cabin with rows of windows, a side door and a docking hatch on the roof;
/// pioneer — side modules bolted on, a roof rack with crates, a dish, engine pods and sturdier legs.
/// The job is kept in the plan's payload.
/// </summary>
public static class Utilities
{
    public static readonly string[] Jobs = ["runabout", "mule", "shuttle", "pioneer"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        p.Family = "utility";
        p.Payload = r.Weighted<string>([("runabout", 0.3), ("mule", 0.25), ("shuttle", 0.25), ("pioneer", 0.2)]);
        // A pinned job also gets that job's engines and paint (the draw above still happens, so nothing else moves).
        if (c.Recipe.Overrides?.Payload is { } pinned && Jobs.Contains(pinned)) p.Payload = pinned;
        var (lmin, lmax) = p.Size switch { "small" => (14f, 20f), "large" => (28f, 36f), _ => (20f, 28f) };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        p.Beam = L * r.Range(0.34f, 0.44f);
        p.Height = L * r.Range(0.24f, 0.32f);
        p.HullForm = "slab";
        p.ProwStyle = "blunt";
        p.BridgeStyle = "cab";
        p.Tiers = 0;
        p.EngineStyle = p.Payload is "runabout" or "pioneer" ? "pod" : "block";
        p.EngineLayout = r.Chance(0.65) ? "2x1" : "1x1";
        p.Asymmetric = false;
        p.Ventral = "none";
        c.PreferredPalette = p.Payload switch
        {
            "shuttle" => r.Chance(0.55) ? "civilian" : null,
            "mule" or "pioneer" => r.Chance(0.45) ? "industrial" : null,
            _ => r.Chance(0.2) ? "civilian" : null,
        };
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Num.Clamp(c.Lang.Chamfer, 0.6f, 1.5f);
        var job = p.Payload;
        var podEngines = p.EngineStyle == "pod";
        var engLen = podEngines ? L * 0.03f : L * r.Range(0.1f, 0.14f);
        var cabLen = L * (job == "shuttle" ? r.Range(0.2f, 0.26f) : r.Range(0.28f, 0.36f));
        var zBody0 = zR + engLen;
        var zCab0 = zN - cabLen;
        var round = job == "shuttle" ? 1.6f : 1f;
        var body = job == "mule"
            ? new Profile(0, -H * 0.24f, B, B * 0.96f, H * 0.52f, H * 0.06f * bev, H * 0.12f * bev)
            : new Profile(0, 0, B, B * 0.92f, H, H * 0.16f * bev * round, H * 0.16f * bev * round);
        var cab = new Profile(0, -H * 0.04f, B * 0.94f, B * 0.86f, H * 0.92f, H * 0.2f * bev * round, H * 0.16f * bev);
        var screenTop = r.Range(0.55f, 0.7f);
        List<(float, Profile)> rings =
        [
            (zBody0, body.Scaled(0.94f, 0.94f)),
            (zBody0 + L * 0.02f, body),
            (zCab0, body),
            (zCab0, cab),
            (zCab0 + cabLen * r.Range(0.35f, 0.5f), cab),
            (zN - cabLen * 0.1f, cab with { H = cab.H * screenTop, Cy = cab.Bottom + cab.H * screenTop / 2, WTop = cab.WTop * 0.9f }),
            (zN, cab.Scaled(0.9f, 0.42f) with { Cy = cab.Bottom + cab.H * 0.24f }),
        ];
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        Windows(c, hull, loft, cab, cabLen);

        if (podEngines) Pods(c, hull, body, zR, L, B);
        else EngineBlock(c, hull, body, zR, zBody0, L);
        Gear(c, hull, rings, zBody0, zN, B, H, job == "pioneer" ? 1.35f : 1f);
        Nose(c, hull, cab, zN, B, H);

        var roofY = body.Top;
        switch (job)
        {
            case "mule": Bed(c, hull, body, zBody0, zCab0, B, H, L); break;
            case "shuttle": Cabin(c, hull, loft, body, zBody0, zCab0, B, H, L); break;
            case "pioneer": Pioneer(c, hull, body, zBody0, zCab0, B, H, L); break;
            default: LightBar(c, hull, cab, zCab0, cabLen, B, H); break;
        }
        Mast(c, hull, job == "mule" ? cab.Top : roofY, job == "mule" ? zCab0 + cabLen * 0.2f : zCab0 - L * 0.05f, B, H);
    }

    // ------------------------------------------------------------------ shared parts

    /// <summary>Big wraparound cab windows: the sloped windscreen and the upper cab sides, with a bridge marker.</summary>
    private static void Windows(BuildContext c, Module hull, Loft loft, Profile cab, float cabLen)
    {
        // Ring 4 → 5 is the windscreen slope; ring 3 → 4 the cab sides.
        var screen = 4;
        foreach (var f in new[] { Profile.FaceTopPosX, Profile.FaceTop, Profile.FaceTopNegX })
        {
            Shapes.Patch(hull.Mesh, Roles.Glass, loft, screen, f, 0.08f, 0.94f, 0.05f, 0.95f, 0.04f);
            c.UseSurface(BuildContext.FaceKey(hull, screen, f), 0, 1, 0, 1);
        }
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            // Side windows on the cab and continuing along the windscreen segment.
            foreach (var s in new[] { 3, screen })
            {
                var v0 = Parts.UpV(f, 0.55f);
                var v1 = Parts.UpV(f, 0.95f);
                if (v0 > v1) (v0, v1) = (v1, v0);
                Shapes.Patch(hull.Mesh, Roles.Glass, loft, s, f, s == 3 ? 0.45f : 0.02f, s == 3 ? 0.98f : 0.7f, v0, v1, 0.04f);
                c.UseSurface(BuildContext.FaceKey(hull, s, f), 0, 1, MathF.Min(v0, v1) - 0.05f, 1);
            }
        }
        var z = loft.Zs[screen];
        c.AddMarker("bridge", hull, new Vector3(0, cab.Cy + cab.H * 0.15f, z), Vector3.UnitZ);
    }

    private static void Nose(BuildContext c, Module hull, Profile cab, float zN, float B, float H)
    {
        var m = c.Add("bumper", "bumper", ModuleTier.Secondary, hull.Id);
        var y = cab.Bottom + cab.H * 0.14f;
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(0, y, zN - 0.1f), new Vector3(B * 0.82f, H * 0.08f, 0.5f));
        foreach (var s in new[] { 1f, -1f })
        {
            Shapes.Box(m.Mesh, Roles.Light, new Vector3(s * B * 0.3f, y + H * 0.09f, zN - 0.05f), new Vector3(B * 0.12f, H * 0.04f, 0.3f));
            c.AddMarker("floodlight", m, new Vector3(s * B * 0.3f, y + H * 0.09f, zN + 0.1f), Vector3.UnitZ, H * 0.03f);
            Shapes.Box(m.Mesh, Roles.Hazard, new Vector3(s * B * 0.38f, y, zN + 0.18f), new Vector3(B * 0.08f, H * 0.06f, 0.1f));
        }
    }

    /// <summary>Two engine pods on short pylons along the flanks, each with an intake, a nozzle and a nav light.</summary>
    private static void Pods(BuildContext c, Module hull, Profile body, float zR, float L, float B)
    {
        var r = c.Shape;
        var pw = B * r.Range(0.3f, 0.4f);
        var len = L * r.Range(0.45f, 0.6f);
        // Low on the flanks: the upper body stays clear for the name and registration.
        var y = body.Cy - body.H * r.Range(0.18f, 0.3f);
        var x = B / 2 + pw * 0.62f;
        foreach (var s in new[] { 1f, -1f })
        {
            var pr = new Profile(s * x, y, pw, pw * 0.92f, pw * 0.9f, pw * 0.28f, pw * 0.28f);
            var (m, pl) = c.AddLoft(s > 0 ? "pod_port" : "pod_starboard", "engine", ModuleTier.Secondary, hull.Id,
                [(zR, pr.Scaled(0.85f, 0.85f)), (zR + len * 0.12f, pr), (zR + len * 0.8f, pr), (zR + len, pr.Scaled(0.75f, 0.72f))], Roles.HullSecondary, "pod");
            // Pylon to the hull.
            Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(s * (B / 2 + pw * 0.05f), y, zR + len * 0.5f), new Vector3(pw * 0.6f, pw * 0.35f, len * 0.4f));
            var front = pl.Rings[^1];
            var ctr = new Vector3(s * x, y, zR + len);
            m.Mesh.AddPolygon(Roles.DarkMechanics, front.Select(q => ctr + (q - ctr) * 0.75f + new Vector3(0, 0, 0.02f)).ToArray(), Vector3.UnitZ);
            var a = new Vector3(s * x, y, zR + 0.1f);
            var nr = pw * 0.36f;
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, a - new Vector3(0, 0, nr * 0.9f), nr * 0.85f, nr, 10, 0, capA: false, capB: true, capBMat: Roles.EngineGlow);
            c.AddMarker("engine_exhaust", m, a - new Vector3(0, 0, nr * 0.9f + 0.1f), -Vector3.UnitZ, nr, L * 0.6f);
            Shapes.Box(m.Mesh, s > 0 ? Roles.NavRed : Roles.Light, new Vector3(s * (x + pw * 0.47f), y, zR + len * 0.6f), new Vector3(0.15f, pw * 0.15f, pw * 0.3f));
        }
    }

    /// <summary>An engine block at the back with one or two round nozzles.</summary>
    private static void EngineBlock(BuildContext c, Module hull, Profile body, float zR, float zBody0, float L)
    {
        var p = c.Plan;
        var housing = body.Scaled(0.88f, 0.78f);
        var (m, _) = c.AddLoft("engine_block", "engine", ModuleTier.Secondary, hull.Id,
            [(zR, housing.Scaled(0.94f, 0.94f)), (zR + (zBody0 - zR) * 0.25f, housing), (zBody0 + 0.3f, housing)], Roles.HullSecondary, "pod");
        var n = p.EngineLayout == "2x1" ? 2 : 1;
        var nr = MathF.Min(housing.WBottom / n * 0.34f, housing.H * 0.36f);
        for (var i = 0; i < n; i++)
        {
            var x = n == 1 ? 0 : (i - 0.5f) * housing.WBottom * 0.48f;
            var a = new Vector3(x, housing.Cy, zR + 0.1f);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, a - new Vector3(0, 0, nr * 0.8f), nr * 0.85f, nr, 12, 0, capA: false, capB: true, capBMat: Roles.EngineGlow);
            c.AddMarker("engine_exhaust", m, a - new Vector3(0, 0, nr * 0.8f + 0.1f), -Vector3.UnitZ, nr, L * 0.6f);
        }
    }

    /// <summary>Four landing legs with pads under the hull, folded half down.</summary>
    private static void Gear(BuildContext c, Module hull, List<(float z, Profile p)> rings, float z0, float z1, float B, float H, float scale)
    {
        var m = c.Add("landing_gear", "landing_gear", ModuleTier.Secondary, hull.Id);
        var t = MathF.Max(0.12f, H * 0.035f) * scale;
        foreach (var zf in new[] { 0.15f, 0.8f })
        {
            var z = float.Lerp(z0, z1, zf);
            var pr = Fighters.ProfileAt(rings, z);
            foreach (var s in new[] { 1f, -1f })
            {
                var foot = new Vector3(s * pr.BottomFlatHalf * 0.8f, pr.Bottom + 0.1f, z);
                var pad = foot + new Vector3(s * B * 0.08f, -H * 0.22f * scale, 0);
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, foot, pad, t);
                Shapes.Beam(m.Mesh, Roles.HullSecondary, foot + new Vector3(0, 0, -t * 3), Vector3.Lerp(foot, pad, 0.6f), t * 0.7f);
                Shapes.Box(m.Mesh, Roles.Hazard, pad - new Vector3(0, t * 0.5f, 0), new Vector3(t * 6, t, t * 7));
                c.AddMarker("landing_pad", m, pad - new Vector3(0, t, 0), -Vector3.UnitY, t * 3);
            }
        }
    }

    private static void Mast(BuildContext c, Module hull, float roofY, float z, float B, float H)
    {
        var m = c.Add("mast", "antenna", ModuleTier.Secondary, hull.Id);
        var x = B * 0.28f;
        var h = H * c.Shape.Range(0.3f, 0.5f);
        Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(x, roofY - 0.1f, z), new Vector3(x, roofY + h, z), MathF.Max(0.06f, H * 0.012f));
        Shapes.Box(m.Mesh, Roles.NavRed, new Vector3(x, roofY + h, z), new Vector3(0.15f, 0.15f, 0.15f));
    }

    private static void LightBar(BuildContext c, Module hull, Profile cab, float zCab0, float cabLen, float B, float H)
    {
        var m = c.Add("light_bar", "lights", ModuleTier.Secondary, hull.Id);
        var z = zCab0 + cabLen * 0.25f;
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(0, cab.Top + H * 0.03f, z), new Vector3(B * 0.6f, H * 0.06f, H * 0.08f));
        for (var i = 0; i < 4; i++)
            Shapes.Box(m.Mesh, i % 3 == 0 ? Roles.Hazard : Roles.Light, new Vector3((i - 1.5f) * B * 0.14f, cab.Top + H * 0.03f, z + H * 0.045f), new Vector3(B * 0.1f, H * 0.04f, 0.05f));
    }

    // ------------------------------------------------------------------ jobs

    /// <summary>The mule's open bed: rails, a tailgate, and strapped containers.</summary>
    private static void Bed(BuildContext c, Module hull, Profile body, float z0, float z1, float B, float H, float L)
    {
        var r = c.Shape;
        var m = c.Add("cargo_bed", "cargo", ModuleTier.Secondary, hull.Id);
        var y = body.Top;
        var half = body.TopFlatHalf;
        var t = MathF.Max(0.1f, H * 0.03f);
        foreach (var s in new[] { 1f, -1f })
            Shapes.Box(m.Mesh, Roles.Hazard, new Vector3(s * (half - t), y + H * 0.06f, (z0 + z1) / 2), new Vector3(t * 2, H * 0.12f, z1 - z0 - 0.4f));
        Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(0, y + H * 0.08f, z0 + 0.4f), new Vector3(half * 2, H * 0.16f, t * 2));
        var len = z1 - z0 - 1;
        var n = len > B * 1.6f ? 2 : 1;
        var cl = len / n * 0.86f;
        for (var i = 0; i < n; i++)
        {
            var z = z0 + 0.5f + (i + 0.5f) * len / n;
            var ch = H * r.Range(0.32f, 0.45f);
            var cw = half * 2 * 0.82f;
            Shapes.Box(m.Mesh, r.Pick(Roles.Cargo), new Vector3(0, y + ch / 2, z), new Vector3(cw, ch, cl));
            foreach (var dz in new[] { -cl * 0.3f, cl * 0.3f })
                Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(0, y + ch / 2, z + dz), new Vector3(cw + t, ch + t, t * 1.5f));
            c.AddMarker("cargo_hardpoint", m, new Vector3(0, y + ch, z), Vector3.UnitY, cw / 2);
        }
    }

    /// <summary>The shuttle's cabin: window rows on both sides, a side door and a roof docking hatch.</summary>
    private static void Cabin(BuildContext c, Module hull, Loft loft, Profile body, float z0, float z1, float B, float H, float L)
    {
        // Body segment is ring 1 → 2.
        var s = 1;
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            var (along, _) = loft.FaceSize(s, f);
            var v0 = Parts.UpV(f, 0.52f);
            var v1 = Parts.UpV(f, 0.74f);
            if (v0 > v1) (v0, v1) = (v1, v0);
            Parts.PaneRow(hull.Mesh, loft, s, f, 0.22f, 0.96f, v0, v1, Math.Max(3, (int)(along / 1.4f)), alongU: true, gap: 0.3f);
            // Door near the front of the cabin.
            var d0 = Parts.UpV(f, 0.08f);
            var d1 = Parts.UpV(f, 0.7f);
            if (d0 > d1) (d0, d1) = (d1, d0);
            Shapes.Patch(hull.Mesh, Roles.Hazard, loft, s, f, 0.05f, 0.18f, d0 - 0.02f, d1 + 0.02f, 0.02f);
            Shapes.Patch(hull.Mesh, Roles.DarkMechanics, loft, s, f, 0.065f, 0.165f, d0, d1, 0.04f);
            // Reserve the window band and the door; the rest of the side stays free for the name.
            c.UseSurface(BuildContext.FaceKey(hull, s, f), 0.2f, 0.98f, v0 - 0.04f, v1 + 0.04f);
            c.UseSurface(BuildContext.FaceKey(hull, s, f), 0.03f, 0.2f, 0, 1);
            var n = loft.FaceNormal(s, f);
            c.AddMarker("hatch", hull, loft.FacePoint(s, f, 0.115f, (d0 + d1) / 2) + n * 0.1f, n, H * 0.2f);
        }
        // Roof docking hatch towards the back.
        var m = c.Add("docking_hatch", "docking_collar", ModuleTier.Secondary, hull.Id);
        var R = MathF.Min(body.TopFlatHalf * 0.7f, H * 0.18f);
        var hz = float.Lerp(z0, z1, 0.3f);
        var top = body.Top;
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(0, top - 0.1f, hz), new Vector3(0, top + R * 0.3f, hz), R, R * 0.95f, 12, 0);
        Shapes.Prism(m.Mesh, Roles.Hazard, new Vector3(0, top + R * 0.3f, hz), new Vector3(0, top + R * 0.38f, hz), R * 0.95f, R * 0.95f, 12, 0, capB: true, capBMat: Roles.DarkMechanics);
        c.AddMarker("docking_port", m, new Vector3(0, top + R * 0.4f, hz), Vector3.UnitY, R, H * 0.6f);
    }

    /// <summary>The pioneer's kit: side modules, a roof rack with crates and a dish.</summary>
    private static void Pioneer(BuildContext c, Module hull, Profile body, float z0, float z1, float B, float H, float L)
    {
        var r = c.Shape;
        var m = c.Add("side_modules", "side_module", ModuleTier.Secondary, hull.Id);
        var len = (z1 - z0) * r.Range(0.4f, 0.6f);
        var zc = float.Lerp(z0, z1, r.Range(0.35f, 0.6f));
        var y = body.Cy - body.H * 0.15f;
        foreach (var s in new[] { 1f, -1f })
        {
            var x = s * body.HalfWidthAt(y);
            var mw = B * 0.14f;
            Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(x + s * mw * 0.35f, y, zc), new Vector3(mw, body.H * 0.4f, len));
            Shapes.Box(m.Mesh, Roles.Hazard, new Vector3(x + s * mw * 0.86f, y + body.H * 0.12f, zc), new Vector3(0.08f, body.H * 0.06f, len * 0.9f));
        }
        var rack = c.Add("roof_rack", "cargo", ModuleTier.Secondary, hull.Id);
        var top = body.Top;
        var half = body.TopFlatHalf * 0.9f;
        var t = MathF.Max(0.08f, H * 0.02f);
        var rz0 = z0 + (z1 - z0) * 0.1f;
        var rz1 = z0 + (z1 - z0) * 0.7f;
        foreach (var s in new[] { 1f, -1f })
            Shapes.Beam(rack.Mesh, Roles.DarkMechanics, new Vector3(s * half, top + H * 0.08f, rz0), new Vector3(s * half, top + H * 0.08f, rz1), t);
        for (var k = 0; k <= 3; k++)
        {
            var z = float.Lerp(rz0, rz1, k / 3f);
            Shapes.Beam(rack.Mesh, Roles.DarkMechanics, new Vector3(-half, top + H * 0.08f, z), new Vector3(half, top + H * 0.08f, z), t);
            foreach (var s in new[] { 1f, -1f })
                Shapes.Beam(rack.Mesh, Roles.DarkMechanics, new Vector3(s * half, top - 0.05f, z), new Vector3(s * half, top + H * 0.08f, z), t);
        }
        for (var k = 0; k < r.Int(1, 3); k++)
        {
            var cs = H * r.Range(0.12f, 0.2f);
            Shapes.Box(rack.Mesh, r.Pick(Roles.Cargo), new Vector3(r.Range(-half * 0.5f, half * 0.5f), top + H * 0.08f + cs / 2, float.Lerp(rz0, rz1, (k + 0.5f) / 3f)), new Vector3(cs * 1.4f, cs, cs * 1.2f));
        }
        // A small dish on a post at the back of the roof.
        var dz = float.Lerp(z0, z1, 0.85f);
        var dr = H * 0.18f;
        var post = new Vector3(-half * 0.6f, top, dz);
        Shapes.Beam(rack.Mesh, Roles.DarkMechanics, post, post + new Vector3(0, H * 0.22f, 0), t * 1.2f);
        var dc = post + new Vector3(0, H * 0.22f + dr * 0.3f, 0);
        var aim = Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.5f));
        Shapes.Prism(rack.Mesh, Roles.Radome, dc - aim * dr * 0.2f, dc + aim * dr * 0.15f, dr * 0.2f, dr, 10, 0);
        c.AddMarker("sensor", rack, dc, aim, dr);
    }
}
