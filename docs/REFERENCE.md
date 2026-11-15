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
- **furniture**: `kind` (bunk, console, mess_table…), the `room` and `deck` it is in, `width` (along the wall), `depth`,
  `height`, and `facing` (from the wall into the room). Its origin is the middle of its footprint on the floor.

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

Keys can also be `"type/kind"` for one kind of a type: `"furniture/bunk"`, `"door/airlock_outer"`.
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

Generated by `badship vocab --count 12` (generator 0.38.0): 11 archetypes × 12 seeds, small, medium and large, with interiors.

### Exterior part types (`extras.type`)

| type | seen | extras keys | archetypes |
|---|---:|---|---|
| `access_trunk` | 9 | `size` | freighter, industrial, patrol, raider, research, ring |
| `antenna` | 237 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `armour` | 7 | `markers`, `mount`, `size` | citadel, fighter |
| `belly_door` | 7 | `markers`, `mount`, `size` | freighter |
| `boom` | 5 | `markers`, `mount`, `size` | civilian |
| `bridge` | 12 | `markers`, `mount`, `size` | citadel |
| `bridge_block` | 95 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `bridge_deck` | 8 | `size` | freighter, patrol, raider, research |
| `broadside` | 12 | `markers`, `mount`, `size` | citadel |
| `bumper` | 12 | `markers`, `mount`, `size` | utility |
| `buttress` | 12 | `size` | citadel |
| `cargo` | 8 | `markers`, `mount`, `size` | industrial, utility |
| `cargo_bay` | 22 | `markers`, `mount`, `size` | civilian, freighter, patrol, research, ring |
| `cargo_pod` | 12 | `size` | freighter |
| `claw` | 2 | `markers`, `mount`, `size` | industrial |
| `cockpit` | 12 | `markers`, `mount`, `size` | fighter |
| `command_section` | 4 | `markers`, `mount`, `size` | freighter, research |
| `container_rack` | 1 | `markers`, `mount`, `size` | freighter |
| `container_stack` | 4 | `size` | freighter |
| `conveyor` | 4 | `size` | industrial |
| `cradle` | 4 | `markers`, `mount`, `size` | industrial |
| `crane` | 29 | `markers`, `mount`, `size` | freighter, industrial, raider |
| `cross_girder` | 14 | `size` | freighter, patrol, raider, research |
| `details` | 986 | `markers`, `mount`, `size` | all |
| `dish` | 4 | `markers`, `mount`, `size` | civilian |
| `docking_collar` | 74 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `dome` | 8 | `markers`, `mount`, `size` | civilian |
| `dorsal_ridge` | 8 | `size` | civilian, freighter, industrial, patrol, research, ring |
| `drill` | 4 | `markers`, `mount`, `size` | industrial |
| `engine` | 230 | `markers`, `mount`, `size` | all |
| `engine_shroud` | 24 | `size` | patrol, raider |
| `engineering_bay` | 104 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `engineering_block` | 4 | `size` | freighter, research |
| `fin` | 48 | `size` | civilian, fighter, freighter, industrial, patrol, raider, research, ring, warship |
| `flight_deck` | 2 | `markers`, `mount`, `size` | warship |
| `floodlight` | 218 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `gantry` | 4 | `size` | industrial |
| `gondola` | 9 | `markers`, `mount`, `size` | freighter, industrial, patrol, raider, research, ring |
| `gun` | 26 | `markers`, `mount`, `size` | fighter |
| `hangar_bay` | 24 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research |
| `hardpoint` | 170 | `markers`, `mount`, `size` | civilian, industrial, patrol, raider, ring, warship |
| `hopper` | 6 | `size` | industrial |
| `hull` | 120 | `markers`, `mount`, `size` | all |
| `junk` | 47 | `size` | raider |
| `keel` | 64 | `size` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `keep` | 51 | `size` | citadel |
| `lab_pod` | 8 | `size` | research |
| `landing_gear` | 12 | `markers`, `mount`, `size` | utility |
| `lattice_mast` | 69 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring, warship |
| `lifeboat` | 4 | `size` | civilian |
| `lights` | 4 | `size` | utility |
| `livery` | 240 | `size` | all |
| `machinery_block` | 188 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `markings` | 3 | `size` | civilian |
| `missile_cells` | 5 | `markers`, `mount`, `size` | warship |
| `plant` | 2 | `markers`, `mount`, `size` | industrial |
| `prong` | 4 | `size` | fighter |
| `radiator` | 24 | `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `radome` | 28 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring, warship |
| `ring` | 17 | `size` | ring |
| `ring_collar` | 17 | `markers`, `mount`, `size` | ring |
| `sail` | 5 | `size` | raider |
| `scoop` | 2 | `markers`, `mount`, `size` | industrial |
| `sensor_dish` | 34 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, research, ring, warship |
| `shuttle` | 20 | `height`, `kind`, `length`, `size`, `width` | civilian, freighter, industrial, raider, research, warship |
| `side_hull` | 16 | `markers`, `mount`, `size` | freighter, patrol, raider, research |
| `side_module` | 47 | `size` | freighter, patrol, raider, research, utility |
| `slung_pod` | 10 | `size` | freighter, industrial |
| `slung_tank` | 27 | `size` | industrial, patrol, raider, research |
| `spike` | 12 | `size` | raider |
| `spine` | 4 | `size` | freighter, research |
| `spoke` | 17 | `size` | ring |
| `sponson` | 24 | `size` | patrol, raider |
| `structural_collar` | 16 | `size` | freighter, research |
| `strut` | 2 | `size` | warship |
| `superstructure` | 90 | `markers`, `mount`, `size` | civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `tank_cluster` | 6 | `size` | freighter |
| `tower` | 156 | `size` | citadel |
| `turret` | 276 | `markers`, `mount`, `size` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, warship |
| `undercroft` | 9 | `size` | citadel |
| `utility_claw` | 1 | `markers`, `mount`, `size` | freighter |
| `ventral_sensor_dome` | 20 | `markers`, `mount`, `size` | civilian, industrial, patrol, raider, research, ring, warship |
| `window` | 7744 | `converted`, `deck`, `height`, `markers`, `mount`, `normal`, `room`, `size`, `width`, `window` | citadel, civilian, freighter, industrial, patrol, raider, research, ring, utility, warship |
| `wing` | 14 | `size` | fighter |
| `work_face` | 12 | `markers`, `mount`, `size` | industrial |

### Exterior marker kinds (`extras.kind` on `marker.*` nodes)

| kind | seen |
|---|---:|
| `bridge` | 132 |
| `broadside` | 296 |
| `cargo_door` | 53 |
| `cargo_hardpoint` | 3 |
| `claw` | 1 |
| `crane_hook` | 29 |
| `docking_port` | 74 |
| `elevator` | 2 |
| `engine_exhaust` | 295 |
| `floodlight` | 278 |
| `hangar` | 46 |
| `hardpoint` | 494 |
| `hatch` | 14 |
| `landing_pad` | 48 |
| `launch_lane` | 2 |
| `missile_cells` | 5 |
| `nav_light` | 194 |
| `observation_deck` | 39 |
| `rcs_thruster` | 432 |
| `ring_hub` | 17 |
| `sensor` | 191 |
| `tool` | 18 |
| `vent` | 5 |
| `window` | 7744 |

### Exterior materials (by role)

| material | seen |
|---|---:|
| `accent` | 551 |
| `accent_glow` | 24 |
| `cargo_a` | 81 |
| `cargo_b` | 92 |
| `cargo_c` | 69 |
| `dark_mechanics` | 2828 |
| `emissive_light` | 1131 |
| `engine_glow` | 230 |
| `glass` | 609 |
| `hazard` | 1340 |
| `hull_main` | 1689 |
| `hull_panel` | 795 |
| `hull_secondary` | 10267 |
| `livery` | 240 |
| `nav_red` | 315 |
| `radome` | 145 |
| `window_glass` | 7341 |

### Interior node types (`extras.type`)

| type | seen | extras keys |
|---|---:|---|
| `ceiling` | 604 |  |
| `deck` | 604 | `deck`, `floor_y` |
| `door` | 28456 | `height`, `kind`, `opens`, `rooms`, `slide_axis`, `width` |
| `elevator` | 53 | `access`, `decks`, `doors`, `kind`, `rooms`, `shaft`, `stops_y` |
| `furniture` | 245441 | `deck`, `depth`, `facing`, `height`, `kind`, `room`, `width` |
| `interior` | 113 |  |
| `ladder` | 166 | `access`, `bottom`, `decks`, `kind`, `rooms`, `shaft`, `stops_y`, `top` |
| `railing` | 577 |  |
| `room` | 40361 | `deck`, `decks`, `kind`, `max`, `min` |
| `stairs` | 366 | `from_deck`, `to_deck` |
| `walls` | 1367 |  |

### Interior marker kinds

| kind | seen |
|---|---:|
| `airlock` | 113 |
| `door` | 28343 |
| `elevator` | 53 |
| `ladder` | 166 |
| `room` | 40361 |
| `spawn` | 113 |

### Interior materials

| material | seen |
|---|---:|
| `emissive_light` | 149511 |
| `furniture_frame` | 239532 |
| `furniture_soft` | 66839 |
| `furniture_top` | 206585 |
| `hazard` | 76968 |
| `interior_door` | 28396 |
| `interior_floor` | 1627 |
| `interior_skin` | 1144 |
| `interior_trim` | 6461 |
| `interior_wall` | 893 |
| `zone_cargo` | 6220 |
| `zone_command` | 4679 |
| `zone_crew` | 10074 |
| `zone_medical` | 620 |
| `zone_plaza` | 353 |
| `zone_work` | 7174 |

### Deck parts (`decks[].part`)

| part | seen |
|---|---:|
| `gondola` | 9 |
| `hull` | 377 |
| `keel` | 28 |
| `superstructure` | 190 |

### Room kinds (`rooms[].kind`; `extras.kind` on room nodes)

| kind | seen |
|---|---:|
| `airlock` | 77 |
| `alcove` | 1657 |
| `armoury` | 1651 |
| `atrium` | 13 |
| `bridge` | 169 |
| `briefing` | 1459 |
| `brig` | 736 |
| `bunk_room` | 1984 |
| `captain_cabin` | 389 |
| `captain_office` | 243 |
| `cargo_bay` | 14 |
| `cargo_hold` | 162 |
| `clean_room` | 489 |
| `cockpit` | 1 |
| `commons` | 23 |
| `comms` | 1799 |
| `corridor` | 8272 |
| `crew_quarters` | 6403 |
| `elevator` | 289 |
| `elevator_lobby` | 274 |
| `engine_room` | 1 |
| `engineering` | 112 |
| `gallery` | 45 |
| `galley` | 353 |
| `hangar` | 18 |
| `lab` | 739 |
| `ladder_room` | 263 |
| `life_support` | 2247 |
| `lobby` | 4 |
| `lounge` | 337 |
| `medbay` | 80 |
| `mess` | 79 |
| `observation` | 579 |
| `operations` | 40 |
| `passenger_cabin` | 918 |
| `reactor` | 430 |
| `stair_hall` | 106 |
| `stairwell` | 331 |
| `storage` | 6026 |
| `suite_lounge` | 317 |
| `ward` | 51 |
| `workshop` | 1181 |

### Door kinds (`doors[].kind`)

| kind | seen |
|---|---:|
| `airlock_outer` | 113 |
| `elevator` | 289 |
| `interior` | 28054 |
| `opening` | 11698 |

### Lifts (`lifts[].kind`)

| kind | seen |
|---|---:|
| `elevator` | 53 |
| `ladder` | 166 |

### Furniture kinds (`extras.kind` on furniture nodes; swap one with `furniture/KIND`)

| kind | seen |
|---|---:|
| `bed` | 1260 |
| `bench` | 1396 |
| `bunk` | 54970 |
| `console` | 21604 |
| `counter` | 2042 |
| `crates` | 31206 |
| `desk` | 3943 |
| `locker` | 27969 |
| `machine` | 20152 |
| `med_bed` | 345 |
| `mess_table` | 412 |
| `rack` | 25444 |
| `shelf` | 40260 |
| `sofa` | 5909 |
| `workbench` | 8529 |
