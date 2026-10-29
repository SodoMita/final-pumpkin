extends Control
## Main menu. Buttons only - no story, no save slots, no gameplay hooks.

const OPTIONS_SCENE := preload("res://scenes/ui/options_menu.tscn")

const GAME_SCENE := "res://scenes/level.tscn"

@onready var _buttons: VBoxContainer = $Center/Panel/Column/Buttons

var _options: Control


func _ready() -> void:
	VirtualInput.reset()
	if Settings.skip_menu_on_start:
		_start_game()
		return
	_build_buttons()
	(_buttons.get_child(0) as Button).grab_focus()


func _build_buttons() -> void:
	_add_button("Start", _start_game)
	_add_button("Options", _open_options)
	_add_button("Quit", _quit)


func _add_button(text: String, handler: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(320, 52)
	button.pressed.connect(handler)
	_buttons.add_child(button)


func _start_game() -> void:
	get_tree().paused = false
	get_tree().change_scene_to_file(GAME_SCENE)


func _open_options() -> void:
	if _options != null and is_instance_valid(_options):
		return
	_options = OPTIONS_SCENE.instantiate()
	_options.closed.connect(func() -> void:
		_options = null
		(_buttons.get_child(0) as Button).grab_focus())
	add_child(_options)


func _quit() -> void:
	get_tree().quit()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause") and (_options == null or not is_instance_valid(_options)):
		# Escape on the main menu quits on desktop, stays put on mobile.
		if not DisplayServer.is_touchscreen_available() and not OS.has_feature("mobile"):
			_quit()
