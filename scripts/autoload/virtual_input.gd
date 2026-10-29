extends Node
## Thin bridge between on-screen touch controls and the player.
##
## Touch controls write analog values here; the player reads them the same way
## it reads `Input.get_axis()`. Keyboard, mouse, gamepad and touch therefore all
## feed the exact same tank-control code path.

## -1 (back) .. 1 (forward)
var move := 0.0
## -1 (right) .. 1 (left)
var turn := 0.0


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS


func reset() -> void:
	move = 0.0
	turn = 0.0
