extends CanvasLayer
## In-game pause overlay: Resume, Options, Main Menu.
##
## The scene tree is paused while this is open, so the player stops dead.

const OPTIONS_SCENE := preload("res://scenes/ui/options_menu.tscn")
const MENU_SCENE := "res://scenes/main_menu.tscn"

@onready var _root: Control = $Root
@onready var _buttons: VBoxContainer = $Root/Center/Panel/Column/Buttons

var _options: Control


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_root.visible = false
	_build_buttons()


func _build_buttons() -> void:
	for child in _buttons.get_children():
		child.queue_free()
	_add_button("Resume", close)
	_add_button("Options", _open_options)
	_add_button("Main Menu", _to_main_menu)


func _add_button(text: String, handler: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(320, 52)
	button.pressed.connect(handler)
	_buttons.add_child(button)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause"):
		if _options != null and is_instance_valid(_options):
			return
		get_viewport().set_input_as_handled()
		if is_open():
			close()
		else:
			open()


func is_open() -> bool:
	return _root.visible


func open() -> void:
	VirtualInput.reset()
	_root.visible = true
	get_tree().paused = true
	(_buttons.get_child(0) as Button).grab_focus()


func close() -> void:
	_root.visible = false
	get_tree().paused = false
	if _options != null and is_instance_valid(_options):
		_options.queue_free()
		_options = null


func _open_options() -> void:
	if _options != null and is_instance_valid(_options):
		return
	_options = OPTIONS_SCENE.instantiate()
	_options.closed.connect(func() -> void:
		_options = null
		(_buttons.get_child(0) as Button).grab_focus())
	_root.add_child(_options)


func _to_main_menu() -> void:
	get_tree().paused = false
	VirtualInput.reset()
	get_tree().change_scene_to_file(MENU_SCENE)
