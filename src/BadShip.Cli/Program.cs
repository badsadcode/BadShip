using System.Diagnostics;
using System.Text.Json;
using BadShip.Export;
using BadShip.Generation;
using BadShip.Validation;

// badship: generate procedural industrial spaceship exteriors from a seed.
//
//   badship generate --seed 42 [--archetype research] [--family barge] [--length 150] [--palette naval] [--detail high] [--out out/ship]
//   badship gallery  --count 12 [--start 1] [--archetype ...] [--family ...] [--out out/gallery]
//   badship recipe   path/to/ship.recipe.json [--out out/ship]
//   badship stress   --count 300 [--start 1]

var cmd = args.Length > 0 ? args[0] : "help";
var opts = ParseOptions(args.Skip(1).ToArray());

try
{
    switch (cmd)
    {
        case "generate":
            {
                var recipe = RecipeFrom(opts, ulong.Parse(Opt("seed", "1")));
                var dir = Opt("out", "out/ship");
                return Emit(recipe, dir, Opt("name", $"ship_{recipe.Seed}")) ? 0 : 2;
            }
        case "recipe":
            {
                var path = opts.TryGetValue("_", out var p) ? p : throw new ArgumentException("Give a recipe file.");
                var recipe = ShipFiles.ReadRecipe(path);
                if (recipe.GeneratorVersion != ShipGenerator.Version)
                    Console.WriteLine($"note: recipe was made by generator {recipe.GeneratorVersion}, this is {ShipGenerator.Version}; the ship may differ.");
                return Emit(recipe, Opt("out", "out/ship"), Opt("name", $"ship_{recipe.Seed}")) ? 0 : 2;
            }
        case "gallery":
            {
                var count = int.Parse(Opt("count", "12"));
                var start = ulong.Parse(Opt("start", "1"));
                var dir = Opt("out", "out/gallery");
                Directory.CreateDirectory(dir);
                var index = new List<object>();
                var failures = 0;
                for (var i = 0; i < count; i++)
                {
                    var seed = start + (ulong)i;
                    var recipe = RecipeFrom(opts, seed);
                    var name = $"ship_{seed:D4}";
                    var ship = ShipGenerator.Generate(recipe);
                    var report = ShipValidator.Validate(ship);
                    ShipFiles.Save(ship, report, dir, name);
                    if (report.HasErrors) failures++;
                    index.Add(new
                    {
                        name, seed, archetype = ship.Plan.Archetype, family = ship.Plan.Family, prow = ship.Plan.ProwStyle,
                        length = MathF.Round(ship.Bounds.Size.Z, 1), triangles = ship.Triangles, status = report.Status,
                    });
                    Console.WriteLine($"{name}  {ship.Plan.Archetype,-9} {ship.Plan.Family,-6} {ship.Bounds.Size.Z,6:0} m  {ship.Triangles,7} tris  {report.Status}");
                    foreach (var issue in report.Issues) Console.WriteLine($"    [{issue.Severity}] {issue.Code}: {issue.Message}");
                }
                File.WriteAllText(Path.Combine(dir, "index.json"), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
                return failures == 0 ? 0 : 2;
            }
        case "stress":
            {
                var count = int.Parse(Opt("count", "300"));
                var start = ulong.Parse(Opt("start", "1"));
                var sw = Stopwatch.StartNew();
                var errors = 0;
                var warns = 0;
                var codes = new Dictionary<string, int>();
                long tris = 0;
                for (var i = 0; i < count; i++)
                {
                    var ship = ShipGenerator.Generate(RecipeFrom(opts, start + (ulong)i));
                    var report = ShipValidator.Validate(ship);
                    tris += ship.Triangles;
                    if (report.Status == "error") errors++;
                    if (report.Status == "warn") warns++;
                    foreach (var issue in report.Issues) codes[issue.Code] = codes.GetValueOrDefault(issue.Code) + 1;
                }
                Console.WriteLine($"{count} ships in {sw.Elapsed.TotalSeconds:0.0} s ({sw.Elapsed.TotalMilliseconds / count:0.0} ms each), avg {tris / count} triangles");
                Console.WriteLine($"errors: {errors}, warnings: {warns}");
                foreach (var (code, n) in codes.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  {code}: {n}");
                return errors == 0 ? 0 : 2;
            }
        default:
            Console.WriteLine("""
                badship — procedural industrial spaceship exteriors

                  badship generate --seed N [--archetype freighter|research|patrol] [--family barge|spine|twin|block]
                                   [--length M] [--palette gunmetal|naval|offwhite|slate] [--detail low|medium|high]
                                   [--name NAME] [--out DIR]
                  badship gallery  --count N [--start S] [same options] [--out DIR]
                  badship recipe   FILE.recipe.json [--out DIR]
                  badship stress   --count N [--start S] [same options]
                """);
            return cmd == "help" ? 0 : 1;
    }
}
catch (Exception e) when (e is ArgumentException or FormatException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

bool Emit(ShipRecipe recipe, string dir, string name)
{
    var sw = Stopwatch.StartNew();
    var ship = ShipGenerator.Generate(recipe);
    var report = ShipValidator.Validate(ship);
    var paths = ShipFiles.Save(ship, report, dir, name);
    Console.WriteLine($"{name}: {ship.Plan.Archetype} / {ship.Plan.Family}, {ship.Bounds.Size.Z:0} m, {ship.Triangles} triangles, {report.Status} ({sw.ElapsedMilliseconds} ms)");
    foreach (var issue in report.Issues) Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
    foreach (var path in paths) Console.WriteLine($"  {path}");
    return !report.HasErrors;
}

ShipRecipe RecipeFrom(Dictionary<string, string> o, ulong seed) => new()
{
    Seed = seed,
    Archetype = o.GetValueOrDefault("archetype", "auto"),
    Family = o.GetValueOrDefault("family", "auto"),
    Length = o.TryGetValue("length", out var l) ? float.Parse(l, System.Globalization.CultureInfo.InvariantCulture) : null,
    Palette = o.GetValueOrDefault("palette", "auto"),
    Detail = o.GetValueOrDefault("detail", "high"),
    Name = o.GetValueOrDefault("name"),
};

string Opt(string key, string fallback) => opts.GetValueOrDefault(key, fallback);

static Dictionary<string, string> ParseOptions(string[] a)
{
    var d = new Dictionary<string, string>();
    for (var i = 0; i < a.Length; i++)
    {
        if (a[i].StartsWith("--"))
        {
            var key = a[i][2..];
            d[key] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        }
        else d["_"] = a[i];
    }
    return d;
}
