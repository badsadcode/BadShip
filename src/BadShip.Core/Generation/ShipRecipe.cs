namespace BadShip.Generation;

/// <summary>
/// What the user asks for. "auto" fields are chosen from the seed. The same recipe and generator version always give
/// the same ship; the resolved <see cref="ShipPlan"/> is saved beside it so every choice is visible and repeatable.
/// </summary>
public sealed class ShipRecipe
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string GeneratorVersion { get; set; } = ShipGenerator.Version;
    public ulong Seed { get; set; }

    /// <summary>freighter | research | patrol | auto</summary>
    public string Archetype { get; set; } = "auto";

    /// <summary>barge | spine | twin | block | auto (the macro hull family)</summary>
    public string Family { get; set; } = "auto";

    /// <summary>Overall length in metres, or null for the archetype's usual range.</summary>
    public float? Length { get; set; }

    /// <summary>gunmetal | naval | offwhite | slate | auto</summary>
    public string Palette { get; set; } = "auto";

    /// <summary>low (silhouette only) | medium | high</summary>
    public string Detail { get; set; } = "high";

    /// <summary>Metadata only.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Seeds that replace the main seed for single stages (plan, silhouette, equipment, details, look). Keeping the
    /// main seed and changing one of these locks everything else: reroll the equipment, the surface detail or the
    /// colours of a hull you like without moving the hull.
    /// </summary>
    public Dictionary<string, ulong>? StageSeeds { get; set; }

    /// <summary>Choices fixed by hand. Anything left out is still chosen from the seed.</summary>
    public PlanOverrides? Overrides { get; set; }
}

/// <summary>Plan choices a user can pin. Null means "let the seed decide".</summary>
public sealed class PlanOverrides
{
    public string? HullForm { get; set; }
    public string? ProwStyle { get; set; }
    public string? BridgeStyle { get; set; }
    public int? Tiers { get; set; }
    public string? EngineLayout { get; set; }
    public string? EngineStyle { get; set; }
    public string? Payload { get; set; }
    public bool? Asymmetric { get; set; }

    /// <summary>Beam and height as fractions of the length.</summary>
    public float? BeamRatio { get; set; }
    public float? HeightRatio { get; set; }
}

/// <summary>Every choice the generator made from the recipe and seed (the "semantic plan" of the ship).</summary>
public sealed class ShipPlan
{
    public string Archetype { get; set; } = "";
    public string Family { get; set; } = "";
    public float Length { get; set; }
    public float Beam { get; set; }
    public float Height { get; set; }
    public string ProwStyle { get; set; } = "";

    /// <summary>slab (upright sides) or faceted (wide shallow top facets, shoulder crease, deep sloped lower hull).</summary>
    public string HullForm { get; set; } = "";
    public string BridgeStyle { get; set; } = "";
    public int Tiers { get; set; }
    public string EngineLayout { get; set; } = "";
    public string EngineStyle { get; set; } = "";
    public string Payload { get; set; } = "";
    public bool Asymmetric { get; set; }
    public string Palette { get; set; } = "";
}
