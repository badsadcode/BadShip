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

    /// <summary>small | medium | large | auto: size classes have their own rules, not just a scale.</summary>
    public string Size { get; set; } = "auto";

    /// <summary>The operating faction (paint, roles, sizes, suppliers): haulers | survey | patrol | miners | independent | faction-N | auto.</summary>
    public string Faction { get; set; } = "auto";

    /// <summary>The yard whose design language shapes the ship: halvorsen | meridian | korsak | tanaka | brightwater | yard-N | auto (from the faction's suppliers).</summary>
    public string Manufacturer { get; set; } = "auto";

    /// <summary>Overall length in metres, or null for the archetype's usual range.</summary>
    public float? Length { get; set; }

    /// <summary>gunmetal | naval | offwhite | slate | auto</summary>
    public string Palette { get; set; } = "auto";

    /// <summary>low (silhouette only) | medium | high</summary>
    public string Detail { get; set; } = "high";

    /// <summary>The ship's name, painted on the hull when it has livery (otherwise one is picked from the seed).</summary>
    public string? Name { get; set; }

    /// <summary>standard (name, registration, emblems, chevrons) | none (clean, unmarked).</summary>
    public string Livery { get; set; } = "standard";

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
    public string? Ventral { get; set; }

    /// <summary>Beam and height as fractions of the length.</summary>
    public float? BeamRatio { get; set; }
    public float? HeightRatio { get; set; }
}

/// <summary>Every choice the generator made from the recipe and seed (the "semantic plan" of the ship).</summary>
public sealed class ShipPlan
{
    public string Size { get; set; } = "";
    public string Faction { get; set; } = "";
    public string FactionName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string ManufacturerName { get; set; } = "";
    public string Signature { get; set; } = "";
    public string ShipName { get; set; } = "";
    public string Registration { get; set; } = "";
    public string Livery { get; set; } = "";
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

    /// <summary>What hangs below the main hull: none, keel (inverted terraces), gondola (on pylons) or pods (slung).</summary>
    public string Ventral { get; set; } = "";
    public string Palette { get; set; } = "";
}
