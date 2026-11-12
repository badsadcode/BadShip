using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Manufacturer signature elements that are architecture rather than paint. (accent_bands lives in the detail pass,
/// engine_collar in the engine assembly.) dorsal_ridge: a long, low, chamfered spine along the main deck's centre
/// line, one of the strongest "same yard" cues in a silhouette.
/// </summary>
public static class Signatures
{
    public static void Apply(BuildContext c)
    {
        if (c.Lang.Signature == "dorsal_ridge") DorsalRidge(c);
    }

    private static void DorsalRidge(BuildContext c)
    {
        var r = c.Shape;
        var root = c.Ship.Modules.FirstOrDefault(m => m.ParentId is null);
        var deck = c.Decks.Where(d => !d.Underside && d.Level == 0 && (root == null || d.ModuleId == root.Id || c.Ship.Find(d.ModuleId)?.ParentId == root.Id))
            .OrderByDescending(d => d.Length * d.Width).FirstOrDefault();
        if (deck is null) return;
        // The longest free run along the centre line.
        var cx = (deck.X0 + deck.X1) / 2;
        var w = MathF.Max(1.2f, deck.Width * r.Range(0.1f, 0.16f));
        var taken = deck.Used.Where(u => u.x0 < cx + w / 2 && u.x1 > cx - w / 2).OrderBy(u => u.z0).ToList();
        var best = (z0: 0f, z1: 0f);
        var z = deck.Z0;
        foreach (var u in taken.Append((x0: 0f, x1: 0f, z0: deck.Z1, z1: deck.Z1)))
        {
            if (u.z0 - z > best.z1 - best.z0) best = (z, u.z0);
            z = MathF.Max(z, u.z1);
        }
        var len = (best.z1 - best.z0) * r.Range(0.6f, 0.85f) - 2;
        if (len < 6) return;
        var z0 = best.z0 + (best.z1 - best.z0 - len) / 2;
        var h = MathF.Max(1f, c.Plan.Height * r.Range(0.1f, 0.16f));
        var p = new Profile(cx, deck.Y - 0.3f + (h + 0.3f) / 2, w, w * 0.7f, h + 0.3f, w * 0.25f, 0);
        var low = p.ScaledOnBase(0.8f, 0.35f);
        var (m, _) = c.AddLoft("dorsal_ridge", "dorsal_ridge", ModuleTier.Secondary, deck.ModuleId,
            [(z0, low), (z0 + MathF.Min(len * 0.3f, h * 3), p), (z0 + len - MathF.Min(len * 0.15f, h * 1.5f), p), (z0 + len, low)], Roles.HullSecondary, "tier");
        deck.Take(cx - w / 2, cx + w / 2, z0, z0 + len);
        // A row of small lights along its crest.
        for (var i = 1; i < (int)(len / 6); i++)
            Shapes.Box(m.Mesh, Roles.Light, new Vector3(cx, p.Top + 0.05f, z0 + i * 6), new Vector3(0.4f, 0.12f, 0.4f));
    }
}
