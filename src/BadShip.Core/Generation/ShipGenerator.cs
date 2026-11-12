using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The pipeline: recipe → plan → primary masses (hull family) → engines and superstructure → equipment → surface
/// detail → finished <see cref="Ship"/>. Validation and export are separate steps.
/// </summary>
public static class ShipGenerator
{
    /// <summary>Bumped whenever a change makes the same recipe give a different ship.</summary>
    public const string Version = "0.1.0";

    public static readonly string[] Archetypes = ["freighter", "research", "patrol"];
    public static readonly string[] Families = ["barge", "spine", "twin", "block"];

    public static Ship Generate(ShipRecipe recipe)
    {
        var c = new BuildContext(recipe);
        Planner.Resolve(c);
        switch (c.Plan.Family)
        {
            case "barge": HullFamilies.Barge(c); break;
            case "spine": HullFamilies.Spine(c); break;
            case "twin": HullFamilies.Twin(c); break;
            case "block": HullFamilies.Block(c); break;
            default: throw new ArgumentException($"Unknown family '{c.Plan.Family}'.");
        }
        Equipment.Place(c);
        if (c.DetailLevel > 0) Details.Add(c);
        c.Ship.Finish();
        return c.Ship;
    }
}

/// <summary>Turns the recipe's "auto" fields into concrete choices.</summary>
public static class Planner
{
    private static readonly Dictionary<string, (string, double)[]> FamilyWeights = new()
    {
        ["freighter"] = [("barge", 0.3), ("spine", 0.4), ("twin", 0.3)],
        ["research"] = [("barge", 0.5), ("spine", 0.25), ("twin", 0.25)],
        ["patrol"] = [("block", 0.65), ("barge", 0.25), ("twin", 0.1)],
    };

    private static readonly Dictionary<string, (string, double)[]> ProwWeights = new()
    {
        ["freighter"] = [("blunt", 0.3), ("stepped", 0.25), ("faceted", 0.2), ("shovel", 0.15), ("wedge", 0.1)],
        ["research"] = [("faceted", 0.4), ("wedge", 0.25), ("stepped", 0.15), ("shovel", 0.2)],
        ["patrol"] = [("wedge", 0.45), ("faceted", 0.3), ("shovel", 0.25)],
    };

    public static void Resolve(BuildContext c)
    {
        var r = c.PlanRng;
        var rec = c.Recipe;
        var p = c.Plan;

        p.Archetype = Choose(rec.Archetype, ShipGenerator.Archetypes, () => r.Pick(ShipGenerator.Archetypes), "archetype");
        p.Family = Choose(rec.Family, ShipGenerator.Families, () => r.Weighted(FamilyWeights[p.Archetype]), "family");

        var (lmin, lmax) = (p.Archetype, p.Family) switch
        {
            (_, "block") => (70f, 115f),
            ("patrol", _) => (90f, 130f),
            ("research", _) => (110f, 170f),
            _ => (130f, 210f),
        };
        p.Length = rec.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        (p.Beam, p.Height) = p.Family switch
        {
            "barge" => (L * r.Range(0.2f, 0.28f), L * r.Range(0.11f, 0.15f)),
            "spine" => (L * r.Range(0.18f, 0.24f), L * r.Range(0.1f, 0.13f)),
            "twin" => (L * r.Range(0.3f, 0.4f), L * r.Range(0.1f, 0.13f)),
            _ => (L * r.Range(0.34f, 0.46f), L * r.Range(0.17f, 0.24f)),
        };

        p.ProwStyle = r.Weighted(ProwWeights[p.Archetype]);
        if (p.Family == "twin" && p.ProwStyle == "stepped") p.ProwStyle = "faceted";
        p.BridgeStyle = r.Weighted<string>([("slope", 0.45), ("overhang", 0.4), ("blunt", 0.15)]);
        p.Tiers = p.Family switch { "barge" => r.Int(2, 4), "block" => r.Int(1, 3), _ => r.Int(1, 3) };
        p.EngineLayout = r.Weighted<string>([("2x1", 0.3), ("3x1", 0.2), ("2x2", 0.25), ("1x2", 0.15), ("4x1", 0.1)]);
        p.EngineStyle = r.Weighted<string>([("pod", 0.55), ("nacelle", 0.45)]);
        p.Payload = p.Family != "spine" ? "" : p.Archetype switch
        {
            "freighter" => r.Weighted<string>([("containers", 0.5), ("tanks", 0.3), ("pods", 0.2)]),
            "research" => r.Weighted<string>([("pods", 0.7), ("tanks", 0.3)]),
            _ => "pods",
        };
        p.Asymmetric = r.Chance(p.Archetype == "freighter" ? 0.35 : 0.2);
        p.Palette = Choose(rec.Palette, Palettes.Names, () => c.Look.Pick(Palettes.Names), "palette");
        c.Ship.Materials = Palettes.Build(p.Palette, c.Look);
    }

    private static string Choose(string requested, string[] allowed, Func<string> auto, string what)
    {
        if (string.IsNullOrEmpty(requested) || requested == "auto") return auto();
        if (!allowed.Contains(requested)) throw new ArgumentException($"Unknown {what} '{requested}'. Use one of: {string.Join(", ", allowed)}, auto.");
        return requested;
    }
}
