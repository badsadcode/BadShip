## Puts your own assets on a BadShip ship (or interior) after it is loaded or imported.
##
##   var parts := {"turret": preload("res://art/turret.tscn"), "engine": {"scene": preload("res://art/engine.tscn"), "fit": true}}
##   var mats := {"hull_main": preload("res://art/hull_metal.tres"), "window_glass": preload("res://art/glass.tres")}
##   AssetSwap.apply(ship_root, parts, mats)
##
## Parts are matched by their glTF extras "type" (see docs/REFERENCE.md for every type). Each match gets your scene
## as a child, at the part's origin (the centre of the part; a window's centre; a door's leaf), and the generated
## mesh is hidden (not freed, so gameplay scripts on it keep working). With "fit": true the scene is scaled
## uniformly to fit the part's "size" from the extras, assuming your scene is about 1 m across: handy for blocking
## out. Materials are matched by name on every mesh surface. Returns how many parts and surfaces were swapped.
class_name AssetSwap

static func apply(root: Node, parts: Dictionary = {}, materials: Dictionary = {}) -> Dictionary:
	var counts := {"parts": 0, "surfaces": 0}
	for node in _all(root):
		var extras: Dictionary = node.get_meta("extras", {})
		var type := str(extras.get("type", ""))
		if type != "" and parts.has(type) and node is Node3D:
			var spec = parts[type]
			var scene: PackedScene = spec if spec is PackedScene else spec.get("scene")
			if scene == null: continue
			var inst: Node3D = scene.instantiate()
			inst.name = "asset"
			if spec is Dictionary and spec.get("fit", false) and extras.has("size"):
				var s: Array = extras["size"]
				var k := minf(float(s[0]), minf(float(s[1]), float(s[2])))
				inst.scale = Vector3.ONE * maxf(k, 0.01)
			# Face the way the part's mount points (a turret's hardpoint, an engine's exhaust), if it has one.
			if extras.has("mount"):
				var dir: Array = extras["mount"].get("direction", [0, 0, 1])
				var d := Vector3(float(dir[0]), float(dir[1]), float(dir[2]))
				if d.length() > 0.01 and absf(d.normalized().dot(Vector3.UP)) < 0.99:
					inst.basis = Basis.looking_at(-d.normalized(), Vector3.UP) * Basis.from_scale(inst.scale)
			node.add_child(inst)
			if node is MeshInstance3D: (node as MeshInstance3D).visible = false
			else:
				for c in node.get_children():
					if c is MeshInstance3D and c != inst: (c as MeshInstance3D).visible = false
			counts.parts += 1
		if node is MeshInstance3D and not materials.is_empty():
			var mi := node as MeshInstance3D
			if mi.mesh == null: continue
			for i in mi.mesh.get_surface_count():
				var m := mi.mesh.surface_get_material(i)
				if m != null and materials.has(m.resource_name):
					mi.set_surface_override_material(i, materials[m.resource_name])
					counts.surfaces += 1
	return counts

static func _all(n: Node) -> Array:
	var out := [n]
	for c in n.get_children():
		out.append_array(_all(c))
	return out
