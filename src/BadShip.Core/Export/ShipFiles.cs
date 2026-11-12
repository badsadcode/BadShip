using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BadShip.Generation;
using BadShip.Model;
using BadShip.Validation;

namespace BadShip.Export;

/// <summary>Writes a ship's files: model (.glb), recipe, semantic metadata and validation report.</summary>
public static class ShipFiles
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes <c>{name}.glb</c>, <c>.recipe.json</c>, <c>.metadata.json</c>, <c>.validation.json</c>; returns the paths.</summary>
    public static string[] Save(Ship ship, ValidationReport report, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var paths = new[]
        {
            Path.Combine(dir, name + ".glb"),
            Path.Combine(dir, name + ".recipe.json"),
            Path.Combine(dir, name + ".metadata.json"),
            Path.Combine(dir, name + ".validation.json"),
        };
        File.WriteAllBytes(paths[0], GlbWriter.Write(ship));
        File.WriteAllText(paths[1], RecipeJson(ship));
        File.WriteAllText(paths[2], MetadataJson(ship));
        File.WriteAllText(paths[3], JsonSerializer.Serialize(report, Json));
        return paths;
    }

    public static string RecipeJson(Ship ship) =>
        JsonSerializer.Serialize(new { recipe = ship.Recipe, resolved = ship.Plan }, Json);

    public static ShipRecipe ReadRecipe(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var el = doc.RootElement.TryGetProperty("recipe", out var r) ? r : doc.RootElement;
        return el.Deserialize<ShipRecipe>(Json) ?? throw new InvalidDataException("Not a ship recipe.");
    }

    public static string MetadataJson(Ship ship)
    {
        static float[] V(Vector3 v) => [MathF.Round(v.X, 3), MathF.Round(v.Y, 3), MathF.Round(v.Z, 3)];
        var meta = new
        {
            generator = "BadShip",
            generatorVersion = ShipGenerator.Version,
            seed = ship.Recipe.Seed,
            archetype = ship.Plan.Archetype,
            family = ship.Plan.Family,
            axes = new { units = "meters", up = "+Y", forward = "+Z", port = "+X", handedness = "right" },
            bounds = new { min = V(ship.Bounds.Min), max = V(ship.Bounds.Max), size = V(ship.Bounds.Size) },
            triangles = ship.Triangles,
            modules = ship.Modules.Select(m => new
            {
                id = m.Id,
                type = m.Type,
                tier = m.Tier.ToString().ToLowerInvariant(),
                parent = m.ParentId,
                detachable = m.Detachable ? true : (bool?)null,
                triangles = m.Mesh.TriangleCount,
                bounds = m.Mesh.IsEmpty ? null : new { min = V(m.Bounds.Min), max = V(m.Bounds.Max) },
            }),
            markers = ship.Markers.Select(k => new
            {
                id = k.Id,
                kind = k.Kind,
                module = k.ModuleId,
                position = V(k.Position),
                direction = V(k.Direction),
                radius = MathF.Round(k.Radius, 3),
                clearance = k.Clearance > 0 ? MathF.Round(k.Clearance, 3) : (float?)null,
            }),
            envelopes = ship.Envelopes.Select(e => new { id = e.Id, kind = e.Kind, module = e.ModuleId, min = V(e.Box.Min), max = V(e.Box.Max) }),
            materials = ship.Materials.Values.Select(d => new { role = d.Role, color_linear = V(d.Color), roughness = d.Roughness, metallic = d.Metallic }),
        };
        return JsonSerializer.Serialize(meta, Json);
    }
}
