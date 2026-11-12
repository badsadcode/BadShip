using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>Equipment that hangs from the undersides: sensor domes, a utility claw, belly doors, ventral docking
/// ports, ventral hardpoints and floodlights. Anything that would run into another module is dropped again.</summary>
public static partial class Equipment
{
    private static void PlaceVentral(BuildContext c)
    {
        var r = c.Equip;
        var L = c.Plan.Length;
        switch (c.Plan.Archetype)
        {
            case "research":
                Repeat(r.Int(1, 2), () => HangingDome(c, Num.Clamp(L * r.Range(0.012f, 0.02f), 1.6f, 4f)));
                if (r.Chance(0.4)) VentralDock(c);
                if (r.Chance(0.3)) BellyDoor(c);
                break;
            case "freighter":
                if (r.Chance(0.55)) Claw(c);
                Repeat(r.Int(1, 2), () => BellyDoor(c));
                if (r.Chance(0.4)) VentralDock(c);
                break;
            default:
                Repeat(r.Int(1, 2), () => VentralHardpoint(c));
                if (r.Chance(0.4)) HangingDome(c, Num.Clamp(L * 0.015f, 1.4f, 2.8f));
                break;
        }
        Repeat(r.Int(2, 6), () => Floodlight(c));
    }

    /// <summary>Keeps a freshly placed hanging item only if it doesn't run into another module.</summary>
    private static bool KeepIfClear(BuildContext c, Module m, Deck deck)
    {
        m.UpdateBounds();
        if (c.Clear(m.Bounds.Expand(-0.05f), deck.ModuleId, m.Id)) return true;
        c.Ship.Modules.Remove(m);
        c.Ship.Markers.RemoveAll(k => k.ModuleId == m.Id);
        return false;
    }

    public static void HangingDome(BuildContext c, float radius)
    {
        if (Spot(c, radius * 2f, radius * 2f, high: false, 1f, underside: true) is not var (deck, x, z)) return;
        var m = c.Add("ventral_dome", "ventral_sensor_dome", ModuleTier.Secondary, deck.ModuleId);
        var neck = radius * 0.45f;
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(x, deck.Y + 0.3f, z), new Vector3(x, deck.Y - neck, z), radius * 0.55f, radius * 0.45f, 8, MathF.PI / 8);
        Shapes.Geodesic(m.Mesh, Roles.Radome, new Vector3(x, deck.Y - neck - radius * 0.7f, z), radius, 1);
        c.AddMarker("sensor", m, new Vector3(x, deck.Y - neck - radius * 1.7f, z), -Vector3.UnitY, radius);
        KeepIfClear(c, m, deck);
    }

    /// <summary>A folding utility arm with a three-fingered claw (the research explorer concept's mining claw).</summary>
    public static void Claw(BuildContext c)
    {
        var r = c.Equip;
        var s = Num.Clamp(c.Plan.Length * 0.008f, 0.9f, 1.8f);
        if (Spot(c, s * 3f, s * 3f, high: false, 1.5f, underside: true) is not var (deck, x, z)) return;
        var m = c.Add("utility_claw", "utility_claw", ModuleTier.Secondary, deck.ModuleId);
        var paint = r.Chance(0.7) ? Roles.Hazard : Roles.Accent;
        var y = deck.Y;
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, y - s * 0.4f + 0.2f, z), new Vector3(s * 2.6f, s * 0.8f + 0.4f, s * 2.6f));
        var dir = r.Chance(0.5) ? 1f : -1f;
        var shoulder = new Vector3(x, y - s * 1f, z);
        var elbow = shoulder + new Vector3(0, -s * r.Range(2f, 3f), dir * s * r.Range(3.5f, 5f));
        var wrist = elbow + new Vector3(0, -s * r.Range(2.5f, 3.5f), -dir * s * r.Range(1f, 2f));
        Shapes.Box(m.Mesh, paint, shoulder, new Vector3(s * 1.1f));
        Shapes.Beam(m.Mesh, paint, shoulder, elbow, s * 0.55f);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, elbow, new Vector3(s * 0.9f));
        Shapes.Beam(m.Mesh, paint, elbow, wrist, s * 0.45f);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, wrist, new Vector3(s * 0.8f, s * 0.6f, s * 0.8f));
        for (var i = 0; i < 3; i++)
        {
            var ang = i * MathF.Tau / 3;
            var spread = new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * s * 0.7f;
            var knuckle = wrist + spread + new Vector3(0, -s * 0.6f, 0);
            var tip = wrist + spread * 0.4f + new Vector3(0, -s * 1.6f, 0);
            Shapes.Beam(m.Mesh, paint, wrist, knuckle, s * 0.22f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, knuckle, tip, s * 0.18f);
        }
        c.AddMarker("claw", m, wrist + new Vector3(0, -s * 1.6f, 0), -Vector3.UnitY, s);
        KeepIfClear(c, m, deck);
    }

    /// <summary>A large hatch in the belly: dark doors in a raised frame, with hazard stripes along the ends.</summary>
    public static void BellyDoor(BuildContext c)
    {
        var r = c.Equip;
        var w = Num.Clamp(c.Plan.Beam * r.Range(0.12f, 0.22f), 3f, 9f);
        var d = Num.Clamp(c.Plan.Length * r.Range(0.05f, 0.09f), 5f, 16f);
        if (Spot(c, w + 1, d + 1, high: false, 1f, underside: true) is not var (deck, x, z)) return;
        var m = c.Add("belly_door", "belly_door", ModuleTier.Secondary, deck.ModuleId);
        var y = deck.Y;
        Shapes.BoxMinMax(m.Mesh, Roles.DarkMechanics, new Vector3(x - w / 2, y - 0.12f, z - d / 2), new Vector3(x + w / 2, y + 0.2f, z + d / 2));
        Shapes.BoxMinMax(m.Mesh, Roles.DarkMechanics, new Vector3(x - 0.08f, y - 0.16f, z - d / 2), new Vector3(x + 0.08f, y - 0.1f, z + d / 2));
        var f = 0.45f;
        Shapes.BoxMinMax(m.Mesh, Roles.HullSecondary, new Vector3(x - w / 2 - f, y - 0.3f, z - d / 2 - f), new Vector3(x - w / 2, y + 0.2f, z + d / 2 + f));
        Shapes.BoxMinMax(m.Mesh, Roles.HullSecondary, new Vector3(x + w / 2, y - 0.3f, z - d / 2 - f), new Vector3(x + w / 2 + f, y + 0.2f, z + d / 2 + f));
        var stripes = Math.Max(4, (int)(w / 0.9f));
        foreach (var ze in new[] { z - d / 2 - f, z + d / 2 })
            for (var i = 0; i < stripes; i++)
                Shapes.BoxMinMax(m.Mesh, i % 2 == 0 ? Roles.Hazard : Roles.DarkMechanics,
                    new Vector3(x - w / 2 + w * i / stripes, y - 0.3f, ze), new Vector3(x - w / 2 + w * (i + 1) / stripes, y + 0.2f, ze + f));
        c.AddMarker("cargo_door", m, new Vector3(x, y - 0.3f, z), -Vector3.UnitY, MathF.Min(w, d) / 2);
        KeepIfClear(c, m, deck);
    }

    /// <summary>A docking collar pointing straight down, with its approach corridor kept clear.</summary>
    public static void VentralDock(BuildContext c)
    {
        var R = Num.Clamp(c.Plan.Length * 0.012f, 1.4f, 2.6f);
        var clearance = 12f;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (Spot(c, R * 2.8f, R * 2.8f, high: false, 1f, underside: true) is not var (deck, x, z)) return;
            var reach = R * 1.1f;
            var top = new Vector3(x, deck.Y + 1f, z);
            var end = new Vector3(x, deck.Y - reach - 0.4f, z);
            var corridor = Aabb.FromPoints(end - new Vector3(R * 1.4f, clearance, R * 1.4f), end + new Vector3(R * 1.4f, -0.2f, R * 1.4f));
            if (!c.Clear(corridor, deck.ModuleId)) continue;
            if (c.Ship.Markers.Any(k => k.Kind == "engine_exhaust" && corridor.Penetration(Validation.ShipValidator.ExhaustVolume(k)) > 0.05f)) continue;
            var m = c.Add("ventral_dock", "docking_collar", ModuleTier.Secondary, deck.ModuleId);
            Shapes.Prism(m.Mesh, Roles.HullSecondary, top, new Vector3(x, deck.Y - reach, z), R, R, 8, MathF.PI / 8);
            Shapes.Prism(m.Mesh, Roles.Hazard, new Vector3(x, deck.Y - reach * 0.4f, z), new Vector3(x, deck.Y - reach * 0.65f, z), R * 1.06f, R * 1.06f, 8, MathF.PI / 8);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y - reach, z), end, R * 1.3f, R * 1.3f, 8, MathF.PI / 8);
            c.AddMarker("docking_port", m, end, -Vector3.UnitY, R, clearance);
            if (KeepIfClear(c, m, deck)) return;
        }
    }

    public static void VentralHardpoint(BuildContext c)
    {
        var R = Num.Clamp(c.Plan.Length * 0.016f, 1.2f, 2.5f);
        if (Spot(c, R * 2.4f, R * 2.4f, high: false, 1.2f, underside: true) is not var (deck, x, z)) return;
        var m = c.Add("ventral_hardpoint", "hardpoint", ModuleTier.Secondary, deck.ModuleId);
        Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y + 0.2f, z), new Vector3(x, deck.Y - 0.5f, z), R, R * 0.95f, 8, MathF.PI / 8);
        Shapes.Prism(m.Mesh, Roles.Accent, new Vector3(x, deck.Y - 0.5f, z), new Vector3(x, deck.Y - 0.7f, z), R * 0.95f, R * 0.95f, 8, MathF.PI / 8);
        Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(x, deck.Y - 1.2f, z), new Vector3(R * 1.1f, R * 0.7f, R * 1.3f));
        c.AddMarker("hardpoint", m, new Vector3(x, deck.Y - 0.7f, z), -Vector3.UnitY, R);
        KeepIfClear(c, m, deck);
    }

    /// <summary>A downward floodlight: the references are lit from below.</summary>
    public static void Floodlight(BuildContext c)
    {
        if (Spot(c, 1.4f, 1.4f, high: false, 1f, underside: true) is not var (deck, x, z)) return;
        var m = c.Add("floodlight", "floodlight", ModuleTier.Secondary, deck.ModuleId);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y - 0.2f, z), new Vector3(1.1f, 0.6f, 1.1f));
        Shapes.Box(m.Mesh, Roles.Light, new Vector3(x, deck.Y - 0.55f, z), new Vector3(0.8f, 0.12f, 0.8f));
        c.AddMarker("floodlight", m, new Vector3(x, deck.Y - 0.6f, z), -Vector3.UnitY, 0.4f);
        KeepIfClear(c, m, deck);
    }
}
