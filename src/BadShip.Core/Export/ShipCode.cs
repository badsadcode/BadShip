using System.Globalization;
using System.Text;
using BadShip.Generation;

namespace BadShip.Export;

/// <summary>
/// A ship code: a compact, URL-fragment-safe text (lowercase letters, digits and hyphens only) that names one exact
/// ship, e.g. <c>v0p4p0-s917417-fsurvey-mmeridian-zlarge-qe263208</c>. The first token is the generator version that
/// made it, so a viewer can open the code with that same version and get the same ship after later updates. Every
/// other token is a one-letter key and a value; anything left at "auto" is omitted.
/// </summary>
/// <remarks>
/// Keys: v version · s seed · f faction · m manufacturer · z size · a archetype · b family (hull body) · h hull form ·
/// u underside · p palette · d detail · i livery (none) · c condition (damaged, derelict) · l length (decimetres) · w prow · g bridge · t tiers · e engine layout ·
/// n engine style · y payload · k asymmetric (1/0) · x beam ratio (‰) · o height ratio (‰) ·
/// q stage seed: q + stage letter (p plan, s silhouette, u ventral, e equipment, d details, l look) + number.
/// Procedural names lose their hyphen: faction-12 → ffaction12, yard-7 → myard7.
/// </remarks>
public static class ShipCode
{
    private static readonly Dictionary<string, char> StageLetters = new()
    {
        ["plan"] = 'p', ["silhouette"] = 's', ["ventral"] = 'u', ["equipment"] = 'e', ["details"] = 'd', ["look"] = 'l', ["livery"] = 'i', ["condition"] = 'c',
    };

    public static string Encode(ShipRecipe r, string? version = null)
    {
        var t = new List<string> { "v" + (version ?? ShipGenerator.Version).Replace('.', 'p'), "s" + r.Seed.ToString(CultureInfo.InvariantCulture) };
        void Add(char key, string? value)
        {
            if (string.IsNullOrEmpty(value) || value == "auto") return;
            t.Add(key + Clean(value.Replace("-", "")));
        }
        Add('f', r.Faction);
        Add('m', r.Manufacturer);
        Add('z', r.Size);
        Add('a', r.Archetype);
        Add('b', r.Family);
        if (r.Palette is { Length: > 0 } pal && pal != "auto") Add('p', pal);
        if (r.Detail is { Length: > 0 } det && det != "high") Add('d', det);
        if (r.Livery is { Length: > 0 } liv && liv != "standard") Add('i', liv);
        if (r.Condition is { Length: > 0 } cond && cond != "intact") Add('c', cond);
        if (r.Length is { } len) t.Add("l" + ((int)MathF.Round(len * 10)).ToString(CultureInfo.InvariantCulture));
        // All 26 letters are taken: digit keys from here on. 1: interior (with windows).
        if (r.Interior) t.Add("1y");
        if (r.Overrides is { } o)
        {
            Add('h', o.HullForm);
            Add('u', o.Ventral);
            Add('w', o.ProwStyle);
            Add('g', o.BridgeStyle);
            if (o.Tiers is { } tiers) t.Add("t" + tiers);
            Add('e', o.EngineLayout);
            Add('n', o.EngineStyle);
            Add('y', o.Payload);
            if (o.Asymmetric is { } asym) t.Add("k" + (asym ? "1" : "0"));
            if (o.BeamRatio is { } br) t.Add("x" + (int)MathF.Round(br * 1000));
            if (o.HeightRatio is { } hr) t.Add("o" + (int)MathF.Round(hr * 1000));
            Add('r', o.WingStyle);
            if (o.WingSpan is { } wsp) t.Add("j" + (int)MathF.Round(wsp * 100));
        }
        if (r.StageSeeds is { } seeds)
            foreach (var (stage, seed) in seeds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                if (StageLetters.TryGetValue(stage, out var letter)) t.Add("q" + letter + seed.ToString(CultureInfo.InvariantCulture));
        return string.Join('-', t);
    }

    /// <summary>The generator version a code was made with ("0.4.0"), or null if the code has no version token.</summary>
    public static string? VersionOf(string code)
    {
        var first = Normalize(code).Split('-', 2)[0];
        return first.Length > 1 && first[0] == 'v' ? first[1..].Replace('p', '.') : null;
    }

    public static ShipRecipe Decode(string code)
    {
        var tokens = Normalize(code).Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new ArgumentException("That ship code is empty.");
        var r = new ShipRecipe();
        PlanOverrides O() => r.Overrides ??= new PlanOverrides();
        var inv = CultureInfo.InvariantCulture;
        var sawSeed = false;
        foreach (var tok in tokens)
        {
            if (tok.Length < 2) throw new ArgumentException($"'{tok}' in the ship code is not a key and value.");
            var v = tok[1..];
            try
            {
                switch (tok[0])
                {
                    case 'v': r.GeneratorVersion = v.Replace('p', '.'); break;
                    case 's': r.Seed = ulong.Parse(v, inv); sawSeed = true; break;
                    case 'f': r.Faction = Procedural(v, "faction"); break;
                    case 'm': r.Manufacturer = Procedural(v, "yard"); break;
                    case 'z': r.Size = v; break;
                    case 'a': r.Archetype = v; break;
                    case 'b': r.Family = v; break;
                    case 'h': O().HullForm = v; break;
                    case 'u': O().Ventral = v; break;
                    case 'p': r.Palette = v; break;
                    case 'd': r.Detail = v; break;
                    case 'i': r.Livery = v; break;
                    case 'c': r.Condition = v; break;
                    case 'l': r.Length = int.Parse(v, inv) / 10f; break;
                    case 'w': O().ProwStyle = v; break;
                    case 'g': O().BridgeStyle = v; break;
                    case 't': O().Tiers = int.Parse(v, inv); break;
                    case 'e': O().EngineLayout = v; break;
                    case 'n': O().EngineStyle = v; break;
                    case 'y': O().Payload = v; break;
                    case 'k': O().Asymmetric = v == "1"; break;
                    case 'x': O().BeamRatio = int.Parse(v, inv) / 1000f; break;
                    case 'o': O().HeightRatio = int.Parse(v, inv) / 1000f; break;
                    case 'r': O().WingStyle = v; break;
                    case 'j': O().WingSpan = int.Parse(v, inv) / 100f; break;
                    case '1': r.Interior = v == "y"; break;
                    case 'q':
                        var stage = StageLetters.FirstOrDefault(kv => kv.Value == v[0]).Key ?? throw new ArgumentException($"Unknown stage letter '{v[0]}'.");
                        (r.StageSeeds ??= [])[stage] = ulong.Parse(v[1..], inv);
                        break;
                    default: throw new ArgumentException($"Unknown key '{tok[0]}' in the ship code.");
                }
            }
            catch (FormatException) { throw new ArgumentException($"'{tok}' in the ship code has a bad number."); }
            catch (OverflowException) { throw new ArgumentException($"'{tok}' in the ship code has a number that is too large."); }
        }
        if (!sawSeed) throw new ArgumentException("That ship code has no seed (an s… token).");
        return r;
    }

    /// <summary>Accepts a bare code, "#code" or a whole link ending in "#code".</summary>
    private static string Normalize(string code)
    {
        var s = code.Trim();
        var hash = s.LastIndexOf('#');
        if (hash >= 0) s = s[(hash + 1)..];
        return s.ToLowerInvariant();
    }

    private static string Procedural(string v, string prefix) =>
        v.StartsWith(prefix) && v.Length > prefix.Length && v[prefix.Length..].All(char.IsAsciiDigit) ? $"{prefix}-{v[prefix.Length..]}" : v;

    private static string Clean(string v)
    {
        var sb = new StringBuilder();
        foreach (var ch in v.ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }
}
