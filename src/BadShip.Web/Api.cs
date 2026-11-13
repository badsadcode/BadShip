using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using BadShip.Export;
using BadShip.Generation;
using BadShip.Validation;

namespace BadShip.Web;

public static partial class Api
{
    private static string _info = "{}";

    /// <summary>Generates a ship from a recipe (JSON, same format as ship.recipe.json) and returns its GLB bytes.</summary>
    [JSExport]
    public static byte[] Generate(string recipeJson)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var recipe = JsonSerializer.Deserialize<ShipRecipe>(recipeJson, ShipFiles.Json) ?? new ShipRecipe();
        var ship = ShipGenerator.Generate(recipe);
        var report = ShipValidator.Validate(ship);
        var glb = GlbWriter.Write(ship);
        _info = "{\"ms\":" + sw.ElapsedMilliseconds + ",\"recipe\":" + ShipFiles.RecipeJson(ship) + ",\"metadata\":" + ShipFiles.MetadataJson(ship) +
                ",\"validation\":" + JsonSerializer.Serialize(report, ShipFiles.Json) + "}";
        return glb;
    }

    /// <summary>
    /// The interior of the ship a recipe makes: one deck (or all of them with <paramref name="deck"/> = -1) as GLB
    /// bytes, without the roof so it can be seen from above; empty when the ship has no interior. Interior info
    /// (decks, rooms by kind, validation) is in <see cref="InteriorInfo"/>.
    /// </summary>
    [JSExport]
    public static byte[] Interior(string recipeJson, int deck)
    {
        var recipe = JsonSerializer.Deserialize<ShipRecipe>(recipeJson, ShipFiles.Json) ?? new ShipRecipe();
        var ship = ShipGenerator.Generate(recipe);
        var interior = ship.Interior ?? BadShip.Interior.InteriorGenerator.Build(ship);
        if (interior is null) { _interiorInfo = """{"decks":0}"""; return []; }
        var report = BadShip.Interior.InteriorValidator.Validate(interior);
        var model = interior.Model;
        var d = Math.Clamp(deck, -1, interior.Decks.Count - 1);
        bool Keep(BadShip.Model.Module m)
        {
            if (m.Type == "ceiling") return false;
            if (d < 0) return true;
            for (var cur = m; cur != null; cur = cur.ParentId == null ? null : model.Find(cur.ParentId))
                if (cur.Id == $"deck_{d}") return true;
            return m.ParentId == null;
        }
        // A plain dictionary: the trimmed browser build cannot serialize anonymous types.
        var kinds = interior.Rooms.Where(r => d < 0 || r.Deck == d).GroupBy(r => r.Kind).OrderByDescending(g => g.Count())
            .Select(g => $"\"{g.Key}\":{g.Count()}");
        var parts = string.Join(",", interior.Decks.Select(x => $"\"{x.Part}\""));
        _interiorInfo = $"{{\"decks\":{interior.Decks.Count},\"deck\":{d},\"rooms\":{interior.Rooms.Count}," +
            $"\"doors\":{interior.Doors.Count(x => x.Kind != "opening")},\"deck_rooms\":{{{string.Join(",", kinds)}}},\"status\":\"{report.Status}\"," +
            $"\"parts\":[{parts}],\"ladders\":{interior.Lifts.Count(l => l.Kind == "ladder")},\"elevators\":{interior.Lifts.Count(l => l.Kind == "elevator")}}}";
        return GlbWriter.Write(model, Keep, markers: false);
    }

    private static string _interiorInfo = "{}";

    [JSExport]
    public static string InteriorInfo() => _interiorInfo;

    /// <summary>Recipe, metadata, validation and timing of the last generated ship, as JSON.</summary>
    [JSExport]
    public static string LastInfo() => _info;

    [JSExport]
    public static string Version() => ShipGenerator.Version;

    /// <summary>The ship code for a recipe (JSON): what the page puts in its address.</summary>
    [JSExport]
    public static string Encode(string recipeJson) =>
        ShipCode.Encode(JsonSerializer.Deserialize<ShipRecipe>(recipeJson, ShipFiles.Json) ?? new ShipRecipe());

    /// <summary>A ship code back to a recipe (JSON); throws with a readable message for a bad code.</summary>
    [JSExport]
    public static string Decode(string code) => JsonSerializer.Serialize(ShipCode.Decode(code), ShipFiles.Json);
}
