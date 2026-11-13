## Set this as the import script of an .interior.glb (Import dock > Import Script) to get working doors and
## collision straight from the editor import.
@tool
extends EditorScenePostImport

func _post_import(scene: Node) -> Object:
	InteriorSetup.apply(scene)
	return scene
