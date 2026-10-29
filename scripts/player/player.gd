extends CharacterBody3D
## Tank controls, the Resident Evil 1-3 / Signalis way.
##
## * The character only ever turns on its own Y axis and walks along its facing.
## * The camera never steers the character: it is a fixed cinematic view.
## * Forward is "up on the stick", turning is "left/right on the stick".
##
## Swapping this out for another control scheme (modern twin-stick, for example)
## means rewriting `_physics_process` only - everything else stays untouched.

@export_group("Movement")
## How fast the character builds up / bleeds off speed, in m/s squared.
@export var acceleration := 14.0
## Backpedalling is slower than walking forward, as in the reference games.
@export var backpedal_factor := 0.6
@export var gravity := 20.0
## Time in seconds between two foot steps. Purely cosmetic.
@export var step_interval := 0.32

@export_group("Feel")
## Delay between "run" being held and the character actually sprinting.
@export var run_hold_time := 0.25

@onready var _visual: Node3D = $Visual
@onready var _feet: Node3D = $Visual/Feet

var frozen := false
var aiming := false

var _speed := 0.0
var _step_phase := 0.0
var _run_timer := 0.0


func _physics_process(delta: float) -> void:
	if frozen:
		_speed = move_toward(_speed, 0.0, acceleration * delta)
		_tick_feet(delta, absf(_speed))
		return

	aiming = Input.is_action_pressed("aim")

	# --- Turning (independent from movement, always active) -----------------
	var turn_input := Input.get_axis("turn_right", "turn_left") + VirtualInput.turn
	if Settings.invert_turn:
		turn_input = -turn_input
	var turn_speed := Settings.aim_turn_speed if aiming else Settings.turn_speed
	rotate_y(clampf(turn_input, -1.0, 1.0) * turn_speed * delta)

	# --- Forward / backward along the facing direction ----------------------
	var move_input := Input.get_axis("move_back", "move_forward") + VirtualInput.move
	if aiming:
		# Aiming roots the character in place, like drawing a weapon does.
		move_input = 0.0
	move_input = clampf(move_input, -1.0, 1.0)

	_run_timer = _run_timer + delta if Input.is_action_pressed("run") else 0.0
	# Sprinting only works going forwards: backpedalling stays slow.
	var wants_run := Settings.simultaneous_run and move_input > 0.0 and _run_timer >= run_hold_time
	var speed := Settings.run_speed if wants_run else Settings.walk_speed
	if move_input < 0.0:
		speed *= backpedal_factor

	var target_speed := speed * move_input
	_speed = move_toward(_speed, target_speed, acceleration * delta)

	velocity.x = -global_basis.z.x * _speed
	velocity.z = -global_basis.z.z * _speed
	velocity.y -= gravity * delta
	move_and_slide()

	_tick_feet(delta, absf(turn_input) * 2.0 + absf(_speed))


## Cosmetic only: nudges the little cylinder feet so movement reads on screen.
func _tick_feet(delta: float, activity: float) -> void:
	if activity < 0.05:
		_step_phase = move_toward(_step_phase, 0.0, delta * 4.0)
		_feet.position.z = move_toward(_feet.position.z, 0.0, delta)
		_feet.rotation.y = move_toward(_feet.rotation.y, 0.0, delta)
		return

	_step_phase += delta / maxf(step_interval, 0.05)
	var swing := sin(_step_phase * TAU) * 0.12 * minf(activity, 1.5)
	_feet.position.z = swing
	_feet.position.y = 0.12 + absf(swing) * 0.15
	_feet.rotation.y = swing * 1.2


func teleport_to(target: Transform3D) -> void:
	global_transform = target
	velocity = Vector3.ZERO
	_speed = 0.0
