using System;
using System.Collections.Generic;
using UnityEngine;

namespace Foundation
{
    public enum GameAction
    {
        MoveForward = 0,
        MoveBack = 1,
        TurnLeft = 2,
        TurnRight = 3,
        Action = 4,
        Run = 5,
        Aim = 6,
        SwapShoulder = 7,
        Pause = 8,
    }

    /// <summary>A single input binding: either a key/button or one direction of an axis.</summary>
    [Serializable]
    public class Binding
    {
        public KeyCode key = KeyCode.None;
        public string axis = "";
        public float dir = 1f;

        public bool IsAxis => !string.IsNullOrEmpty(axis);

        public static Binding FromKey(KeyCode code)
        {
            return new Binding { key = code };
        }

        public static Binding FromAxis(string axisName, float direction)
        {
            return new Binding { axis = axisName, dir = Mathf.Sign(direction) };
        }

        public Binding Clone()
        {
            return new Binding { key = key, axis = axis, dir = dir };
        }

        public bool Matches(Binding other)
        {
            if (other == null) return false;
            if (IsAxis || other.IsAxis)
            {
                return IsAxis && other.IsAxis && axis == other.axis && Mathf.Approximately(dir, other.dir);
            }
            return key == other.key;
        }

        /// -1..1 raw contribution of this binding this frame.
        public float Value()
        {
            if (IsAxis)
            {
                float raw = SafeAxis(axis);
                float value = raw * dir;
                return value > 0f ? value : 0f;
            }
            return key != KeyCode.None && Input.GetKey(key) ? 1f : 0f;
        }

        public bool Held()
        {
            return Value() > 0.5f;
        }

        public bool Pressed()
        {
            if (IsAxis) return false; // edge detection for axes lives in InputService
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        /// Reads an axis without throwing if the project is missing the entry.
        public static float SafeAxis(string axisName)
        {
            if (string.IsNullOrEmpty(axisName)) return 0f;
            try
            {
                return Input.GetAxisRaw(axisName);
            }
            catch (Exception)
            {
                return 0f;
            }
        }
    }

    /// <summary>
    /// Owns every gameplay action: defaults, runtime state, rebinding and the
    /// touch bridge. Keyboard, gamepad and the on-screen controls all end up in
    /// the same GetAxis / GetPressed calls.
    ///
    /// Uses Unity's legacy Input Manager on purpose: no packages, and rebinding
    /// is plain data instead of generated assets.
    /// </summary>
    public class InputService : MonoBehaviour
    {
        public static InputService Instance { get; private set; }

        /// Axes shipped in ProjectSettings/InputManager.asset.
        public static readonly string[] AnalogAxes =
        {
            "Joy LX", "Joy LY", "Joy RX", "Joy RY", "Joy LT", "Joy RT",
        };

        public static readonly GameAction[] RebindableActions =
        {
            GameAction.MoveForward, GameAction.MoveBack, GameAction.TurnLeft, GameAction.TurnRight,
            GameAction.Action, GameAction.Run, GameAction.Aim, GameAction.SwapShoulder, GameAction.Pause,
        };

        public event Action BindingsChanged;

        readonly Dictionary<GameAction, List<Binding>> _bindings = new Dictionary<GameAction, List<Binding>>();
        readonly bool[] _held = new bool[9];
        readonly bool[] _pressed = new bool[9];
        readonly bool[] _virtualHold = new bool[9];
        readonly float[] _axisPrev = new float[AnalogAxes.Length];

        float _virtualMove;
        float _virtualTurn;
        int _frame = -1;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Init()
        {
            LoadBindings();
        }

        // ------------------------------------------------------------- queries

        /// -1 (back) .. 1 (forward). Includes the touch d-pad.
        public float MoveAxis
        {
            get
            {
                EnsureFrame();
                float value = ValueOf(GameAction.MoveForward) - ValueOf(GameAction.MoveBack) + _virtualMove;
                return Mathf.Clamp(value, -1f, 1f);
            }
        }

        /// -1 (right) .. 1 (left). Includes the touch d-pad.
        public float TurnAxis
        {
            get
            {
                EnsureFrame();
                float value = ValueOf(GameAction.TurnLeft) - ValueOf(GameAction.TurnRight) + _virtualTurn;
                return Mathf.Clamp(value, -1f, 1f);
            }
        }

        public bool GetHeld(GameAction action)
        {
            EnsureFrame();
            return _held[(int)action];
        }

        public bool GetPressed(GameAction action)
        {
            EnsureFrame();
            return _pressed[(int)action];
        }

        /// Analog value of a single action, before positive/negative combination.
        public float ValueOf(GameAction action)
        {
            List<Binding> list;
            if (!_bindings.TryGetValue(action, out list)) return 0f;
            float best = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                float value = list[i].Value();
                if (value > best) best = value;
            }
            return best;
        }

        void EnsureFrame()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Poll();
        }

        void Poll()
        {
            for (int i = 0; i < RebindableActions.Length; i++)
            {
                GameAction action = RebindableActions[i];
                int index = (int)action;
                float best = 0f;
                bool pressed = false;

                List<Binding> list;
                if (_bindings.TryGetValue(action, out list))
                {
                    for (int b = 0; b < list.Count; b++)
                    {
                        Binding binding = list[b];
                        float value = binding.Value();
                        if (value > best) best = value;
                        if (binding.Pressed()) pressed = true;
                        if (binding.IsAxis)
                        {
                            // Edge detection for analog directions.
                            int axisIndex = Array.IndexOf(AnalogAxes, binding.axis);
                            if (axisIndex >= 0 && value > 0.6f)
                            {
                                float previous = _axisPrev[axisIndex] * binding.dir;
                                if (previous <= 0.6f) pressed = true;
                            }
                        }
                    }
                }

                _held[index] = best > 0.5f || _virtualHold[index];
                _pressed[index] = pressed || (_virtualHold[index] && !_virtualHoldWas[index]);
            }

            for (int a = 0; a < AnalogAxes.Length; a++)
            {
                _axisPrev[a] = Binding.SafeAxis(AnalogAxes[a]);
            }
            Array.Copy(_virtualHold, _virtualHoldWas, _virtualHold.Length);
        }

        readonly bool[] _virtualHoldWas = new bool[9];

        // ---------------------------------------------------------- touch API

        public void SetVirtualAnalog(float move, float turn)
        {
            _virtualMove = Mathf.Clamp(move, -1f, 1f);
            _virtualTurn = Mathf.Clamp(turn, -1f, 1f);
        }

        public void SetVirtualHold(GameAction action, bool held)
        {
            _virtualHold[(int)action] = held;
        }

        public void ClearVirtualInput()
        {
            _virtualMove = 0f;
            _virtualTurn = 0f;
            for (int i = 0; i < _virtualHold.Length; i++) _virtualHold[i] = false;
        }

        // ------------------------------------------------------------ bindings

        public List<Binding> GetBindings(GameAction action)
        {
            List<Binding> list;
            if (!_bindings.TryGetValue(action, out list))
            {
                list = new List<Binding>();
                _bindings[action] = list;
            }
            return list;
        }

        public void SetBinding(GameAction action, int index, Binding binding)
        {
            List<Binding> list = GetBindings(action);
            if (index < 0 || index >= list.Count) list.Add(binding);
            else list[index] = binding;
            PushBindingsToDisk();
        }

        public void AddBinding(GameAction action, Binding binding)
        {
            GetBindings(action).Add(binding);
            PushBindingsToDisk();
        }

        public void RemoveLastBinding(GameAction action)
        {
            List<Binding> list = GetBindings(action);
            if (list.Count <= 1) return;
            list.RemoveAt(list.Count - 1);
            PushBindingsToDisk();
        }

        public void ResetBinding(GameAction action)
        {
            _bindings[action] = DefaultBindings(action);
            PushBindingsToDisk();
        }

        public void ResetAllBindings()
        {
            foreach (GameAction action in RebindableActions)
            {
                _bindings[action] = DefaultBindings(action);
            }
            PushBindingsToDisk();
        }

        /// Which action already uses this binding (empty string if none).
        public GameAction? FindConflict(Binding binding, GameAction except)
        {
            foreach (GameAction action in RebindableActions)
            {
                if (action == except) continue;
                List<Binding> list = GetBindings(action);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Matches(binding)) return action;
                }
            }
            return null;
        }

        public static string Describe(Binding binding)
        {
            if (binding == null) return "-";
            if (binding.IsAxis) return binding.axis + (binding.dir > 0f ? " +" : " -");
            return KeyLabel(binding.key);
        }

        public static string KeyLabel(KeyCode key)
        {
            if (key >= KeyCode.JoystickButton0 && key <= KeyCode.JoystickButton19)
            {
                return "Pad " + PadButtonLabel(key - KeyCode.JoystickButton0);
            }
            switch (key)
            {
                case KeyCode.LeftShift: return "L Shift";
                case KeyCode.RightShift: return "R Shift";
                case KeyCode.LeftControl: return "L Ctrl";
                case KeyCode.RightControl: return "R Ctrl";
                case KeyCode.LeftAlt: return "L Alt";
                case KeyCode.RightAlt: return "R Alt";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Return: return "Enter";
                case KeyCode.Space: return "Space";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Tab: return "Tab";
                default: return key.ToString();
            }
        }

        static string PadButtonLabel(int index)
        {
            switch (index)
            {
                case 0: return "A";
                case 1: return "B";
                case 2: return "X";
                case 3: return "Y";
                case 4: return "LB";
                case 5: return "RB";
                case 6: return "Back";
                case 7: return "Start";
                case 8: return "L3";
                case 9: return "R3";
                default: return (index + 1).ToString();
            }
        }

        public static string ActionLabel(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "Forward";
                case GameAction.MoveBack: return "Back";
                case GameAction.TurnLeft: return "Turn Left";
                case GameAction.TurnRight: return "Turn Right";
                case GameAction.Action: return "Action";
                case GameAction.Run: return "Run";
                case GameAction.Aim: return "Aim";
                case GameAction.SwapShoulder: return "Change Camera";
                case GameAction.Pause: return "Pause";
                default: return action.ToString();
            }
        }

        static List<Binding> DefaultBindings(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward:
                    return new List<Binding> { Binding.FromKey(KeyCode.W), Binding.FromKey(KeyCode.UpArrow), Binding.FromAxis("Joy LY", -1f) };
                case GameAction.MoveBack:
                    return new List<Binding> { Binding.FromKey(KeyCode.S), Binding.FromKey(KeyCode.DownArrow), Binding.FromAxis("Joy LY", 1f) };
                case GameAction.TurnLeft:
                    return new List<Binding> { Binding.FromKey(KeyCode.A), Binding.FromKey(KeyCode.LeftArrow), Binding.FromAxis("Joy LX", -1f) };
                case GameAction.TurnRight:
                    return new List<Binding> { Binding.FromKey(KeyCode.D), Binding.FromKey(KeyCode.RightArrow), Binding.FromAxis("Joy LX", 1f) };
                case GameAction.Action:
                    return new List<Binding> { Binding.FromKey(KeyCode.Space), Binding.FromKey(KeyCode.E), Binding.FromKey(KeyCode.JoystickButton0), Binding.FromAxis("Joy RT", 1f) };
                case GameAction.Run:
                    return new List<Binding> { Binding.FromKey(KeyCode.LeftShift), Binding.FromKey(KeyCode.JoystickButton1) };
                case GameAction.Aim:
                    return new List<Binding> { Binding.FromKey(KeyCode.Q), Binding.FromKey(KeyCode.JoystickButton2) };
                case GameAction.SwapShoulder:
                    return new List<Binding> { Binding.FromKey(KeyCode.C), Binding.FromKey(KeyCode.JoystickButton4) };
                case GameAction.Pause:
                    return new List<Binding> { Binding.FromKey(KeyCode.Escape), Binding.FromKey(KeyCode.JoystickButton7) };
                default:
                    return new List<Binding>();
            }
        }

        // -------------------------------------------------------- persistence

        void LoadBindings()
        {
            _bindings.Clear();
            foreach (GameAction action in RebindableActions)
            {
                _bindings[action] = DefaultBindings(action);
            }

            SettingsService settings = SettingsService.Instance;
            if (settings == null || settings.Data.bindings == null || settings.Data.bindings.Count == 0) return;

            var seen = new HashSet<GameAction>();
            foreach (BindingData stored in settings.Data.bindings)
            {
                GameAction action;
                if (!TryParseAction(stored.action, out action)) continue;
                Binding binding = new Binding
                {
                    key = (KeyCode)stored.key,
                    axis = stored.axis ?? "",
                    dir = stored.dir >= 0f ? 1f : -1f,
                };
                if (!binding.IsAxis && binding.key == KeyCode.None) continue;

                List<Binding> list = GetBindings(action);
                if (!seen.Contains(action))
                {
                    list.Clear();
                    seen.Add(action);
                }
                list.Add(binding);
            }

            BindingsChanged?.Invoke();
        }

        void PushBindingsToDisk()
        {
            SettingsService settings = SettingsService.Instance;
            if (settings != null)
            {
                settings.Data.bindings = new List<BindingData>();
                foreach (GameAction action in RebindableActions)
                {
                    foreach (Binding binding in GetBindings(action))
                    {
                        settings.Data.bindings.Add(new BindingData
                        {
                            action = action.ToString(),
                            key = (int)binding.key,
                            axis = binding.axis ?? "",
                            dir = binding.dir,
                        });
                    }
                }
                settings.Save();
            }
            BindingsChanged?.Invoke();
        }

        static bool TryParseAction(string name, out GameAction action)
        {
            foreach (GameAction candidate in RebindableActions)
            {
                if (candidate.ToString() == name)
                {
                    action = candidate;
                    return true;
                }
            }
            action = GameAction.Action;
            return false;
        }
    }
}
