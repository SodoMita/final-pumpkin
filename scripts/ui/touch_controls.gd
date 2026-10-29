extends CanvasLayer
## On-screen controls for phones and tablets.
##
## Left side: a d-pad that writes analog values into `VirtualInput`
## (up/down = forward/back, left/right = turn).
## Right side: action buttons that press the very same InputMap actions the
## keyboard and gamepad use, so rebinding applies to touch as well.
##
## The overlay is built in code, so this single file is the whole thing.

@export var pad_margin := 26.0
@export var pad_size := 230.0
@export var button_diameter := 88.0

var _show_mode := "auto"
var _ui_scale := 1.0
var _opacity := 0.55

var _root: Control
var _pad: DPad
var _pad_knob: Panel
var _buttons: GridContainer
var _pad_directions: Dictionary = {}


func _ready() -> void:
	layer = 10
	_build()
	get_viewport().size_changed.connect(_layout)
	Settings.settings_changed.connect(_apply_settings)
	_apply_settings()


func _apply_settings() -> void:
	_show_mode = Settings.touch_controls_mode
	_ui_scale = Settings.touch_controls_scale
	_opacity = Settings.touch_controls_opacity
	visible = _should_show()
	_layout()


func _should_show() -> bool:
	match _show_mode:
		"always":
			return true
		"never":
			return false
		_:
			return DisplayServer.is_touchscreen_available() or OS.has_feature("mobile")


func _layout() -> void:
	if _root == null:
		return
	var view := get_viewport().get_visible_rect().size
	var margin := pad_margin * _ui_scale
	var pad_side := pad_size * _ui_scale

	_root.modulate.a = _opacity

	_pad.size = Vector2(pad_side, pad_side)
	_pad.position = Vector2(margin, view.y - pad_side - margin)
	_pad_knob.size = Vector2(84, 84) * _ui_scale
	_pad_knob.pivot_offset = _pad_knob.size * 0.5
	_pad_knob.position = _pad.size * 0.5 - _pad_knob.size * 0.5

	var diameter := button_diameter * _ui_scale
	for child in _buttons.get_children():
		(child as Control).custom_minimum_size = Vector2(diameter, diameter)
		(child as Control).queue_redraw()

	var separation := 14.0 * _ui_scale
	_buttons.add_theme_constant_override("h_separation", int(separation))
	_buttons.add_theme_constant_override("v_separation", int(separation))
	var buttons_size := Vector2(diameter * 2.0 + separation, diameter * 2.0 + separation)
	_buttons.size = buttons_size
	_buttons.position = Vector2(view.x - buttons_size.x - margin, view.y - buttons_size.y - margin)


func _build() -> void:
	_root = Control.new()
	_root.name = "TouchRoot"
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)

	_pad = DPad.new()
	_pad.name = "DPad"
	_pad.mouse_filter = Control.MOUSE_FILTER_STOP
	_pad.direction_changed.connect(_on_direction)
	_root.add_child(_pad)

	_pad_knob = _make_round(Color(1, 1, 1, 0.22), 84.0)
	_pad.add_child(_pad_knob)

	_buttons = GridContainer.new()
	_buttons.name = "Buttons"
	_buttons.columns = 2
	_buttons.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(_buttons)

	_add_action_button("aim", "AIM", Color(0.78, 0.74, 0.45, 0.5))
	_add_action_button("run", "RUN", Color(0.52, 0.6, 0.78, 0.5))
	_add_action_button("action", "ACT", Color(0.82, 0.5, 0.46, 0.55))
	_add_action_button("pause", "II", Color(0.6, 0.6, 0.6, 0.45))


func _add_action_button(action: String, label_text: String, tint: Color) -> void:
	var button := TouchActionButton.new()
	button.action = StringName(action)
	button.label = label_text
	button.tint = tint
	_buttons.add_child(button)


func _make_round(color: Color, diameter: float) -> Panel:
	var panel := Panel.new()
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.set_corner_radius_all(int(diameter * 0.5))
	style.anti_aliasing = true
	panel.add_theme_stylebox_override("panel", style)
	return panel


func _on_direction(direction: String, active: bool) -> void:
	_pad_directions[direction] = active
	var move := (1.0 if _pad_directions.get("up", false) else 0.0) \
		- (1.0 if _pad_directions.get("down", false) else 0.0)
	var turn := (1.0 if _pad_directions.get("left", false) else 0.0) \
		- (1.0 if _pad_directions.get("right", false) else 0.0)
	VirtualInput.move = clampf(move, -1.0, 1.0)
	VirtualInput.turn = clampf(turn, -1.0, 1.0)

	var reach := _pad.size.x * 0.28
	var knob_offset := Vector2(-turn, -move) * reach
	var tween := create_tween()
	tween.tween_property(_pad_knob, "position",
		_pad.size * 0.5 - _pad_knob.size * 0.5 + knob_offset, 0.08)


# ---------------------------------------------------------------------------
# Touch widgets
# ---------------------------------------------------------------------------

class DPad:
	extends Control
	signal direction_changed(direction: String, active: bool)

	var _touches: Dictionary = {}   # pointer index -> direction
	var _mouse_down := false

	func _gui_input(event: InputEvent) -> void:
		if event is InputEventScreenTouch:
			_update(event.index, event.position, event.pressed)
			accept_event()
		elif event is InputEventScreenDrag:
			_update(event.index, event.position, true)
			accept_event()
		elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
			_mouse_down = event.pressed
			_update(0, event.position, event.pressed)
			accept_event()
		elif event is InputEventMouseMotion and _mouse_down:
			_update(0, event.position, true)
			accept_event()

	func _update(index: int, local: Vector2, down: bool) -> void:
		var before := _touches.duplicate()
		if down:
			_touches[index] = _direction_at(local)
		else:
			_touches.erase(index)
		_emit(before)

	func _direction_at(local: Vector2) -> String:
		var offset := local - size * 0.5
		if offset.length() < size.x * 0.10:
			return ""
		if absf(offset.x) > absf(offset.y):
			return "left" if offset.x < 0.0 else "right"
		return "up" if offset.y < 0.0 else "down"

	func _emit(before: Dictionary) -> void:
		var now := {"up": false, "down": false, "left": false, "right": false}
		for dir in _touches.values():
			if now.has(dir):
				now[dir] = true
		var was := {"up": false, "down": false, "left": false, "right": false}
		for dir in before.values():
			if was.has(dir):
				was[dir] = true
		for dir in now.keys():
			if now[dir] != was[dir]:
				direction_changed.emit(dir, now[dir])


class TouchActionButton:
	extends Control
	## Round button that holds an InputMap action for as long as it is touched.

	var action: StringName = &"action"
	var label := ""
	var tint := Color(0.8, 0.8, 0.8, 0.5)

	var _pointers: Dictionary = {}

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP

	func _gui_input(event: InputEvent) -> void:
		if event is InputEventScreenTouch:
			_set_pointer("touch_%d" % event.index, event.pressed)
			accept_event()
		elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
			_set_pointer("mouse", event.pressed)
			accept_event()

	func _set_pointer(key: String, down: bool) -> void:
		var was := not _pointers.is_empty()
		if down:
			_pointers[key] = true
		else:
			_pointers.erase(key)
		var now := not _pointers.is_empty()
		if now == was:
			return
		if now:
			Input.action_press(action)
			if Settings.vibration and OS.has_feature("mobile"):
				Input.vibrate_handheld(20)
		else:
			Input.action_release(action)
		queue_redraw()

	func _draw() -> void:
		var radius := minf(size.x, size.y) * 0.5
		if radius <= 0.0:
			return
		var center := size * 0.5
		var fill := tint.lightened(0.3) if not _pointers.is_empty() else tint
		draw_circle(center, radius, fill)
		draw_arc(center, radius - 2.0, 0.0, TAU, 48, Color(1, 1, 1, 0.3), 2.0, true)
		var font := get_theme_default_font()
		if font == null:
			return
		var font_size := int(maxf(radius * 0.45, 10.0))
		var text_size := font.get_string_size(label, HORIZONTAL_ALIGNMENT_CENTER, -1, font_size)
		draw_string(font, center - text_size * 0.5 + Vector2(0, text_size.y * 0.32), label,
			HORIZONTAL_ALIGNMENT_CENTER, -1, font_size, Color(1, 1, 1, 0.9))
