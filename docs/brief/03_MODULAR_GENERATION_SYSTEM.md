# Technical Design — Modular Spaceship Exterior Generation

## Architectural recommendation
Independent, engine-neutral **procedural geometry core** + immutable recipe + exporters + offline viewer. Language is Sage's choice for the prototype. Make the code easy to eventually port/integrate into NoorMD's C# environment, but **do not integrate now**.

### Main pipeline
```text
Seed + ship archetype + parameters
  -> semantic ship plan (functional zones and adjacency)
  -> silhouette skeleton (spine, primary masses and volumes)
  -> constrained module placement (structural attachments)
  -> engine / docking / sensor clearance checks
  -> secondary forms (armor and equipment)
  -> tertiary surface pass (panels, vents, lights, decals markers)
  -> optional damage pass (later)
  -> validation + metrics + preview
  -> GLB + recipe + semantic metadata
```

## Data model (illustrative, not frozen)
- `ShipRecipe`: `schema_version`, `generator_version`, `seed`, `class`, proportions, palette, density, optional locked components.
- `ShipPlan`: local coordinate system, functional zones, adjacency/attachment graph, global bounds, intended forward/up.
- `ModuleInstance`: stable id, category/type, parent id, local transform, dimensions, material role, sockets, constraints, optional envelope.
- `Socket`: transform, normal, width/depth/height clearance, allowed connection categories, load class.
- `FeatureMarker`: docking port transform, engine exhaust direction and cone, bridge location, cargo doors, sensors, service nodes.
- `ValidationIssue`: severity, affected IDs, geometric region, reason and proposed fix.

## Module taxonomy
**Primary:** spine, keel box, habitable block, armored prow, cargo block, engineering bay, bridge block, side hull/pod, engine nacelle.

**Secondary:** structural girders, chamfer sections, armor panels, service bays, docking collar, cargo rack, radiator, gantry, crane, sensor tower, truss.

**Tertiary:** vents, access hatch, plate seams, conduits, safety railings, beacons, lights, decals/IDs, maintenance covers, bolts.

Every module must have authored or procedural connection sockets, valid scale ranges, occupied volume and optional exclusion volume (e.g. engine exhaust).

## Silhouette synthesis
1. Choose 1 of several **macro families** (broad barge; twin-pod; central spine; compact block; offset hauler; multi-deck brick).
2. Reserve bridge, propulsion and cargo/mission zones.
3. Generate major masses as boxes/chamfered boxes/trapezoidal prisms; avoid freeform organic interpolation.
4. Use coarse spatial occupancy / bounding volumes for positioning and collision tests.
5. Use a weighted grammar or constraint-driven assembly (not unconstrained random stacking).
6. Score for hard constraints first (no critical collisions, propulsion clearance), then soft preferences (silhouette distinction, symmetry bias, negative space).
7. Preserve seed and decisions; expose per-stage random streams so changing tertiary details need not reshuffle major hull layout.

## Rules and constraints
- Every main module has an attachment path to the root spine; intentional detachable cargo is marked.
- No main engine exhaust through any structural module (test simple cone/box intersections to start).
- Docking collar clearance is reserved and access axis published in metadata.
- Bridge/sensor visibility may be considered as a soft rather than universal hard rule.
- Exterior openings cannot accidentally expose hollow/unclosed geometry; place explicit recess/caps.
- Large armor plates must not obscure ports, windows or important joints.
- Geometry stays within configured maximum bounds / polygon budget.
- Units and axes are defined centrally (suggest meters, right-handed world, Y-up glTF export, explicit forward axis).
- Intentional asymmetry is possible but still structurally legible.

## Mesh generation
- Generate meshes per module (or per material group with retained mapping), not one monolithic anonymous triangle soup.
- Build simple primitives with bevel/chamfer variants and indexed normals/UVs.
- Use constructive cutouts sparingly at first: recesses assembled from meshes can be safer for export.
- Keep named hierarchy, stable module UUIDs or path-based IDs, correct winding and normals.
- Materials by role: hull_main, hull_secondary, dark_mechanics, glass, emissive, hazard, accent.
- Use tiling/world-space-compatible UV guidelines; later support trim sheets and texture atlases.
- Allow instanced/reused mesh primitives and optional LOD tiers.

## Determinism / reproducibility
Document generator version, recipe schema and seeded RNG behavior. Use independent sub-seeds per stage (silhouette, modules, details, colors) so appearance-only rerolls do not alter the ship's shape. Pixel-perfect cross-language matching is **not** needed, but same build + same recipe should be stable.

## Export strategy
**V1:** GLB 2.0 with scene graph, mesh primitives, materials and glTF `extras` for semantic markers; recipe JSON; metadata JSON.
**Later:** modular OBJ, Blender workflows, Godot scene adapters, Unity prefabs or Unreal import preparation. Do not claim exporters work until round-trip tested.
**Validation:** reopen GLB with an independent parser or DCC; check all buffer/accessor bounds, transforms, material IDs and scene nodes.

## Test plan
- Unit: seeded stage stability, socket matching, attachment graph, bounds/exhaust/docking tests, mesh winding/normals.
- Property: hundreds of randomized seeds produce no fatal violations or return explicit failure and bounded reroll.
- Regression: golden recipes and expected structural signatures, not fragile screenshot equality.
- Export: round-trip GLB in external software or independent library.
- Visual: contact sheet per archetype and seed set, silhouette-only renders to identify repeated shapes.
- Performance: CPU time, memory, output triangles, draw calls and GLB size across tiers.

## Incremental implementation
**A — Proven core:** three macro hull families, three archetypes, spine and module attachment, thrusters, bridge, glTF output, seeded variant gallery and critical validation. The deliverable should create compelling blocky shapes even before detail.

**B — Usability:** parameter editor, locked modules, deterministic detail rerolls, export report, orthographic/silhouette views.

**C — Richness:** cargo cranes, bays, sensor towers, radiators, armor, LODs, decals, alternate palettes, expanded archetypes.

**D — Optional expansion:** localized damage, modular external templates, interior constraint/envelope data, engine integrations and NoorShip handoff.

## Beware
Do not conflate spaceship *exterior modeling* with procedural FPS interior layout. Reserve docking and habitable envelopes now if cheap, but keep actual interior generation for a separate project. Do not create an in-game spaceship flight simulator. Focus on beautiful, robust reusable assets.
