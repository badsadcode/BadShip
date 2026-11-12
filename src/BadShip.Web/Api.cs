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

    /// <summary>Recipe, metadata, validation and timing of the last generated ship, as JSON.</summary>
    [JSExport]
    public static string LastInfo() => _info;

    [JSExport]
    public static string Version() => ShipGenerator.Version;
}
