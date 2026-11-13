## A sliding door for BadShip interiors. Attach it to a node whose glTF extras say type "door" (interior_setup.gd
## does that for every door when a ship's interior is loaded or imported). The door reads its size and slide axis
## from the extras, slides sideways into the wall when something enters its sensor, and closes when it is clear.
## Outer airlock doors ("kind": "airlock_outer") stay shut until open() is called by your game code, and elevator
## doors ("kind": "elevator") until the car arrives (elevator.gd opens them).
extends Node3D

signal opened
signal closed

@export var open_time := 0.35
var width := 1.2
var height := 2.2
var axis := Vector3.RIGHT
var kind := "interior"
var is_open := false
var _closed_pos: Vector3
var _sensor_count := 0

func _ready() -> void:
	var extras: Dictionary = get_meta("extras", {})
	width = float(extras.get("width", width))
	height = float(extras.get("height", height))
	kind = str(extras.get("kind", kind))
	axis = Vector3.RIGHT if str(extras.get("slide_axis", "x")) == "x" else Vector3.BACK
	_closed_pos = position
	# A solid leaf so it blocks while closed.
	var body := StaticBody3D.new()
	body.name = "leaf_body"
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(width, height, 0.12) if axis == Vector3.RIGHT else Vector3(0.12, height, width)
	shape.shape = box
	body.add_child(shape)
	add_child(body)
	if kind not in ["airlock_outer", "elevator"]:
		var sensor := Area3D.new()
		sensor.name = "sensor"
		var sshape := CollisionShape3D.new()
		var sbox := BoxShape3D.new()
		sbox.size = Vector3(width + 1.0, height, 3.0) if axis == Vector3.RIGHT else Vector3(3.0, height, width + 1.0)
		sshape.shape = sbox
		sensor.add_child(sshape)
		add_child(sensor)
		sensor.body_entered.connect(func(_b): _sensor_count += 1; open())
		sensor.body_exited.connect(func(_b): _sensor_count = max(0, _sensor_count - 1); if _sensor_count == 0: close())

func open() -> void:
	if is_open: return
	is_open = true
	_slide(_closed_pos + axis * (width - 0.1))
	opened.emit()

func close() -> void:
	if not is_open: return
	is_open = false
	_slide(_closed_pos)
	closed.emit()

func _slide(target: Vector3) -> void:
	if not is_inside_tree():
		position = target
		return
	var t := create_tween()
	t.tween_property(self, "position", target, open_time).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
