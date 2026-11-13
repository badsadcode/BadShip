using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Civilian ships on the shared hull body, prow, engines and bridge tiers, with a job (kept in the plan's payload):
/// liner — long and sleek, three bands of passenger windows down each flank, a long promenade tier and a glass dome;
/// medical — a white-and-teal hospital ship with a ward block marked with our own medical symbol (a pulse line in a
/// ring, not the protected red cross), a lit triage door and floodlights;
/// explorer — an observation dome, a big dish on a mast and long sensor booms reaching ahead of the bow.
/// The shared equipment, kit, livery and damage passes run on top.
/// </summary>
public static class CivilianShips
{
    public static readonly string[] Jobs = ["liner", "medical", "explorer"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        var lang = c.Lang;
        p.Family = "civilian";
        p.Payload = r.Weighted<string>([("liner", 0.4), ("medical", 0.25), ("explorer", 0.35)]);
        if (c.Recipe.Overrides?.Payload is { } pinned && Jobs.Contains(pinned)) p.Payload = pinned;
        var (lmin, lmax) = (p.Payload, p.Size) switch
        {
            ("liner", "small") => (90f, 150f), ("liner", "large") => (260f, 400f), ("liner", _) => (150f, 260f),
            (_, "small") => (60f, 110f), (_, "large") => (190f, 300f), _ => (110f, 190f),
        };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        p.Beam = L * (p.Payload == "liner" ? r.Range(0.16f, 0.21f) : r.Range(0.2f, 0.27f));
        p.Height = L * (p.Payload == "liner" ? r.Range(0.11f, 0.14f) : r.Range(0.12f, 0.16f));
        p.HullForm = p.Payload == "liner" || r.Chance(lang.Faceted) ? "faceted" : "slab";
        p.ProwStyle = p.Payload switch
        {
            "liner" => r.Weighted<string>([("faceted", 0.6), ("wedge", 0.4)]),
            "medical" => r.Weighted<string>([("blunt", 0.4), ("faceted", 0.35), ("stepped", 0.25)]),
            _ => r.Weighted<string>([("faceted", 0.45), ("stepped", 0.3), ("wedge", 0.25)]),
        };
        p.BridgeStyle = r.Weighted<string>([("slope", 0.5), ("overhang", 0.35), ("blunt", 0.15)]);
        p.Tiers = p.Size == "large" ? r.Int(2, 3) : r.Int(1, 2);
        p.EngineLayout = p.Size == "small" ? r.Weighted<string>([("2x1", 0.6), ("1x2", 0.4)]) : r.Weighted<string>([("2x1", 0.35), ("2x2", 0.25), ("3x1", 0.25), ("1x2", 0.15)]);
        p.EngineStyle = p.Payload == "liner" ? "nacelle" : r.Chance(lang.PodEngines) ? "pod" : "nacelle";
        p.Asymmetric = false;
        p.Ventral = p.Payload == "liner" ? r.Weighted<string>([("none", 0.6), ("keel", 0.4)]) : r.Weighted<string>([("keel", 0.3), ("gondola", 0.25), ("none", 0.45)]);
        c.PreferredPalette = p.Payload switch
        {
            "liner" => r.Chance(0.55) ? "luxury" : "civilian",
            "medical" => "medical",
            _ => r.Chance(0.5) ? "research" : null,
        };
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.06f, 0.08f);
        var zRear = -L / 2 + engLen;
        var zFront = L / 2;
        var prowLen = Parts.ProwLength(p.ProwStyle, L, r, p.HullForm == "faceted") * (p.Payload == "liner" ? 1f : 0.8f);
        var body = Parts.HullBody(c, 0, 0, B, H);
        var zBody0 = zRear + L * 0.025f;
        var zProw = zFront - prowLen;
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.93f, 0.9f)), (zBody0, body), (zProw, body)];
        rings.AddRange(Parts.Prow(body, zProw, zFront, p.ProwStyle));
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        var deck = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, zBody0 + 1, zProw - 0.5f, 0);
        if (p.ProwStyle == "stepped")
        {
            var (y, half, z0, z1) = Parts.StepDeck(body, zProw, zFront);
            c.AddDeck(hull, y, -half, half, z0, z1, 0);
        }
        c.AddBelly(hull, body, zBody0 + 1, zProw - 0.5f, main: true);
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.35f, body.Cy - H * 0.2f, zBody0 + 2), new Vector3(B * 0.35f, body.Top - 1, zProw)));
        Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy + H * r.Range(-0.05f, 0.1f), B * r.Range(0.65f, 0.9f), H * r.Range(0.85f, 1.1f));

        switch (p.Payload)
        {
            case "liner": Liner(c, hull, loft, body, deck, L, B, H); break;
            case "medical": Medical(c, hull, body, deck, zBody0, zProw, L, B, H); break;
            default: Explorer(c, hull, body, deck, zProw, zFront, L, B, H); break;
        }
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
    }

    // ------------------------------------------------------------------ liner

    private static void Liner(BuildContext c, Module hull, Loft loft, Profile body, Deck deck, float L, float B, float H)
    {
        var r = c.Shape;
        // Three window bands down each flank (the hull's main segment), with gaps for the lifeboat bays.
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            var (along, across) = loft.FaceSize(1, f);
            var key = BuildContext.FaceKey(hull, 1, f);
            for (var row = 0; row < 3; row++)
            {
                var up = 0.3f + row * 0.22f;
                var v0 = Parts.UpV(f, up);
                var v1 = Parts.UpV(f, up + 0.08f);
                if (v0 > v1) (v0, v1) = (v1, v0);
                Parts.PaneRow(hull.Mesh, loft, 1, f, 0.04f, 0.96f, v0, v1, Math.Max(6, (int)(along / 2.2f)), alongU: true, gap: 0.35f);
                c.UseSurface(key, 0.03f, 0.97f, v0 - 0.02f, v1 + 0.02f);
            }
        }
        // A long promenade tier with a glass roof strip, then the bridge tiers, then a dome.
        var tier = Parts.Superstructure(c, deck, p_Tiers(c), 0.7f, 0.92f, 0.8f, lengthMin: 0.5f, lengthMax: 0.75f, anchorAt: "mid");
        var top = tier?.Deck ?? deck;
        Dome(c, top, H * r.Range(0.28f, 0.4f), glassy: true);
        if (tier != null)
        {
            var m = c.DetailsOf(tier.Module);
            var lt = tier.Loft;
            Shapes.Patch(m.Mesh, Roles.Glass, lt, 1, Profile.FaceTop, 0.08f, 0.92f, 0.3f, 0.7f, 0.06f);
            c.UseSurface(BuildContext.FaceKey(tier.Module, 1, Profile.FaceTop), 0.06f, 0.94f, 0.28f, 0.72f);
        }
        // Lifeboat pods slung along the upper flanks.
        var boats = c.Add("lifeboats", "lifeboat", ModuleTier.Secondary, hull.Id);
        var n = Math.Clamp((int)(L / 35), 2, 8);
        var y = body.Top - body.ClampedChamfers().ct * 0.6f;
        var bl = Num.Clamp(L * 0.035f, 3f, 9f);
        foreach (var s in new[] { 1f, -1f })
            for (var i = 0; i < n; i++)
            {
                var z = float.Lerp(deck.Z0 + bl, deck.Z1 - bl, (i + 0.5f) / n);
                var x = s * (body.HalfWidthAt(y) - bl * 0.12f);
                var a = new Vector3(x, y, z - bl / 2);
                Shapes.Prism(boats.Mesh, Roles.Hazard, a, a + new Vector3(0, 0, bl), bl * 0.22f, bl * 0.22f, 8, MathF.PI / 8);
                Shapes.Box(boats.Mesh, Roles.Glass, a + new Vector3(s * bl * 0.18f, bl * 0.06f, bl * 0.7f), new Vector3(0.1f, bl * 0.1f, bl * 0.35f));
            }
    }

    private static int p_Tiers(BuildContext c) => c.Plan.Tiers;

    /// <summary>A glass dome on a ring base with frame ribs, on the given deck (a lounge, an observatory).</summary>
    private static void Dome(BuildContext c, Deck deck, float radius, bool glassy)
    {
        var r = c.Shape;
        var R = MathF.Min(radius, deck.Width * 0.45f);
        if (R < 1.5f || !deck.TryPlace(r, R * 2.1f, R * 2.1f, 0.8f, out var x, out var z, centerX: (deck.X0 + deck.X1) / 2)) return;
        var m = c.Add("dome", "dome", ModuleTier.Secondary, deck.ModuleId);
        var baseH = R * 0.25f;
        var foot = new Vector3(x, deck.Y - 0.2f, z);
        Shapes.Prism(m.Mesh, Roles.HullSecondary, foot, foot + new Vector3(0, baseH, 0), R * 1.05f, R, 16, 0);
        Shapes.Geodesic(m.Mesh, glassy ? Roles.Glass : Roles.Radome, foot + new Vector3(0, baseH, 0), R, 2, 0f);
        // Frame ribs over the glass.
        for (var k = 0; k < 4; k++)
        {
            var a = k * MathF.PI / 4;
            var d = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var prev = foot + new Vector3(0, baseH, 0) + d * R * 1.01f;
            for (var j = 1; j <= 6; j++)
            {
                var t = j / 6f * MathF.PI;
                var p = foot + new Vector3(0, baseH, 0) + (d * MathF.Cos(t) + Vector3.UnitY * MathF.Sin(t)) * R * 1.01f;
                Shapes.Beam(m.Mesh, Roles.HullSecondary, prev, p, MathF.Max(0.15f, R * 0.035f));
                prev = p;
            }
        }
        c.AddMarker("observation_deck", m, foot + new Vector3(0, baseH + R * 0.5f, 0), Vector3.UnitY, R);
    }

    // ------------------------------------------------------------------ medical

    private static void Medical(BuildContext c, Module hull, Profile body, Deck deck, float z0, float z1, float L, float B, float H)
    {
        var r = c.Shape;
        // The ward block: a big box over the deck with the medical symbol on both sides and the roof.
        var ww = MathF.Min(deck.Width * 0.95f, B * 0.85f);
        var wl = (z1 - z0) * r.Range(0.3f, 0.42f);
        var wh = H * r.Range(0.45f, 0.65f);
        if (deck.TryPlace(r, ww, wl, 1f, out var wx, out var wz, centerX: 0))
        {
            var info = Parts.TierBlock(c, "ward_block", hull.Id, wx, deck.Y - 0.3f, wz - wl / 2, wz + wl / 2, ww, wh + 0.3f, "blunt", bridge: false, windows: true);
            // The roof carries the symbol: nothing else goes there.
            if (info.Deck is { } roof) roof.Take(roof.X0, roof.X1, roof.Z0, roof.Z1);
            var m = c.DetailsOf(info.Module);
            var cy = deck.Y + wh * 0.5f;
            var symR = MathF.Min(wh * 0.36f, wl * 0.2f);
            foreach (var s in new[] { 1f, -1f })
            {
                var x = wx + s * (ww / 2 * 0.98f);
                Symbol(m.Mesh, new Vector3(x, cy, wz), new Vector3(s, 0, 0), symR);
                // Triage door: lit frame, hazard sill.
                var dz = wz + wl * 0.32f;
                Shapes.Box(m.Mesh, Roles.Light, new Vector3(x + s * 0.12f, deck.Y + wh * 0.42f, dz), new Vector3(0.2f, wh * 0.66f, wl * 0.14f));
                Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x + s * 0.2f, deck.Y + wh * 0.4f, dz), new Vector3(0.2f, wh * 0.58f, wl * 0.11f));
                Shapes.Box(m.Mesh, Roles.Hazard, new Vector3(x + s * 0.22f, deck.Y + 0.4f, dz), new Vector3(0.25f, 0.6f, wl * 0.14f));
                c.AddMarker("hatch", info.Module, new Vector3(x + s * 0.3f, deck.Y + wh * 0.4f, dz), new Vector3(s, 0, 0), wh * 0.3f);
            }
            Symbol(m.Mesh, new Vector3(wx, deck.Y + wh + 0.35f, wz), Vector3.UnitY, MathF.Min(ww * 0.35f, wl * 0.3f));
            // Floodlights on the roof corners.
            foreach (var s in new[] { 1f, -1f })
                foreach (var e in new[] { -1f, 1f })
                {
                    var at = new Vector3(wx + s * ww * 0.42f, deck.Y + wh + 0.6f, wz + e * wl * 0.45f);
                    Shapes.Box(m.Mesh, Roles.Light, at, new Vector3(0.8f, 0.5f, 0.8f));
                    c.AddMarker("floodlight", info.Module, at, new Vector3(s, 0.3f, e), 0.5f);
                }
        }
        Parts.Superstructure(c, deck, c.Plan.Tiers, 0.5f, 0.75f, 0.85f, lengthMin: 0.15f, lengthMax: 0.25f, anchorAt: "fore");
        // A band of the accent along both flanks.
        var band = c.Add("medical_band", "markings", ModuleTier.Tertiary, hull.Id);
        foreach (var s in new[] { 1f, -1f })
        {
            var y = body.Cy + body.H * 0.05f;
            Shapes.Box(band.Mesh, Roles.Accent, new Vector3(s * (body.HalfWidthAt(y) + 0.05f), y, (z0 + z1) / 2), new Vector3(0.12f, body.H * 0.08f, (z1 - z0) * 0.92f));
        }
    }

    /// <summary>Our medical symbol: an accent disc with a white ring and a white pulse line across it, facing <paramref name="n"/>.</summary>
    private static void Symbol(Mesh m, Vector3 c, Vector3 n, float R)
    {
        Shapes.Prism(m, Roles.Accent, c - n * 0.05f, c + n * 0.12f, R, R, 20, 0);
        var up = MathF.Abs(n.Y) > 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        var side = Vector3.Normalize(Vector3.Cross(up, n));
        var face = c + n * 0.15f;
        // The ring.
        for (var i = 0; i < 20; i++)
        {
            var a0 = i * MathF.Tau / 20;
            var a1 = (i + 1) * MathF.Tau / 20;
            Shapes.Beam(m, Roles.Radome, face + (side * MathF.Cos(a0) + up * MathF.Sin(a0)) * R * 0.82f, face + (side * MathF.Cos(a1) + up * MathF.Sin(a1)) * R * 0.82f, R * 0.07f);
        }
        // The pulse line: flat, a sharp peak, a dip, flat again.
        (float x, float y)[] pulse = [(-0.7f, 0), (-0.25f, 0), (-0.1f, 0.5f), (0.08f, -0.42f), (0.22f, 0.18f), (0.32f, 0), (0.7f, 0)];
        for (var i = 0; i + 1 < pulse.Length; i++)
            Shapes.Beam(m, Roles.Radome, face + (side * pulse[i].x + up * pulse[i].y) * R, face + (side * pulse[i + 1].x + up * pulse[i + 1].y) * R, R * 0.08f);
    }

    // ------------------------------------------------------------------ explorer

    private static void Explorer(BuildContext c, Module hull, Profile body, Deck deck, float zProw, float zFront, float L, float B, float H)
    {
        var r = c.Shape;
        var tier = Parts.Superstructure(c, deck, c.Plan.Tiers, 0.55f, 0.8f, 0.9f, lengthMin: 0.25f, lengthMax: 0.4f, anchorAt: "mid");
        // The observation dome forward on the main deck, the big dish aft.
        Dome(c, deck, H * r.Range(0.3f, 0.42f), glassy: true);
        var dr = MathF.Min(B * r.Range(0.3f, 0.42f), H * 0.6f);
        if (deck.TryPlace(r, dr * 1.4f, dr * 1.4f, 1f, out var x, out var z))
        {
            var m = c.Add("survey_dish", "dish", ModuleTier.Secondary, hull.Id);
            var mastH = H * r.Range(0.35f, 0.55f);
            var foot = new Vector3(x, deck.Y - 0.2f, z);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, foot, foot + new Vector3(0, mastH * 0.2f, 0), dr * 0.25f, dr * 0.2f, 8, 0);
            Shapes.Beam(m.Mesh, Roles.HullSecondary, foot, foot + new Vector3(0, mastH, 0), MathF.Max(0.4f, dr * 0.08f));
            var hub = foot + new Vector3(0, mastH, 0);
            var aim = Vector3.Normalize(new Vector3(r.Range(-0.4f, 0.4f), 0.75f, r.Range(0.2f, 0.6f)));
            Shapes.Prism(m.Mesh, Roles.Radome, hub - aim * dr * 0.1f, hub + aim * dr * 0.3f, dr * 0.18f, dr, 16, 0, capA: true, capB: true, capBMat: Roles.HullSecondary);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, hub + aim * dr * 0.3f, hub + aim * dr * 0.95f, MathF.Max(0.15f, dr * 0.03f));
            Shapes.Box(m.Mesh, Roles.Light, hub + aim * dr * 0.95f, new Vector3(dr * 0.08f, dr * 0.08f, dr * 0.08f));
            c.AddMarker("sensor", m, hub + aim * dr * 0.5f, aim, dr);
        }
        // Sensor booms reaching ahead of the bow from the shoulders, each tipped with a sensor pod.
        var booms = c.Add("sensor_booms", "boom", ModuleTier.Secondary, hull.Id);
        var y = body.Top - body.ClampedChamfers().ct;
        var bx = body.HalfWidthAt(y) * 0.8f;
        var zb = zProw - L * 0.05f;
        var len = (zFront - zb) + L * r.Range(0.02f, 0.06f);
        var t = MathF.Max(0.25f, H * 0.025f);
        foreach (var s in new[] { 1f, -1f })
        {
            var a = new Vector3(s * bx, y, zb);
            var b = new Vector3(s * bx * r.Range(0.6f, 1.1f), y + H * r.Range(0.02f, 0.12f), zb + len);
            Shapes.Beam(booms.Mesh, Roles.HullSecondary, a, b, t);
            Shapes.Prism(booms.Mesh, Roles.Radome, b - new Vector3(0, 0, t * 4), b + new Vector3(0, 0, t * 4), t * 2.2f, t * 1.2f, 8, MathF.PI / 8);
            Shapes.Box(booms.Mesh, s > 0 ? Roles.NavRed : Roles.Light, b + new Vector3(0, 0, t * 4.2f), new Vector3(t, t, t));
            c.AddMarker("sensor", booms, b, Vector3.UnitZ, t * 2);
        }
        _ = tier;
    }
}
