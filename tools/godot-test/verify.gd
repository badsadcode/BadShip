# Checks BadShip exports inside Godot, the two ways a game uses them:
#  1. editor import: res://ships/*.glb imported by Godot's glTF importer, loaded as PackedScenes;
#  2. runtime load: the same files read with GLTFDocument while the game runs.
# For each ship: node hierarchy and names, mesh count, triangle count, bounds against our metadata,
# materials, node extras, LOD files getting lighter, and collision pieces that become convex shapes.
# Writes res://report.json and exits with 1 if anything failed.
extends SceneTree

var failures := []
var report := []

func _init() -> void:
	var manifest = JSON.parse_string(FileAccess.get_file_as_string("res://ships/manifest.json"))
	for ship in manifest:
		var r := {"name": ship.name}
		check_imported(ship, r)
		check_runtime(ship, r)
		check_lods(ship, r)
		check_collision(ship, r)
		report.append(r)
		print("%s  import:%s  runtime:%s  lods:%s  collision:%s" % [ship.name, r.get("import", "?"), r.get("runtime", "?"), r.get("lods", "?"), r.get("collision", "?")])
	var f := FileAccess.open("res://report.json", FileAccess.WRITE)
	f.store_string(JSON.stringify({"godot": Engine.get_version_info().string, "ships": report, "failures": failures}, "  "))
	f.close()
	print("Godot %s: %d ships, %d failures" % [Engine.get_version_info().string, manifest.size(), failures.size()])
	for msg in failures:
		print("  FAIL ", msg)
	quit(1 if failures.size() > 0 else 0)

func fail(ship_name: String, msg: String) -> void:
	failures.append("%s: %s" % [ship_name, msg])

# ------------------------------------------------------------------ helpers

# Walks the tree accumulating transforms (no SceneTree needed): returns meshes with their world transforms.
func collect(node: Node, xform: Transform3D, out: Array) -> void:
	var t := xform
	if node is Node3D:
		t = xform * (node as Node3D).transform
	if node is MeshInstance3D and (node as MeshInstance3D).mesh != null:
		out.append({"node": node, "xform": t})
	for child in node.get_children():
		collect(child, t, out)

func names_of(node: Node, out: Dictionary) -> void:
	out[String(node.name)] = node
	for child in node.get_children():
		names_of(child, out)

func stats(root: Node) -> Dictionary:
	var meshes := []
	collect(root, Transform3D.IDENTITY, meshes)
	var aabb := AABB()
	var first := true
	var tris := 0
	var materials := {}
	for m in meshes:
		var mesh: Mesh = m.node.mesh
		var box: AABB = m.xform * mesh.get_aabb()
		aabb = box if first else aabb.merge(box)
		first = false
		for s in mesh.get_surface_count():
			var arrays := mesh.surface_get_arrays(s)
			var idx = arrays[Mesh.ARRAY_INDEX]
			tris += (idx.size() if idx != null else arrays[Mesh.ARRAY_VERTEX].size()) / 3
			var mat := mesh.surface_get_material(s)
			if mat != null:
				materials[mat.resource_name] = mat
	return {"meshes": meshes.size(), "aabb": aabb, "tris": tris, "materials": materials}

# Godot turns characters it can't use in node names (like ".") into "_".
func godot_name(id: String) -> String:
	return id.validate_node_name().replace(".", "_")

func compare(ship, r: Dictionary, key: String, root: Node) -> bool:
	var s := stats(root)
	var ok := true
	var size: Vector3 = s.aabb.size
	var want := Vector3(ship.size[0], ship.size[1], ship.size[2])
	if (size - want).abs().length() > maxf(0.05, want.length() * 0.005):
		fail(ship.name, "%s bounds %s, expected %s" % [key, size, want]); ok = false
	if s.tris != int(ship.triangles):
		fail(ship.name, "%s has %d triangles, expected %d" % [key, s.tris, ship.triangles]); ok = false
	if s.meshes < ship.mesh_modules.size():
		fail(ship.name, "%s has %d meshes for %d modules with geometry" % [key, s.meshes, ship.mesh_modules.size()]); ok = false
	var names := {}
	names_of(root, names)
	var missing := 0
	for id in ship.modules:
		if not names.has(godot_name(id)):
			missing += 1
	if missing > 0:
		fail(ship.name, "%s is missing %d of %d module nodes" % [key, missing, ship.modules.size()]); ok = false
	for role in s.materials.keys():
		if not ship.materials.has(role):
			fail(ship.name, "%s has an unexpected material '%s'" % [key, role]); ok = false
	if not s.materials.has("hull_main"):
		fail(ship.name, "%s has no hull_main material" % key); ok = false
	elif s.materials.has("engine_glow") and not (s.materials["engine_glow"] as BaseMaterial3D).emission_enabled:
		fail(ship.name, "%s: engine_glow is not emissive" % key); ok = false
	if s.materials.has("livery"):
		var lm := s.materials["livery"] as BaseMaterial3D
		if lm.albedo_texture == null:
			fail(ship.name, "%s: the livery decal sheet did not arrive as a texture" % key); ok = false
		elif lm.transparency != BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR:
			fail(ship.name, "%s: livery is not alpha cut-out (transparency %d)" % [key, lm.transparency]); ok = false
		r[key + "_livery_texture"] = lm.albedo_texture != null
	# Node extras (id, type, tier...) come through as metadata in Godot 4.4+.
	var hull = names.get(godot_name(ship.modules[0]))
	var extras_ok: bool = hull != null and hull.has_meta("extras") and hull.get_meta("extras").get("id", "") == ship.modules[0]
	r[key + "_extras"] = extras_ok
	r[key + "_meshes"] = s.meshes
	r[key + "_triangles"] = s.tris
	r[key + "_size"] = [snappedf(size.x, 0.01), snappedf(size.y, 0.01), snappedf(size.z, 0.01)]
	r[key + "_markers"] = names.keys().filter(func(n): return n.begins_with("marker_")).size()
	return ok

# ------------------------------------------------------------------ the four checks

func check_imported(ship, r: Dictionary) -> void:
	var scene = load("res://ships/%s.glb" % ship.name)
	if scene == null or not (scene is PackedScene):
		fail(ship.name, "the editor import did not produce a scene"); r["import"] = "FAIL"; return
	var root: Node = scene.instantiate()
	r["import"] = "ok" if compare(ship, r, "import", root) else "FAIL"
	root.free()

func check_runtime(ship, r: Dictionary) -> void:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var path := ProjectSettings.globalize_path("res://ships/%s.glb" % ship.name)
	var err := doc.append_from_file(path, state)
	if err != OK:
		fail(ship.name, "GLTFDocument could not read the file (error %d)" % err); r["runtime"] = "FAIL"; return
	var root := doc.generate_scene(state)
	r["runtime"] = "ok" if compare(ship, r, "runtime", root) else "FAIL"
	root.free()

func check_lods(ship, r: Dictionary) -> void:
	var tris := []
	for suffix in ["", ".lod1", ".lod2"]:
		var scene = load("res://ships/%s%s.glb" % [ship.name, suffix])
		if scene == null:
			fail(ship.name, "LOD file %s did not import" % suffix); r["lods"] = "FAIL"; return
		var root: Node = scene.instantiate()
		tris.append(stats(root).tris)
		root.free()
	var ok: bool = tris[0] > tris[1] and tris[1] > tris[2] and tris[2] > 0
	for i in 3:
		if tris[i] != int(ship.lod_triangles[i]):
			ok = false
	if not ok:
		fail(ship.name, "LOD triangles %s, expected %s" % [tris, ship.lod_triangles])
	r["lod_triangles"] = tris
	r["lods"] = "ok" if ok else "FAIL"

func check_collision(ship, r: Dictionary) -> void:
	var scene = load("res://ships/%s.collision.glb" % ship.name)
	if scene == null:
		fail(ship.name, "the collision file did not import"); r["collision"] = "FAIL"; return
	var root: Node = scene.instantiate()
	var meshes := []
	collect(root, Transform3D.IDENTITY, meshes)
	var shapes := 0
	for m in meshes:
		var shape: ConvexPolygonShape3D = (m.node.mesh as Mesh).create_convex_shape(true, false)
		if shape != null and shape.points.size() >= 4:
			shapes += 1
	root.free()
	var ok: bool = meshes.size() == int(ship.collision_pieces) and shapes == meshes.size()
	if not ok:
		fail(ship.name, "collision: %d pieces, %d convex shapes, expected %d" % [meshes.size(), shapes, ship.collision_pieces])
	r["collision_pieces"] = meshes.size()
	r["collision_shapes"] = shapes
	r["collision"] = "ok" if ok else "FAIL"
