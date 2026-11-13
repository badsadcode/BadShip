using BadShip.Geometry;
using BadShip.Generation;
using BadShip.Model;

namespace BadShip.Export;

/// <summary>
/// Game-ready extras derived from a finished ship: levels of detail and collision shapes.
/// LOD0 is the full ship. LOD1 drops surface detail and small equipment (antennas, hardpoints, crane loads...).
/// LOD2 keeps only the primary masses. Collision is a set of convex pieces: one per straight loft segment of every
/// primary or large secondary module, and a box for anything that isn't lofted.
/// </summary>
public static class GameAssets
{
    public static bool Lod1(Module m, Ship ship) =>
        m.Tier == ModuleTier.Primary || (m.Tier == ModuleTier.Secondary && m.Bounds.Size.Length() >= ship.Plan.Length * 0.06f);

    public static bool Lod2(Module m) => m.Tier == ModuleTier.Primary;

    public static int Triangles(Ship ship, Func<Module, bool> include) => ship.Modules.Where(include).Sum(m => m.Mesh.TriangleCount);

    /// <summary>A ship whose modules are the collision pieces (one node each, named "{module}.col{n}").</summary>
    public static Ship Collision(Ship ship)
    {
        var col = new Ship { Recipe = ship.Recipe, Plan = ship.Plan, Materials = ship.Materials };
        foreach (var m in ship.Modules)
        {
            if (m.Tier == ModuleTier.Tertiary || m.Mesh.IsEmpty) continue;
            if (m.Tier == ModuleTier.Secondary && m.Bounds.Size.Length() < ship.Plan.Length * 0.03f) continue;
            var n = 0;
            Module Piece() => new() { Id = $"{m.Id}.col{n++}", Type = "collision_convex", Tier = ModuleTier.Primary, ParentId = null };
            if (m.Lofts.Count > 0)
            {
                foreach (var loft in m.Lofts)
                    for (var s = 0; s < loft.Segments; s++)
                    {
                        if (loft.IsStep(s)) continue;
                        var piece = Piece();
                        new Loft([(loft.Zs[s], loft.Profiles[s]), (loft.Zs[s + 1], loft.Profiles[s + 1])]).Build(piece.Mesh, Roles.HullMain);
                        col.Modules.Add(piece);
                    }
            }
            else
            {
                var piece = Piece();
                Shapes.BoxMinMax(piece.Mesh, Roles.HullMain, m.Bounds.Min, m.Bounds.Max);
                col.Modules.Add(piece);
            }
        }
        col.Finish();
        return col;
    }
}
