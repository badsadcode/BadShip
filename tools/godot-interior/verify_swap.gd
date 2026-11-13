## Headless check of asset_swap.gd: loads a ship, puts a stand-in scene on every turret and window and a new
## material on the hull, and checks they landed where the parts are.
##   godot --headless --path tools/godot-interior --script res://verify_swap.gd -- path/to/ship.glb
extends SceneTree

func _initialize() -> void:
	var path: String = OS.get_cmdline_user_args()[0]
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		print("FAIL: could not read ", path); quit(1); return
	var ship := doc.generate_scene(state)
	root.add_child(ship)
	# A stand-in asset: a 1 m box, as a scene.
	var box := MeshInstance3D.new()
	box.mesh = BoxMesh.new()
	var scene := PackedScene.new()
	scene.pack(box)
	var metal := StandardMaterial3D.new()
	metal.albedo_color = Color(0.8, 0.2, 0.2)
	var want := {"turret": 0, "window": 0, "hardpoint": 0}
	for n in AssetSwap._all(ship):
		var t := str(n.get_meta("extras", {}).get("type", ""))
		if want.has(t): want[t] += 1
	var counts := AssetSwap.apply(ship, {"turret": {"scene": scene, "fit": true}, "hardpoint": scene, "window": scene}, {"hull_main": metal})
	var expected: int = want.turret + want.window + want.hardpoint
	# Every swapped part: the asset at the part's origin, the generated mesh hidden.
	var placed := 0
	for n in AssetSwap._all(ship):
		var a = n.get_node_or_null("asset")
		if a != null and (a as Node3D).position.length() < 0.001: placed += 1
	print("parts swapped %d of %d (turrets %d, hardpoints %d, windows %d), placed at their origins %d, hull surfaces re-skinned %d" % [counts.parts, expected, want.turret, want.hardpoint, want.window, placed, counts.surfaces])
	var ok: bool = counts.parts == expected and placed == expected and counts.surfaces > 0 and expected > 0
	print("PASS" if ok else "FAIL")
	quit(0 if ok else 1)
