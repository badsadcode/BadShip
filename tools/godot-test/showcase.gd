# Renders imported ships with Godot's own renderer (for a picture of them inside the engine).
#   xvfb-run godot --path . --rendering-driver opengl3 --resolution 1600x900 --script res://showcase.gd -- ship_0001 ship_0005 ...
extends SceneTree

func _initialize() -> void:
	var names := OS.get_cmdline_user_args()
	RenderingServer.set_default_clear_color(Color("#121821"))
	var world := Node3D.new()
	root.add_child(world)

	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color("#121821")
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color("#8ea3bd")
	env.ambient_light_energy = 0.35
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.glow_enabled = true
	env.glow_intensity = 0.6
	var we := WorldEnvironment.new()
	we.environment = env
	world.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.light_energy = 2.0
	sun.rotation_degrees = Vector3(-38, -130, 0)
	world.add_child(sun)
	var rim := DirectionalLight3D.new()
	rim.light_color = Color("#7fa8ff")
	rim.light_energy = 0.6
	rim.rotation_degrees = Vector3(-15, 40, 0)
	world.add_child(rim)

	# Ships side by side across X (bows towards +Z), gaps proportional to their size.
	var x := 0.0
	var bounds := AABB()
	for i in names.size():
		var scene: PackedScene = load("res://ships/%s.glb" % names[i])
		var ship: Node3D = scene.instantiate()
		var markers := ship.find_child("markers", true, false)
		if markers:
			markers.queue_free()
		world.add_child(ship)
		var box := aabb_of(ship)
		ship.position = Vector3(x - box.position.x, -box.position.y, -box.get_center().z)
		x += box.size.x + maxf(12.0, box.size.z * 0.12)
		var placed := AABB(ship.position + box.position, box.size)
		bounds = placed if i == 0 else bounds.merge(placed)

	var cam := Camera3D.new()
	cam.fov = 32
	cam.environment = env  # the camera's own environment wins over any default one
	var center := bounds.get_center()
	var dist := bounds.size.length() * 0.95
	world.add_child(cam)
	cam.look_at_from_position(center + Vector3(-0.55, 0.32, 1.0).normalized() * dist, center)
	cam.current = true

	for f in 8:
		await process_frame
	var img := root.get_viewport().get_texture().get_image()
	img.save_png("res://godot_render.png")
	print("saved res://godot_render.png ", img.get_size())
	quit()

func aabb_of(node: Node) -> AABB:
	var out := []
	collect(node, Transform3D.IDENTITY, out)
	var box := AABB()
	for i in out.size():
		box = out[i] if i == 0 else box.merge(out[i])
	return box

func collect(node: Node, xform: Transform3D, out: Array) -> void:
	var t := xform
	if node is Node3D:
		t = xform * (node as Node3D).transform
	if node is MeshInstance3D and (node as MeshInstance3D).mesh:
		out.append(t * (node as MeshInstance3D).mesh.get_aabb())
	for c in node.get_children():
		collect(c, t, out)
