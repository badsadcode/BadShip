using System.Numerics;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>Material roles: every triangle belongs to one, and each becomes one replaceable glTF material.</summary>
public static class Roles
{
    public const string HullMain = "hull_main";
    public const string HullSecondary = "hull_secondary";
    public const string DarkMechanics = "dark_mechanics";
    public const string Glass = "glass";
    public const string Light = "emissive_light";
    public const string NavRed = "nav_red";
    public const string EngineGlow = "engine_glow";
    public const string Hazard = "hazard";
    public const string Accent = "accent";
    public const string Radome = "radome";
    public const string CargoA = "cargo_a";
    public const string CargoB = "cargo_b";
    public const string CargoC = "cargo_c";
    public const string Livery = "livery";
    /// <summary>Thin glowing accent lines (fighters): the accent colour, lit.</summary>
    public const string Glow = "accent_glow";

    public static readonly string[] All =
        [HullMain, HullSecondary, DarkMechanics, Glass, Light, NavRed, EngineGlow, Hazard, Accent, Radome, CargoA, CargoB, CargoC, Livery, Glow,
         Damage.Heat, Damage.Scorch, Damage.Breach, Damage.Embers, Damage.GlassDark, Damage.LightOff, Damage.EngineCold];

    public static readonly string[] Cargo = [CargoA, CargoB, CargoC];
}

/// <summary>Colour schemes from the style brief: restrained cool greys with ochre / yellow / orange / red utility accents.</summary>
public static class Palettes
{
    public static readonly string[] Names = ["gunmetal", "naval", "offwhite", "slate", "industrial", "military", "civilian", "research", "stealth", "pirate", "luxury"];

    /// <summary>
    /// Hull colours per scheme; the later schemes (from the hull concept sheets) also fix their accent and finish:
    /// industrial hazard yellow, military red, civilian and research blues, stealth near-black with a cyan glow,
    /// pirate rust with mismatched plates, luxury white with gold.
    /// </summary>
    private static readonly Dictionary<string, (string main, string secondary, string? accent, float rough, float metal)> Hulls = new()
    {
        ["gunmetal"] = ("#4a5058", "#6b7179", null, 0.62f, 0.55f),
        ["naval"] = ("#6c7378", "#8b9297", null, 0.62f, 0.55f),
        ["offwhite"] = ("#a7a59d", "#7d7f80", null, 0.62f, 0.55f),
        ["slate"] = ("#363c44", "#555c66", null, 0.62f, 0.55f),
        ["industrial"] = ("#3d4044", "#55585c", "#e0a81e", 0.7f, 0.5f),
        ["military"] = ("#2b2f35", "#3f444b", "#a8221c", 0.72f, 0.5f),
        ["civilian"] = ("#c9ccd0", "#8e98a3", "#2f6fa8", 0.5f, 0.45f),
        ["research"] = ("#d3d5d7", "#7c8794", "#1f5fae", 0.45f, 0.4f),
        ["stealth"] = ("#1c1f23", "#2a2e33", "#3fb8c8", 0.82f, 0.3f),
        ["pirate"] = ("#57463a", "#6d5c4a", "#8a2a1f", 0.85f, 0.45f),
        ["luxury"] = ("#dcd9d2", "#b5b0a6", "#c9a24a", 0.28f, 0.65f),
    };

    /// <summary>Schemes that wear their accent in bold bands (like the concept sheets' diagonal stripes).</summary>
    public static bool Banded(string palette) => palette is "industrial" or "military" or "civilian" or "research" or "luxury";

    private static readonly string[] Accents = ["#c7902a", "#d9b21c", "#b85a22", "#a8291f"];

    public static Dictionary<string, MaterialDef> Build(string name, Rng look, string? accentHex = null)
    {
        var (main, secondary, accent, rough, metal) = Hulls[name];
        var d = Build(main, secondary, accent ?? accentHex, look);
        d[Roles.HullMain] = d[Roles.HullMain] with { Roughness = rough, Metallic = metal };
        d[Roles.HullSecondary] = d[Roles.HullSecondary] with { Roughness = MathF.Max(0.2f, rough - 0.06f), Metallic = metal };
        if (name == "stealth") d[Roles.Glass] = d[Roles.Glass] with { Emissive = Linear("#3fb8c8"), EmissiveStrength = 0.9f };
        if (name == "luxury") d[Roles.Glass] = d[Roles.Glass] with { Emissive = Linear("#ffd9a0"), EmissiveStrength = 1.6f };
        if (name == "pirate")
        {
            // Scavenged plating: the cargo colours become rust, faded paint and bare metal.
            d[Roles.CargoA] = d[Roles.CargoA] with { Color = Linear("#7a4a2e"), Roughness = 0.9f };
            d[Roles.CargoB] = d[Roles.CargoB] with { Color = Linear("#5b6166"), Roughness = 0.8f };
            d[Roles.CargoC] = d[Roles.CargoC] with { Color = Linear("#8c7a52"), Roughness = 0.9f };
        }
        return d;
    }

    /// <summary>Materials from explicit hull colours (a faction's paint); a missing accent is picked from the seed.</summary>
    public static Dictionary<string, MaterialDef> Build(string main, string secondary, string? accentHex, Rng look)
    {
        var picked = look.Pick(Accents);
        var accent = string.IsNullOrEmpty(accentHex) ? picked : accentHex;
        var cargo = new List<string> { "#9c4a2a", "#c08a2a", "#5e6e7a", "#7a3326", "#46604a", "#b8a27a" };
        // Shuffle the container colours deterministically and take three.
        for (var i = cargo.Count - 1; i > 0; i--)
        {
            var j = look.Int(0, i);
            (cargo[i], cargo[j]) = (cargo[j], cargo[i]);
        }
        var d = new Dictionary<string, MaterialDef>();
        void Add(string role, string hex, float rough, float metal, string? emissive = null, float strength = 1f) =>
            d[role] = new MaterialDef(role, Linear(hex), rough, metal, emissive == null ? Vector3.Zero : Linear(emissive), strength);
        Add(Roles.HullMain, main, 0.62f, 0.55f);
        Add(Roles.HullSecondary, secondary, 0.55f, 0.6f);
        Add(Roles.DarkMechanics, "#22262b", 0.7f, 0.5f);
        Add(Roles.Glass, "#1c1a17", 0.2f, 0.1f, "#e8913a", 1.25f);
        Add(Roles.Light, "#f0e6d0", 0.4f, 0f, "#ffe2b0", 4f);
        Add(Roles.NavRed, "#ff2a1a", 0.4f, 0f, "#ff2a1a", 5f);
        Add(Roles.EngineGlow, "#9fdcff", 0.3f, 0f, "#5ec8ff", 6f);
        Add(Roles.Hazard, "#d9b21c", 0.55f, 0.2f);
        Add(Roles.Accent, accent, 0.55f, 0.3f);
        Add(Roles.Glow, accent, 0.4f, 0f, accent, 3.5f);
        Add(Roles.Radome, "#d8d8d2", 0.5f, 0.05f);
        Add(Roles.CargoA, cargo[0], 0.65f, 0.35f);
        Add(Roles.CargoB, cargo[1], 0.65f, 0.35f);
        Add(Roles.CargoC, cargo[2], 0.65f, 0.35f);
        return d;
    }

    /// <summary>
    /// The stealth look for fighters: the operator's hull colours pushed dark and matte, glass dimmed, so the glowing
    /// accent lines and engine slits carry the colour.
    /// </summary>
    public static void Stealth(Dictionary<string, MaterialDef> mats)
    {
        void Darken(string role, float k, float rough, float metal)
        {
            if (mats.TryGetValue(role, out var d)) mats[role] = d with { Color = d.Color * k + new Vector3(0.006f, 0.007f, 0.008f), Roughness = rough, Metallic = metal };
        }
        Darken(Roles.HullMain, 0.42f, 0.78f, 0.35f);
        Darken(Roles.HullSecondary, 0.38f, 0.7f, 0.45f);
        Darken(Roles.DarkMechanics, 0.6f, 0.8f, 0.4f);
        if (mats.TryGetValue(Roles.Glass, out var g)) mats[Roles.Glass] = g with { Color = Linear("#0d0f12"), EmissiveStrength = 0.7f, Roughness = 0.12f };
    }

    /// <summary>sRGB hex to linear RGB (glTF colour factors are linear).</summary>
    public static Vector3 Linear(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        static float L(int c)
        {
            var s = c / 255f;
            return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
        return new Vector3(L((v >> 16) & 255), L((v >> 8) & 255), L(v & 255));
    }
}
