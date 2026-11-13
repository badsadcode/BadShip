# BadShip reference: finding parts and putting your own assets on them

Every ship BadShip makes is a tree of named parts. A game can find any of them by code, hang behaviour on them, or hide the generated mesh and put a proper asset in its place. This page says what is in the files and what you can rely on.

## Conventions

- Units are **meters**. **+Y is up, +Z is forward** (the bow), **+X is port**. Right-handed, as glTF expects.
- The ship's origin is its centre of mass, roughly. Lengths in `size` are the part's bounding box (x, y, z).
- Node translations are **relative to the parent node** (normal glTF). Positions inside extras (`mount.position`, marker positions in `metadata.json`) are in **ship space**.

## Files you get

| file | what it is |
|---|---|
| `NAME.glb` | the ship: every part a node, plus a `markers` group |
| `NAME.lod1.glb`, `NAME.lod2.glb` | lighter versions, same part names |
| `NAME.collision.glb` | simple collision shapes |
| `NAME.interior.glb`, `NAME.interior.deck_N.glb` | the walkable interior, whole and one deck per file |
| `NAME.ship_and_interior.glb` | both together, lined up |
| `NAME.interior.json` | decks, rooms, doors, lifts, stairs: the interior as data |
| `NAME.metadata.json` | parts, markers and stats as data, without loading any mesh |
| `NAME.recipe.json` | the settings that made the ship (generate it again from this) |

## Finding a part

Every part is a glTF node whose **name is its ID** and whose `extras` tell you what it is:

```json
"name": "turret",
"extras": {
  "id": "turret", "type": "turret", "tier": "secondary", "parent": "hull_main", "detachable": false,
  "size": [16.36, 7.81, 32.62],
  "markers": ["hardpoint_0"],
  "mount": { "kind": "hardpoint", "position": [0, 14.19, 35.0], "direction": [0, 0, 1], "radius": 8.43 }
}
```

- **`type`** is what you search for: every turret has `type: "turret"`, whatever its ID. The full list is below.
- **`id`** is unique within the ship. `parent` is the ID of the part it sits on.
- **`tier`** is `primary` (hull, engines), `secondary` (blocks, turrets) or `tertiary` (small bits, windows).
- **`size`** is the bounding box, for fitting a replacement.
- **`markers`** lists the IDs of the markers that belong to this part.
- **`mount`** is the part's main attachment point (a turret's hardpoint, an engine's exhaust, a window's centre): kind, ship-space position, the direction it faces, and a radius. Point your asset along `direction`.
- The node's **origin is the centre of the part** (or its pivot, for parts that turn), so a replacement placed at the node lands in the right spot.

In Godot, glTF extras arrive as node metadata: `node.get_meta("extras")["type"]`. In Unity (glTFast) and Unreal (glTFRuntime / the glTF importer), look for "extras" or "custom properties" in the importer settings.

### Windows

Each window is its own node (`type: "window"`) under the part it is cut into, with the glass and the frame as its mesh. Its extras add `room` (the interior room it lights), `deck`, `width`, `height`, `normal` (facing out), and `converted` (true where a painted pane on the hull became a real window). Swap the node to change the frame style, or switch the glass material to make it opaque from the outside.

### Markers

The `markers` group holds empty nodes named `marker.ID`, with `extras.kind` (see the table below), the `module` they belong to, and a `direction` and `radius` where it matters. Their rotation points their local +Z along `direction`. Use them for gameplay spots: where engine flames go (`engine_exhaust`), where guns fire from (`hardpoint`, `broadside`), docking (`docking_port`), lights (`nav_light`, `floodlight`), RCS puffs (`rcs_thruster`), the camera seat (`bridge`).

### Materials

Materials are named by **role**, not by look: `hull_main`, `hull_secondary`, `accent`, `window_glass`, `engine_glow`, and so on. Replace a material by name to restyle every surface that has that role, on every ship.

### Interiors

In the interior files, rooms, doors, ladders, elevators and stairs are nodes with their own extras:

- **room**: `kind` (crew_quarters, bridge, galley…), `deck`, `min`/`max` bounds.
- **door**: `kind` (interior, elevator, airlock_outer, opening), `rooms` it joins, `width`, `height`, `slide_axis`, `opens`.
- **ladder / elevator**: `decks` it links, `stops_y` per deck, `doors` (elevator), `access` points for navigation links, `shaft` bounds.
- **stairs**: `from_deck`, `to_deck`.

The `tools/godot-interior` scripts show all of these in use: doors slide, elevators ride, and navigation links join the decks.

## Swapping in your own assets (Godot)

`tools/godot-interior/asset_swap.gd` does it in one call:

```gdscript
var parts := {
    "turret": preload("res://art/turret.tscn"),                    # placed at each turret, facing its mount
    "engine": {"scene": preload("res://art/engine.tscn"), "fit": true}, # also scaled to the part's size
}
var materials := {"hull_main": preload("res://art/hull_plates.tres"), "window_glass": preload("res://art/glass.tres")}
var counts := AssetSwap.apply(ship_root, parts, materials)   # {"parts": 6, "surfaces": 41}
```

Each matching part gets your scene as a child named `asset`, and the generated mesh is hidden (not deleted, so anything else you attached keeps working). `verify_swap.gd` is a headless test of it on a real ship:

```bash
godot --headless --path tools/godot-interior --script res://verify_swap.gd -- path/to/ship.glb
```

Other engines do the same in a few lines: walk the nodes, read `extras.type`, parent your prefab to the node, hide its mesh renderer.

## What stays stable

- **Types, marker kinds, material roles, room, door and lift kinds** are the vocabulary. Code can depend on them. New ones may be added; existing ones won't be renamed without a note in the README.
- **IDs are deterministic**: the same recipe and generator version give the same IDs. A new generator version may number parts differently, so match on `type` (and `parent`, `mount`) in code, and on `id` only for a ship you have saved.
- Each file says which generator made it (`asset.generator` in the glTF, `generator_version` in the JSON).

## Vocabulary

Everything below comes from actual ships, generated by the command in the first line. Run it again for the current list:

```bash
badship vocab --count 12 --out docs/REFERENCE.vocab.md
```

Generated by `badship vocab --count 12` (generator 0.29.0): 11 archetypes × 12 seeds, small, medium and large, with interiors.

### Exterior part types (`extras.type`)

| type | seen | extras keys | archetypes |
|---|---:|---|---|
| `access_trunk` | 9 | `size` | freighter, industrial, patrol, raider, research, ring |
| `antenna` | 246 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `armour` | 7 | `markers`, `mount`, `size` | citadel, fighter |
| `belly_door` | 6 | `markers`, `mount`, `size` | freighter |
| `boom` | 5 | `markers`, `mount`, `size` | civilian |
| `bridge` | 12 | `markers`, `mount`, `size` | citadel |
| `bridge_block` | 95 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `bridge_deck` | 8 | `size` | freighter, patrol, raider, research |
| `broadside` | 12 | `markers`, `mount`, `size` | citadel |
| `bumper` | 12 | `markers`, `mount`, `size` | utility |
| `buttress` | 12 | `size` | citadel |
| `cargo` | 8 | `markers`, `mount`, `size` | industrial, utility |
| `cargo_bay` | 24 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring |
| `cargo_pod` | 12 | `size` | freighter |
| `claw` | 2 | `markers`, `mount`, `size` | industrial |
| `cockpit` | 12 | `markers`, `mount`, `size` | fighter |
| `command_section` | 4 | `markers`, `mount`, `size` | freighter, research |
| `container_rack` | 2 | `markers`, `mount`, `size` | freighter |
| `container_stack` | 3 | `size` | freighter |
| `conveyor` | 4 | `size` | industrial |
| `cradle` | 4 | `markers`, `mount`, `size` | industrial |
| `crane` | 30 | `markers`, `mount`, `size` | freighter, industrial, raider |
| `cross_girder` | 14 | `size` | freighter, patrol, raider, research |
| `details` | 937 | `markers`, `mount`, `size` | all |
| `dish` | 4 | `markers`, `mount`, `size` | civilian |
| `docking_collar` | 77 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `dome` | 7 | `markers`, `mount`, `size` | civilian |
| `dorsal_ridge` | 7 | `size` | civilian, freighter, industrial, patrol, research, ring |
| `drill` | 4 | `markers`, `mount`, `size` | industrial |
| `engine` | 230 | `markers`, `mount`, `size` | all |
| `engine_shroud` | 24 | `size` | patrol, raider |
| `engineering_bay` | 104 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `engineering_block` | 4 | `size` | freighter, research |
| `fin` | 54 | `size` | civilian, fighter, freighter, industrial, patrol, raider, research, ring, warship |
| `flight_deck` | 2 | `markers`, `mount`, `size` | warship |
| `floodlight` | 192 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `gantry` | 4 | `size` | industrial |
| `gondola` | 9 | `markers`, `mount`, `size` | freighter, industrial, patrol, raider, research, ring |
| `gun` | 26 | `markers`, `mount`, `size` | fighter |
| `hangar_bay` | 15 | `markers`, `mount`, `size` | civilian, freighter, patrol, raider, research |
| `hardpoint` | 163 | `markers`, `mount`, `size` | civilian, industrial, patrol, raider, ring, warship |
| `hopper` | 6 | `size` | industrial |
| `hull` | 120 | `markers`, `mount`, `size` | all |
| `junk` | 45 | `size` | raider |
| `keel` | 63 | `size` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `keep` | 51 | `size` | citadel |
| `lab_pod` | 10 | `size` | research |
| `landing_gear` | 12 | `markers`, `mount`, `size` | utility |
| `lattice_mast` | 94 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring, warship |
| `lifeboat` | 4 | `size` | civilian |
| `lights` | 4 | `size` | utility |
| `livery` | 253 | `size` | all |
| `machinery_block` | 179 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `markings` | 3 | `size` | civilian |
| `missile_cells` | 6 | `markers`, `mount`, `size` | warship |
| `plant` | 2 | `markers`, `mount`, `size` | industrial |
| `prong` | 4 | `size` | fighter |
| `radiator` | 26 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `radome` | 38 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring, warship |
| `ring` | 17 | `size` | ring |
| `ring_collar` | 17 | `markers`, `mount`, `size` | ring |
| `sail` | 6 | `size` | raider |
| `scoop` | 2 | `markers`, `mount`, `size` | industrial |
| `sensor_dish` | 8 | `markers`, `mount`, `size` | civilian, industrial, patrol, research |
| `side_hull` | 16 | `markers`, `mount`, `size` | freighter, patrol, raider, research |
| `side_module` | 45 | `size` | freighter, patrol, raider, research, utility |
| `slung_pod` | 10 | `size` | freighter, industrial |
| `slung_tank` | 21 | `size` | industrial, patrol, raider, research |
| `spike` | 12 | `size` | raider |
| `spine` | 4 | `size` | freighter, research |
| `spoke` | 17 | `size` | ring |
| `sponson` | 24 | `size` | patrol, raider |
| `structural_collar` | 18 | `size` | freighter, research |
| `strut` | 2 | `size` | warship |
| `superstructure` | 85 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `tank` | 4 | `size` | industrial, raider, ring, warship |
| `tank_cluster` | 6 | `size` | freighter, research |
| `tower` | 156 | `size` | citadel |
| `turret` | 280 | `markers`, `mount`, `size` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `undercroft` | 9 | `size` | citadel |
| `utility_claw` | 3 | `markers`, `mount`, `size` | freighter |
| `ventral_sensor_dome` | 23 | `markers`, `mount`, `size` | civilian, industrial, patrol, raider, research, ring, warship |
| `window` | 6941 | `converted`, `deck`, `height`, `markers`, `mount`, `normal`, `room`, `size`, `width`, `window` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `wing` | 14 | `size` | fighter |
| `work_face` | 12 | `markers`, `mount`, `size` | industrial |

### Exterior marker kinds (`extras.kind` on `marker.*` nodes)

| kind | seen |
|---|---:|
| `bridge` | 132 |
| `broadside` | 296 |
| `cargo_door` | 45 |
| `cargo_hardpoint` | 4 |
| `claw` | 3 |
| `crane_hook` | 30 |
| `docking_port` | 77 |
| `elevator` | 2 |
| `engine_exhaust` | 295 |
| `floodlight` | 252 |
| `hangar` | 46 |
| `hardpoint` | 491 |
| `hatch` | 14 |
| `landing_pad` | 48 |
| `launch_lane` | 2 |
| `missile_cells` | 6 |
| `nav_light` | 239 |
| `observation_deck` | 37 |
| `rcs_thruster` | 432 |
| `ring_hub` | 17 |
| `sensor` | 203 |
| `tool` | 16 |
| `vent` | 4 |
| `window` | 6941 |

### Exterior materials (by role)

| material | seen |
|---|---:|
| `accent` | 525 |
| `accent_glow` | 24 |
| `cargo_a` | 94 |
| `cargo_b` | 99 |
| `cargo_c` | 79 |
| `dark_mechanics` | 2348 |
| `emissive_light` | 1062 |
| `engine_glow` | 230 |
| `glass` | 633 |
| `hazard` | 297 |
| `hull_main` | 1819 |
| `hull_secondary` | 9148 |
| `livery` | 253 |
| `nav_red` | 347 |
| `radome` | 140 |
| `window_glass` | 6632 |

### Interior node types (`extras.type`)

| type | seen | extras keys |
|---|---:|---|
| `ceiling` | 522 |  |
| `deck` | 522 | `deck`, `floor_y` |
| `door` | 25792 | `height`, `kind`, `opens`, `rooms`, `slide_axis`, `width` |
| `elevator` | 51 | `access`, `decks`, `doors`, `kind`, `rooms`, `shaft`, `stops_y` |
| `interior` | 112 |  |
| `ladder` | 117 | `access`, `bottom`, `decks`, `kind`, `rooms`, `shaft`, `stops_y`, `top` |
| `railing` | 502 |  |
| `room` | 30522 | `deck`, `decks`, `kind`, `max`, `min` |
| `stairs` | 339 | `from_deck`, `to_deck` |
| `walls` | 1173 |  |

### Interior marker kinds

| kind | seen |
|---|---:|
| `airlock` | 112 |
| `door` | 25680 |
| `elevator` | 51 |
| `ladder` | 117 |
| `room` | 30522 |
| `spawn` | 112 |

### Interior materials

| material | seen |
|---|---:|
| `emissive_light` | 56365 |
| `hazard` | 163 |
| `interior_door` | 25731 |
| `interior_floor` | 1434 |
| `interior_skin` | 980 |
| `interior_trim` | 5835 |
| `interior_wall` | 753 |
| `zone_cargo` | 4138 |
| `zone_command` | 3963 |
| `zone_crew` | 9603 |
| `zone_medical` | 557 |
| `zone_plaza` | 341 |
| `zone_work` | 6391 |

### Deck parts (`decks[].part`)

| part | seen |
|---|---:|
| `gondola` | 9 |
| `hull` | 376 |
| `keel` | 28 |
| `superstructure` | 109 |

### Room kinds (`rooms[].kind`; `extras.kind` on room nodes)

| kind | seen |
|---|---:|
| `airlock` | 77 |
| `armoury` | 1541 |
| `atrium` | 13 |
| `bridge` | 150 |
| `briefing` | 1298 |
| `brig` | 699 |
| `bunk_room` | 2014 |
| `captain_cabin` | 314 |
| `captain_office` | 207 |
| `cargo_bay` | 15 |
| `cargo_hold` | 152 |
| `clean_room` | 433 |
| `cockpit` | 1 |
| `commons` | 22 |
| `comms` | 1554 |
| `corridor` | 4355 |
| `crew_quarters` | 6102 |
| `elevator` | 282 |
| `elevator_lobby` | 267 |
| `engine_room` | 1 |
| `engineering` | 111 |
| `gallery` | 46 |
| `galley` | 282 |
| `hangar` | 18 |
| `lab` | 629 |
| `ladder_room` | 194 |
| `life_support` | 1983 |
| `lounge` | 265 |
| `medbay` | 77 |
| `mess` | 77 |
| `observation` | 399 |
| `operations` | 40 |
| `passenger_cabin` | 863 |
| `reactor` | 364 |
| `stair_hall` | 54 |
| `stairwell` | 331 |
| `storage` | 3953 |
| `suite_lounge` | 306 |
| `ward` | 47 |
| `workshop` | 986 |

### Door kinds (`doors[].kind`)

| kind | seen |
|---|---:|
| `airlock_outer` | 112 |
| `elevator` | 282 |
| `interior` | 25398 |
| `opening` | 4581 |

### Lifts (`lifts[].kind`)

| kind | seen |
|---|---:|
| `elevator` | 51 |
| `ladder` | 117 |
