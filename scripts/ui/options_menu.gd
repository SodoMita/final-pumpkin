extends Control
## Options screen: display, audio, controls (with rebinding), touch, system.
##
## Every row is built from a small table in `_build_*_tab()`, so adding a new
## option is one line. Changes are written to disk immediately.

signal closed

const TABS := ["Display", "Audio", "Controls", "Touch", "System"]

const FPS_CHOICES := [
	["Unlimited", 0], ["30", 30], ["60", 60], ["72", 72],
	["90", 90], ["120", 120], ["144", 144], ["240", 240],
]
const MSAA_CHOICES := [["Off", 0], ["2x", 1], ["4x", 2], ["8x", 3]]
const TOUCH_MODE_CHOICES := [["Automatic", "auto"], ["Always On", "always"], ["Never", "never"]]
const LANGUAGE_CHOICES := [["English", "en"]]

var _tab := 0
var _capture_action: StringName = &""
var _capture_slot := -1

var _tabs: HBoxContainer
var _content: VBoxContainer
var _scroll: ScrollContainer
var _hint: Label
var _back: Button


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	set_anchors_preset(Control.PRESET_FULL_RECT)
	_build_shell()
	_refresh()


func _build_shell() -> void:
	var background := ColorRect.new()
	background.color = Color(0.05, 0.055, 0.065, 0.97)
	background.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(background)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 32)
	add_child(margin)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 18)
	margin.add_child(column)

	var title := Label.new()
	title.text = "OPTIONS"
	title.add_theme_font_size_override("font_size", 34)
	column.add_child(title)

	_tabs = HBoxContainer.new()
	_tabs.add_theme_constant_override("separation", 8)
	column.add_child(_tabs)
	for i in TABS.size():
		var button := Button.new()
		button.text = TABS[i]
		button.toggle_mode = true
		button.custom_minimum_size = Vector2(150, 44)
		button.pressed.connect(_select_tab.bind(i))
		_tabs.add_child(button)

	_scroll = ScrollContainer.new()
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	column.add_child(_scroll)

	_content = VBoxContainer.new()
	_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_content.add_theme_constant_override("separation", 10)
	_scroll.add_child(_content)

	_hint = Label.new()
	_hint.add_theme_color_override("font_color", Color(0.75, 0.78, 0.85))
	column.add_child(_hint)

	_back = Button.new()
	_back.text = "Back"
	_back.custom_minimum_size = Vector2(180, 48)
	_back.pressed.connect(_close)
	column.add_child(_back)


func _select_tab(index: int) -> void:
	_tab = clampi(index, 0, TABS.size() - 1)
	_capture_action = &""
	_refresh()


func _refresh() -> void:
	for i in _tabs.get_child_count():
		(_tabs.get_child(i) as Button).set_pressed_no_signal(i == _tab)

	for child in _content.get_children():
		_content.remove_child(child)
		child.queue_free()

	match _tab:
		0: _build_display_tab()
		1: _build_audio_tab()
		2: _build_controls_tab()
		3: _build_touch_tab()
		4: _build_system_tab()

	_set_hint("Changes are saved automatically.")
	_focus_first()


## Puts keyboard / gamepad focus on the first control so the menu is usable
## without a mouse.
func _focus_first() -> void:
	if get_viewport().gui_get_focus_owner() != null:
		return
	var first := _find_focusable(_content)
	if first != null:
		first.grab_focus()


func _find_focusable(node: Node) -> Control:
	for child in node.get_children():
		if child is Control and (child as Control).focus_mode != Control.FOCUS_NONE:
			return child
		var nested := _find_focusable(child)
		if nested != null:
			return nested
	return null


func _set_hint(text: String) -> void:
	_hint.text = text


func _close() -> void:
	_capture_action = &""
	closed.emit()
	queue_free()


func _unhandled_input(event: InputEvent) -> void:
	if _capture_action != &"":
		return
	if event.is_action_pressed("pause") or event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_close()


# ---------------------------------------------------------------------------
# Tabs
# ---------------------------------------------------------------------------

func _build_display_tab() -> void:
	_add_option_row("Display Mode", Settings.DISPLAY_MODES,
		_index_of(Settings.DISPLAY_MODES, Settings.display_mode),
		func(value: Variant) -> void:
			Settings.display_mode = str(value)
			_settings_changed(true))

	var resolution_index := 0
	for i in Settings.RESOLUTION_PRESETS.size():
		if Settings.RESOLUTION_PRESETS[i][1] == Settings.window_width \
				and Settings.RESOLUTION_PRESETS[i][2] == Settings.window_height:
			resolution_index = i
	_add_option_row("Resolution", _labeled(Settings.RESOLUTION_PRESETS), resolution_index,
		func(index: int) -> void:
			Settings.window_width = Settings.RESOLUTION_PRESETS[index][1]
			Settings.window_height = Settings.RESOLUTION_PRESETS[index][2]
			_settings_changed(true))

	_add_check_row("Vertical Sync", Settings.vsync,
		func(value: bool) -> void:
			Settings.vsync = value
			_settings_changed())

	_add_option_row("FPS Limit", FPS_CHOICES, _index_of(FPS_CHOICES, Settings.max_fps),
		func(index: int) -> void:
			Settings.max_fps = int(FPS_CHOICES[index][1])
			_settings_changed())

	_add_slider_row("Render Scale", 0.5, 2.0, 0.05, Settings.resolution_scale,
		func(value: float) -> void:
			Settings.resolution_scale = value
			_settings_changed(),
		"x")

	_add_option_row("Anti-aliasing (MSAA)", MSAA_CHOICES, Settings.msaa,
		func(index: int) -> void:
			Settings.msaa = index
			_settings_changed())

	_add_check_row("Anti-aliasing (FXAA)", Settings.fxaa,
		func(value: bool) -> void:
			Settings.fxaa = value
			_settings_changed())


func _build_audio_tab() -> void:
	_add_slider_row("Master Volume", 0.0, 1.0, 0.01, Settings.master_volume,
		func(value: float) -> void:
			Settings.master_volume = value
			_settings_changed(), "%")
	_add_slider_row("Music Volume", 0.0, 1.0, 0.01, Settings.music_volume,
		func(value: float) -> void:
			Settings.music_volume = value
			_settings_changed(), "%")
	_add_slider_row("Effects Volume", 0.0, 1.0, 0.01, Settings.sfx_volume,
		func(value: float) -> void:
			Settings.sfx_volume = value
			_settings_changed(), "%")
	_add_slider_row("Voice Volume", 0.0, 1.0, 0.01, Settings.voice_volume,
		func(value: float) -> void:
			Settings.voice_volume = value
			_settings_changed(), "%")
	_add_check_row("Mute When Unfocused", Settings.muffle_when_unfocused,
		func(value: bool) -> void:
			Settings.muffle_when_unfocused = value
			_settings_changed())


func _build_controls_tab() -> void:
	_add_slider_row("Turn Speed", 0.5, 5.0, 0.1, Settings.turn_speed,
		func(value: float) -> void:
			Settings.turn_speed = value
			_settings_changed(), " rad/s")
	_add_slider_row("Aim Turn Speed", 0.5, 4.0, 0.1, Settings.aim_turn_speed,
		func(value: float) -> void:
			Settings.aim_turn_speed = value
			_settings_changed(), " rad/s")
	_add_slider_row("Walk Speed", 1.0, 6.0, 0.1, Settings.walk_speed,
		func(value: float) -> void:
			Settings.walk_speed = value
			_settings_changed(), " m/s")
	_add_slider_row("Run Speed", 2.0, 10.0, 0.1, Settings.run_speed,
		func(value: float) -> void:
			Settings.run_speed = value
			_settings_changed(), " m/s")
	_add_check_row("Invert Turning", Settings.invert_turn,
		func(value: bool) -> void:
			Settings.invert_turn = value
			_settings_changed())
	_add_check_row("Sprint While Holding Forward", Settings.simultaneous_run,
		func(value: bool) -> void:
			Settings.simultaneous_run = value
			_settings_changed())

	_add_header("KEYBOARD / GAMEPAD BINDINGS")

	var actions: Array = Settings.REBINDABLE_ACTIONS.keys()
	for action: String in actions:
		_build_binding_row(action)

	var reset_all := Button.new()
	reset_all.text = "Reset All Bindings"
	reset_all.pressed.connect(func() -> void:
		Settings.reset_all_actions()
		Settings.save()
		_set_hint("All bindings restored to defaults.")
		_refresh())
	_content.add_child(reset_all)


func _build_touch_tab() -> void:
	_add_option_row("Show Touch Controls", TOUCH_MODE_CHOICES,
		_index_of(TOUCH_MODE_CHOICES, Settings.touch_controls_mode),
		func(index: int) -> void:
			Settings.touch_controls_mode = str(TOUCH_MODE_CHOICES[index][1])
			_settings_changed())
	_add_slider_row("Overlay Size", 0.6, 1.8, 0.05, Settings.touch_controls_scale,
		func(value: float) -> void:
			Settings.touch_controls_scale = value
			_settings_changed(), "x")
	_add_slider_row("Overlay Opacity", 0.2, 1.0, 0.01, Settings.touch_controls_opacity,
		func(value: float) -> void:
			Settings.touch_controls_opacity = value
			_settings_changed(), "%")
	_add_check_row("Vibration", Settings.vibration,
		func(value: bool) -> void:
			Settings.vibration = value
			_settings_changed())
	_add_header("NOTES")
	_add_paragraph("The left pad always drives tank controls: up/down moves along the "
		+ "character's facing, left/right turns it. The buttons on the right press the "
		+ "same actions you bind in the Controls tab.")


func _build_system_tab() -> void:
	_add_option_row("Language", LANGUAGE_CHOICES,
		_index_of(LANGUAGE_CHOICES, Settings.language),
		func(index: int) -> void:
			Settings.language = str(LANGUAGE_CHOICES[index][1])
			_settings_changed())
	_add_check_row("Skip Main Menu On Start", Settings.skip_menu_on_start,
		func(value: bool) -> void:
			Settings.skip_menu_on_start = value
			_settings_changed())

	_add_header("RESET")
	var reset := Button.new()
	reset.text = "Reset All Settings To Defaults"
	reset.pressed.connect(func() -> void:
		Settings.reset_to_defaults()
		_set_hint("Everything restored to defaults.")
		_refresh())
	_content.add_child(reset)

	_add_paragraph("Defaults live in the script that owns them: display, audio and "
		+ "touch in scripts/autoload/settings.gd, bindings in project.godot.")


# ---------------------------------------------------------------------------
# Binding rows
# ---------------------------------------------------------------------------

func _build_binding_row(action: String) -> void:
	var row := _make_row(str(Settings.REBINDABLE_ACTIONS[action]))
	var buttons := HBoxContainer.new()
	buttons.add_theme_constant_override("separation", 6)
	row.add_child(buttons)

	var events := Settings.get_action_events(StringName(action))
	for i in events.size():
		var slot := Button.new()
		slot.custom_minimum_size = Vector2(130, 38)
		if _capture_action == StringName(action) and _capture_slot == i:
			slot.text = "Press..."
		else:
			slot.text = _event_text(events[i])
		slot.pressed.connect(_start_capture.bind(action, i))
		buttons.add_child(slot)

	var add := Button.new()
	add.text = "+"
	add.custom_minimum_size = Vector2(38, 38)
	add.pressed.connect(func() -> void:
		Settings.add_action_event(StringName(action), _first_event(action))
		Settings.save()
		_refresh())
	buttons.add_child(add)

	var remove := Button.new()
	remove.text = "-"
	remove.custom_minimum_size = Vector2(38, 38)
	remove.disabled = events.size() <= 1
	remove.pressed.connect(func() -> void:
		Settings.remove_last_action_event(StringName(action))
		Settings.save()
		_refresh())
	buttons.add_child(remove)

	var reset := Button.new()
	reset.text = "Reset"
	reset.custom_minimum_size = Vector2(84, 38)
	reset.pressed.connect(func() -> void:
		Settings.reset_action(StringName(action))
		Settings.save()
		_refresh())
	buttons.add_child(reset)


## Keyboard binding used when a slot is added by hand.
func _first_event(action: String) -> InputEvent:
	var events := Settings.get_default_events(StringName(action))
	if not events.is_empty():
		return events[0].duplicate()
	return InputEventKey.new()


func _start_capture(action: String, slot: int) -> void:
	_capture_action = StringName(action)
	_capture_slot = slot
	_set_hint("Press a key, a gamepad button or push a stick. Esc cancels.")
	_refresh()


func _input(event: InputEvent) -> void:
	if _capture_action == &"":
		return

	if event is InputEventKey and event.pressed and not event.echo \
			and (event.keycode == KEY_ESCAPE or event.physical_keycode == KEY_ESCAPE):
		_cancel_capture()
		get_viewport().set_input_as_handled()
		return

	if not _is_bindable(event):
		return

	get_viewport().set_input_as_handled()
	var binding := _clean_event(event)
	var conflict := Settings.find_action_for_event(binding, _capture_action)
	Settings.replace_action_event(_capture_action, _capture_slot, binding)
	Settings.save()
	var message := "Bound to %s." % _event_text(binding)
	if conflict != &"":
		message += "  Warning: also used by \"%s\"." % str(Settings.REBINDABLE_ACTIONS[conflict])
	_capture_action = &""
	_capture_slot = -1
	_refresh()
	_set_hint(message)


func _cancel_capture() -> void:
	_capture_action = &""
	_capture_slot = -1
	_refresh()
	_set_hint("Rebinding cancelled.")


func _is_bindable(event: InputEvent) -> bool:
	if event is InputEventKey:
		return event.pressed and not event.echo
	if event is InputEventJoypadButton:
		return event.pressed
	if event is InputEventJoypadMotion:
		return absf(event.axis_value) > 0.6
	if event is InputEventMouseButton:
		return event.pressed
	return false


func _clean_event(event: InputEvent) -> InputEvent:
	var binding: InputEvent = event.duplicate()
	if binding is InputEventKey or binding is InputEventJoypadButton or binding is InputEventMouseButton:
		binding.pressed = false
	if binding is InputEventJoypadMotion:
		binding.axis_value = signf(binding.axis_value)
	return binding


func _event_text(event: InputEvent) -> String:
	if event is InputEventKey:
		var key := event as InputEventKey
		var code: Key = key.physical_keycode if key.physical_keycode != KEY_NONE else key.keycode
		var text := OS.get_keycode_string(code)
		return text if text != "" else "Key %d" % code
	if event is InputEventJoypadButton:
		var button := event as InputEventJoypadButton
		return "Pad %s" % _joy_button_name(button.button_index)
	if event is InputEventJoypadMotion:
		var motion := event as InputEventJoypadMotion
		var stick := "L" if motion.axis == JOY_AXIS_LEFT_X or motion.axis == JOY_AXIS_LEFT_Y else "R"
		var direction := ""
		if motion.axis == JOY_AXIS_LEFT_X or motion.axis == JOY_AXIS_RIGHT_X:
			direction = "Right" if motion.axis_value > 0.0 else "Left"
		else:
			direction = "Down" if motion.axis_value > 0.0 else "Up"
		return "Stick %s %s" % [stick, direction]
	if event is InputEventMouseButton:
		return "Mouse %d" % (event as InputEventMouseButton).button_index
	return event.as_text()


func _joy_button_name(button: int) -> String:
	match button:
		JOY_BUTTON_A: return "A"
		JOY_BUTTON_B: return "B"
		JOY_BUTTON_X: return "X"
		JOY_BUTTON_Y: return "Y"
		JOY_BUTTON_LEFT_SHOULDER: return "LB"
		JOY_BUTTON_RIGHT_SHOULDER: return "RB"
		JOY_BUTTON_BACK: return "Back"
		JOY_BUTTON_START: return "Start"
		JOY_BUTTON_LEFT_STICK: return "L3"
		JOY_BUTTON_RIGHT_STICK: return "R3"
		JOY_BUTTON_DPAD_UP: return "D-Up"
		JOY_BUTTON_DPAD_DOWN: return "D-Down"
		JOY_BUTTON_DPAD_LEFT: return "D-Left"
		JOY_BUTTON_DPAD_RIGHT: return "D-Right"
		_: return str(button)


# ---------------------------------------------------------------------------
# Row helpers
# ---------------------------------------------------------------------------

func _add_header(text: String) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", 18)
	label.add_theme_color_override("font_color", Color(0.85, 0.68, 0.42))
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 10)
	_content.add_child(spacer)
	_content.add_child(label)


func _add_paragraph(text: String) -> void:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_color_override("font_color", Color(0.62, 0.65, 0.7))
	_content.add_child(label)


func _make_row(text: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(320, 0)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	row.add_child(label)
	_content.add_child(row)
	return row


func _add_option_row(text: String, choices: Array, selected: int, on_change: Callable) -> void:
	var row := _make_row(text)
	var option := OptionButton.new()
	option.custom_minimum_size = Vector2(230, 38)
	for i in choices.size():
		var entry: Variant = choices[i]
		var label_text := str(entry[0]) if entry is Array else str(entry)
		option.add_item(label_text, i)
	option.selected = clampi(selected, 0, maxi(choices.size() - 1, 0))
	option.item_selected.connect(func(index: int) -> void: on_change.call(index))
	row.add_child(option)


func _add_slider_row(text: String, min_value: float, max_value: float, step: float,
		value: float, on_change: Callable, suffix := "") -> void:
	var row := _make_row(text)
	var value_label := Label.new()
	value_label.custom_minimum_size = Vector2(90, 0)
	value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value_label.text = _format_value(value, suffix)

	var slider := HSlider.new()
	slider.custom_minimum_size = Vector2(300, 32)
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	slider.min_value = min_value
	slider.max_value = max_value
	slider.step = step
	slider.value = clampf(value, min_value, max_value)
	slider.value_changed.connect(func(new_value: float) -> void:
		value_label.text = _format_value(new_value, suffix)
		on_change.call(new_value))

	row.add_child(slider)
	row.add_child(value_label)


func _add_check_row(text: String, value: bool, on_change: Callable) -> void:
	var row := _make_row(text)
	var check := CheckButton.new()
	check.button_pressed = value
	check.toggled.connect(func(pressed: bool) -> void: on_change.call(pressed))
	row.add_child(check)


func _format_value(value: float, suffix: String) -> String:
	if suffix == "%":
		return "%d%%" % roundi(value * 100.0)
	return "%.2f%s" % [value, suffix]


func _labeled(presets: Array) -> Array:
	var out: Array = []
	for preset: Array in presets:
		out.append([str(preset[0])])
	return out


func _index_of(choices: Array, value: Variant) -> int:
	for i in choices.size():
		var entry: Variant = choices[i]
		var candidate: Variant = entry[1] if entry is Array and entry.size() > 1 else entry
		if candidate == value:
			return i
	return 0


func _settings_changed(_window_changed: bool = false) -> void:
	Settings.apply_all()
	Settings.save()
