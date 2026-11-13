# Independent Procedural Spaceship Exterior Generator — Product Concept

## Goal
Create an artist-friendly procedural modeling tool that produces **visually distinct, believable, editable, game-ready spaceship exteriors** from a seed and a constrained set of parameters. Focus on hard-surface industrial spacecraft that can be used in many different games and engines.

## Non-goals for the initial version
No detailed interiors; no gameplay or weapons simulation; no orbital mechanics; no space flight; no NoorMD dependency; no requirement for live Boolean geometry in exported files; no fully automatic textured AAA spaceship.

## User journey
1. Choose a ship archetype or start from a blank hull recipe.
2. Set dimensions, silhouette bias, module density, symmetry, propulsion and detail level.
3. Generate multiple deterministic candidates and inspect their silhouettes quickly.
4. Lock desired components, reroll unlocked components while preserving constraints.
5. Inspect part hierarchy and functional markers in a 3D viewer.
6. Export GLB plus a recipe and validation report; optionally export metadata and later other formats.

## Suggested control groups
- **Ship identity:** archetype, seed, ship name (metadata only), overall scale range.
- **Form:** length, beam, height, mass distribution, asymmetry, keel/spine family, number of major hull modules.
- **Structure:** bridge location, habitable blocks, side pods, structural frames, docking interfaces, cargo spaces.
- **Propulsion:** main engine count/size, aft arrangement, maneuvering thruster positions; exhaust clearances.
- **Surface:** armor density, plating size, ribs, vents, maintenance panels, sensor equipment.
- **Look:** palette, emissive color, marking density, controlled wear.
- **Output:** detail tier, LOD count, texture strategy, export units and axis metadata.

## Interface proposal
- Center: orbitable 3D preview; front/side/top orthographic views; silhouette mode.
- Left: generation presets and recipe controls.
- Right: selected module tree with category, size, transform, material role and lock toggle.
- Bottom: validation report, output log, seed history, reroll variants.
- Commands: Generate, Reroll Selection, Lock Module, Randomize Appearance, Validate, Save Recipe, Export.
- No bespoke DCC editor required in V1; preserve editable module hierarchy in GLB when possible.

## Ship consistency
- The output should resemble a real assembly: visible joints or plausible mounts, engine exhaust clearance, reachable docking collars, credible mass distribution, no egregious intersections.
- Preserve consistent local origin, scale, axes and standardized forward/up orientation.
- Mark functional regions (bridge, engines, cargo, docking, sensors) with stable IDs.
- Useful future feature: reserved internal envelope volumes for a future interior generator, **without generating rooms now**.

## Files
- `ship.glb`: named meshes/nodes, material slots, details split by category or module, optional collision hull proxies.
- `ship.recipe.json` (or future `.noorship`): full parameters, seed, algorithm version, module IDs and locked components.
- `ship.metadata.json`: bounding boxes, docks, engine positions and exhaust vectors, markers, envelope volumes, warnings.
- `ship.validation.json`: explicit pass/warn/error results.
- `ship_preview.png`: optional screenshot/contact sheet.
- Later: OBJ/FBX (if suitable tools), engine adapters, batch/CLI, custom module library.

## Quality gates / success criteria
1. A dozen generated seeds visibly differ at silhouette level, not just by panels or color.
2. All mandatory parts remain attached or are explicitly flagged as intentional detachable modules.
3. Exports reopen in at least one independent GLB viewer/DCC (Blender or Godot test).
4. Materials are usable and replaceable; part transforms are sane.
5. Seed reproducibility within a generator version; algorithm changes bump version.
6. No blocking mesh corruption; nonmanifold details documented rather than hidden.
7. Realistic performance budgets for viewport and export (measure; no promised fixed speed).
8. A user can obtain plausible results without knowing modeling terminology.
9. Project stays independent of NoorMD and any single game engine.

## Future roadmap (not a promise)
- Constrained custom hull footprints and artist-authored modules.
- Structured damage pass that preserves underlying intact geometry.
- Interior envelope metadata for BadLevel/other generators.
- More advanced collision/LOD/UV baking and lightmap options.
- Direct Godot/Unity/Unreal workflows via adapters.
- NoorMD bench integration as **NoorShip**, through a future technical handoff.

## Intellectual property / style
Use original structural designs. The Thalassa image is visual inspiration from the user, not a model to replicate. Do not reproduce recognizably copyrighted franchise ships.
