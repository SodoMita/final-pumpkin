extends Node
## Persistent game settings.
##
## Autoloaded as `Settings`. Owns the settings file, applies every setting to the
## engine, and exposes the list of remappable actions shared by the options menu.

const CONFIG_PATH := "user://settings.cfg"

const DISPLAY_MODES := [["Windowed", "windowed"], ["Fullscreen", "fullscreen"], ["Borderless", "exclusive_fullscreen"]]
const RESOLUTIONS := [
	[1280, 720], [1366, 768], [1600, 900], [1920, 1080], [2560, 1440], [3840, 2160],
]
const RESOLUTION_PRESETS := [
	["720p", 1280, 720],
	["768p", 1366, 768],
	["900p", 1600, 900],
	["1080p", 1920, 1080],
	["1440p", 2560, 1440],
	["4K", 3840, 2160],
]

## Every action the player can rebind. Keys are action names, values are
## human readable labels.
const REBINDABLE_ACTIONS := {
	"move_forward": "Forward",
	"move_back": "Back",
	"turn_left": "Turn Left",
	"turn_right": "Turn Right",
	"action": "Action",
	"run": "Run",
	"aim": "Aim",
	"pause": "Pause",
	"swap_shoulder": "Swap Shoulder",
}

const SETTINGS_VERSION := 1
const MAX_JOYPADS := 4

signal settings_changed

# --- Video ---------------------------------------------------------------
var display_mode := "windowed"
var window_width := 1280
var window_height := 720
var vsync := true
var max_fps := 0
var resolution_scale := 1.0
var msaa := 1
var fxaa := false

# --- Audio ---------------------------------------------------------------
var master_volume := 0.8
var music_volume := 0.7
var sfx_volume := 0.8
var voice_volume := 0.8
var muffle_when_unfocused := false

# --- Controls ------------------------------------------------------------
var turn_speed := 2.2
var aim_turn_speed := 1.4
var walk_speed := 2.6
var run_speed := 5.0
var invert_turn := false
var simultaneous_run := true
var vibration := true

# --- Touch controls ------------------------------------------------------
var touch_controls_mode := "auto"       # auto | always | never
var touch_controls_scale := 1.0
var touch_controls_opacity := 0.55

# --- Session -------------------------------------------------------------
var language := "en"
var skip_menu_on_start := false

var _initial := {}         # key -> factory default value
var _inputs := {}          # action -> Array[InputEvent]
var _defaults := {}        # action -> Array[InputEvent]


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_snapshot_defaults()
	_load()
	_ensure_buses()
	_apply_window()
	_apply_video()
	_apply_audio()
	_apply_input_remaps()


# ---------------------------------------------------------------------------
# Persistence
# ---------------------------------------------------------------------------

func save() -> void:
	var cfg := ConfigFile.new()
	cfg.set_value("meta", "version", SETTINGS_VERSION)

	cfg.set_value("video", "display_mode", display_mode)
	cfg.set_value("video", "width", window_width)
	cfg.set_value("video", "height", window_height)
	cfg.set_value("video", "vsync", vsync)
	cfg.set_value("video", "max_fps", max_fps)
	cfg.set_value("video", "resolution_scale", resolution_scale)
	cfg.set_value("video", "msaa", msaa)
	cfg.set_value("video", "fxaa", fxaa)

	cfg.set_value("audio", "master", master_volume)
	cfg.set_value("audio", "music", music_volume)
	cfg.set_value("audio", "sfx", sfx_volume)
	cfg.set_value("audio", "voice", voice_volume)
	cfg.set_value("audio", "muffle_unfocused", muffle_when_unfocused)

	cfg.set_value("controls", "turn_speed", turn_speed)
	cfg.set_value("controls", "aim_turn_speed", aim_turn_speed)
	cfg.set_value("controls", "walk_speed", walk_speed)
	cfg.set_value("controls", "run_speed", run_speed)
	cfg.set_value("controls", "invert_turn", invert_turn)
	cfg.set_value("controls", "simultaneous_run", simultaneous_run)
	cfg.set_value("controls", "vibration", vibration)

	cfg.set_value("touch", "mode", touch_controls_mode)
	cfg.set_value("touch", "scale", touch_controls_scale)
	cfg.set_value("touch", "opacity", touch_controls_opacity)

	cfg.set_value("session", "language", language)
	cfg.set_value("session", "skip_menu", skip_menu_on_start)

	for action in _inputs.keys():
		var events: Array[InputEvent] = _inputs[action]
		var encoded := []
		for ev in events:
			encoded.append(var_to_str(ev))
		cfg.set_value("input", action, encoded)

	var err := cfg.save(CONFIG_PATH)
	if err != OK:
		push_warning("Could not write settings file: %s" % err)


func _load() -> void:
	var cfg := ConfigFile.new()
	if cfg.load(CONFIG_PATH) == OK:
		display_mode = cfg.get_value("video", "display_mode", display_mode)
		window_width = cfg.get_value("video", "width", window_width)
		window_height = cfg.get_value("video", "height", window_height)
		vsync = cfg.get_value("video", "vsync", vsync)
		max_fps = cfg.get_value("video", "max_fps", max_fps)
		resolution_scale = cfg.get_value("video", "resolution_scale", resolution_scale)
		msaa = cfg.get_value("video", "msaa", msaa)
		fxaa = cfg.get_value("video", "fxaa", fxaa)

		master_volume = cfg.get_value("audio", "master", master_volume)
		music_volume = cfg.get_value("audio", "music", music_volume)
		sfx_volume = cfg.get_value("audio", "sfx", sfx_volume)
		voice_volume = cfg.get_value("audio", "voice", voice_volume)
		muffle_when_unfocused = cfg.get_value("audio", "muffle_unfocused", muffle_when_unfocused)

		turn_speed = cfg.get_value("controls", "turn_speed", turn_speed)
		aim_turn_speed = cfg.get_value("controls", "aim_turn_speed", aim_turn_speed)
		walk_speed = cfg.get_value("controls", "walk_speed", walk_speed)
		run_speed = cfg.get_value("controls", "run_speed", run_speed)
		invert_turn = cfg.get_value("controls", "invert_turn", invert_turn)
		simultaneous_run = cfg.get_value("controls", "simultaneous_run", simultaneous_run)
		vibration = cfg.get_value("controls", "vibration", vibration)

		touch_controls_mode = cfg.get_value("touch", "mode", touch_controls_mode)
		touch_controls_scale = cfg.get_value("touch", "scale", touch_controls_scale)
		touch_controls_opacity = cfg.get_value("touch", "opacity", touch_controls_opacity)

		language = cfg.get_value("session", "language", language)
		skip_menu_on_start = cfg.get_value("session", "skip_menu", skip_menu_on_start)

	_capture_defaults()

	# Stored bindings first, engine defaults for anything missing.
	for action in REBINDABLE_ACTIONS.keys():
		_inputs[action] = _defaults[action].duplicate()
	for action in REBINDABLE_ACTIONS.keys():
		if not cfg.has_section_key("input", action):
			continue
		var loaded: Array[InputEvent] = []
		for raw in cfg.get_value("input", action, []):
			var ev: Variant = str_to_var(raw)
			if ev is InputEvent:
				loaded.append(ev)
		if not loaded.is_empty():
			_inputs[action] = loaded

	if display_mode == "exclusive_fullscreen":
		display_mode = "windowed"


func _capture_defaults() -> void:
	_defaults.clear()
	for action in REBINDABLE_ACTIONS.keys():
		var list: Array[InputEvent] = []
		for ev in InputMap.action_get_events(action):
			list.append(ev.duplicate())
		_defaults[action] = list
		_inputs[action] = list.duplicate()


# ---------------------------------------------------------------------------
# Applying settings
# ---------------------------------------------------------------------------

func apply_all() -> void:
	_apply_window()
	_apply_video()
	_apply_audio()
	_apply_input_remaps()
	settings_changed.emit()


func _apply_window() -> void:
	if DisplayServer.get_name() == "headless":
		return
	if display_mode == "windowed":
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		DisplayServer.window_set_size(Vector2i(window_width, window_height))
		_center_window()
	else:
		var mode := DisplayServer.WINDOW_MODE_FULLSCREEN
		if display_mode == "exclusive_fullscreen":
			mode = DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
		DisplayServer.window_set_mode(mode)


func _center_window() -> void:
	var screen := DisplayServer.window_get_current_screen()
	var usable := DisplayServer.screen_get_usable_rect(screen)
	var size := Vector2i(window_width, window_height)
	if usable.size.x > 0:
		size.x = mini(size.x, usable.size.x)
	if usable.size.y > 0:
		size.y = mini(size.y, usable.size.y)
	DisplayServer.window_set_size(size)
	DisplayServer.window_set_position(usable.position + (usable.size - size) / 2)


func _apply_video() -> void:
	Engine.max_fps = maxi(max_fps, 0)
	DisplayServer.window_set_vsync_mode(
		DisplayServer.VSYNC_ENABLED if vsync else DisplayServer.VSYNC_DISABLED
	)

	var vp := get_viewport()
	if vp and not DisplayServer.get_name() == "headless":
		var msaa_mode := Viewport.MSAA_DISABLED
		match msaa:
			1: msaa_mode = Viewport.MSAA_2X
			2: msaa_mode = Viewport.MSAA_4X
			3: msaa_mode = Viewport.MSAA_8X
		vp.msaa_3d = msaa_mode
		vp.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA if fxaa else Viewport.SCREEN_SPACE_AA_DISABLED
		vp.scaling_3d_scale = clampf(resolution_scale, 0.5, 2.0)
		vp.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR


func _ensure_buses() -> void:
	for bus_name in ["Music", "SFX", "Voice"]:
		var index := AudioServer.get_bus_index(bus_name)
		if index == -1:
			AudioServer.add_bus()
			index = AudioServer.bus_count - 1
			AudioServer.set_bus_name(index, bus_name)
			AudioServer.set_bus_send(index, "Master")


func _apply_audio() -> void:
	_set_bus_volume("Master", master_volume)
	_set_bus_volume("Music", music_volume)
	_set_bus_volume("SFX", sfx_volume)
	_set_bus_volume("Voice", voice_volume)


func _set_bus_volume(bus_name: String, linear: float) -> void:
	var index := AudioServer.get_bus_index(bus_name)
	if index == -1:
		return
	AudioServer.set_bus_mute(index, linear <= 0.001)
	AudioServer.set_bus_volume_db(index, linear_to_db(maxf(linear, 0.0001)))


func _apply_input_remaps() -> void:
	for action in _inputs.keys():
		_push_action(action)


func _push_action(action: StringName) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action, 0.25)
	InputMap.action_erase_events(action)
	for ev in _expand_for_all_joypads(_inputs[action]):
		InputMap.action_add_event(action, ev)


## Godot 4.7 matches gamepad bindings per device ID. One stored binding becomes
## one binding per local gamepad so player 1 can use any connected controller.
func _expand_for_all_joypads(events: Array[InputEvent]) -> Array[InputEvent]:
	var out: Array[InputEvent] = []
	for ev in events:
		if ev is InputEventJoypadButton or ev is InputEventJoypadMotion:
			for device in range(MAX_JOYPADS):
				var copy: InputEvent = ev.duplicate()
				copy.device = device
				out.append(copy)
		else:
			out.append(ev)
	return out


# ---------------------------------------------------------------------------
# Input API (used by the options menu)
# ---------------------------------------------------------------------------

func get_action_events(action: StringName) -> Array[InputEvent]:
	if _inputs.has(action):
		return _inputs[action]
	return []


func get_default_events(action: StringName) -> Array[InputEvent]:
	if _defaults.has(action):
		return _defaults[action]
	return []


## Replaces the bindings of one action. An empty list restores the default.
func set_action_events(action: StringName, events: Array[InputEvent]) -> void:
	if events.is_empty():
		_inputs[action] = get_default_events(action).duplicate()
	else:
		_inputs[action] = events.duplicate()
	_push_action(action)


func add_action_event(action: StringName, event: InputEvent) -> void:
	var events := get_action_events(action).duplicate()
	events.append(event)
	set_action_events(action, events)


func erase_action_event(action: StringName, index: int) -> void:
	var events := get_action_events(action).duplicate()
	if index < 0 or index >= events.size():
		return
	events.remove_at(index)
	set_action_events(action, events)


## Points one slot of an action at a new event.
func replace_action_event(action: StringName, index: int, event: InputEvent) -> void:
	var events := get_action_events(action).duplicate()
	if index < 0 or index >= events.size():
		events.append(event)
	else:
		events[index] = event
	set_action_events(action, events)


## Drops the last binding of an action (defaults are kept if it is the last one).
func remove_last_action_event(action: StringName) -> void:
	var events := get_action_events(action).duplicate()
	if events.size() <= 1:
		return
	events.remove_at(events.size() - 1)
	set_action_events(action, events)


func reset_action(action: StringName) -> void:
	set_action_events(action, get_default_events(action).duplicate())


func reset_all_actions() -> void:
	for action in REBINDABLE_ACTIONS.keys():
		reset_action(action)


## Returns the action currently bound to `event`, ignoring `except_action`.
func find_action_for_event(event: InputEvent, except_action: StringName) -> StringName:
	for action in _inputs.keys():
		if action == except_action:
			continue
		for bound in _inputs[action]:
			if bound.is_match(event):
				return action
	return &""


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

func get_bus_index(bus_name: String) -> int:
	return AudioServer.get_bus_index(bus_name)


## Keys that live in the settings file. Used for saving, loading resets.
func setting_keys() -> PackedStringArray:
	return PackedStringArray([
		"display_mode", "window_width", "window_height", "vsync", "max_fps",
		"resolution_scale", "msaa", "fxaa",
		"master_volume", "music_volume", "sfx_volume", "voice_volume", "muffle_when_unfocused",
		"turn_speed", "aim_turn_speed", "walk_speed", "run_speed", "invert_turn",
		"simultaneous_run", "vibration",
		"touch_controls_mode", "touch_controls_scale", "touch_controls_opacity",
		"language", "skip_menu_on_start",
	])


func _snapshot_defaults() -> void:
	for key in setting_keys():
		_initial[key] = get(key)


## Resets every setting and every binding to the shipped defaults.
func reset_to_defaults() -> void:
	for key in _initial.keys():
		set(key, _initial[key])
	reset_all_actions()
	apply_all()
	save()


func _notification(what: int) -> void:
	if not muffle_when_unfocused or DisplayServer.get_name() == "headless":
		return
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		AudioServer.set_bus_mute(AudioServer.get_bus_index("Master"), true)
	elif what == NOTIFICATION_APPLICATION_FOCUS_IN:
		_set_bus_volume("Master", master_volume)
