extends Node3D
## Fixed / cinematic camera rig.
##
## Cameras are just children of this node - place as many `Camera3D`s as you
## like and the rig picks the enabled ones. `swap_shoulder` cycles through them,
## which is how you cut between fixed angles in this style of game.
##
## No camera ever follows the mouse: the character is steered on its own axis.

## Node the cameras should keep in frame.
@export var target_path: NodePath
## Height on the target the cameras look at.
@export var look_height := 1.1
## When true every camera re-aims at the target each frame (smooth pan).
## When false the cameras keep the rotation authored in the scene.
@export var track_target := true

## Extra horizontal view offset toggled by `swap_shoulder`.
@export var shoulder_offset := 0.0

var _cameras: Array[Camera3D] = []
var _active := 0
var _target: Node3D


func _ready() -> void:
	add_to_group(&"camera_rig")
	_target = get_node_or_null(target_path) as Node3D
	_collect(self)
	if _cameras.is_empty():
		push_warning("CameraRig has no Camera3D children.")
		return
	_activate(0)


func _collect(node: Node) -> void:
	for child in node.get_children():
		if child is Camera3D:
			_cameras.append(child)
		else:
			_collect(child)


func _process(_delta: float) -> void:
	if not track_target or _target == null:
		return
	var focus := _target.global_position + Vector3.UP * look_height
	for cam in _cameras:
		if cam.global_position.distance_squared_to(focus) > 0.001:
			cam.look_at(focus, Vector3.UP)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("swap_shoulder"):
		cycle(1)


func cycle(step: int = 1) -> void:
	if _cameras.size() < 2:
		return
	_activate(wrapi(_active + step, 0, _cameras.size()))


func _activate(index: int) -> void:
	_active = index
	for i in _cameras.size():
		_cameras[i].current = i == index
	if _cameras.size() > 0 and is_instance_valid(_cameras[index]):
		_cameras[index].h_offset = shoulder_offset


func get_active_camera() -> Camera3D:
	if _cameras.is_empty():
		return null
	return _cameras[_active]
