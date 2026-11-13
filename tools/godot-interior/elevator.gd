## An elevator car for BadShip interiors. Attach it to a node whose glTF extras say type "elevator" (interior_setup.gd
## does that). The car starts at the lowest stop; call call_to(i) to send it to stop i (0 = lowest deck). It closes the
## door where it is, rides to the new stop and opens the door there. Elevator doors ("kind": "elevator") never open by
## themselves, so nobody walks into an empty shaft. Hook call_to() up to buttons, a panel or your AI as you like.
extends Node3D

signal arrived(stop: int)

@export var speed := 2.0          ## metres per second
@export var dwell := 0.4          ## seconds between the door closing and the car moving
var stops_y: Array = []
var door_ids: Array = []
var stop := 0
var moving := false
var _base_y := 0.0
var _doors: Array = []

func _ready() -> void:
	var x: Dictionary = get_meta("extras", {})
	stops_y = x.get("stops_y", [])
	door_ids = x.get("doors", [])
	_base_y = position.y
	# The platform rides with the car, carrying whoever stands on it.
	var shaft: Array = x.get("shaft", [0, 2.4, 0, 3.2])
	var body := AnimatableBody3D.new()
	body.name = "car_body"
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(float(shaft[1]) - float(shaft[0]) - 0.24, 0.2, float(shaft[3]) - float(shaft[2]) - 0.24)
	shape.shape = box
	shape.position = Vector3(0, -0.1, 0)
	body.add_child(shape)
	add_child(body)
	_find_doors.call_deferred()

func _find_doors() -> void:
	var top := get_tree().root
	_doors.clear()
	for id in door_ids:
		_doors.append(top.find_child(str(id), true, false))
	_set_door(stop, true)

## Sends the car to stop i (0 = lowest).
func call_to(i: int) -> void:
	if moving or i == stop or i < 0 or i >= stops_y.size(): return
	moving = true
	_set_door(stop, false)
	var target := _base_y + float(stops_y[i]) - float(stops_y[0])
	var t := create_tween()
	t.tween_interval(dwell)
	t.tween_property(self, "position:y", target, absf(float(stops_y[i]) - float(stops_y[stop])) / speed).set_trans(Tween.TRANS_SINE)
	t.tween_callback(func():
		stop = i
		moving = false
		_set_door(i, true)
		arrived.emit(i))

func _set_door(i: int, open: bool) -> void:
	if i >= _doors.size() or _doors[i] == null: return
	if open: _doors[i].open()
	else: _doors[i].close()
