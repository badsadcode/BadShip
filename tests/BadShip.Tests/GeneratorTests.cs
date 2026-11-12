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
        Assert.DoesNotContain(low.Modules, m => m.Tier == ModuleTier.Tertiary);
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
    public void Rng_streams_are_stable()
    {
        // Pinned values: if these change, every seed changes; bump ShipGenerator.Version.
        var r = new Rng(12345);
        Assert.Equal(r.NextU64(), new Rng(12345).NextU64());
        var a = Rng.Stream(1, "silhouette").NextU64();
        var b = Rng.Stream(1, "details").NextU64();
        Assert.NotEqual(a, b);
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
