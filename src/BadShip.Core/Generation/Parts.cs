using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

public sealed record TierInfo(Module Module, Loft Loft, Deck? Deck, float TopY, float FrontZ);

/// <summary>Shared building blocks: prows, engine assemblies, superstructure tiers, bridges and windows.</summary>
public static class Parts
{
    // ------------------------------------------------------------------ hull bodies

    /// <summary>
    /// The main cross-section of a hull mass. "slab": upright sides with modest chamfers. "faceted": the Thalassa
    /// look, wide shallow top facets meeting the sides in a shoulder crease, over a deep inward-sloping lower hull.
    /// </summary>
    public static Profile HullBody(BuildContext c, float cx, float cy, float w, float h, float slabTop = 0.05f)
    {
        var r = c.Shape;
        var k = c.Lang.Chamfer;
        float Taper(float ratio) => Math.Clamp(1 - (1 - ratio) * c.Lang.Taper, 0.4f, 1f);
        if (c.Plan.HullForm == "faceted")
            return new Profile(cx, cy, w * Taper(r.Range(0.5f, 0.66f)), w, h, h * r.Range(0.16f, 0.24f) * k, h * r.Range(0.38f, 0.5f) * MathF.Sqrt(k)) { TopRun = r.Range(1.8f, 2.8f) };
        return new Profile(cx, cy, w * Taper(r.Range(0.66f, 0.86f)), w, h, w * r.Range(slabTop * 0.6f, slabTop * 1.4f) * k, h * r.Range(0.25f, 0.42f) * k);
    }

    // ------------------------------------------------------------------ prows

    public static float ProwLength(string style, float length, Rng r, bool faceted = false) => faceted && style is "faceted" or "wedge"
        ? length * r.Range(0.24f, 0.33f) // the Thalassa's long faceted nose, home of the observation deck
        : style switch
    {
        "blunt" => length * r.Range(0.035f, 0.06f),
        "stepped" => length * r.Range(0.14f, 0.22f),
        _ => length * r.Range(0.13f, 0.22f),
    };

    /// <summary>Rings that carry a hull body profile <paramref name="b"/> from z0 forward to the nose at z1.</summary>
    public static IEnumerable<(float, Profile)> Prow(Profile b, float z0, float z1, string style)
    {
        var len = z1 - z0;
        switch (style)
        {
            case "wedge": // the top drops towards a low nose
                yield return (z0 + len * 0.55f, b with { WTop = b.WTop * 0.9f, WBottom = b.WBottom * 0.88f, H = b.H * 0.8f, Cy = b.Cy - b.H * 0.1f });
                yield return (z1, b with
                {
                    WTop = b.WTop * 0.6f, WBottom = b.WBottom * 0.5f, H = b.H * 0.4f, Cy = b.Cy - b.H * 0.25f,
                    ChamferTop = b.ChamferTop * 0.6f, ChamferBottom = b.ChamferBottom * 0.35f,
                });
                break;
            case "faceted": // narrows to a blunt octagonal nose
                yield return (z0 + len * 0.5f, b with { WTop = b.WTop * 0.9f, WBottom = b.WBottom * 0.86f, H = b.H * 0.94f });
                yield return (z1, b with
                {
                    WTop = b.WTop * 0.52f, WBottom = b.WBottom * 0.42f, H = b.H * 0.6f, Cy = b.Cy - b.H * 0.04f,
                    ChamferTop = b.ChamferTop * 0.8f, ChamferBottom = b.ChamferBottom * 0.5f,
                });
                break;
            case "shovel": // the keel sweeps up to a flat-topped nose
                yield return (z0 + len * 0.5f, b with { WBottom = b.WBottom * 0.85f, H = b.H * 0.86f, Cy = b.Cy + b.H * 0.07f });
                yield return (z1, b with
                {
                    WTop = b.WTop * 0.78f, WBottom = b.WBottom * 0.55f, H = b.H * 0.42f, Cy = b.Cy + b.H * 0.25f,
                    ChamferBottom = b.ChamferBottom * 0.4f, ChamferTop = b.ChamferTop * 0.8f,
                });
                break;
            case "stepped": // a lower foredeck stepping down from the main block
                {
                    var zs = z0 + len * 0.3f;
                    yield return (zs, b);
                    var low = StepLow(b);
                    yield return (zs, low);
                    yield return (z1 - len * 0.14f, low);
                    yield return (z1, low with { WTop = low.WTop * 0.78f, WBottom = low.WBottom * 0.66f, H = low.H * 0.78f, Cy = low.Cy - low.H * 0.06f });
                    break;
                }
            default: // blunt: a near-vertical chamfered face
                yield return (z1 - len * 0.45f, b with { WTop = b.WTop * 0.97f, WBottom = b.WBottom * 0.95f, H = b.H * 0.96f });
                yield return (z1, b with { WTop = b.WTop * 0.85f, WBottom = b.WBottom * 0.8f, H = b.H * 0.85f, ChamferTop = b.ChamferTop * 1.4f });
                break;
        }
    }

    public static Profile StepLow(Profile b) => b with { H = b.H * 0.62f, Cy = b.Cy - b.H * 0.19f, WTop = b.WTop * 0.93f, ChamferTop = b.ChamferTop * 0.7f };

    /// <summary>For a stepped prow: the foredeck below the step, as (y, half width, z0, z1).</summary>
    public static (float y, float half, float z0, float z1) StepDeck(Profile b, float z0, float z1)
    {
        var len = z1 - z0;
        var low = StepLow(b);
        return (low.Top, low.TopFlatHalf, z0 + len * 0.3f + 0.5f, z1 - len * 0.16f);
    }

    // ------------------------------------------------------------------ engines

    /// <summary>
    /// An engineering bay behind <paramref name="zFace"/> (the parent's rear face) with a grid of engine pods or nacelles
    /// on its back, each with a nozzle, a glowing core and an exhaust marker. Returns the bay.
    /// </summary>
    public static Module Engines(BuildContext c, string parentId, string prefix, float zFace, float cx, float cy, float bayW, float bayH, float? nacelleLength = null, float? bayLength = null)
    {
        var r = c.Shape;
        var p = c.Plan;
        bayW *= c.Lang.EngineScale;
        bayH *= c.Lang.EngineScale;
        var bayLen = bayLength ?? MathF.Min(bayH * r.Range(0.4f, 0.7f), p.Length * 0.1f);
        c.LastBayLength = bayLen;
        var cham = MathF.Min(bayW, bayH);
        var bp = new Profile(cx, cy, bayW * 0.9f, bayW, bayH, cham * r.Range(0.08f, 0.18f), cham * r.Range(0.08f, 0.18f));
        var zBack = zFace - bayLen;
        var (bay, bayLoft) = c.AddLoft(prefix + "engineering", "engineering_bay", ModuleTier.Primary, parentId,
            [(zBack, bp.Scaled(0.94f, 0.94f)), (zBack + bayLen * 0.15f, bp), (zFace + 1f, bp)], Roles.HullMain);

        if (c.Lang.Signature == "engine_collar")
        {
            // The yard's mark: a heavy accent collar round the engineering bay.
            var collar = bp.Scaled(1.08f, 1.1f);
            var cz = zBack + bayLen * r.Range(0.35f, 0.55f);
            new Loft([(cz - bayLen * 0.12f, collar), (cz + bayLen * 0.12f, collar)]).Build(bay.Mesh, Roles.Accent);
        }
        var cols = p.EngineLayout[0] - '0';
        var rows = p.EngineLayout[2] - '0';
        // Fit the layout to the bay's shape: a narrow bay can't take four engines abreast.
        while (cols > 1 && bayW / cols < bayH / rows * 0.7f) cols--;
        while (rows > 1 && bayH / rows < bayW / cols * 0.45f) rows--;
        var cellW = bayW / cols;
        var cellH = bayH / rows;
        var rad = MathF.Min(cellW, cellH) * 0.5f * r.Range(1.0f, 1.15f);
        if (cols == 1 && rows == 2) rad = MathF.Min(bayW * 0.42f, cellH * 0.5f * 0.95f);
        var nl = nacelleLength ?? MathF.Min(rad * r.Range(1.8f, 2.6f) + bayLen * 0.2f, p.Length * (p.Size == "small" ? 0.07f : 0.09f));
        c.LastNacelleLength = nl;
        var k = 0;
        for (var j = 0; j < rows; j++)
            for (var i = 0; i < cols; i++)
            {
                var x = cx + (i - (cols - 1) * 0.5f) * cellW;
                var y = cy + (j - (rows - 1) * 0.5f) * cellH;
                var eng = c.Add($"{prefix}engine_{k++}", "engine", ModuleTier.Primary, bay.Id);
                var zs = zBack + MathF.Min(rad * 0.8f, bayLen * 0.6f);
                var ze = zBack - nl;
                if (p.EngineStyle == "pod")
                {
                    var pod = Profile.Box(x, y, rad * 2.05f, rad * 2f, rad * 0.5f);
                    var loft = new Loft([(ze, pod.Scaled(0.86f, 0.86f)), (ze + rad * 0.35f, pod), (zs, pod)]);
                    loft.Build(eng.Mesh, Roles.HullMain);
                    eng.Lofts.Add(loft);
                    c.Lofts.Add(new LoftPart(eng, loft, "pod"));
                    // Two cooling bands around the pod.
                    foreach (var t in new[] { 0.35f, 0.7f })
                    {
                        var zb = float.Lerp(ze + rad * 0.35f, zs, t);
                        var band = Profile.Box(x, y, rad * 2.15f, rad * 2.1f, rad * 0.55f);
                        new Loft([(zb - rad * 0.12f, band), (zb + rad * 0.12f, band)]).Build(eng.Mesh, Roles.DarkMechanics);
                    }
                }
                else
                {
                    var a = new Vector3(x, y, zs);
                    var b = new Vector3(x, y, ze + rad * 0.3f);
                    Shapes.Prism(eng.Mesh, Roles.HullMain, a, b, rad, rad, 8, MathF.PI / 8);
                    Shapes.Prism(eng.Mesh, Roles.HullMain, b, new Vector3(x, y, ze), rad, rad * 0.88f, 8, MathF.PI / 8);
                    foreach (var t in new[] { 0.3f, 0.65f })
                    {
                        var zb = float.Lerp(ze, zs, t);
                        Shapes.Prism(eng.Mesh, Roles.DarkMechanics, new Vector3(x, y, zb - rad * 0.1f), new Vector3(x, y, zb + rad * 0.1f), rad * 1.07f, rad * 1.07f, 8, MathF.PI / 8);
                    }
                }
                // Nozzle bell with the glowing core as its end cap.
                var zn = ze - rad * 0.55f;
                Shapes.Prism(eng.Mesh, Roles.DarkMechanics, new Vector3(x, y, ze + 0.2f), new Vector3(x, y, zn), rad * 0.7f, rad * 0.84f, 12, 0,
                    capA: false, capBMat: Roles.EngineGlow);
                c.AddMarker("engine_exhaust", eng, new Vector3(x, y, zn), -Vector3.UnitZ, rad * 0.84f, rad * 8f);
            }

        c.AddDeck(bay, bp.Top, cx - bp.TopFlatHalf, cx + bp.TopFlatHalf, zBack + bayLen * 0.2f, zFace - 0.5f, 0);
        c.AddEnvelope("engineering", bay, new Aabb(new Vector3(cx - bayW * 0.4f, cy - bayH * 0.35f, zBack + 1), new Vector3(cx + bayW * 0.4f, cy + bayH * 0.35f, zFace)));
        return bay;
    }

    // ------------------------------------------------------------------ superstructure

    /// <summary>
    /// One superstructure block standing on a deck at <paramref name="yBase"/>: slightly tapered, chamfered, with a front
    /// that is sloped, undercut ("overhang", a ship's bridge) or blunt. Side windows on habitable tiers; a window band
    /// and a bridge marker when <paramref name="bridge"/> is set.
    /// </summary>
    public static TierInfo TierBlock(BuildContext c, string id, string parentId, float cx, float yBase, float z0, float z1,
        float w, float h, string front, bool bridge, bool windows = true)
    {
        var r = c.Shape;
        var taper = r.Range(0.84f, 0.97f);
        var ct = MathF.Min(w, h) * r.Range(0.1f, 0.22f);
        var len = z1 - z0;
        var slope = front switch
        {
            "overhang" => MathF.Min(len * 0.25f, h * r.Range(0.35f, 0.6f)),
            "blunt" => MathF.Min(len * 0.12f, h * 0.25f),
            _ => MathF.Min(len * 0.35f, h * r.Range(0.6f, 1.1f)),
        };
        var y0 = yBase - 0.3f;
        var hh = h + 0.3f;
        var full = new Profile(cx, y0 + hh / 2, w, w * taper, hh, ct, 0);
        var frontP = front switch
        {
            "overhang" => new Profile(cx, y0 + hh * 0.6f, w * 0.96f, w * taper, hh * 0.8f, ct, hh * 0.28f),
            "blunt" => full.ScaledOnBase(0.93f, 0.92f),
            _ => new Profile(cx, y0 + hh * 0.3f, w * 0.97f, w * taper * 0.88f, hh * 0.6f, ct * 0.6f, 0),
        };
        var rearLen = MathF.Min(0.8f, len * 0.05f);
        var (m, loft) = c.AddLoft(id, bridge ? "bridge_block" : "superstructure", ModuleTier.Primary, parentId,
            [(z0, full.ScaledOnBase(0.97f, 0.95f)), (z0 + rearLen, full), (z1 - slope, full), (z1, frontP)], Roles.HullMain, "tier");

        if (windows) SideWindows(c, m, loft, 1, r.Chance(0.5) ? 0.55f : 0.62f);
        if (bridge)
        {
            Vector3 center;
            if (front == "slope")
            {
                var (_, across) = loft.FaceSize(2, Profile.FaceTop);
                var panes = Math.Max(2, (int)(across / 2.2f));
                PaneRow(m.Mesh, loft, 2, Profile.FaceTop, 0.18f, 0.72f, 0.06f, 0.94f, panes, alongU: false);
                Shapes.Patch(m.Mesh, Roles.Glass, loft, 2, Profile.FaceTopPosX, 0.18f, 0.72f, 0.15f, 0.85f, 0.06f);
                Shapes.Patch(m.Mesh, Roles.Glass, loft, 2, Profile.FaceTopNegX, 0.18f, 0.72f, 0.15f, 0.85f, 0.06f);
                center = loft.FacePoint(2, Profile.FaceTop, 0.45f, 0.5f);
            }
            else
            {
                // A band of panes across the vertical front face.
                var fp = loft.Profiles[^1];
                var ya = y0 + hh * (front == "overhang" ? 0.5f : 0.45f);
                var yb = y0 + hh * (front == "overhang" ? 0.8f : 0.75f);
                var bw = MathF.Min(fp.WBottom, fp.WTop) * 0.86f;
                var panes = Math.Max(3, (int)(bw / 2.4f));
                var gap = 0.22f;
                var pw = (bw - gap * (panes - 1)) / panes;
                for (var i = 0; i < panes; i++)
                {
                    var x0 = cx - bw / 2 + i * (pw + gap);
                    Shapes.BoxMinMax(m.Mesh, Roles.Glass, new Vector3(x0, ya, z1 - 0.1f), new Vector3(x0 + pw, yb, z1 + 0.1f));
                }
                center = new Vector3(cx, (ya + yb) / 2, z1);
            }
            c.AddMarker("bridge", m, center, Vector3.UnitZ, w * 0.4f);
        }
        if (windows || bridge)
            c.AddEnvelope("habitable", m, new Aabb(new Vector3(cx - w * 0.4f, yBase, z0 + 0.5f), new Vector3(cx + w * 0.4f, yBase + h * 0.85f, z1 - slope)));

        var topHalf = full.TopFlatHalf;
        var deck = c.AddDeck(m, full.Top, cx - topHalf, cx + topHalf, z0 + rearLen + 0.3f, z1 - slope - 0.3f, 1);
        return new TierInfo(m, loft, deck, full.Top, z1);
    }

    /// <summary>
    /// Stacked superstructure tiers on a deck, each smaller than the one below; the top one is the bridge.
    /// </summary>
    public static TierInfo? Superstructure(BuildContext c, Deck deck, int tiers, float widthMin = 0.55f, float widthMax = 0.85f,
        float heightScale = 1f, string prefix = "superstructure", float lengthMin = 0.35f, float lengthMax = 0.62f,
        string? anchorAt = null, bool bridge = true)
    {
        var r = c.Shape;
        var p = c.Plan;
        lengthMin = MathF.Min(lengthMin * c.Lang.TierLength, 0.9f);
        lengthMax = MathF.Min(lengthMax * c.Lang.TierLength, 0.95f);
        var len = MathF.Min(MathF.Max(deck.Length * r.Range(lengthMin, lengthMax), 7f), deck.Length);
        var w = MathF.Min(MathF.Max(deck.Width * r.Range(widthMin, widthMax), 5f), deck.Width);
        var h = p.Height * r.Range(0.4f, 0.6f) * heightScale;
        var anchor = r.Pick(["aft", "mid", "fore"]);
        if (anchorAt != null) anchor = anchorAt;
        var zc = anchor switch
        {
            "aft" => deck.Z0 + len / 2 + deck.Length * 0.04f,
            "mid" => (deck.Z0 + deck.Z1) / 2 + r.Range(-0.1f, 0.1f) * deck.Length,
            _ => deck.Z1 - len / 2 - deck.Length * 0.03f,
        };
        zc = Num.Clamp(zc, deck.Z0 + len / 2, deck.Z1 - len / 2);
        var mid = (deck.X0 + deck.X1) / 2;
        var cx = p.Asymmetric ? mid + (deck.Width - w) / 2 * r.Range(0.5f, 1f) * r.Sign() : mid;

        // Plan the stack first (each tier smaller than the one below) so the top tier is always the bridge.
        var sizes = new List<(float len, float w, float h)> { (len, w, h) };
        for (var k = 1; k < tiers; k++)
        {
            var (pl, pw, ph) = sizes[^1];
            var next = (pl * r.Range(0.45f, 0.72f), pw * r.Range(0.62f, 0.88f), ph * r.Range(0.7f, 0.95f));
            if (next.Item1 < 5 || next.Item2 < 4) break;
            sizes.Add(next);
        }

        TierInfo? top = null;
        var current = deck;
        for (var k = 0; k < sizes.Count; k++)
        {
            var last = k == sizes.Count - 1;
            if (k > 0)
            {
                // Sit on the tier below: towards its front (bridge forward) or anywhere along it.
                len = MathF.Min(sizes[k].len, current.Length);
                w = MathF.Min(sizes[k].w, current.Width);
                h = sizes[k].h;
                zc = r.Chance(0.6) ? current.Z1 - len / 2 : current.Z0 + len / 2 + r.Range(0, MathF.Max(0, current.Length - len));
                zc = Num.Clamp(zc, current.Z0 + len / 2, current.Z1 - len / 2);
                cx = (current.X0 + current.X1) / 2 + (p.Asymmetric ? (current.Width - w) / 2 * r.Range(-1f, 1f) : 0);
            }
            var t = TierBlock(c, $"{prefix}_{k}", current.ModuleId, cx, current.Y, zc - len / 2, zc + len / 2, w, h,
                last && bridge ? p.BridgeStyle : r.Pick(["slope", "blunt"]), bridge: last && bridge);
            current.Take(cx - w / 2, cx + w / 2, zc - len / 2, zc + len / 2);
            top = t;
            if (last) break;
            if (t.Deck is null || t.Deck.Length < 3 || t.Deck.Width < 3)
            {
                // No room above: this tier becomes the bridge after all.
                if (bridge) c.AddMarker("bridge", t.Module, new Vector3(cx, t.TopY - h * 0.3f, zc + len / 2), Vector3.UnitZ, w * 0.4f);
                break;
            }
            current = t.Deck;
        }
        return top;
    }

    // ------------------------------------------------------------------ windows

    /// <summary>v measured from the low edge of a face to its high edge (faces on the −X side run top to bottom).</summary>
    public static float UpV(int face, float up) => face is Profile.FaceNegX or Profile.FaceTopNegX or Profile.FaceBottomNegX ? 1 - up : up;

    /// <summary>A row of window panes along both sides of a loft segment, at height fraction <paramref name="up"/>.</summary>
    public static void SideWindows(BuildContext c, Module m, Loft loft, int s, float up)
    {
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            var (along, across) = loft.FaceSize(s, f);
            if (along < 4 || across < 1.6f) continue;
            var style = c.Lang.Windows;
            var band = style == "slits" ? MathF.Min(0.1f, 0.5f / across) : MathF.Min(0.22f, 1.2f / across);
            var v0 = UpV(f, up);
            var v1 = UpV(f, up + band);
            if (v0 > v1) (v0, v1) = (v1, v0);
            var key = BuildContext.FaceKey(m, s, f);
            // Restrained: one to three short clusters of panes rather than a ribbon down the whole side.
            var r = c.Shape;
            if (style == "bands" && c.TryUseSurface(key, 0.06f, 0.94f, v0 - 0.02f, v1 + 0.02f))
            {
                PaneRow(m.Mesh, loft, s, f, 0.06f, 0.94f, v0, v1, Math.Max(2, (int)(along / 2.6f)), alongU: true);
                continue;
            }
            var clusters = along > 30 ? r.Int(1, 3) : 1;
            var paneLen = style == "slits" ? 3.2f : 2.4f;
            for (var k = 0; k < clusters; k++)
            {
                var panes = r.Int(2, Math.Max(2, Math.Min(8, (int)(along * 0.3f / paneLen))));
                var du = panes * paneLen / along;
                if (du > 0.88f) du = 0.88f;
                var slot = 1f / clusters;
                var u0 = k * slot + r.Range(0.06f, MathF.Max(0.06f, slot - du - 0.04f));
                if (!c.TryUseSurface(key, u0, u0 + du, v0 - 0.02f, v1 + 0.02f)) continue;
                PaneRow(m.Mesh, loft, s, f, u0, u0 + du, v0, v1, panes, alongU: true);
            }
        }
    }

    /// <summary>Glass panes with mullion gaps, split along u (or along v).</summary>
    public static void PaneRow(Mesh m, Loft loft, int s, int f, float u0, float u1, float v0, float v1, int count, bool alongU, float gap = 0.18f)
    {
        var span = alongU ? u1 - u0 : v1 - v0;
        var g = span * gap / count;
        var pw = (span - g * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var a = (alongU ? u0 : v0) + i * (pw + g);
            if (alongU) Shapes.Patch(m, Roles.Glass, loft, s, f, a, a + pw, v0, v1, 0.06f);
            else Shapes.Patch(m, Roles.Glass, loft, s, f, u0, u1, a, a + pw, 0.06f);
        }
    }
}
