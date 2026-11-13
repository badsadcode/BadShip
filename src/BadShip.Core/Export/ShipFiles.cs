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
            Path.Combine(dir, name + ".lod1.glb"),
            Path.Combine(dir, name + ".lod2.glb"),
            Path.Combine(dir, name + ".collision.glb"),
        };
        File.WriteAllBytes(paths[0], GlbWriter.Write(ship));
        File.WriteAllBytes(paths[4], GlbWriter.Write(ship, m => GameAssets.Lod1(m, ship)));
        File.WriteAllBytes(paths[5], GlbWriter.Write(ship, GameAssets.Lod2));
        File.WriteAllBytes(paths[6], GlbWriter.Write(GameAssets.Collision(ship), markers: false));
        File.WriteAllText(paths[1], RecipeJson(ship));
        File.WriteAllText(paths[2], MetadataJson(ship));
        File.WriteAllText(paths[3], JsonSerializer.Serialize(report, Json));
        return paths;
    }

    /// <summary>
    /// Writes an interior: name.interior.glb (walkable geometry, door and room nodes), name.interior.json (decks,
    /// rooms, doors, stairs and the validation report) and, for previews, name.interior.deck_N.glb per deck without
    /// the ceiling.
    /// </summary>
    public static string[] SaveInterior(Interior.ShipInterior interior, ValidationReport report, string dir, string name, Ship? exterior = null)
    {
        Directory.CreateDirectory(dir);
        var glb = Path.Combine(dir, name + ".interior.glb");
        var json = Path.Combine(dir, name + ".interior.json");
        File.WriteAllBytes(glb, GlbWriter.Write(interior.Model));
        File.WriteAllText(json, JsonSerializer.Serialize(new
        {
            decks = interior.Decks, rooms = interior.Rooms, doors = interior.Doors, stairs = interior.Stairs, windows = interior.Windows, validation = report,
        }, Json));
        var paths = new List<string> { glb, json };
        // Exterior and interior in one file, lined up (one scene to drop into an engine).
        if (interior.Model.Recipe is { } && exterior is not null)
        {
            var both = new Ship { Recipe = exterior.Recipe, Plan = exterior.Plan };
            both.Materials = new Dictionary<string, MaterialDef>(exterior.Materials);
            foreach (var (k, v) in interior.Model.Materials) both.Materials.TryAdd(k, v);
            both.Modules.AddRange(exterior.Modules);
            both.Modules.AddRange(interior.Model.Modules);
            both.Markers.AddRange(exterior.Markers);
            both.Markers.AddRange(interior.Model.Markers);
            both.Finish();
            var bothPath = Path.Combine(dir, name + ".ship_and_interior.glb");
            File.WriteAllBytes(bothPath, GlbWriter.Write(both));
            paths.Add(bothPath);
        }
        var model = interior.Model;
        foreach (var d in interior.Decks)
        {
            var deckId = $"deck_{d.Index}";
            bool OnDeck(Module m)
            {
                if (m.Type == "ceiling") return false;
                for (var cur = m; cur != null; cur = cur.ParentId == null ? null : model.Find(cur.ParentId))
                    if (cur.Id == deckId) return true;
                return m.ParentId == null;
            }
            var path = Path.Combine(dir, $"{name}.interior.deck_{d.Index}.glb");
            File.WriteAllBytes(path, GlbWriter.Write(model, OnDeck, markers: false));
            paths.Add(path);
        }
        return [.. paths];
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
            lods = new[]
            {
                new { level = 0, file = "", triangles = ship.Triangles },
                new { level = 1, file = ".lod1.glb", triangles = GameAssets.Triangles(ship, m => GameAssets.Lod1(m, ship)) },
                new { level = 2, file = ".lod2.glb", triangles = GameAssets.Triangles(ship, GameAssets.Lod2) },
            },
            collision = new { file = ".collision.glb", pieces = GameAssets.Collision(ship).Modules.Count, shape = "convex pieces, one node each" },
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
