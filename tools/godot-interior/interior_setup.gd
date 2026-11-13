## Turns a loaded BadShip interior into something you can walk around in: every node whose extras say type "door"
## gets door.gd; walls, floors, ceilings and stairs get trimesh collision. Use it at runtime (InteriorSetup.apply(root))
## or from an import script (see post_import.gd).
class_name InteriorSetup

const DOOR := preload("res://door.gd")

static func apply(root: Node) -> Dictionary:
	var counts := {"doors": 0, "colliders": 0, "rooms": 0}
	for node in _all(root):
		var extras: Dictionary = node.get_meta("extras", {})
		var type := str(extras.get("type", ""))
		if type == "door":
			node.set_script(DOOR)
			counts.doors += 1
		elif type == "room":
			counts.rooms += 1
		elif type in ["walls", "deck", "ceiling", "stairs", "railing"] and node is MeshInstance3D:
			(node as MeshInstance3D).create_trimesh_collision()
			counts.colliders += 1
	return counts

static func _all(n: Node) -> Array:
	var out := [n]
	for c in n.get_children():
		out.append_array(_all(c))
	return out
