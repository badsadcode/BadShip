## Turns a loaded BadShip interior into something you can walk around in: every node whose extras say type "door"
## gets door.gd, every elevator car elevator.gd; walls, floors, ceilings, stairs and railings get trimesh collision;
## every ladder and elevator gets NavigationLink3D nodes (ladder: foot to top; elevator: stop to stop) so navigation
## agents path across them (your game decides how a character actually climbs or rides). Use it at runtime
## (InteriorSetup.apply(root)) or from an import script (see post_import.gd).
class_name InteriorSetup

const DOOR := preload("res://door.gd")
const ELEVATOR := preload("res://elevator.gd")

static func apply(root: Node) -> Dictionary:
	var counts := {"doors": 0, "colliders": 0, "rooms": 0, "ladders": 0, "elevators": 0, "links": 0}
	var links := []
	for node in _all(root):
		var extras: Dictionary = node.get_meta("extras", {})
		var type := str(extras.get("type", ""))
		if type == "door":
			node.set_script(DOOR)
			counts.doors += 1
		elif type == "elevator":
			node.set_script(ELEVATOR)
			counts.elevators += 1
			links.append(extras)
		elif type == "ladder":
			counts.ladders += 1
			links.append(extras)
		elif type == "room":
			counts.rooms += 1
		elif type in ["walls", "deck", "ceiling", "stairs", "railing"] and node is MeshInstance3D:
			(node as MeshInstance3D).create_trimesh_collision()
			counts.colliders += 1
	for x in links:
		var access: Array = x.get("access", [])
		for i in range(access.size() - 1):
			var link := NavigationLink3D.new()
			link.name = "%s_link_%d" % [str(x.get("kind", "lift")), counts.links]
			link.start_position = _v(access[i])
			link.end_position = _v(access[i + 1])
			link.bidirectional = true
			link.set_meta("kind", str(x.get("kind", "")))
			root.add_child(link)
			counts.links += 1
	return counts

static func _v(a) -> Vector3:
	return Vector3(float(a[0]), float(a[1]), float(a[2]))

static func _all(n: Node) -> Array:
	var out := [n]
	for c in n.get_children():
		out.append_array(_all(c))
	return out
