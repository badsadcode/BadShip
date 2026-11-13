## Headless check of an interior: loads the GLB at runtime, applies InteriorSetup, opens and closes a door, then bakes
## a navigation mesh (door leaves left out: open) and asks for a walking path from the spawn point to every room.
##   godot --headless --path tools/godot-interior --script res://verify.gd -- path/to/name.interior.glb
extends SceneTree

func _initialize() -> void:
	var path: String = OS.get_cmdline_user_args()[0]
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		print("FAIL: could not read ", path); quit(1); return
	var scene := doc.generate_scene(state)
	root.add_child(scene)
	var counts := InteriorSetup.apply(scene)
	print("doors with script: ", counts.doors, ", colliders: ", counts.colliders, ", rooms: ", counts.rooms)
	_run.call_deferred(scene, counts)

func _nodes(n: Node, out: Array) -> void:
	out.append(n)
	for c in n.get_children(): _nodes(c, out)

func _run(scene: Node, counts: Dictionary) -> void:
	var all := []
	_nodes(scene, all)
	# A door opens and closes.
	var door: Node3D = null
	for n in all:
		if n.get_script() != null and str(n.get_meta("extras", {}).get("kind", "")) == "interior":
			door = n; break
	if door == null:
		print("FAIL: no interior door"); quit(1); return
	var start: Vector3 = door.position
	door.open()
	await create_timer(0.6).timeout
	var moved: float = door.position.distance_to(start)
	door.close()
	await create_timer(0.6).timeout
	var back: float = door.position.distance_to(start)
	print("door slides %.2f m open and returns to %.2f m" % [moved, back])
	# Navigation: bake from walls, floors and stairs (not door leaves).
	var nav := NavigationMesh.new()
	nav.agent_radius = 0.35
	nav.agent_height = 1.8
	nav.agent_max_climb = 0.4
	nav.agent_max_slope = 50.0
	nav.cell_size = 0.2
	nav.cell_height = 0.05
	nav.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_MESH_INSTANCES
	var src := NavigationMeshSourceGeometryData3D.new()
	for n in all:
		if n is MeshInstance3D:
			var t := str(n.get_meta("extras", {}).get("type", ""))
			if t in ["walls", "deck", "ceiling", "stairs"]:
				src.add_mesh((n as MeshInstance3D).mesh, (n as MeshInstance3D).global_transform)
	NavigationServer3D.bake_from_source_geometry_data(nav, src)
	var map := get_root().get_world_3d().navigation_map
	NavigationServer3D.map_set_cell_size(map, nav.cell_size)
	NavigationServer3D.map_set_cell_height(map, nav.cell_height)
	var before_sync := NavigationServer3D.map_get_iteration_id(map)
	var region := NavigationServer3D.region_create()
	NavigationServer3D.region_set_map(region, map)
	NavigationServer3D.region_set_navigation_mesh(region, nav)
	# Ladders and elevators: the links InteriorSetup made, registered on this map.
	for n in all:
		if n is NavigationLink3D:
			var l := NavigationServer3D.link_create()
			NavigationServer3D.link_set_map(l, map)
			NavigationServer3D.link_set_start_position(l, (n as NavigationLink3D).global_transform * (n as NavigationLink3D).start_position)
			NavigationServer3D.link_set_end_position(l, (n as NavigationLink3D).global_transform * (n as NavigationLink3D).end_position)
			NavigationServer3D.link_set_bidirectional(l, true)
	# An elevator rides to its top stop and opens the door there.
	for n in all:
		if n.get_script() != null and str(n.get_meta("extras", {}).get("type", "")) == "elevator":
			var y0: float = n.position.y
			n.call_to(n.stops_y.size() - 1)
			var ride: float = absf(float(n.stops_y[-1]) - float(n.stops_y[0])) / n.speed + n.dwell + 0.5
			await create_timer(ride).timeout
			print("elevator rides %.1f m up, stop %d, top door open: %s" % [n.position.y - y0, n.stop, str(n._doors[-1] != null and n._doors[-1].is_open)])
	# Big interiors take a while to sync: wait until the map has synced since the region and links were added.
	for i in 600:
		await process_frame
		if NavigationServer3D.map_get_iteration_id(map) > before_sync: break
	# Spawn and room markers.
	var spawn := Vector3.ZERO
	var rooms := []
	for n in all:
		var x: Dictionary = n.get_meta("extras", {})
		if str(x.get("kind", "")) == "spawn": spawn = (n as Node3D).global_position
		if str(x.get("type", "")) == "room" and str(x.get("kind", "")) not in ["stairwell", "elevator"]: rooms.append(n)
	var reached := 0
	var failed := []
	for room in rooms:
		var target: Vector3 = (room as Node3D).global_position + Vector3(0, 0.1, 0)
		var p := NavigationServer3D.map_get_path(map, spawn + Vector3(0, 0.1, 0), target, true)
		if p.size() > 0 and p[p.size() - 1].distance_to(target) < 1.0: reached += 1
		else: failed.append(room.name)
	print("navigation: %d of %d rooms reachable on foot from the spawn point" % [reached, rooms.size()])
	if failed.size() > 0: print("  unreachable: ", failed.slice(0, 10))
	var per := {}
	for room in rooms:
		var dk = room.get_meta("extras", {}).get("deck", -1)
		per[dk] = per.get(dk, [0, 0])
		per[dk][1] += 1
		if not failed.has(room.name): per[dk][0] += 1
	print("  per deck (reached, total): ", per)
	var ok: bool = counts.doors > 0 and moved > 0.9 and back < 0.05 and reached == rooms.size()
	print("PASS" if ok else "FAIL")
	quit(0 if ok else 1)
