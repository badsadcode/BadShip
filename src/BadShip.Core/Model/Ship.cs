using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;

namespace BadShip.Model;

/// <summary>Primary modules make the silhouette, secondary ones are equipment and structure, tertiary ones are surface detail.</summary>
public enum ModuleTier { Primary, Secondary, Tertiary }

/// <summary>One named part of the ship: its own mesh (in ship space), its parent and what kind of thing it is.</summary>
public sealed class Module
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public ModuleTier Tier { get; init; }
    public string? ParentId { get; init; }

    /// <summary>Intentionally separable (hanging cargo pods): still attached, but marked so tools may drop it.</summary>
    public bool Detachable { get; set; }

    public Mesh Mesh { get; } = new();
    public Aabb Bounds { get; private set; } = Aabb.Empty;

    /// <summary>The lofted base forms of this module (before windows and plates): used for collision shapes.</summary>
    public List<Loft> Lofts { get; } = [];

    /// <summary>Extra data written into the node's glTF extras (doors: the rooms they join, size, how they open).</summary>
    public Dictionary<string, object>? Extras { get; set; }

    /// <summary>Where the node's origin goes (otherwise the mesh's centre): a room's centre, a door's hinge line.</summary>
    public Vector3? Pivot { get; set; }

    public void UpdateBounds() => Bounds = Mesh.Bounds();
}

/// <summary>A functional point with a direction: engine exhausts, docking ports, the bridge, sensors, hardpoints, lights.</summary>
public sealed record Marker(string Id, string Kind, string ModuleId, Vector3 Position, Vector3 Direction, float Radius, float Clearance);

/// <summary>A reserved volume inside the hull (habitable, cargo) for a future interior generator. No rooms are made.</summary>
public sealed record Envelope(string Id, string Kind, string ModuleId, Aabb Box);

/// <summary>A material by role, with linear colour, roughness, metalness and light given off.</summary>
public sealed record MaterialDef(string Role, Vector3 Color, float Roughness, float Metallic, Vector3 Emissive, float EmissiveStrength)
{
    /// <summary>A base-colour texture (PNG bytes) embedded in the GLB: the livery decal sheet.</summary>
    public byte[]? Png { get; init; }

    /// <summary>Alpha-cutout (glTF MASK): transparent parts of the texture are not drawn.</summary>
    public bool Cutout { get; init; }

    /// <summary>Opacity below 1 makes a see-through material (glTF BLEND): window glass.</summary>
    public float Alpha { get; init; } = 1f;

    /// <summary>Drawn from both sides (glass seen from inside and out).</summary>
    public bool DoubleSided { get; init; }
}

public sealed class Ship
{
    public required ShipRecipe Recipe { get; init; }
    public required ShipPlan Plan { get; init; }
    public List<Module> Modules { get; } = [];
    public List<Marker> Markers { get; } = [];
    public List<Envelope> Envelopes { get; } = [];
    public Dictionary<string, MaterialDef> Materials { get; set; } = [];

    /// <summary>The walkable interior, when the recipe asked for one (its windows are cut into this hull).</summary>
    public Interior.ShipInterior? Interior { get; set; }
    public Aabb Bounds { get; private set; } = Aabb.Empty;

    public Module? Find(string id) => Modules.FirstOrDefault(m => m.Id == id);

    public int Triangles => Modules.Sum(m => m.Mesh.TriangleCount);

    /// <summary>Drops empty detail modules and computes every bounding box.</summary>
    public void Finish()
    {
        Modules.RemoveAll(m => m.Tier == ModuleTier.Tertiary && m.Mesh.IsEmpty && Modules.All(c => c.ParentId != m.Id));
        Bounds = Aabb.Empty;
        foreach (var m in Modules)
        {
            m.UpdateBounds();
            // Drifting debris is near the ship, not part of it.
            if (m.Type != "debris") Bounds = Bounds.Union(m.Bounds);
        }
    }
}
