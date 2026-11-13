using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Validation;

public sealed record ValidationIssue(string Severity, string Code, string Message, string[] Modules, string? Fix = null);

public sealed class ValidationReport
{
    /// <summary>"pass", "warn" or "error": the worst issue found.</summary>
    public string Status { get; set; } = "pass";
    public List<ValidationIssue> Issues { get; } = [];
    public Dictionary<string, object> Metrics { get; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == "error");

    public void Add(string severity, string code, string message, string[] modules, string? fix = null)
    {
        Issues.Add(new ValidationIssue(severity, code, message, modules, fix));
        if (severity == "error") Status = "error";
        else if (severity == "warn" && Status == "pass") Status = "warn";
    }
}

/// <summary>
/// Checks a finished ship against the functional rules: valid meshes, every module attached by a chain to the root,
/// engine exhaust and docking corridors clear, a bridge and engines present, budgets respected.
/// Collision tests use bounding boxes (conservative and fast); see the docs for that limitation.
/// </summary>
public static class ShipValidator
{
    public static readonly Dictionary<string, int> TriangleBudget = new() { ["low"] = 20_000, ["medium"] = 60_000, ["high"] = 150_000 };

    public static ValidationReport Validate(Ship ship)
    {
        var rep = new ValidationReport();
        var byId = new Dictionary<string, Module>();
        foreach (var m in ship.Modules)
            if (!byId.TryAdd(m.Id, m)) rep.Add("error", "module.duplicate_id", $"Two modules share the id '{m.Id}'.", [m.Id]);

        CheckMeshes(ship, rep);
        CheckAttachment(ship, byId, rep);
        CheckExhaust(ship, byId, rep);
        CheckDocking(ship, rep);

        if (!ship.Markers.Any(m => m.Kind == "engine_exhaust"))
            rep.Add("error", "propulsion.missing", "The ship has no main engines.", []);
        if (!ship.Markers.Any(m => m.Kind == "bridge"))
            rep.Add("warn", "bridge.missing", "No bridge was placed.", [], "Reroll or add superstructure tiers.");

        var tris = ship.Triangles;
        var budget = TriangleBudget.GetValueOrDefault(ship.Recipe.Detail, 150_000);
        if (tris > budget) rep.Add("warn", "budget.triangles", $"{tris} triangles is over the {budget} budget for '{ship.Recipe.Detail}' detail.", []);
        var size = ship.Bounds.Size;
        if (size.Z > ship.Plan.Length * 1.25f)
            rep.Add("warn", "bounds.length", $"The ship is {size.Z:0.#} m long, well over the planned {ship.Plan.Length:0.#} m.", []);

        rep.Metrics["triangles"] = tris;
        rep.Metrics["modules"] = ship.Modules.Count;
        rep.Metrics["modules_by_tier"] = ship.Modules.GroupBy(m => m.Tier.ToString().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
        rep.Metrics["markers_by_kind"] = ship.Markers.GroupBy(m => m.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        rep.Metrics["size_m"] = new[] { Round(size.X), Round(size.Y), Round(size.Z) };
        return rep;
    }

    private static float Round(float v) => MathF.Round(v, 2);

    private static void CheckMeshes(Ship ship, ValidationReport rep)
    {
        foreach (var m in ship.Modules)
        {
            var mesh = m.Mesh;
            if (mesh.IsEmpty)
            {
                if (m.Tier != ModuleTier.Tertiary) rep.Add("warn", "mesh.empty", $"Module '{m.Id}' has no geometry.", [m.Id]);
                continue;
            }
            if (mesh.Positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
                rep.Add("error", "mesh.non_finite", $"Module '{m.Id}' has non-finite vertex positions.", [m.Id]);
            if (mesh.Normals.Any(n => MathF.Abs(n.Length() - 1) > 1e-3f))
                rep.Add("error", "mesh.bad_normal", $"Module '{m.Id}' has normals that are not unit length.", [m.Id]);
            var count = mesh.Positions.Count;
            if (mesh.Groups.Values.Any(g => g.Count % 3 != 0 || g.Any(i => i < 0 || i >= count)))
                rep.Add("error", "mesh.bad_index", $"Module '{m.Id}' has out-of-range or incomplete triangles.", [m.Id]);
        }
    }

    private static void CheckAttachment(Ship ship, Dictionary<string, Module> byId, ValidationReport rep)
    {
        var roots = ship.Modules.Where(m => m.ParentId is null).ToList();
        if (roots.Count != 1)
            rep.Add("error", "attach.root", $"Expected one root module, found {roots.Count}.", roots.Select(m => m.Id).ToArray());
        foreach (var m in ship.Modules)
        {
            if (m.ParentId is null) continue;
            if (!byId.TryGetValue(m.ParentId, out var parent))
            {
                rep.Add("error", "attach.missing_parent", $"'{m.Id}' names a parent '{m.ParentId}' that does not exist.", [m.Id]);
                continue;
            }
            // Walk up: the chain must end at the root without loops.
            var seen = new HashSet<string> { m.Id };
            var cur = parent;
            while (cur.ParentId is not null && byId.TryGetValue(cur.ParentId, out var next))
            {
                if (!seen.Add(cur.Id)) { rep.Add("error", "attach.cycle", $"'{m.Id}' is in a parent loop.", [m.Id]); break; }
                cur = next;
            }
            if (m.Tier == ModuleTier.Tertiary || m.Mesh.IsEmpty || parent.Mesh.IsEmpty) continue;
            if (!m.Bounds.Touches(parent.Bounds, 0.1f))
                rep.Add("error", "attach.floating", $"'{m.Id}' does not touch its parent '{parent.Id}'.", [m.Id, parent.Id], "Move it onto its parent or reroll.");
        }
    }

    private static void CheckExhaust(Ship ship, Dictionary<string, Module> byId, ValidationReport rep)
    {
        foreach (var e in ship.Markers.Where(m => m.Kind == "engine_exhaust"))
        {
            var cone = ExhaustVolume(e);
            foreach (var m in ship.Modules)
            {
                if (m.Tier == ModuleTier.Tertiary || m.Id == e.ModuleId) continue;
                if (m.Bounds.Penetration(cone) > 0.05f)
                    rep.Add("error", "exhaust.blocked", $"The exhaust of '{e.ModuleId}' passes through '{m.Id}'.", [e.ModuleId, m.Id], "Move the obstacle or shorten it.");
            }
        }
    }

    /// <summary>The box around an engine's exhaust cone (10° spread over its clearance length), starting just behind the nozzle.</summary>
    public static Aabb ExhaustVolume(Marker e)
    {
        var start = e.Position + e.Direction * 0.3f;
        var end = e.Position + e.Direction * e.Clearance;
        var rEnd = e.Radius + e.Clearance * MathF.Tan(10f * MathF.PI / 180f);
        var cone = Aabb.FromPoints(end - new Vector3(rEnd), end + new Vector3(rEnd))
            .Union(Aabb.FromPoints(start - new Vector3(e.Radius), start + new Vector3(e.Radius)));
        return TrimBehind(cone, start, e.Direction);
    }

    /// <summary>The approach corridor in front of a docking port.</summary>
    public static Aabb DockingVolume(Marker d)
    {
        var r = d.Radius * 1.4f;
        var a = d.Position + d.Direction * 0.2f;
        var b = d.Position + d.Direction * d.Clearance;
        var corridor = Aabb.FromPoints(a - new Vector3(r), a + new Vector3(r)).Union(Aabb.FromPoints(b - new Vector3(r), b + new Vector3(r)));
        return TrimBehind(corridor, a, d.Direction);
    }

    private static Aabb TrimBehind(Aabb box, Vector3 start, Vector3 dir)
    {
        var min = box.Min;
        var max = box.Max;
        if (dir.Z < -0.9f) max.Z = MathF.Min(max.Z, start.Z);
        if (dir.Z > 0.9f) min.Z = MathF.Max(min.Z, start.Z);
        if (dir.X < -0.9f) max.X = MathF.Min(max.X, start.X);
        if (dir.X > 0.9f) min.X = MathF.Max(min.X, start.X);
        if (dir.Y < -0.9f) max.Y = MathF.Min(max.Y, start.Y);
        if (dir.Y > 0.9f) min.Y = MathF.Max(min.Y, start.Y);
        return new Aabb(min, max);
    }

    private static void CheckDocking(Ship ship, ValidationReport rep)
    {
        foreach (var d in ship.Markers.Where(m => m.Kind == "docking_port"))
        {
            // The surface the collar stands on is skipped: its bounding box is wider than the (often sloped) face.
            var host = ship.Find(d.ModuleId)?.ParentId;
            var corridor = DockingVolume(d);
            foreach (var m in ship.Modules)
            {
                if (m.Tier == ModuleTier.Tertiary || m.Id == d.ModuleId || m.Id == host) continue;
                if (m.Bounds.Penetration(corridor) > 0.05f)
                    rep.Add("error", "docking.blocked", $"The approach to '{d.ModuleId}' is blocked by '{m.Id}'.", [d.ModuleId, m.Id]);
            }
        }
    }
}
