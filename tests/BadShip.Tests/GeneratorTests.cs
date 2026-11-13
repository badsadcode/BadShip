using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using BadShip.Export;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;
using BadShip.Validation;

namespace BadShip.Tests;

public class GeneratorTests
{
    private static Ship Make(ulong seed, string archetype = "auto", string family = "auto", string palette = "auto", string detail = "high") =>
        ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = archetype, Family = family, Palette = palette, Detail = detail });

    private static string Signature(Ship s) => string.Join("|", s.Modules.Select(m =>
        $"{m.Id}:{m.Mesh.Positions.Count}:{m.Bounds.Min.X:F3},{m.Bounds.Min.Y:F3},{m.Bounds.Min.Z:F3}:{m.Bounds.Max.X:F3},{m.Bounds.Max.Y:F3},{m.Bounds.Max.Z:F3}"));

    [Fact]
    public void Same_recipe_gives_the_same_ship()
    {
        var a = Make(42);
        var b = Make(42);
        Assert.Equal(Signature(a), Signature(b));
        Assert.Equal(GlbWriter.Write(a), GlbWriter.Write(b));
    }

    [Fact]
    public void Different_seeds_give_different_ships()
    {
        var signatures = Enumerable.Range(1, 12).Select(i => Signature(Make((ulong)i))).ToHashSet();
        Assert.Equal(12, signatures.Count);
    }

    [Fact]
    public void Changing_the_palette_does_not_move_geometry()
    {
        foreach (var seed in new ulong[] { 3, 17, 99 })
        {
            var a = Make(seed, palette: "naval");
            var b = Make(seed, palette: "offwhite");
            Assert.Equal(Signature(a), Signature(b));
            Assert.NotEqual(a.Materials[Roles.HullMain].Color, b.Materials[Roles.HullMain].Color);
        }
    }

    [Fact]
    public void Detail_level_does_not_move_the_primary_masses()
    {
        foreach (var seed in new ulong[] { 5, 8, 21 })
        {
            string Primaries(Ship s) => Signature(new Ship { Recipe = s.Recipe, Plan = s.Plan }.Also(x => x.Modules.AddRange(s.Modules.Where(m => m.Tier == ModuleTier.Primary))));
            Assert.Equal(Primaries(Make(seed, detail: "low")), Primaries(Make(seed, detail: "high")));
        }
    }

    [Fact]
    public void Low_detail_has_no_tertiary_modules_and_fewer_triangles()
    {
        var low = Make(7, detail: "low");
        var high = Make(7, detail: "high");
        Assert.DoesNotContain(low.Modules, m => m.Tier == ModuleTier.Tertiary && m.Type != "livery"); // livery is optional decoration, not detail
        Assert.True(low.Triangles < high.Triangles);
    }

    [Theory]
    [InlineData("freighter", "barge")]
    [InlineData("freighter", "spine")]
    [InlineData("freighter", "twin")]
    [InlineData("research", "barge")]
    [InlineData("research", "spine")]
    [InlineData("patrol", "block")]
    [InlineData("patrol", "twin")]
    public void Every_family_and_archetype_validates_across_seeds(string archetype, string family)
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var ship = Make(seed, archetype, family);
            var report = ShipValidator.Validate(ship);
            Assert.False(report.HasErrors, $"{archetype}/{family} seed {seed}: " + string.Join("; ", report.Issues.Select(i => i.Message)));
            Assert.Equal(family, ship.Plan.Family);
            Assert.Contains(ship.Markers, m => m.Kind == "engine_exhaust");
            Assert.Contains(ship.Markers, m => m.Kind == "bridge");
        }
    }

    [Fact]
    public void Hundreds_of_random_seeds_have_no_errors()
    {
        var failures = new List<string>();
        for (ulong seed = 1000; seed < 1400; seed++)
        {
            var report = ShipValidator.Validate(Make(seed));
            if (report.HasErrors) failures.Add($"{seed}: {report.Issues.First(i => i.Severity == "error").Message}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void Modules_form_one_attached_tree()
    {
        var ship = Make(11);
        var ids = ship.Modules.Select(m => m.Id).ToHashSet();
        Assert.Single(ship.Modules, m => m.ParentId is null);
        Assert.All(ship.Modules.Where(m => m.ParentId is not null), m => Assert.Contains(m.ParentId!, ids));
    }

    [Fact]
    public void Validator_catches_a_floating_module_and_a_blocked_exhaust()
    {
        var ship = Make(4);
        var root = ship.Modules.Single(m => m.ParentId is null);
        var floater = new Module { Id = "floater", Type = "test", Tier = ModuleTier.Secondary, ParentId = root.Id };
        Shapes.Box(floater.Mesh, Roles.HullMain, root.Bounds.Max + new Vector3(50), new Vector3(2));
        var exhaust = ship.Markers.First(m => m.Kind == "engine_exhaust");
        var blocker = new Module { Id = "blocker", Type = "test", Tier = ModuleTier.Secondary, ParentId = root.Id };
        Shapes.Box(blocker.Mesh, Roles.HullMain, exhaust.Position + exhaust.Direction * 5, new Vector3(4));
        ship.Modules.Add(floater);
        ship.Modules.Add(blocker);
        ship.Finish();
        var codes = ShipValidator.Validate(ship).Issues.Select(i => i.Code).ToList();
        Assert.Contains("attach.floating", codes);
        Assert.Contains("exhaust.blocked", codes);
    }

    [Fact]
    public void Recipe_round_trips_through_json()
    {
        var ship = Make(77, archetype: "research");
        var path = Path.Combine(Path.GetTempPath(), $"badship_{Guid.NewGuid():N}.recipe.json");
        File.WriteAllText(path, ShipFiles.RecipeJson(ship));
        try
        {
            var again = ShipGenerator.Generate(ShipFiles.ReadRecipe(path));
            Assert.Equal(Signature(ship), Signature(again));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Rerolling_a_stage_keeps_the_hull_and_changes_that_stage()
    {
        foreach (var seed in new ulong[] { 2, 7, 31 })
        {
            var baseRecipe = new ShipRecipe { Seed = seed, Archetype = "freighter", Family = "barge" };
            var reroll = new ShipRecipe { Seed = seed, Archetype = "freighter", Family = "barge", StageSeeds = new() { ["equipment"] = 999 } };
            var a = ShipGenerator.Generate(baseRecipe);
            var b = ShipGenerator.Generate(reroll);
            string Primaries(Ship s) => string.Join("|", s.Modules.Where(m => m.Tier == ModuleTier.Primary).Select(m => $"{m.Id}:{m.Bounds.Min}:{m.Bounds.Max}"));
            string Equipment(Ship s) => string.Join("|", s.Modules.Where(m => m.Tier == ModuleTier.Secondary).Select(m => $"{m.Id}:{m.Bounds.Min}"));
            Assert.Equal(Primaries(a), Primaries(b));
            Assert.NotEqual(Equipment(a), Equipment(b));
            Assert.False(ShipValidator.Validate(b).HasErrors);
        }
    }

    [Fact]
    public void Overrides_pin_choices_without_reshuffling_the_rest()
    {
        var plain = Make(12, archetype: "research", family: "barge");
        var pinned = ShipGenerator.Generate(new ShipRecipe
        {
            Seed = 12, Archetype = "research", Family = "barge",
            Overrides = new PlanOverrides { HullForm = "faceted", Tiers = 4, EngineLayout = "2x2", ProwStyle = "wedge" },
        });
        Assert.Equal("faceted", pinned.Plan.HullForm);
        Assert.Equal(4, pinned.Plan.Tiers);
        Assert.Equal("2x2", pinned.Plan.EngineLayout);
        Assert.Equal("wedge", pinned.Plan.ProwStyle);
        Assert.Equal(plain.Plan.BridgeStyle, pinned.Plan.BridgeStyle);
        Assert.Equal(plain.Plan.Length, pinned.Plan.Length);
        Assert.Throws<ArgumentException>(() => ShipGenerator.Generate(new ShipRecipe { Seed = 1, Overrides = new PlanOverrides { ProwStyle = "pointy" } }));
        Assert.Throws<ArgumentException>(() => ShipGenerator.Generate(new ShipRecipe { Seed = 1, StageSeeds = new() { ["hull"] = 3 } }));
    }

    [Fact]
    public void Lods_get_smaller_and_collision_covers_the_hull()
    {
        foreach (var seed in new ulong[] { 1, 5, 9 })
        {
            var ship = Make(seed);
            var lod1 = GameAssets.Triangles(ship, m => GameAssets.Lod1(m, ship));
            var lod2 = GameAssets.Triangles(ship, GameAssets.Lod2);
            Assert.True(ship.Triangles > lod1 && lod1 > lod2 && lod2 > 0, $"{ship.Triangles} > {lod1} > {lod2}");
            var col = GameAssets.Collision(ship);
            Assert.NotEmpty(col.Modules);
            var hull = ship.Modules.Single(m => m.ParentId is null).Bounds;
            Assert.True(col.Bounds.Penetration(hull) > 1f);
            Assert.All(col.Modules, m => Assert.EndsWith(".col" + m.Id.Split(".col")[^1], m.Id));
        }
    }

    [Theory]
    [InlineData("barge", "keel")]
    [InlineData("barge", "gondola")]
    [InlineData("barge", "pods")]
    [InlineData("block", "keel")]
    [InlineData("spine", "gondola")]
    [InlineData("twin", "pods")]
    public void Every_ventral_structure_validates_and_hangs_below_the_hull(string family, string ventral)
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Family = family, Overrides = new PlanOverrides { Ventral = ventral } });
            var report = ShipValidator.Validate(ship);
            Assert.False(report.HasErrors, $"{family}/{ventral} seed {seed}: " + string.Join("; ", report.Issues.Select(i => i.Message)));
            Assert.Equal(ventral, ship.Plan.Ventral);
            var type = ventral switch { "keel" => "keel", "gondola" => "gondola", _ => "slung_" };
            var parts = ship.Modules.Where(m => m.Type.StartsWith(type)).ToList();
            if (parts.Count == 0) continue; // a span too short for the structure is allowed to skip it
            var parentBottom = ship.Find(parts[0].ParentId!)!.Bounds.Min.Y;
            Assert.All(parts, m => Assert.True(m.Bounds.Min.Y < parentBottom - 0.5f, $"{m.Id} should hang below its parent"));
        }
    }

    [Fact]
    public void Rerolling_the_ventral_stage_keeps_the_topside()
    {
        foreach (var seed in new ulong[] { 3, 6, 14 })
        {
            var a = Make(seed, family: "barge");
            var b = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Family = "barge", StageSeeds = new() { ["ventral"] = 77 } });
            string Topside(Ship s) => string.Join("|", s.Modules.Where(m => m.Tier == ModuleTier.Primary && m.Type is not ("keel" or "gondola"))
                .Select(m => $"{m.Id}:{m.Bounds.Min}:{m.Bounds.Max}"));
            Assert.Equal(Topside(a), Topside(b));
        }
    }

    [Fact]
    public void A_manufacturer_gives_its_ships_a_shared_architecture()
    {
        // Halvorsen builds square: almost never faceted, pods for engines, blunt or stepped prows, its engine collar.
        var ships = Enumerable.Range(1, 60).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Manufacturer = "halvorsen" })).ToList();
        Assert.All(ships, s => Assert.Equal("Halvorsen Heavy Works", s.Plan.ManufacturerName));
        // (Fighters have their own engines and forms.)
        ships = ships.Where(s => s.Plan.Family is "barge" or "spine" or "twin" or "block").ToList();
        Assert.True(ships.Count >= 18);
        Assert.All(ships, s => Assert.Equal("pod", s.Plan.EngineStyle));
        Assert.True(ships.Count(s => s.Plan.HullForm == "slab") >= ships.Count * 0.8);
        Assert.True(ships.Count(s => s.Plan.ProwStyle is "blunt" or "stepped") >= ships.Count * 0.6);
        // Meridian builds faceted survey hulls with nacelles.
        var meridian = Enumerable.Range(1, 30).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Manufacturer = "meridian", Archetype = "research" })).ToList();
        Assert.True(meridian.Count(s => s.Plan.HullForm == "faceted") >= 25);
        Assert.True(meridian.Count(s => s.Plan.EngineStyle == "nacelle") >= 20);
    }

    [Fact]
    public void A_faction_paints_its_ships_and_picks_its_roles()
    {
        var fleet = Enumerable.Range(1, 30).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Faction = "patrol" })).ToList();
        var paint = Palettes.Linear(Factions.Presets["patrol"].HullMain);
        // Fighters wear the same paint pushed dark (the stealth look).
        // Ship types with a preferred scheme (military warships, ...) may wear it instead; the rest wear the faction's paint.
        Assert.All(fleet.Where(s => s.Plan.Family != "fighter" && s.Plan.Palette == "faction"), s => Assert.Equal(paint, s.Materials[Roles.HullMain].Color));
        Assert.True(fleet.Count(s => s.Plan.Palette == "faction") >= 12);
        Assert.All(fleet.Where(s => s.Plan.Family == "fighter"), s => Assert.True(s.Materials[Roles.HullMain].Color.Length() < paint.Length()));
        Assert.True(fleet.Count(s => s.Plan.Archetype is "patrol" or "fighter") >= 15);
        Assert.Contains(fleet, s => s.Plan.Archetype == "fighter");
        Assert.All(fleet, s => Assert.Contains(s.Plan.Manufacturer, Factions.Presets["patrol"].Suppliers.Keys));
    }

    [Fact]
    public void Size_classes_have_their_own_scale_and_complexity()
    {
        double Avg(string size, Func<Ship, double> f) => Enumerable.Range(1, 25).Select(i => f(ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Size = size }))).Average();
        Assert.True(Avg("small", s => s.Plan.Length) < 65);
        Assert.True(Avg("large", s => s.Plan.Length) > 180);
        Assert.True(Avg("small", s => s.Triangles) < Avg("medium", s => s.Triangles));
        Assert.True(Avg("medium", s => s.Triangles) < Avg("large", s => s.Triangles));
        // Small craft are stubbier, not scaled-down large ships.
        Assert.True(Avg("small", s => s.Plan.Height / s.Plan.Length) > Avg("large", s => s.Plan.Height / s.Plan.Length) * 1.3);
        Assert.All(Enumerable.Range(1, 25).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Size = "small" })), s => Assert.True(s.Plan.Tiers <= 2));
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public void Every_size_class_validates(string size)
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var report = ShipValidator.Validate(ShipGenerator.Generate(new ShipRecipe { Seed = seed, Size = size }));
            Assert.False(report.HasErrors, $"{size} seed {seed}: " + string.Join("; ", report.Issues.Select(i => i.Message)));
        }
    }

    [Fact]
    public void Procedural_yards_and_factions_are_stable_and_valid()
    {
        Assert.Equal(Manufacturers.Procedural(42).Name, Manufacturers.Procedural(42).Name);
        Assert.NotEqual(Manufacturers.Procedural(42).Name, Manufacturers.Procedural(43).Name);
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Faction = $"faction-{seed}", Manufacturer = $"yard-{seed * 7}" });
            Assert.False(ShipValidator.Validate(ship).HasErrors);
        }
        Assert.Throws<ArgumentException>(() => ShipGenerator.Generate(new ShipRecipe { Seed = 1, Manufacturer = "acme" }));
    }

    [Fact]
    public void Ship_codes_round_trip_to_the_same_ship()
    {
        var recipes = new[]
        {
            new ShipRecipe { Seed = 917417 },
            new ShipRecipe { Seed = 3, Faction = "survey", Manufacturer = "meridian", Size = "large", Archetype = "research", Family = "barge", Palette = "naval", Detail = "medium" },
            new ShipRecipe { Seed = 12, Faction = "faction-12", Manufacturer = "yard-7", StageSeeds = new() { ["equipment"] = 263208, ["look"] = 5, ["ventral"] = 99 } },
            new ShipRecipe { Seed = 8, Length = 152.3f, Overrides = new PlanOverrides { HullForm = "faceted", Ventral = "keel", ProwStyle = "wedge", Tiers = 3, EngineLayout = "2x2", EngineStyle = "pod", Asymmetric = true, BeamRatio = 0.25f } },
        };
        foreach (var r in recipes)
        {
            var code = ShipCode.Encode(r);
            Assert.Matches("^[a-z0-9-]+$", code);
            Assert.StartsWith("v" + ShipGenerator.Version.Replace('.', 'p') + "-s", code);
            var back = ShipCode.Decode(code);
            Assert.Equal(ShipCode.Encode(back), code);
            Assert.Equal(Signature(ShipGenerator.Generate(r)), Signature(ShipGenerator.Generate(back)));
        }
        Assert.Equal(ShipGenerator.Version, ShipCode.VersionOf("https://claude.ai/artifact/x#" + ShipCode.Encode(recipes[1])));
        Assert.Equal("v0p4p0-s917417", ShipCode.Encode(new ShipRecipe { Seed = 917417 }, "0.4.0"));
    }

    [Fact]
    public void Bad_ship_codes_explain_themselves()
    {
        Assert.Throws<ArgumentException>(() => ShipCode.Decode(""));
        Assert.Throws<ArgumentException>(() => ShipCode.Decode("v0p4p0-fsurvey"));
        Assert.Throws<ArgumentException>(() => ShipCode.Decode("v0p4p0-s12-9x"));
        Assert.Throws<ArgumentException>(() => ShipCode.Decode("v0p4p0-sabc"));
    }

    [Fact]
    public void Livery_is_painted_on_and_can_be_left_off()
    {
        foreach (var seed in new ulong[] { 4, 9, 12 })
        {
            var marked = ShipGenerator.Generate(new ShipRecipe { Seed = seed });
            var clean = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Livery = "none" });
            Assert.Contains(marked.Modules, m => m.Type == "livery" && !m.Mesh.IsEmpty);
            Assert.NotNull(marked.Materials[Roles.Livery].Png);
            Assert.True(marked.Materials[Roles.Livery].Cutout);
            Assert.DoesNotContain(clean.Modules, m => m.Type == "livery");
            Assert.False(clean.Materials.ContainsKey(Roles.Livery));
            Assert.Matches("^[A-Z]{3}-[0-9]{3,4}$", marked.Plan.Registration);
            Assert.False(string.IsNullOrWhiteSpace(marked.Plan.ShipName));
            // Livery never moves the hull.
            string Primaries(Ship s) => string.Join("|", s.Modules.Where(m => m.Tier == ModuleTier.Primary).Select(m => $"{m.Id}:{m.Bounds.Min}:{m.Bounds.Max}"));
            Assert.Equal(Primaries(marked), Primaries(clean));
            Assert.False(ShipValidator.Validate(marked).HasErrors);
        }
        var named = ShipGenerator.Generate(new ShipRecipe { Seed = 3, Name = "Thalassa" });
        Assert.Equal("THALASSA", named.Plan.ShipName);
        Assert.Equal("v" + ShipGenerator.Version.Replace('.', 'p') + "-s3-inone", ShipCode.Encode(new ShipRecipe { Seed = 3, Livery = "none" }));
    }

    [Fact]
    public void Decal_sheet_is_a_valid_png()
    {
        var png = ShipGenerator.Generate(new ShipRecipe { Seed = 2 }).Materials[Roles.Livery].Png!;
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        var glb = GlbWriter.Write(ShipGenerator.Generate(new ShipRecipe { Seed = 2 }));
        var jsonLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLen));
        Assert.Equal("image/png", doc.RootElement.GetProperty("images")[0].GetProperty("mimeType").GetString());
        Assert.Contains(doc.RootElement.GetProperty("materials").EnumerateArray(), m => m.TryGetProperty("alphaMode", out var a) && a.GetString() == "MASK");
    }

    [Theory]
    [InlineData("damaged")]
    [InlineData("derelict")]
    public void Condition_keeps_the_ships_identity(string condition)
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var intact = ShipGenerator.Generate(new ShipRecipe { Seed = seed });
            var worn = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Condition = condition });
            Assert.False(ShipValidator.Validate(worn).HasErrors, $"{condition} seed {seed}: " + string.Join("; ", ShipValidator.Validate(worn).Issues.Select(i => i.Message)));
            // Same hull, same plan: the primary masses are untouched (detachable cargo may be gone on a derelict).
            string Primaries(Ship s, Ship reference) => string.Join("|", s.Modules
                .Where(m => m.Tier == ModuleTier.Primary && !m.Detachable && reference.Find(m.Id) is { Detachable: false })
                .Select(m => $"{m.Id}:{m.Bounds.Min}:{m.Bounds.Max}"));
            Assert.Equal(Primaries(intact, worn), Primaries(worn, intact));
            Assert.True(worn.Modules.Count(m => m.Tier == ModuleTier.Primary && !m.Detachable) == intact.Modules.Count(m => m.Tier == ModuleTier.Primary && !m.Detachable));
            Assert.Equal(intact.Plan.ShipName, worn.Plan.ShipName);
            Assert.Contains(worn.Modules, m => m.Type == "damage");
            if (condition == "derelict")
            {
                // Every light out.
                Assert.DoesNotContain(worn.Modules, m => m.Mesh.Groups.ContainsKey(Roles.Glass) || m.Mesh.Groups.ContainsKey(Roles.EngineGlow) || m.Mesh.Groups.ContainsKey(Roles.Light));
                Assert.All(worn.Modules.Where(m => m.Type == "debris"), m => Assert.True(m.Detachable));
            }
        }
        Assert.Throws<ArgumentException>(() => ShipGenerator.Generate(new ShipRecipe { Seed = 1, Condition = "exploded" }));
    }

    [Fact]
    public void Rng_streams_are_stable()
    {
        // Pinned values: if these change, every seed changes; bump ShipGenerator.Version.
        var r = new Rng(12345);
        Assert.Equal(r.NextU64(), new Rng(12345).NextU64());
        var a = Rng.Stream(1, "silhouette").NextU64();
        var b = Rng.Stream(1, "details").NextU64();
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("dart")]
    [InlineData("wedge")]
    [InlineData("brick")]
    [InlineData("fork")]
    [InlineData("hammer")]
    [InlineData("twinboom")]
    [InlineData("brawler")]
    [InlineData("gyre")]
    public void Every_fighter_form_is_valid_in_every_size_and_condition(string form)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var ship = ShipGenerator.Generate(new ShipRecipe
                    {
                        Seed = seed, Archetype = "fighter", Size = size, Condition = condition,
                        Overrides = new PlanOverrides { HullForm = form },
                    });
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{form} {size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal("fighter", ship.Plan.Family);
                    Assert.Equal(form, ship.Plan.HullForm);
                    Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                    Assert.Contains(ship.Markers, m => m.Kind == "hardpoint");
                }
    }

    [Fact]
    public void Fighters_are_fighter_sized_and_small_ones_are_not_shrunken_big_ones()
    {
        var small = Enumerable.Range(1, 20).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Archetype = "fighter", Size = "small" })).ToList();
        var large = Enumerable.Range(1, 20).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Archetype = "fighter", Size = "large" })).ToList();
        Assert.All(small, s => Assert.InRange(s.Plan.Length, 8, 15));
        Assert.All(large, s => Assert.InRange(s.Plan.Length, 26, 45));
        // Big fighters are mostly gunship forms; small ones never fork.
        Assert.DoesNotContain(small, s => s.Plan.HullForm is "fork" or "twinboom");
        Assert.True(large.Count(s => s.Plan.HullForm is "brick" or "fork" or "twinboom" or "hammer") > large.Count / 2);
    }

    [Fact]
    public void A_pinned_fighter_form_gets_its_own_proportions_and_any_nose()
    {
        // A wedge is wide whatever form the seed would have rolled.
        for (ulong seed = 1; seed <= 15; seed++)
        {
            var wedge = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "wedge" } });
            Assert.True(wedge.Plan.Beam >= wedge.Plan.Length * 0.5f);
        }
        foreach (var nose in Fighters.Noses)
        {
            var ship = ShipGenerator.Generate(new ShipRecipe { Seed = 3, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "dart", ProwStyle = nose } });
            Assert.Equal(nose, ship.Plan.ProwStyle);
            Assert.False(ShipValidator.Validate(ship).HasErrors);
        }
        // Blunt noses are wider at the tip than pointed ones.
        float Tip(string nose) => ShipGenerator.Generate(new ShipRecipe { Seed = 3, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "dart", ProwStyle = nose } })
            .Find("hull_main")!.Lofts[0].Profiles[^1].WBottom;
        Assert.True(Tip("blunt") > Tip("chisel") && Tip("chisel") > Tip("point"));
    }

    [Fact]
    public void Every_wing_style_and_span_validates_and_round_trips_through_a_code()
    {
        foreach (var style in Fighters.WingStyles.Where(w => w != "form"))
            foreach (var form in new[] { "dart", "brick", "hammer", "twinboom", "brawler" })
                foreach (var span in new[] { 0.6f, 1.75f })
                    for (ulong seed = 1; seed <= 3; seed++)
                    {
                        var recipe = new ShipRecipe { Seed = seed, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = form, WingStyle = style, WingSpan = span } };
                        var ship = ShipGenerator.Generate(recipe);
                        var report = ShipValidator.Validate(ship);
                        Assert.False(report.HasErrors, $"{style} {form} {span} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                        Assert.Equal(style, ship.Plan.WingStyle);
                        Assert.Equal(Signature(ship), Signature(ShipGenerator.Generate(ShipCode.Decode(ShipCode.Encode(recipe)))));
                    }
        // Longer wings reach further out; wing pods add engines.
        float Span(float k) => ShipGenerator.Generate(new ShipRecipe { Seed = 4, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "dart", WingStyle = "swept", WingSpan = k } }).Bounds.Size.X;
        Assert.True(Span(1.7f) > Span(0.7f) * 1.3f);
        var pods = ShipGenerator.Generate(new ShipRecipe { Seed = 4, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "dart", WingStyle = "pods" } });
        Assert.True(pods.Markers.Count(m => m.Kind == "engine_exhaust") >= 3);
        // The wedge and the fork keep their own wings.
        Assert.Equal("form", ShipGenerator.Generate(new ShipRecipe { Seed = 4, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "wedge", WingStyle = "x" } }).Plan.WingStyle);
    }

    [Theory]
    [InlineData("ram")]
    [InlineData("blade")]
    [InlineData("bastion")]
    public void Citadels_validate_in_every_size_and_condition(string prow)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "derelict" })
                foreach (var under in Citadels.Undersides)
                {
                    var keep = under == "keel" ? "mid" : "aft";
                    var ship = ShipGenerator.Generate(new ShipRecipe
                    {
                        Seed = (ulong)(size.Length + under.Length), Archetype = "citadel", Size = size, Condition = condition,
                        Overrides = new PlanOverrides { ProwStyle = prow, Ventral = under, BridgeStyle = keep },
                    });
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{prow} {size} {condition} {under}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal("citadel", ship.Plan.Family);
                    Assert.InRange(ship.Plan.Length, 260, 820);
                    Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                    Assert.Contains(ship.Markers, m => m.Kind == "broadside");
                    Assert.Contains(ship.Markers, m => m.Kind == "hangar");
                    Assert.Contains(ship.Modules, m => m.Type == "keep");
                    Assert.True(ship.Triangles < ShipValidator.TriangleBudget["high"]);
                }
        Assert.Equal("citadel", ShipGenerator.Generate(new ShipRecipe { Seed = 2, Family = "citadel" }).Plan.Archetype);
    }

    [Fact]
    public void Colour_schemes_and_kit_parts_work_on_every_ship()
    {
        foreach (var palette in Palettes.Names)
        {
            var ship = ShipGenerator.Generate(new ShipRecipe { Seed = 21, Archetype = "freighter", Palette = palette });
            Assert.False(ShipValidator.Validate(ship).HasErrors, palette);
            Assert.Equal(palette, ship.Plan.Palette);
        }
        // Schemes with their own accent keep it, whatever the operator's accent.
        var mil = ShipGenerator.Generate(new ShipRecipe { Seed = 3, Faction = "haulers", Palette = "military" });
        Assert.Equal(Palettes.Linear("#a8221c"), mil.Materials[Roles.Accent].Color);
        // Banded schemes paint accent bands; pirates bolt on mismatched plates.
        var ind = ShipGenerator.Generate(new ShipRecipe { Seed = 3, Archetype = "freighter", Palette = "industrial" });
        Assert.Contains(ind.Modules, m => m.Type == "details" && m.Mesh.Groups.ContainsKey(Roles.Accent));
        var pirate = ShipGenerator.Generate(new ShipRecipe { Seed = 3, Archetype = "freighter", Palette = "pirate" });
        Assert.Contains(pirate.Modules, m => m.Type == "details" && Roles.Cargo.Any(cr => m.Mesh.Groups.ContainsKey(cr)));
        // Kit parts show up across a fleet, and every ship with them still validates.
        var ships = Enumerable.Range(1, 40).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Archetype = i % 2 == 0 ? "research" : "freighter" })).ToList();
        Assert.All(ships, s => Assert.False(ShipValidator.Validate(s).HasErrors));
        foreach (var kit in new[] { "point_defence", "radiators", "winglets" })
            Assert.Contains(ships, s => s.Modules.Any(m => m.Id.StartsWith(kit)));
    }

    [Theory]
    [InlineData("drill", "drill")]
    [InlineData("refinery", "scoop")]
    [InlineData("crane", "crane")]
    [InlineData("salvage", "claw")]
    public void Industrial_ships_carry_their_tool_and_validate(string job, string toolType)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var recipe = new ShipRecipe { Seed = seed, Archetype = "industrial", Size = size, Condition = condition, Overrides = new PlanOverrides { Payload = job } };
                    var ship = ShipGenerator.Generate(recipe);
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{job} {size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal("industrial", ship.Plan.Family);
                    Assert.Equal(job, ship.Plan.Payload);
                    if (condition == "intact") Assert.Contains(ship.Modules, m => m.Type == toolType);
                    Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                    Assert.Equal(Signature(ship), Signature(ShipGenerator.Generate(ShipCode.Decode(ShipCode.Encode(recipe)))));
                }
        // Most wear the industrial scheme unless a palette is asked for.
        var auto = Enumerable.Range(1, 20).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Archetype = "industrial" })).ToList();
        Assert.True(auto.Count(s => s.Plan.Palette == "industrial") >= 10);
        Assert.Equal("slate", ShipGenerator.Generate(new ShipRecipe { Seed = 1, Archetype = "industrial", Palette = "slate" }).Plan.Palette);
    }

    [Theory]
    [InlineData("runabout", "pod")]
    [InlineData("mule", "block")]
    [InlineData("shuttle", "block")]
    [InlineData("pioneer", "pod")]
    public void Utility_craft_validate_and_keep_their_job_kit(string job, string engines)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var recipe = new ShipRecipe { Seed = seed, Archetype = "utility", Size = size, Condition = condition, Overrides = new PlanOverrides { Payload = job } };
                    var ship = ShipGenerator.Generate(recipe);
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{job} {size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal("utility", ship.Plan.Family);
                    Assert.Equal(engines, ship.Plan.EngineStyle);
                    Assert.InRange(ship.Plan.Length, 14, 36);
                    Assert.Equal(4, ship.Markers.Count(m => m.Kind == "landing_pad"));
                    Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                    if (job == "shuttle") Assert.Contains(ship.Markers, m => m.Kind == "docking_port");
                }
    }

    [Theory]
    [InlineData("science", 1)]
    [InlineData("expedition", 2)]
    [InlineData("habitat", 1)]
    public void Ring_ships_carry_their_rings_clear_of_everything(string job, int ringCount)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var recipe = new ShipRecipe { Seed = seed, Archetype = "ring", Size = size, Condition = condition, Overrides = new PlanOverrides { Payload = job } };
                    var ship = ShipGenerator.Generate(recipe);
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{job} {size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal(ringCount, ship.Modules.Count(m => m.Type == "ring"));
                    Assert.Equal(ringCount, ship.Markers.Count(m => m.Kind == "ring_hub"));
                    Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                    // The rings stand clear of the hull: wider than the hull in both directions.
                    foreach (var ring in ship.Modules.Where(m => m.Type == "ring"))
                        Assert.True(ring.Bounds.Size.X > ship.Plan.Beam * 1.5f);
                }
        foreach (var size in Planner.Sizes)
        {
            var gyre = ShipGenerator.Generate(new ShipRecipe { Seed = 5, Archetype = "fighter", Size = size, Overrides = new PlanOverrides { HullForm = "gyre" } });
            Assert.False(ShipValidator.Validate(gyre).HasErrors);
            Assert.Equal(2, gyre.Markers.Count(m => m.Kind == "engine_exhaust"));
        }
    }

    [Theory]
    [InlineData("liner", "lifeboat")]
    [InlineData("medical", "ward_block")]
    [InlineData("explorer", "boom")]
    public void Civilian_ships_validate_and_carry_their_job(string job, string part)
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = "civilian", Size = size, Condition = condition, Overrides = new PlanOverrides { Payload = job } });
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{job} {size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Equal(job, ship.Plan.Payload);
                    if (condition == "intact") Assert.Contains(ship.Modules, m => m.Type == part || m.Id.StartsWith(part));
                }
        Assert.Equal("medical", ShipGenerator.Generate(new ShipRecipe { Seed = 2, Archetype = "civilian", Overrides = new PlanOverrides { Payload = "medical" } }).Plan.Palette);
    }

    [Fact]
    public void Raiders_are_kitbashed_and_validate()
    {
        foreach (var size in Planner.Sizes)
            foreach (var condition in new[] { "intact", "damaged", "derelict" })
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = "raider", Size = size, Condition = condition });
                    var report = ShipValidator.Validate(ship);
                    Assert.False(report.HasErrors, $"{size} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                    Assert.Contains(ship.Plan.Family, new[] { "barge", "block", "twin" });
                    if (condition == "intact") Assert.Contains(ship.Modules, m => m.Type == "spike");
                }
        var fleet = Enumerable.Range(1, 20).Select(i => ShipGenerator.Generate(new ShipRecipe { Seed = (ulong)i, Faction = "raiders" })).ToList();
        Assert.True(fleet.Count(s => s.Plan.Archetype == "raider") >= 8);
        Assert.All(fleet.Where(s => s.Plan.Archetype == "raider"), s => Assert.StartsWith("RDR-", s.Plan.Registration));
        Assert.True(fleet.Count(s => s.Plan.Palette == "pirate") >= 5);
    }

    [Theory]
    [InlineData("corvette", 60, 90)]
    [InlineData("frigate", 90, 150)]
    [InlineData("destroyer", 150, 220)]
    [InlineData("cruiser", 220, 320)]
    [InlineData("carrier", 300, 450)]
    public void The_warship_ladder_validates_and_scales(string cls, float min, float max)
    {
        foreach (var condition in new[] { "intact", "damaged", "derelict" })
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = "warship", Condition = condition, Overrides = new PlanOverrides { Payload = cls } });
                var report = ShipValidator.Validate(ship);
                Assert.False(report.HasErrors, $"{cls} {condition} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                Assert.InRange(ship.Plan.Length, min, max);
                Assert.Contains(ship.Markers, m => m.Kind == "bridge");
                if (condition != "intact") continue;
                if (cls == "carrier")
                {
                    Assert.Contains(ship.Modules, m => m.Type == "flight_deck");
                    Assert.Contains(ship.Markers, m => m.Kind == "launch_lane");
                    Assert.Contains(ship.Markers, m => m.Kind == "hangar");
                }
                else Assert.Contains(ship.Modules, m => m.Id.StartsWith("turret"));
            }
    }

    [Fact]
    public void Interiors_fit_the_hull_and_every_room_is_reachable()
    {
        var built = 0;
        foreach (var archetype in ShipGenerator.Archetypes)
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = archetype });
                var interior = BadShip.Interior.InteriorGenerator.Build(ship);
                if (archetype == "fighter") { Assert.Null(interior); continue; }
                if (interior is null) continue;
                built++;
                var report = BadShip.Interior.InteriorValidator.Validate(interior);
                Assert.False(report.HasErrors, $"{archetype} {seed}: {string.Join("; ", report.Issues.Select(i => i.Message))}");
                // Doors are nodes of their own with what an engine needs to make them work.
                var door = interior.Model.Modules.First(m => m.Type == "door");
                Assert.NotNull(door.Extras);
                Assert.True(door.Extras!.ContainsKey("rooms") && door.Extras.ContainsKey("slide_axis"));
                Assert.Contains(interior.Model.Markers, m => m.Kind == "spawn");
                // Same ship, same interior.
                var again = BadShip.Interior.InteriorGenerator.Build(ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = archetype }))!;
                Assert.Equal(interior.Rooms.Select(r => r.Id + string.Join(",", r.Min)), again.Rooms.Select(r => r.Id + string.Join(",", r.Min)));
            }
        Assert.True(built > 30);
        // A big ship gets several decks with stairs, a bridge, engineering and an airlock.
        var big = BadShip.Interior.InteriorGenerator.Build(ShipGenerator.Generate(new ShipRecipe { Seed = 7, Archetype = "freighter", Size = "medium" }))!;
        Assert.True(big.Decks.Count >= 2);
        Assert.Equal(big.Decks.Count(d => d.Part == "hull") - 1, big.Stairs.Count(s => s.ToRoom is null));
        foreach (var kind in new[] { "bridge", "engineering", "airlock", "stairwell", "corridor" })
            Assert.Contains(big.Rooms, r => r.Kind == kind);
        Assert.Contains(big.Doors, d => d.B == "exterior");
    }

    [Fact]
    public void Superstructure_and_keel_decks_join_the_hull_by_ladder()
    {
        // BadSad's freighter: a deck in the keel, the hull's own deck and one in the bridge block on top.
        var ship = ShipGenerator.Generate(ShipCode.Decode("v0p21p0-s387544-1y-hfaceted"));
        var interior = ship.Interior!;
        Assert.False(BadShip.Interior.InteriorValidator.Validate(interior).HasErrors);
        Assert.Equal(["keel", "hull", "superstructure"], interior.Decks.Select(d => d.Part).ToArray());
        // Decks are numbered from the bottom up.
        Assert.True(interior.Decks.Zip(interior.Decks.Skip(1)).All(p => p.First.CeilingY < p.Second.FloorY));
        var ladders = interior.Lifts.Where(l => l.Kind == "ladder").ToList();
        Assert.Equal(2, ladders.Count);
        foreach (var l in ladders)
        {
            Assert.Equal(2, l.Decks.Length);
            Assert.Equal(l.Decks[0] + 1, l.Decks[1]);
            // Each end opens into a room on its own deck, and the ladder is a node with its access points.
            for (var i = 0; i < 2; i++) Assert.Equal(l.Decks[i], interior.Rooms.Single(r => r.Id == l.Rooms[i]).Deck);
            var node = interior.Model.Find(l.Id)!;
            Assert.Equal("ladder", node.Type);
            Assert.True(node.Extras!.ContainsKey("top") && node.Extras.ContainsKey("bottom"));
        }
    }

    [Fact]
    public void Bridge_block_decks_follow_the_block_shape()
    {
        // Seed 1's bridge block sits off the centreline, is low, and has chamfered top edges and a sloped front.
        var ship = ShipGenerator.Generate(ShipCode.Decode("v0p24p0-s1-1y"));
        var interior = ship.Interior!;
        Assert.False(BadShip.Interior.InteriorValidator.Validate(interior).HasErrors);
        var deck = interior.Decks.Single(d => d.Part == "superstructure");
        Assert.True(deck.Shaped);
        Assert.True(MathF.Abs(deck.Cx) > 0.3f);
        Assert.Contains(interior.Rooms, r => r.Deck == deck.Index && r.Kind == "bridge");
        // The skin is there, and walls under the chamfers have slanted tops: some wall corners sit between the
        // floor and the flat ceiling, off any box.
        var skin = interior.Model.Find($"skin_{deck.Index}")!;
        Assert.True(skin.Mesh.TriangleCount > 20);
        var walls = interior.Model.Find($"walls_{deck.Index}")!;
        Assert.Contains(walls.Mesh.Positions, p => p.Y > deck.FloorY + BadShip.Interior.InteriorGenerator.DoorH + 0.1f && p.Y < deck.CeilingY - 0.1f);
    }

    [Fact]
    public void Gondolas_get_a_deck_and_a_ladder_up_an_access_trunk()
    {
        var decks = 0;
        foreach (var archetype in new[] { "research", "civilian", "freighter", "ring" })
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var recipe = new ShipRecipe { Seed = seed, Archetype = archetype, Interior = true, Overrides = new PlanOverrides { Ventral = "gondola" } };
                var ship = ShipGenerator.Generate(recipe);
                var interior = ship.Interior;
                if (interior is null || ship.Find("gondola") is null) continue;
                Assert.False(BadShip.Interior.InteriorValidator.Validate(interior).HasErrors, $"{archetype} {seed}");
                var gondola = interior.Decks.FirstOrDefault(d => d.Part == "gondola");
                if (gondola is null) continue;
                decks++;
                // The gondola deck is the lowest, joined to the lowest hull deck by a ladder...
                Assert.Equal(0, gondola.Index);
                var ladder = interior.Lifts.Single(l => l.Kind == "ladder" && l.Decks[0] == gondola.Index);
                Assert.Equal("hull", interior.Decks[ladder.Decks[1]].Part);
                // ...that climbs inside a trunk on the outside, from the gondola up into the hull.
                var trunk = ship.Find("access_trunk")!;
                Assert.True(trunk.Bounds.Min.X <= ladder.Shaft[0] && trunk.Bounds.Max.X >= ladder.Shaft[1]);
                Assert.True(trunk.Bounds.Min.Y <= gondola.CeilingY + 0.01f && trunk.Bounds.Max.Y >= interior.Decks[ladder.Decks[1]].FloorY - 0.5f);
            }
        Assert.True(decks >= 8, $"gondola decks {decks}");
        // Without an interior, gondolas are as before and get no trunk.
        var plain = ShipGenerator.Generate(new ShipRecipe { Seed = 1, Archetype = "research", Overrides = new PlanOverrides { Ventral = "gondola" } });
        Assert.Null(plain.Find("access_trunk"));
    }

    [Fact]
    public void Elevators_stop_at_every_hull_deck_behind_their_own_doors()
    {
        var elevators = 0;
        var annexes = 0;
        foreach (var archetype in new[] { "warship", "civilian", "industrial", "citadel", "research" })
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = archetype, Size = "large", Interior = true });
                var interior = ship.Interior;
                if (interior is null) continue;
                Assert.False(BadShip.Interior.InteriorValidator.Validate(interior).HasErrors, $"{archetype} {seed}");
                annexes += interior.Decks.Count(d => d.Part != "hull");
                foreach (var l in interior.Lifts.Where(l => l.Kind == "elevator"))
                {
                    elevators++;
                    var hull = interior.Decks.Where(d => d.Part == "hull").Select(d => d.Index).ToArray();
                    Assert.Equal(hull, l.Decks);
                    Assert.Equal(l.Decks.Length, l.Doors.Length);
                    Assert.All(l.Doors, id => Assert.Equal("elevator", interior.Doors.Single(d => d.Id == id).Kind));
                    var car = interior.Model.Find("elevator")!;
                    Assert.Equal(l.Decks.Length, ((float[])car.Extras!["stops_y"]).Length);
                }
            }
        Assert.True(elevators >= 5, $"elevators {elevators}");
        Assert.True(annexes >= 5, $"annex decks {annexes}");
    }

    [Fact]
    public void Big_interiors_get_halls_galleries_rings_and_suites()
    {
        int halls = 0, rings = 0, suites = 0;
        foreach (var archetype in new[] { "civilian", "warship", "industrial", "research", "citadel" })
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var ship = ShipGenerator.Generate(new ShipRecipe { Seed = seed, Archetype = archetype, Size = "large" });
                var interior = BadShip.Interior.InteriorGenerator.Build(ship);
                if (interior is null) continue;
                Assert.False(BadShip.Interior.InteriorValidator.Validate(interior).HasErrors, $"{archetype} {seed}");
                foreach (var gallery in interior.Rooms.Where(r => r.Kind == "gallery"))
                {
                    halls++;
                    // The hall below is two decks tall, under the gallery, and a grand staircase joins them.
                    var hall = interior.Rooms.Single(r => r.Deck == gallery.Deck - 1 && r.Min[2] == gallery.Min[2] && r.Max[2] == gallery.Max[2]);
                    Assert.Contains(hall.Kind, new[] { "atrium", "hangar", "cargo_bay", "hall" });
                    Assert.Equal(interior.Decks[gallery.Deck].CeilingY, hall.Max[1], 2);
                    Assert.Contains(interior.Stairs, s => s.Room == hall.Id && s.ToRoom == gallery.Id);
                    Assert.Contains(interior.Model.Modules, m => m.Type == "railing" && m.Id.StartsWith("gallery_rail"));
                }
                rings += interior.Rooms.Count(r => r.Id.StartsWith("ring_") && r.Id.EndsWith("_aft"));
                // A suite's back room opens only into its front room.
                foreach (var back in interior.Rooms.Where(r => r.Kind is "ward" or "clean_room" or "bunk_room"))
                {
                    suites++;
                    var doors = interior.Doors.Where(d => d.A == back.Id || d.B == back.Id).ToList();
                    Assert.Single(doors);
                }
                // Doors are wide enough for game characters to path through.
                Assert.All(interior.Doors, d => Assert.True(d.Width >= 1.4f));
            }
        Assert.True(halls >= 4, $"halls {halls}");
        Assert.True(rings >= 4, $"rings {rings}");
        Assert.True(suites >= 10, $"suites {suites}");
    }

    [Fact]
    public void Windows_are_cut_through_the_hull_and_match_the_interior()
    {
        var withWindows = 0;
        foreach (var archetype in new[] { "freighter", "research", "civilian", "warship", "ring", "industrial", "raider" })
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var recipe = new ShipRecipe { Seed = seed, Archetype = archetype, Interior = true };
                var ship = ShipGenerator.Generate(recipe);
                Assert.False(ShipValidator.Validate(ship).HasErrors, $"{archetype} {seed}");
                if (ship.Interior is null) continue;
                Assert.False(BadShip.Interior.InteriorValidator.Validate(ship.Interior).HasErrors, $"{archetype} {seed} interior");
                if (ship.Interior.Windows.Count == 0) continue;
                withWindows++;
                // One glass pane and one marker per window, in a see-through, double-sided material.
                Assert.Equal(ship.Interior.Windows.Count, ship.Markers.Count(m => m.Kind == "window"));
                var glass = ship.Materials[HullWindows.Glass];
                Assert.True(glass.Alpha < 1 && glass.DoubleSided);
                // Windows sit in rooms people use, on the hull walls.
                Assert.All(ship.Interior.Windows, w => Assert.DoesNotContain(ship.Interior.Rooms.First(r => r.Id == w.Room).Kind, new[] { "storage", "reactor", "cargo_hold", "stairwell" }));
                // The ship code carries the interior option and gives back the same ship.
                Assert.Equal(Signature(ship), Signature(ShipGenerator.Generate(ShipCode.Decode(ShipCode.Encode(recipe)))));
            }
        Assert.True(withWindows >= 10);
        // Without the option, the hull is the usual one.
        Assert.Null(ShipGenerator.Generate(new ShipRecipe { Seed = 1, Archetype = "freighter" }).Interior);
    }

    [Fact]
    public void Fighter_family_implies_fighter_and_codes_round_trip()
    {
        var ship = ShipGenerator.Generate(new ShipRecipe { Seed = 7, Family = "fighter" });
        Assert.Equal("fighter", ship.Plan.Archetype);
        var recipe = new ShipRecipe { Seed = 9, Archetype = "fighter", Overrides = new PlanOverrides { HullForm = "wedge", EngineLayout = "3x1" } };
        var again = ShipCode.Decode(ShipCode.Encode(recipe));
        Assert.Equal(Signature(ShipGenerator.Generate(recipe)), Signature(ShipGenerator.Generate(again)));
        Assert.Throws<ArgumentException>(() => ShipGenerator.Generate(new ShipRecipe { Seed = 1, Archetype = "fighter", Family = "barge" }));
    }
}

public class GlbTests
{
    [Fact]
    public void Glb_is_well_formed_and_accessors_fit_the_buffer()
    {
        var ship = ShipGenerator.Generate(new ShipRecipe { Seed = 9 });
        var glb = GlbWriter.Write(ship);
        Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(glb));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        var jsonLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0, jsonLen % 4);
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLen));
        var root = doc.RootElement;
        var binLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(20 + jsonLen));
        Assert.Equal(root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32(), binLen);

        var views = root.GetProperty("bufferViews").EnumerateArray().ToList();
        foreach (var v in views)
            Assert.True(v.GetProperty("byteOffset").GetInt32() + v.GetProperty("byteLength").GetInt32() <= binLen);
        var sizes = new Dictionary<string, int> { ["SCALAR"] = 1, ["VEC2"] = 2, ["VEC3"] = 3, ["VEC4"] = 4 };
        foreach (var a in root.GetProperty("accessors").EnumerateArray())
        {
            var view = views[a.GetProperty("bufferView").GetInt32()];
            var comp = a.GetProperty("componentType").GetInt32() == 5123 ? 2 : 4;
            Assert.True(a.GetProperty("count").GetInt32() * sizes[a.GetProperty("type").GetString()!] * comp <= view.GetProperty("byteLength").GetInt32());
        }
        // Every module is a named node, and markers are exported.
        var names = root.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()).ToHashSet();
        Assert.All(ship.Modules, m => Assert.Contains(m.Id, names));
        Assert.Contains(names, n => n!.StartsWith("marker.engine_exhaust"));
    }

    [Fact]
    public void Marker_rotation_points_z_along_direction()
    {
        foreach (var d in new[] { Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, Vector3.Normalize(new Vector3(1, 2, 3)) })
        {
            var q = GlbWriter.RotationFromZ(d);
            var z = Vector3.Transform(Vector3.UnitZ, q);
            Assert.True(Vector3.Distance(z, d) < 1e-4f, $"{d} -> {z}");
        }
    }
}

internal static class Ext
{
    public static T Also<T>(this T x, Action<T> a)
    {
        a(x);
        return x;
    }
}
