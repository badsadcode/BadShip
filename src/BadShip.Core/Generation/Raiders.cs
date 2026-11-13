using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Raiders: ordinary hulls (barge, block, twin) that pirates have rebuilt. After the shared passes, a kitbash pass
/// adds ram spikes jutting from the bow, scavenged junk welded on at odd angles (crates, tanks, plates), tattered sails
/// on masts, and extra welded-on guns. Mostly in the pirate scheme, whose mismatched plates come from the detail pass.
/// </summary>
public static class Raiders
{
    public static void Kitbash(BuildContext c)
    {
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("equipment") is { } es and > 0 ? es : c.Recipe.Seed, "kitbash");
        var before = c.Ship.Modules.Count;
        var hull = c.Lofts.FirstOrDefault(l => l.Kind == "hull");
        if (hull is null) return;
        Spikes(c, r, hull);
        Junk(c, r);
        if (r.Chance(0.65)) Sails(c, r);
        for (var i = 0; i < r.Int(1, 3); i++) Equipment.PointDefence(c);
        Equipment.DropBlocking(c, before);
    }

    /// <summary>Ram spikes: cones jutting forward from the bow face, splayed a little.</summary>
    private static void Spikes(BuildContext c, Rng r, LoftPart hull)
    {
        var loft = hull.Loft;
        var L = c.Plan.Length;
        var m = c.Add("ram_spikes", "spike", ModuleTier.Secondary, hull.Module.Id);
        // The last full-size ring before the prow narrows: spikes stand on the prow's front facets.
        var s = Math.Max(0, loft.Segments - 1);
        var n = r.Int(4, 8);
        for (var i = 0; i < n; i++)
        {
            var f = r.Pick(new[] { Profile.FacePosX, Profile.FaceNegX, Profile.FaceTopPosX, Profile.FaceTopNegX, Profile.FaceTop, Profile.FaceBottomPosX, Profile.FaceBottomNegX });
            var nrm = loft.FaceNormal(s, f);
            if (nrm == Vector3.Zero) continue;
            var foot = loft.FacePoint(s, f, r.Range(0.3f, 0.9f), r.Range(0.2f, 0.8f));
            var dir = Vector3.Normalize(Vector3.UnitZ * 1.4f + nrm * r.Range(0.2f, 0.6f));
            var len = L * r.Range(0.05f, 0.09f);
            var rad = len * r.Range(0.13f, 0.2f);
            Shapes.Prism(m.Mesh, r.Chance(0.6) ? Roles.DarkMechanics : Roles.HullSecondary, foot - dir * rad, foot + dir * len, rad, rad * 0.05f, 4, r.Range(0, 1f));
        }
        if (m.Mesh.IsEmpty) c.Ship.Modules.Remove(m);
    }

    /// <summary>Crates, tanks and patch plates welded on at odd angles, on decks and on the flanks.</summary>
    private static void Junk(BuildContext c, Rng r)
    {
        var L = c.Plan.Length;
        var count = 0;
        foreach (var deck in c.Decks.Where(d => !d.Underside && c.Ship.Find(d.ModuleId) != null).OrderBy(_ => r.NextDouble()).ToList())
        {
            for (var k = 0; k < r.Int(1, 3) && count < 8; k++)
            {
                var w = Num.Clamp(L * r.Range(0.025f, 0.05f), 2f, 14f);
                var d = w * r.Range(0.8f, 1.8f);
                if (!deck.TryPlace(r, w * 1.2f, d * 1.2f, 0.6f, out var x, out var z)) continue;
                var m = c.Add("welded_junk", "junk", ModuleTier.Secondary, deck.ModuleId);
                var h = w * r.Range(0.4f, 0.9f);
                var yaw = r.Range(-0.35f, 0.35f);
                var roll = r.Range(-0.15f, 0.15f);
                var ax = new Vector3(MathF.Cos(yaw), MathF.Sin(roll), MathF.Sin(yaw));
                var az = Vector3.Normalize(Vector3.Cross(ax, Vector3.UnitY));
                if (az.Z < 0) az = -az;
                var ay = Vector3.Normalize(Vector3.Cross(az, ax));
                var centre = new Vector3(x, deck.Y + h / 2 - 0.3f, z);
                if (r.Chance(0.35))
                    Shapes.Prism(m.Mesh, r.Pick(Roles.Cargo), centre - az * d / 2, centre + az * d / 2, h / 2, h / 2, 8, MathF.PI / 8);
                else
                    Shapes.OBox(m.Mesh, r.Pick(Roles.Cargo), centre, ax, ay, az, new Vector3(w / 2, h / 2, d / 2));
                // Weld straps.
                Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y + 0.1f, z), new Vector3(w * 1.1f, 0.3f, 0.5f));
                count++;
            }
        }
        // Patch plates bolted over the flanks, each tilted a little off the hull.
        var plates = c.Add("patch_plates", "junk", ModuleTier.Tertiary, c.Lofts.First(l => l.Kind == "hull").Module.Id);
        foreach (var side in c.Sides.Where(s => c.Ship.Modules.Contains(s.Module)).ToList())
        {
            var (along, across) = side.Loft.FaceSize(side.Segment, side.Face);
            var key = BuildContext.FaceKey(side.Module, side.Segment, side.Face);
            for (var k = 0; k < r.Int(1, 4); k++)
            {
                var du = Num.Clamp(r.Range(3f, 9f) / along, 0.04f, 0.3f);
                var dv = Num.Clamp(r.Range(2f, 6f) / across, 0.08f, 0.5f);
                var u0 = r.Range(0.03f, 0.97f - du);
                var v0 = r.Range(0.05f, 0.95f - dv);
                if (!c.TryUseSurface(key, u0, u0 + du, v0, v0 + dv)) continue;
                var n = side.Loft.FaceNormal(side.Segment, side.Face);
                var tilt = r.Range(0.05f, 0.25f);
                Vector3[] o =
                [
                    side.Loft.FacePoint(side.Segment, side.Face, u0, v0) + n * 0.15f,
                    side.Loft.FacePoint(side.Segment, side.Face, u0 + du, v0) + n * 0.15f,
                    side.Loft.FacePoint(side.Segment, side.Face, u0 + du, v0 + dv) + n * (0.15f + tilt * dv * across),
                    side.Loft.FacePoint(side.Segment, side.Face, u0, v0 + dv) + n * (0.15f + tilt * dv * across),
                ];
                Fighters.BevelPlate(plates.Mesh, r.Pick(Roles.Cargo), o, 0.25f, 0.2f);
            }
        }
        if (plates.Mesh.IsEmpty) c.Ship.Modules.Remove(plates);
    }

    /// <summary>Tattered sails: a mast with a yard and strips of sail with gaps, facing forward, on a high deck.</summary>
    private static void Sails(BuildContext c, Rng r)
    {
        var H = c.Plan.Height;
        foreach (var deck in c.Decks.Where(d => !d.Underside && c.Ship.Find(d.ModuleId) != null).OrderByDescending(d => d.Width * d.Length).ToList())
        {
            var sw = deck.Width * r.Range(0.8f, 1.2f);
            if (sw < 4 || !deck.TryPlace(r, 1.5f, 1.5f, 1f, out var x, out var z, centerX: (deck.X0 + deck.X1) / 2)) continue;
            // The yard is wide: keep it clear of the superstructure and masts.
            var mh = H * r.Range(0.9f, 1.5f);
            var foot = new Vector3(x, deck.Y - 0.2f, z);
            var box = new Aabb(foot + new Vector3(-sw / 2, 0.5f, -1f), foot + new Vector3(sw / 2, mh + 1, 1f));
            if (!c.Clear(box, deck.ModuleId)) continue;
            var m = c.Add("sail_mast", "sail", ModuleTier.Secondary, deck.ModuleId);
            var t = MathF.Max(0.3f, H * 0.02f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, foot, foot + new Vector3(0, mh, 0), t * 1.5f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, foot + new Vector3(-sw / 2, mh * 0.95f, 0), foot + new Vector3(sw / 2, mh * 0.95f, 0), t);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, foot + new Vector3(-sw * 0.4f, mh * 0.35f, 0), foot + new Vector3(sw * 0.4f, mh * 0.35f, 0), t * 0.8f);
            // Strips with gaps and ragged lengths.
            var strips = r.Int(4, 7);
            var cloth = r.Chance(0.6) ? Roles.Accent : r.Pick(Roles.Cargo);
            for (var k = 0; k < strips; k++)
            {
                if (r.Chance(0.15)) continue;
                var x0 = -sw / 2 + sw * k / strips + sw * 0.01f;
                var x1 = -sw / 2 + sw * (k + 1) / strips - sw * 0.02f;
                var top = mh * 0.93f;
                var bottom = mh * r.Range(0.38f, 0.55f);
                var billow = r.Range(0.3f, 1.2f);
                Vector3[] o = [foot + new Vector3(x0, top, 0.4f), foot + new Vector3(x1, top, 0.4f), foot + new Vector3(x1, bottom, 0.4f + billow), foot + new Vector3(x0, bottom + r.Range(0, mh * 0.08f), 0.4f + billow)];
                Fighters.BevelPlate(m.Mesh, cloth, o, 0.12f, 0.05f);
            }
            Shapes.Box(m.Mesh, Roles.NavRed, foot + new Vector3(0, mh + 0.4f, 0), new Vector3(0.6f, 0.6f, 0.6f));
            return;
        }
    }
}
