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
    public static readonly string[] Names = ["gunmetal", "naval", "offwhite", "slate"];

    private static readonly Dictionary<string, (string main, string secondary)> Hulls = new()
    {
        ["gunmetal"] = ("#4a5058", "#6b7179"),
        ["naval"] = ("#6c7378", "#8b9297"),
        ["offwhite"] = ("#a7a59d", "#7d7f80"),
        ["slate"] = ("#363c44", "#555c66"),
    };

    private static readonly string[] Accents = ["#c7902a", "#d9b21c", "#b85a22", "#a8291f"];

    public static Dictionary<string, MaterialDef> Build(string name, Rng look, string? accentHex = null)
    {
        var (main, secondary) = Hulls[name];
        return Build(main, secondary, accentHex, look);
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
