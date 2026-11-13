using BadShip.Validation;

namespace BadShip.Interior;

/// <summary>
/// Checks an interior: every room reachable from the airlock (through doors and stairs), every room inside the hull
/// at its floor and ceiling, every door joining rooms that exist, clear height for walking, and no rooms overlapping
/// on a deck (the stairwell inside a cargo hold excepted).
/// </summary>
public static class InteriorValidator
{
    public static ValidationReport Validate(ShipInterior interior)
    {
        var rep = new ValidationReport();
        var rooms = interior.Rooms.ToDictionary(r => r.Id);
        foreach (var d in interior.Doors)
            foreach (var id in new[] { d.A, d.B })
                if (id != "exterior" && !rooms.ContainsKey(id))
                    rep.Add("error", "interior.door_room", $"Door {d.Id} names a room '{id}' that does not exist.", [d.Id]);

        // Reachability from the airlock (or the first room of a small craft) through doors and stairs.
        var links = interior.Rooms.ToDictionary(r => r.Id, _ => new HashSet<string>());
        foreach (var d in interior.Doors.Where(d => rooms.ContainsKey(d.A) && rooms.ContainsKey(d.B)))
        {
            links[d.A].Add(d.B);
            links[d.B].Add(d.A);
        }
        foreach (var s in interior.Stairs)
        {
            var up = s.ToRoom is { } to ? interior.Rooms.FirstOrDefault(r => r.Id == to) : interior.Rooms.FirstOrDefault(r => r.Kind == "stairwell" && r.Deck == s.ToDeck);
            if (up is null) { rep.Add("error", "interior.stairs", $"Stairs {s.Id} lead to a deck without a stairwell.", [s.Id]); continue; }
            links[s.Room].Add(up.Id);
            links[up.Id].Add(s.Room);
        }
        var start = interior.Doors.FirstOrDefault(d => d.B == "exterior")?.A ?? interior.Rooms.First().Id;
        var seen = new HashSet<string> { start };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
            foreach (var next in links[queue.Dequeue()])
                if (seen.Add(next)) queue.Enqueue(next);
        foreach (var r in interior.Rooms.Where(r => !seen.Contains(r.Id)))
            rep.Add("error", "interior.unreachable", $"Room {r.Id} ({r.Kind}) cannot be reached from the airlock.", [r.Id]);
        if (!interior.Doors.Any(d => d.B == "exterior"))
            rep.Add("warn", "interior.no_airlock", "The interior has no way out to the exterior.", []);

        // Inside the hull, with clear height.
        var P = interior.Hull;
        foreach (var r in interior.Rooms)
        {
            var halfX = Math.Max(Math.Abs(r.Min[0]), Math.Abs(r.Max[0]));
            if (r.Max[1] - r.Min[1] < InteriorGenerator.DoorH + 0.3f)
                rep.Add("error", "interior.height", $"Room {r.Id} is too low to walk in.", [r.Id]);
            if (r.Min[1] < P.Bottom || r.Max[1] > P.Top || halfX > P.HalfWidthAt(r.Min[1]) || halfX > P.HalfWidthAt(r.Max[1]))
                rep.Add("error", "interior.outside_hull", $"Room {r.Id} pokes out of the hull.", [r.Id]);
        }
        // No overlapping rooms on a deck.
        foreach (var g in interior.Rooms.GroupBy(r => r.Deck))
        {
            var list = g.ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var (a, b) = (list[i], list[j]);
                    if (a.Kind == "cargo_hold" && b.Kind == "stairwell" || b.Kind == "cargo_hold" && a.Kind == "stairwell") continue;
                    var ox = Math.Min(a.Max[0], b.Max[0]) - Math.Max(a.Min[0], b.Min[0]);
                    var oz = Math.Min(a.Max[2], b.Max[2]) - Math.Max(a.Min[2], b.Min[2]);
                    if (ox > 0.05f && oz > 0.05f) rep.Add("error", "interior.overlap", $"Rooms {a.Id} and {b.Id} overlap.", [a.Id, b.Id]);
                }
        }
        rep.Metrics["decks"] = interior.Decks.Count;
        rep.Metrics["rooms"] = interior.Rooms.Count;
        rep.Metrics["doors"] = interior.Doors.Count;
        rep.Metrics["rooms_by_kind"] = interior.Rooms.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        return rep;
    }
}
