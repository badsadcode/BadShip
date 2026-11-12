using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The macro hull families: each lays out the primary masses (2–5 big readable volumes), the engines and the
/// superstructure, and registers the decks and side faces that equipment and details use later.
/// Coordinates: metres, +Y up, +Z forward (the bow), +X to port; the main hull is centred on the origin.
/// </summary>
public static class HullFamilies
{
    // ------------------------------------------------------------------ barge: one broad stepped hull (Thalassa-like)

    public static void Barge(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.07f, 0.1f);
        var zRear = -L / 2 + engLen;
        var zFront = L / 2;
        var prowLen = Parts.ProwLength(p.ProwStyle, L, r, p.HullForm == "faceted");
        var body = Parts.HullBody(c, 0, 0, B, H);
        var zBody0 = zRear + L * 0.025f;
        var zProw = zFront - prowLen;
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.93f, 0.9f)), (zBody0, body), (zProw, body)];
        rings.AddRange(Parts.Prow(body, zProw, zFront, p.ProwStyle));
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);

        var deck = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, zBody0 + 1, zProw - 0.5f, 0);
        if (p.ProwStyle == "stepped")
        {
            var (y, half, z0, z1) = Parts.StepDeck(body, zProw, zFront);
            c.AddDeck(hull, y, -half, half, z0, z1, 0);
        }
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddEnvelope("cargo", hull, new Aabb(new Vector3(-B * 0.35f, body.Bottom + H * 0.2f, zBody0 + 2), new Vector3(B * 0.35f, body.Top - 1, zProw)));

        Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy + H * r.Range(-0.05f, 0.15f), B * r.Range(0.6f, 0.88f), H * r.Range(0.85f, 1.2f));
        Parts.Superstructure(c, deck, p.Tiers, 0.7f, 0.95f, 1f, lengthMin: 0.55f, lengthMax: 0.88f);
        ProwDressing(c, hull, loft, zProw);
        SideBlocks(c, hull, body, zBody0, zProw);
    }

    // ------------------------------------------------------------------ spine: command block, payload on a keel, engine block

    public static void Spine(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.05f, 0.08f);
        var cmdLen = L * r.Range(0.2f, 0.28f);
        var blockLen = L * r.Range(0.09f, 0.14f);
        var zEngFace = -L / 2 + engLen;
        var zEngFront = zEngFace + blockLen;
        var zCmdRear = L / 2 - cmdLen;

        var sw = B * r.Range(0.24f, 0.34f);
        var sh = H * r.Range(0.38f, 0.55f);
        var spineP = Profile.Box(0, 0, sw, sh, MathF.Min(sw, sh) * 0.18f);
        var (spine, _) = c.AddLoft("spine", "spine", ModuleTier.Primary, null,
            [(zEngFront - 2, spineP), (zCmdRear + 2, spineP)], Roles.HullSecondary, "spine");

        // Rear mass: engineering block.
        var ew = B * r.Range(0.7f, 0.95f);
        var eh = H * r.Range(0.95f, 1.25f);
        var engP = new Profile(0, H * r.Range(-0.05f, 0.1f), ew * 0.9f, ew, eh, ew * r.Range(0.05f, 0.12f), eh * r.Range(0.12f, 0.3f));
        var (eng, engLoft) = c.AddLoft("engine_block", "engineering_block", ModuleTier.Primary, spine.Id,
            [(zEngFace, engP.Scaled(0.94f, 0.94f)), (zEngFace + 1.2f, engP), (zEngFront - 1.2f, engP), (zEngFront, engP.Scaled(0.9f, 0.9f))], Roles.HullMain);
        c.AddDeck(eng, engP.Top, -engP.TopFlatHalf, engP.TopFlatHalf, zEngFace + 1.5f, zEngFront - 1.5f, 0);
        c.AddSides(eng, engLoft, 1, Profile.FacePosX, Profile.FaceNegX);
        Parts.Engines(c, eng.Id, "", zEngFace, engP.Cx, engP.Cy, ew * r.Range(0.85f, 1.0f), eh * r.Range(0.85f, 1.0f));
        SideBlocks(c, eng, engP, zEngFace + 1.2f, zEngFront - 1.2f, chance: 0.5);

        // Front mass: command section with its own prow and superstructure.
        var cw = B * r.Range(0.85f, 1f);
        var ch = H * r.Range(1.1f, 1.45f);
        var cmdP = Parts.HullBody(c, 0, H * r.Range(0.05f, 0.25f), cw, ch, 0.06f);
        var prowLen = MathF.Min(Parts.ProwLength(p.ProwStyle, L, r), cmdLen * 0.55f);
        var zProw = L / 2 - prowLen;
        List<(float, Profile)> rings = [(zCmdRear, cmdP.Scaled(0.92f, 0.92f)), (zCmdRear + 1.5f, cmdP), (zProw, cmdP)];
        rings.AddRange(Parts.Prow(cmdP, zProw, L / 2, p.ProwStyle));
        var (cmd, cmdLoft) = c.AddLoft("command", "command_section", ModuleTier.Primary, spine.Id, rings, Roles.HullMain);
        var cmdDeck = c.AddDeck(cmd, cmdP.Top, -cmdP.TopFlatHalf, cmdP.TopFlatHalf, zCmdRear + 2, zProw - 0.5f, 0);
        if (p.ProwStyle == "stepped")
        {
            var (y, half, z0, z1) = Parts.StepDeck(cmdP, zProw, L / 2);
            c.AddDeck(cmd, y, -half, half, z0, z1, 0);
        }
        c.AddSides(cmd, cmdLoft, 1, Profile.FacePosX, Profile.FaceNegX);
        Parts.SideWindows(c, cmd, cmdLoft, 1, 0.6f);
        c.AddEnvelope("habitable", cmd, new Aabb(new Vector3(-cw * 0.35f, cmdP.Bottom + ch * 0.25f, zCmdRear + 2), new Vector3(cw * 0.35f, cmdP.Top - 1, zProw)));
        Parts.Superstructure(c, cmdDeck, p.Tiers, 0.6f, 0.9f, 1f, lengthMin: 0.5f, lengthMax: 0.8f);
        ProwDressing(c, cmd, cmdLoft, zProw);

        // Middle: payload segments hung on the spine, with gaps that show the keel (negative space).
        var n = r.Int(2, 4);
        var gap = r.Range(2.5f, 6f);
        var z0All = zEngFront + gap * 0.6f;
        var z1All = zCmdRear - gap * 0.6f;
        var segLen = (z1All - z0All - gap * (n - 1)) / n;
        for (var k = 0; k < n; k++)
        {
            var zs = z0All + k * (segLen + gap);
            var ze = zs + segLen;
            var oneSided = p.Asymmetric && r.Chance(0.5) ? r.Sign() : 0f;
            // Mostly the ship's main payload, with the odd different segment so it never reads as a train.
            var kind = r.Chance(0.35) ? r.Pick(["containers", "tanks", "pods"]) : p.Payload;
            if (p.Archetype != "freighter" && kind == "containers") kind = "pods";
            switch (kind)
            {
                case "containers": Payload.ContainerRack(c, spine, k, zs, ze, sw, sh, oneSided); break;
                case "tanks": Payload.Tanks(c, spine, k, zs, ze, sw, sh); break;
                default: Payload.Pods(c, spine, k, zs, ze, sw, sh, oneSided); break;
            }
            // A collar where the segment clamps onto the keel.
            var collar = Profile.Box(0, 0, sw + 0.8f, sh + 0.8f, MathF.Min(sw, sh) * 0.25f);
            var cm = c.Add($"spine_collar_{k}", "structural_collar", ModuleTier.Secondary, spine.Id);
            new Loft([(zs - 0.6f, collar), (zs + 0.6f, collar)]).Build(cm.Mesh, Roles.DarkMechanics);
            new Loft([(ze - 0.6f, collar), (ze + 0.6f, collar)]).Build(cm.Mesh, Roles.DarkMechanics);
        }
    }

    // ------------------------------------------------------------------ twin: two hulls joined by a bridge deck

    public static void Twin(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.07f, 0.1f);
        var wh = B * r.Range(0.27f, 0.34f);
        var hx = B / 2 - wh / 2;
        var prowLen = Parts.ProwLength(p.ProwStyle, L, r) * 0.8f;
        var ct = wh * r.Range(0.05f, 0.1f);
        var cb = H * r.Range(0.22f, 0.4f);
        var shortSide = p.Asymmetric ? r.Sign() : 0f;
        var engW = wh * r.Range(0.85f, 1.05f);
        var engH = H * r.Range(0.85f, 1.15f);

        (Module m, Loft loft, Profile prof, float zRear, float zFront) Hull(float side, string id, string? parent)
        {
            var prof = c.Plan.HullForm == "faceted" ? Parts.HullBody(c, side * hx, 0, wh, H) : new Profile(side * hx, 0, wh * r.Range(0.7f, 0.85f), wh, H, ct, cb);
            var zRear = -L / 2 + engLen;
            var zFront = side == shortSide ? L / 2 - L * r.Range(0.12f, 0.2f) : L / 2;
            var zProw = zFront - prowLen;
            List<(float, Profile)> rings = [(zRear, prof.Scaled(0.92f, 0.9f)), (zRear + L * 0.02f, prof), (zProw, prof)];
            rings.AddRange(Parts.Prow(prof, zProw, zFront, p.ProwStyle));
            var (m, loft) = c.AddLoft(id, "side_hull", ModuleTier.Primary, parent, rings, Roles.HullMain);
            c.AddDeck(m, prof.Top, side * hx - prof.TopFlatHalf, side * hx + prof.TopFlatHalf, zRear + L * 0.03f, zProw - 0.5f, 0);
            c.AddSides(m, loft, 1, side > 0 ? Profile.FacePosX : Profile.FaceNegX);
            return (m, loft, prof, zRear, zFront);
        }

        var port = Hull(1, "hull_port", null);

        // The central bridge deck spans the gap and reaches into both hulls.
        var innerHalf = hx - wh * 0.25f;
        var dh = H * r.Range(0.45f, 0.65f);
        var dTop = H / 2 + H * r.Range(0.05f, 0.3f);
        var shortFront = Math.Min(port.zFront, L / 2 - (shortSide != 0 ? L * 0.12f : 0));
        var zd1 = shortFront - prowLen * r.Range(0.9f, 1.4f);
        var zd0 = zd1 - L * r.Range(0.3f, 0.45f);
        var dw = innerHalf * 2;
        var deckP = new Profile(0, dTop - dh / 2, dw * 0.96f, dw, dh, dh * 0.2f, dh * 0.3f);
        var frontP = new Profile(0, dTop - dh * 0.35f, dw * 0.9f, dw * 0.86f, dh * 0.7f, dh * 0.12f, dh * 0.3f);
        var slope = dh * r.Range(0.8f, 1.6f);
        var (center, centerLoft) = c.AddLoft("bridge_deck", "bridge_deck", ModuleTier.Primary, port.m.Id,
            [(zd0, deckP.Scaled(0.98f, 0.92f)), (zd0 + 1, deckP), (zd1 - slope, deckP), (zd1, frontP)], Roles.HullMain, "tier");
        var centerDeck = c.AddDeck(center, deckP.Top, -deckP.TopFlatHalf, deckP.TopFlatHalf, zd0 + 1.5f, zd1 - slope - 0.5f, 1);
        Shapes.Patch(center.Mesh, Roles.Light, centerLoft, 2, Profile.FaceBottom, 0.3f, 0.7f, 0.2f, 0.8f, 0.05f);

        var starboard = Hull(-1, "hull_starboard", center.Id);

        foreach (var (h, side, prefix) in new[] { (port, 1f, "port_"), (starboard, -1f, "starboard_") })
        {
            Parts.Engines(c, h.m.Id, prefix, h.zRear, side * hx, h.prof.Cy - H * 0.05f, engW, engH);
            ProwDressing(c, h.m, h.loft, h.zFront - prowLen);
        }

        // Rear cross girders, and on freighters a cargo pod slung under them.
        var beams = r.Int(1, 2);
        for (var k = 0; k < beams; k++)
        {
            var zb = float.Lerp(-L / 2 + engLen + L * 0.08f, zd0 - 4, beams == 1 ? 0.5f : k);
            var bd = MathF.Max(2.5f, L * 0.02f);
            var bh = H * r.Range(0.25f, 0.4f);
            var bm = c.Add($"cross_beam_{k}", "cross_girder", ModuleTier.Secondary, port.m.Id);
            var bp = Profile.Box(0, H / 2 - bh / 2 - H * 0.05f, hx * 2, bh, bh * 0.2f);
            new Loft([(zb - bd / 2, bp), (zb + bd / 2, bp)]).Build(bm.Mesh, Roles.HullSecondary);
            if (p.Archetype == "freighter" && k == 0)
            {
                var podW = (hx - wh / 2) * 2 - 2.5f;
                var podH = H * r.Range(0.5f, 0.75f);
                var podTop = bp.Bottom + 0.3f;
                var podLen = MathF.Min(L * 0.22f, (zd0 - (-L / 2 + engLen)) * 0.8f);
                var pod = c.Add("cargo_pod", "cargo_pod", ModuleTier.Secondary, bm.Id);
                pod.Detachable = true;
                var pp = new Profile(0, podTop - podH / 2, podW * 0.9f, podW, podH, podH * 0.15f, podH * 0.25f);
                var podLoft = new Loft([(zb - podLen / 2, pp.Scaled(0.95f, 0.95f)), (zb - podLen / 2 + 0.8f, pp), (zb + podLen / 2 - 0.8f, pp), (zb + podLen / 2, pp.Scaled(0.95f, 0.95f))]);
                podLoft.Build(pod.Mesh, Roles.Accent);
                pod.Lofts.Add(podLoft);
                c.Lofts.Add(new LoftPart(pod, podLoft, "pod"));
                c.AddEnvelope("cargo", pod, Aabb.FromCenterSize(new Vector3(0, pp.Cy, zb), new Vector3(podW * 0.8f, podH * 0.7f, podLen * 0.8f)));
            }
        }

        Parts.Superstructure(c, centerDeck, p.Tiers, 0.55f, 0.85f, 1.15f, lengthMin: 0.45f, lengthMax: 0.8f);
    }

    // ------------------------------------------------------------------ block: a compact armoured brick (patrol / tender)

    public static void Block(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.08f, 0.12f);
        var zRear = -L / 2 + engLen;
        var prowLen = L * r.Range(0.22f, 0.32f);
        var zProw = L / 2 - prowLen;
        var body = Parts.HullBody(c, 0, 0, B, H, 0.1f);
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.9f, 0.9f)), (zRear + L * 0.05f, body), (zProw, body)];
        rings.AddRange(Parts.Prow(body, zProw, L / 2, p.ProwStyle));
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        var deck = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, zRear + L * 0.06f, zProw - 0.5f, 0);
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.3f, body.Bottom + H * 0.25f, zRear + 3), new Vector3(B * 0.3f, body.Top - 1, zProw)));

        // Sponsons: armoured side masses, hardpoint decks on top.
        var spLen = L * r.Range(0.25f, 0.4f);
        var spZ1 = zProw - L * r.Range(0.0f, 0.12f);
        var spW = B * r.Range(0.16f, 0.24f);
        var spH = H * r.Range(0.35f, 0.55f);
        var spY = body.Cy + H * r.Range(-0.1f, 0.15f);
        foreach (var side in new[] { 1f, -1f })
        {
            // Against the hull wall at the sponson's height (the wall is sloped and chamfered), sunk 1 m into it.
            var wall = MathF.Min(body.HalfWidthAt(spY - spH * 0.3f), body.HalfWidthAt(spY + spH * 0.3f));
            var sp = new Profile(side * (wall + spW / 2 - 1f), spY, spW * 0.85f, spW, spH, spW * 0.18f, spH * 0.3f);
            var spFront = sp with { H = sp.H * 0.55f, Cy = sp.Cy - sp.H * 0.12f, WTop = sp.WTop * 0.8f };
            var (sm, sl) = c.AddLoft(side > 0 ? "sponson_port" : "sponson_starboard", "sponson", ModuleTier.Primary, hull.Id,
                [(spZ1 - spLen, spFront), (spZ1 - spLen + spH * 0.6f, sp), (spZ1 - spH * 0.8f, sp), (spZ1, spFront)], Roles.HullMain);
            c.AddDeck(sm, sp.Top, sp.Cx - sp.TopFlatHalf, sp.Cx + sp.TopFlatHalf, spZ1 - spLen + spH * 0.7f, spZ1 - spH * 0.9f, 0);
            c.AddSides(sm, sl, 1, side > 0 ? Profile.FacePosX : Profile.FaceNegX);
        }
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);

        // Engines, protected by armour shrouds on either side.
        var bayW = B * r.Range(0.55f, 0.75f);
        var bayH = H * r.Range(0.65f, 0.9f);
        var bay = Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy - H * 0.05f, bayW, bayH);
        var nozzleZ = c.Ship.Markers.Where(m => m.Kind == "engine_exhaust").Min(m => m.Position.Z);
        var bayBounds = bay.Mesh.Bounds();
        foreach (var side in new[] { 1f, -1f })
        {
            var sm = c.Add(side > 0 ? "engine_shroud_port" : "engine_shroud_starboard", "engine_shroud", ModuleTier.Secondary, bay.Id);
            var x = side * ((side > 0 ? bayBounds.Max.X : -bayBounds.Min.X) + 0.6f);
            var shroud = Profile.Box(x, body.Cy - H * 0.05f, 1.8f, bayH * 1.08f, 0.5f);
            new Loft([(nozzleZ + 1.2f, shroud.Scaled(0.7f, 0.85f)), (nozzleZ + 3f, shroud), (bayBounds.Max.Z - 0.5f, shroud)]).Build(sm.Mesh, Roles.HullSecondary);
        }

        Parts.Superstructure(c, deck, p.Tiers, 0.6f, 0.9f, 1.1f);
        ProwDressing(c, hull, loft, zProw, chance: 0.3);
    }

    // ------------------------------------------------------------------ shared extras

    /// <summary>Observation glass for the prow: wraparound facet glass on faceted hulls, a side window band otherwise.</summary>
    private static void ProwDressing(BuildContext c, Module m, Loft loft, float zProw, double? chance = null)
    {
        var p = c.Plan;
        if (p.HullForm == "faceted")
        {
            var want = chance ?? (p.Archetype == "research" ? 0.85 : p.Archetype == "freighter" ? 0.4 : 0.25);
            if (c.Shape.Chance(want)) ProwGlass(c, m, loft, zProw);
            return;
        }
        var s = Enumerable.Range(0, loft.Segments).FirstOrDefault(i => loft.Zs[i] >= zProw - 0.01f && !loft.IsStep(i), -1);
        if (s >= 0) ProwWindows(c, m, loft, s, chance);
    }

    /// <summary>
    /// The Thalassa's signature: one continuous band of tall panes across the lower prow facets, following every
    /// facet to the nose, with a strip of warm lights under it.
    /// </summary>
    private static void ProwGlass(BuildContext c, Module m, Loft loft, float zProw)
    {
        var placed = false;
        for (var s = 0; s < loft.Segments; s++)
        {
            if (loft.Zs[s] < zProw - 0.01f || loft.IsStep(s)) continue;
            foreach (var f in new[] { Profile.FaceBottomPosX, Profile.FaceBottomNegX })
            {
                var (along, across) = loft.FaceSize(s, f);
                if (along < 2 || across < 1.5f) continue;
                var key = BuildContext.FaceKey(m, s, f);
                c.UseSurface(key, 0, 1, 0, 1);
                var a = Parts.UpV(f, 0.2f);
                var b = Parts.UpV(f, 0.9f);
                Parts.PaneRow(m.Mesh, loft, s, f, 0.01f, 0.99f, MathF.Min(a, b), MathF.Max(a, b), Math.Max(2, (int)(along / 2.4f)), alongU: true, gap: 0.16f);
                var l0 = Parts.UpV(f, 0.06f);
                var l1 = Parts.UpV(f, 0.11f);
                Shapes.Patch(m.Mesh, Roles.Light, loft, s, f, 0.02f, 0.98f, MathF.Min(l0, l1), MathF.Max(l0, l1), 0.07f);
                placed = true;
            }
        }
        if (placed)
        {
            var s0 = Enumerable.Range(0, loft.Segments).First(i => loft.Zs[i] >= zProw - 0.01f);
            c.AddMarker("observation_deck", m, loft.FacePoint(s0, Profile.FaceBottom, 0.5f, 0.5f), Vector3.UnitZ, loft.Profiles[s0].WTop * 0.4f);
        }
    }

    /// <summary>A wide observation window band wrapping the first prow segment (mostly research ships).</summary>
    private static void ProwWindows(BuildContext c, Module m, Loft loft, int s, double? chance = null)
    {
        var p = c.Plan;
        if (s >= loft.Segments || loft.IsStep(s)) return;
        var want = chance ?? (p.Archetype == "research" ? 0.75 : p.Archetype == "freighter" ? 0.3 : 0.15);
        if (!c.Shape.Chance(want)) return;
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            var (along, across) = loft.FaceSize(s, f);
            if (along < 4 || across < 2) continue;
            var v0 = Parts.UpV(f, 0.35f);
            var v1 = Parts.UpV(f, 0.72f);
            if (v0 > v1) (v0, v1) = (v1, v0);
            c.UseSurface(BuildContext.FaceKey(m, s, f), 0.05f, 0.95f, v0, v1);
            Parts.PaneRow(m.Mesh, loft, s, f, 0.08f, 0.9f, v0, v1, Math.Max(3, (int)(along / 2.6f)), alongU: true, gap: 0.12f);
        }
        c.AddMarker("observation_deck", m, loft.FacePoint(s, Profile.FaceTop, 0.5f, 0.5f), Vector3.UnitZ, loft.Profiles[s].WTop * 0.4f);
    }

    /// <summary>
    /// Blocky modules bolted along the hull sides in a row with gaps (machinery, tanks, bays): they break up the slab
    /// sides into the stacked, assembled look of the references. Symmetric unless the ship is asymmetric.
    /// </summary>
    private static void SideBlocks(BuildContext c, Module hull, Profile body, float z0, float z1, double chance = 0.8)
    {
        var r = c.Shape;
        if (!r.Chance(chance)) return;
        var span = z1 - z0;
        var count = r.Int(1, span > 60 ? 3 : 2);
        var ph = body.H * r.Range(0.35f, 0.6f);
        var pw = body.WTop * r.Range(0.1f, 0.17f);
        var y = body.Top - ph / 2 - body.H * r.Range(0.05f, 0.3f);
        var gap = span * r.Range(0.03f, 0.08f);
        var total = span * r.Range(0.45f, 0.85f);
        var len = (total - gap * (count - 1)) / count;
        var start = z0 + r.Range(0, span - total);
        var sides = c.Plan.Asymmetric ? new[] { r.Sign() } : new[] { 1f, -1f };
        var wall = MathF.Min(body.HalfWidthAt(y - ph * 0.3f), body.HalfWidthAt(y + ph * 0.3f));
        for (var k = 0; k < count; k++)
        {
            var zs = start + k * (len + gap);
            var ze = zs + len;
            var h = ph * r.Range(0.8f, 1.15f);
            var inset = MathF.Min(h * 0.4f, len * 0.3f);
            foreach (var side in sides)
            {
                var pp = new Profile(body.Cx + side * (wall + pw / 2 - 0.8f), y, pw * 0.8f, pw, h, pw * 0.25f, h * 0.3f);
                var (pm, pl) = c.AddLoft($"side_block_{(side > 0 ? "port" : "starboard")}", "side_module", ModuleTier.Primary, hull.Id,
                    [(zs, pp.Scaled(0.75f, 0.7f)), (zs + inset, pp), (ze - inset, pp), (ze, pp.Scaled(0.75f, 0.7f))],
                    k % 2 == 0 ? Roles.HullSecondary : Roles.HullMain, "pod");
                c.AddSides(pm, pl, 1, side > 0 ? Profile.FacePosX : Profile.FaceNegX);
                c.AddDeck(pm, pp.Top, pp.Cx - pp.TopFlatHalf, pp.Cx + pp.TopFlatHalf, zs + inset + 0.3f, ze - inset - 0.3f, 0);
            }
        }
    }
}
