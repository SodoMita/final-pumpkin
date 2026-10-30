using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>
    /// Every setting the foundation has: display, audio, controls with full
    /// rebinding, touch, system.
    ///
    /// Display is special. Nothing is written to disk and nothing is applied for
    /// keeps until the player presses Apply *and* confirms they can still see the
    /// screen. If they do not confirm within <see cref="ConfirmSeconds"/>, the
    /// previous resolution comes back automatically. Scalar settings are saved
    /// immediately - a wrong volume is always recoverable, a wrong resolution is not.
    /// </summary>
    public class OptionsScreen : MonoBehaviour
    {
        public const float ConfirmSeconds = 10f;

        public static int OpenCount { get; private set; }
        public static bool AnyOpen => OpenCount > 0;
        public static int ClosedAtFrame { get; private set; } = -1;

        const int TabDisplay = 0;
        const int TabAudio = 1;
        const int TabControls = 2;
        const int TabTouch = 3;
        const int TabSystem = 4;

        static readonly string[] TabNames = { "Display", "Audio", "Controls", "Touch", "System" };

        static readonly KeyCode[] CandidateKeys = BuildCandidateKeys();

        static readonly string[] QualityFallback = { "Low", "Medium", "High" };
        static readonly string[] FpsChoices = { "Uncapped", "30", "60", "72", "90", "120", "144", "240" };
        static readonly int[] FpsValues = { 0, 30, 60, 72, 90, 120, 144, 240 };

        // -- state ------------------------------------------------------------
        int _tab = TabDisplay;
        Canvas _canvas;
        RectTransform _content;
        RectTransform _scroll;
        Text _hint;

        DisplaySettings _pending;
        List<Vector2Int> _detected;
        int _detectedIndex;
        InputField _widthField;
        InputField _heightField;
        Button _applyButton;
        Button _revertButton;

        GameObject _confirmBar;
        Text _confirmText;
        float _confirmTimer;

        GameAction? _captureAction;
        int _captureSlot;

        // -- lifecycle --------------------------------------------------------

        public static OptionsScreen Create(Transform parent)
        {
            var go = new GameObject("OptionsScreen");
            go.transform.SetParent(parent, false);
            var screen = go.AddComponent<OptionsScreen>();
            screen.Build();
            return screen;
        }

        public void Build()
        {
            OpenCount++;
            _pending = SettingsService.Instance.Data.display.Clone();
            _detected = SettingsService.DetectedResolutions();
            _detectedIndex = FindDetectedIndex(_pending);

            _canvas = UiKit.CreateCanvas("OptionsCanvas", 100, transform);
            var background = UiKit.Background(_canvas.transform, UiKit.Overlay);
            background.GetComponent<Image>().raycastTarget = true;

            var safeArea = UiKit.Stretch(UiKit.NewRect("SafeArea", _canvas.transform));
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            var column = UiKit.Stretch(UiKit.Column(safeArea, 14f, 32f));

            BuildHeader(column);
            BuildTabs(column);

            _scroll = UiKit.ScrollColumn(column, out _content, 10f);
            UiKit.SetMinHeight(_scroll, 240f);
            UiKit.SetFlexibleHeight(_scroll, 1f);

            BuildConfirmBar(column);

            _hint = UiKit.Label(column, "", 20, UiKit.TextMuted, TextAnchor.MiddleLeft);
            UiKit.SetMinHeight(_hint.rectTransform, 34f);

            RebuildTab();
        }

        void OnDestroy()
        {
            OpenCount = Mathf.Max(0, OpenCount - 1);
            ClosedAtFrame = Time.frameCount;

            // Leaving with an unconfirmed display change means: put it back.
            if (SettingsService.Instance != null && SettingsService.Instance.HasStagedDisplay)
            {
                SettingsService.Instance.RevertDisplay();
            }
        }

        void Update()
        {
            if (_confirmTimer > 0f)
            {
                _confirmTimer -= Time.unscaledDeltaTime;
                if (_confirmTimer <= 0f)
                {
                    RevertDisplay("Reverted - no confirmation.");
                    return;
                }
                if (_confirmText != null)
                {
                    _confirmText.text = $"Keep these display settings?  Reverting in {Mathf.CeilToInt(_confirmTimer)}s";
                }
            }

            if (_captureAction.HasValue)
            {
                PollRebind();
                return;
            }

            if (InputService.Instance != null && InputService.Instance.GetPressed(GameAction.Pause))
            {
                Close();
            }
        }

        void Close()
        {
            CancelCapture(false);
            if (SettingsService.Instance != null && SettingsService.Instance.HasStagedDisplay)
            {
                SettingsService.Instance.RevertDisplay();
            }
            Destroy(gameObject);
        }

        // -- chrome -----------------------------------------------------------

        void BuildHeader(Transform parent)
        {
            var row = UiKit.Row(parent, 16f, 72f);

            var title = UiKit.Label(row, "OPTIONS", 42, UiKit.TextPrimary, TextAnchor.MiddleLeft);
            UiKit.SetFlexibleWidth(title.rectTransform, 1f);

            UiKit.Button(row, "Back", Close, 56f, 220f);
        }

        void BuildTabs(Transform parent)
        {
            var row = UiKit.Row(parent, 8f, 56f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                Button button = UiKit.Button(row, TabNames[i], () =>
                {
                    _tab = index;
                    RebuildTab();
                }, 56f, 0f);
                UiKit.SetMinWidth(button.GetComponent<RectTransform>(), 200f);
                button.gameObject.AddComponent<TabMarker>().Index = i;
            }
        }

        void BuildConfirmBar(Transform parent)
        {
            _confirmBar = UiKit.NewRect("ConfirmBar", parent).gameObject;
            UiKit.SetMinHeight(_confirmBar.GetComponent<RectTransform>(), 84f);
            UiKit.AddImage(_confirmBar.GetComponent<RectTransform>(), new Color(0.22f, 0.14f, 0.10f, 0.98f), true);

            var row = UiKit.Stretch(UiKit.Row(_confirmBar.transform, 16f), 14f);
            _confirmText = UiKit.Label(row, "", 22, UiKit.TextPrimary, TextAnchor.MiddleLeft);
            UiKit.SetFlexibleWidth(_confirmText.rectTransform, 1f);

            UiKit.Button(row, "Keep", () => ConfirmDisplay(), 56f, 200f);
            UiKit.Button(row, "Revert", () => RevertDisplay("Reverted."), 56f, 200f);

            _confirmBar.SetActive(false);
        }

        void RebuildTab()
        {
            UiKit.Clear(_content);
            _widthField = null;
            _heightField = null;
            _applyButton = null;
            _revertButton = null;

            var markers = _canvas.GetComponentsInChildren<TabMarker>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                var image = markers[i].GetComponent<Image>();
                if (image != null)
                {
                    image.color = markers[i].Index == _tab ? UiKit.ButtonHighlight : UiKit.ButtonColor;
                }
            }

            switch (_tab)
            {
                case TabDisplay: BuildDisplayTab(); break;
                case TabAudio: BuildAudioTab(); break;
                case TabControls: BuildControlsTab(); break;
                case TabTouch: BuildTouchTab(); break;
                default: BuildSystemTab(); break;
            }

            if (_scroll != null) UiKit.ScrollToTop(_scroll);
            RefreshSteppers();
        }

        void RefreshSteppers()
        {
            if (_content != null) UiKit.RefreshAllSteppers(_content);
        }

        void SetHint(string text)
        {
            if (_hint != null) _hint.text = text;
        }

        // -- display ----------------------------------------------------------

        void BuildDisplayTab()
        {
            AddStepper("Display Mode", () => ModeLabel(_pending.mode), step =>
            {
                _pending.mode = (_pending.mode + step + 3) % 3;
                SetHint("Display mode staged. Press Apply to use it.");
            });

            _widthField = UiKit.NumberField(_content, "Width", _pending.width,
                SettingsService.MinWidth, SettingsService.MaxWidth, value =>
                {
                    _pending.width = value;
                    SetHint("Resolution staged. Press Apply to use it.");
                });

            _heightField = UiKit.NumberField(_content, "Height", _pending.height,
                SettingsService.MinHeight, SettingsService.MaxHeight, value =>
                {
                    _pending.height = value;
                    SetHint("Resolution staged. Press Apply to use it.");
                });

            AddStepper("Detected Resolutions", () => DetectedLabel(), step =>
            {
                if (_detected.Count == 0) return;
                _detectedIndex = (_detectedIndex + step + _detected.Count) % _detected.Count;
                Vector2Int size = _detected[_detectedIndex];
                _pending.width = size.x;
                _pending.height = size.y;
                if (_widthField != null) _widthField.text = size.x.ToString();
                if (_heightField != null) _heightField.text = size.y.ToString();
                SetHint("Resolution staged. Press Apply to use it.");
            });

            AddToggle("Vertical Sync", () => _pending.vsync, value =>
            {
                _pending.vsync = value;
                SetHint("V-Sync staged. Press Apply to use it.");
            });

            AddStepper("FPS Limit", () => FpsLabel(_pending.fpsLimit), step =>
            {
                int index = Mathf.Max(0, Array.IndexOf(FpsValues, _pending.fpsLimit));
                index = (index + step + FpsValues.Length) % FpsValues.Length;
                _pending.fpsLimit = FpsValues[index];
                SetHint("Frame rate limit staged. Press Apply to use it.");
            });

            AddStepper("Quality Level", () => QualityLabel(_pending.qualityLevel), step =>
            {
                int max = Mathf.Max(0, QualitySettings.names.Length - 1);
                int current = _pending.qualityLevel < 0 ? max : _pending.qualityLevel;
                current = Mathf.Clamp(current + step, 0, max);
                _pending.qualityLevel = current;
                SetHint("Quality staged. Press Apply to use it.");
            });

            UiKit.Divider(_content);

            var row = UiKit.Row(_content, 12f, 60f);
            _applyButton = UiKit.Button(row, "Apply", ApplyDisplay, 60f, 240f);
            _revertButton = UiKit.Button(row, "Revert", () => RevertDisplay("Reverted."), 60f, 240f);
            UiKit.Button(row, "Restore Default", () =>
            {
                _pending = new DisplaySettings();
                if (_widthField != null) _widthField.text = _pending.width.ToString();
                if (_heightField != null) _heightField.text = _pending.height.ToString();
                _detectedIndex = FindDetectedIndex(_pending);
                RefreshDisplayButtons();
                RefreshSteppers();
                SetHint("Defaults staged. Press Apply to use them.");
            }, 60f, 260f);

            RefreshDisplayButtons();

            UiKit.Paragraph(_content,
                "Resolution, mode, V-Sync, frame limit and quality are staged: nothing changes and "
                + "nothing is written to disk until you press Apply, and the previous settings come back "
                + $"automatically after {ConfirmSeconds:0} seconds unless you press Keep. "
                + "Width and height accept any value in range, not just the detected list.");
        }

        void RefreshDisplayButtons()
        {
            SettingsService settings = SettingsService.Instance;
            bool differs = !_pending.SameAs(settings.Data.display);
            bool staged = settings.HasStagedDisplay;

            if (_applyButton != null) _applyButton.interactable = differs || staged;
            if (_revertButton != null)
            {
                _revertButton.interactable = staged;
            }
        }

        void ApplyDisplay()
        {
            SettingsService settings = SettingsService.Instance;
            settings.StageDisplay(_pending);

            _confirmTimer = ConfirmSeconds;
            if (_confirmBar != null) _confirmBar.SetActive(true);
            if (_confirmText != null)
            {
                _confirmText.text = $"Keep these display settings?  Reverting in {Mathf.CeilToInt(_confirmTimer)}s";
            }
            RefreshDisplayButtons();
            SetHint("Preview applied. Confirm with Keep, or wait for the automatic revert.");
        }

        void ConfirmDisplay()
        {
            SettingsService settings = SettingsService.Instance;
            settings.CommitDisplay();
            _pending = settings.Data.display.Clone();
            _detectedIndex = FindDetectedIndex(_pending);
            _confirmTimer = 0f;
            if (_confirmBar != null) _confirmBar.SetActive(false);
            RefreshDisplayButtons();
            SetHint("Display settings saved.");
        }

        void RevertDisplay(string message)
        {
            SettingsService settings = SettingsService.Instance;
            settings.RevertDisplay();
            _pending = settings.Data.display.Clone();
            _detectedIndex = FindDetectedIndex(_pending);
            _confirmTimer = 0f;
            if (_confirmBar != null) _confirmBar.SetActive(false);
            if (_widthField != null) _widthField.text = _pending.width.ToString();
            if (_heightField != null) _heightField.text = _pending.height.ToString();
            RefreshDisplayButtons();
            RefreshSteppers();
            SetHint(message);
        }

        int FindDetectedIndex(DisplaySettings display)
        {
            for (int i = 0; i < _detected.Count; i++)
            {
                if (_detected[i].x == display.width && _detected[i].y == display.height) return i;
            }
            return 0;
        }

        string DetectedLabel()
        {
            if (_detected.Count == 0) return "none";
            Vector2Int current = new Vector2Int(_pending.width, _pending.height);
            if (_detected[_detectedIndex] != current) return "custom";
            return $"{current.x} x {current.y}";
        }

        static string ModeLabel(int mode)
        {
            switch (mode)
            {
                case DisplaySettings.ModeBorderless: return "Borderless";
                case DisplaySettings.ModeExclusive: return "Exclusive Fullscreen";
                default: return "Windowed";
            }
        }

        static string FpsLabel(int fps)
        {
            return fps <= 0 ? "Uncapped" : fps.ToString();
        }

        static string QualityLabel(int level)
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0) names = QualityFallback;
            if (level < 0) return names[names.Length - 1] + " (default)";
            return names[Mathf.Clamp(level, 0, names.Length - 1)];
        }

        // -- audio ------------------------------------------------------------

        void BuildAudioTab()
        {
            AudioSettings audio = SettingsService.Instance.Data.audio;

            AddVolumeStepper("Master Volume", () => audio.master, value => audio.master = value);
            AddVolumeStepper("Music Volume", () => audio.music, value => audio.music = value);
            AddVolumeStepper("Effects Volume", () => audio.sfx, value => audio.sfx = value);
            AddVolumeStepper("Voice Volume", () => audio.voice, value => audio.voice = value);

            UiKit.Divider(_content);
            AddToggle("Mute When Unfocused", () => audio.muteWhenUnfocused, value =>
            {
                audio.muteWhenUnfocused = value;
                SaveAndApplyAudio();
            });

            UiKit.Paragraph(_content,
                "Four channels with no mixer assets: music, effects and voice each scale under master. "
                + "Audio changes apply and save immediately.");
        }

        void AddVolumeStepper(string title, Func<float> get, Action<float> set)
        {
            AddStepper(title, () => Mathf.RoundToInt(get() * 100f) + "%", step =>
            {
                set(Mathf.Clamp01(get() + step * 0.05f));
                SaveAndApplyAudio();
            });
        }

        void SaveAndApplyAudio()
        {
            SettingsService settings = SettingsService.Instance;
            settings.ApplyAudio();
            settings.Save();
            SetHint("Audio saved.");
        }

        // -- controls ---------------------------------------------------------

        void BuildControlsTab()
        {
            ControlSettings controls = SettingsService.Instance.Data.controls;
            InputService input = InputService.Instance;

            AddStepper("Turn Speed", () => controls.turnSpeed.ToString("0.0") + " rad/s", step =>
            {
                controls.turnSpeed = Mathf.Clamp(controls.turnSpeed + step * 0.1f, 0.5f, 6f);
                SaveSettings();
            });

            AddStepper("Aim Turn Speed", () => controls.aimTurnSpeed.ToString("0.0") + " rad/s", step =>
            {
                controls.aimTurnSpeed = Mathf.Clamp(controls.aimTurnSpeed + step * 0.1f, 0.5f, 5f);
                SaveSettings();
            });

            AddStepper("Walk Speed", () => controls.walkSpeed.ToString("0.0") + " m/s", step =>
            {
                controls.walkSpeed = Mathf.Clamp(controls.walkSpeed + step * 0.1f, 1f, 8f);
                SaveSettings();
            });

            AddStepper("Run Speed", () => controls.runSpeed.ToString("0.0") + " m/s", step =>
            {
                controls.runSpeed = Mathf.Clamp(controls.runSpeed + step * 0.1f, 2f, 12f);
                SaveSettings();
            });

            AddToggle("Invert Turning", () => controls.invertTurn, value =>
            {
                controls.invertTurn = value;
                SaveSettings();
            });

            AddToggle("Sprint While Holding Forward", () => controls.simultaneousRun, value =>
            {
                controls.simultaneousRun = value;
                SaveSettings();
            });

            UiKit.Divider(_content);
            UiKit.Header(_content, "KEYBOARD / GAMEPAD BINDINGS");
            UiKit.Paragraph(_content,
                "Click a binding, then press any key, gamepad button or axis. Escape cancels. "
                + "Bindings save immediately and apply without a restart.");

            for (int i = 0; i < InputService.RebindableActions.Length; i++)
            {
                GameAction action = InputService.RebindableActions[i];
                BuildBindingRow(input, action);
            }

            UiKit.Spacer(_content, 8f);
            UiKit.Button(_content, "Reset All Bindings", () =>
            {
                input.ResetAllBindings();
                RebuildTab();
                SetHint("All bindings restored to defaults.");
            }, 56f);
        }

        void BuildBindingRow(InputService input, GameAction action)
        {
            var row = UiKit.Row(_content, 8f, 52f);

            var label = UiKit.Label(row, InputService.ActionLabel(action), 22, UiKit.TextPrimary, TextAnchor.MiddleLeft);
            UiKit.SetMinWidth(label.rectTransform, 220f);
            UiKit.SetFlexibleWidth(label.rectTransform, 1f);

            List<Binding> bindings = input.GetBindings(action);
            for (int slot = 0; slot < bindings.Count; slot++)
            {
                int capturedSlot = slot;
                bool capturingThis = _captureAction.HasValue && _captureAction.Value == action && _captureSlot == slot;
                Button button = UiKit.Button(row, capturingThis ? "Press any key..." : InputService.Describe(bindings[slot]),
                    () => BeginCapture(action, capturedSlot), 52f, 190f);
                if (capturingThis)
                {
                    var image = button.GetComponent<Image>();
                    if (image != null) image.color = UiKit.Accent;
                }
            }

            UiKit.Button(row, "+", () =>
            {
                input.AddBinding(action, Binding.FromKey(KeyCode.None));
                BeginCapture(action, input.GetBindings(action).Count - 1);
            }, 52f, 60f);

            Button remove = UiKit.Button(row, "-", () =>
            {
                input.RemoveLastBinding(action);
                RebuildTab();
            }, 52f, 60f);
            remove.interactable = bindings.Count > 1;

            UiKit.Button(row, "Reset", () =>
            {
                input.ResetBinding(action);
                RebuildTab();
                SetHint($"{InputService.ActionLabel(action)} restored to defaults.");
            }, 52f, 150f);
        }

        // -- rebinding --------------------------------------------------------

        void BeginCapture(GameAction action, int slot)
        {
            _captureAction = action;
            _captureSlot = slot;
            SetHint("Press a key, a gamepad button, or push an axis. Escape cancels.");
            RebuildTab();
        }

        void CancelCapture(bool rebuild = true)
        {
            _captureAction = null;
            _captureSlot = -1;
            if (rebuild) RebuildTab();
        }

        void PollRebind()
        {
            if (!_captureAction.HasValue) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelCapture();
                SetHint("Rebinding cancelled.");
                return;
            }

            for (int i = 0; i < CandidateKeys.Length; i++)
            {
                if (Input.GetKeyDown(CandidateKeys[i]))
                {
                    CompleteCapture(Binding.FromKey(CandidateKeys[i]));
                    return;
                }
            }

            for (int i = 0; i < InputService.AnalogAxes.Length; i++)
            {
                string axis = InputService.AnalogAxes[i];
                float value = Binding.SafeAxis(axis);
                if (Mathf.Abs(value) > 0.6f)
                {
                    CompleteCapture(Binding.FromAxis(axis, value));
                    return;
                }
            }
        }

        void CompleteCapture(Binding binding)
        {
            InputService input = InputService.Instance;
            GameAction action = _captureAction.Value;
            int slot = _captureSlot;

            GameAction? conflict = input.FindConflict(binding, action);
            input.SetBinding(action, slot, binding);
            _captureAction = null;
            _captureSlot = -1;
            RebuildTab();

            string message = $"{InputService.ActionLabel(action)} bound to {InputService.Describe(binding)}.";
            if (conflict.HasValue)
            {
                message += $"  Also used by {InputService.ActionLabel(conflict.Value)}.";
            }
            SetHint(message);
        }

        static KeyCode[] BuildCandidateKeys()
        {
            var keys = new List<KeyCode>();
            for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++) keys.Add(key);
            for (KeyCode key = KeyCode.Alpha0; key <= KeyCode.Alpha9; key++) keys.Add(key);
            for (KeyCode key = KeyCode.Keypad0; key <= KeyCode.Keypad9; key++) keys.Add(key);
            for (KeyCode key = KeyCode.F1; key <= KeyCode.F12; key++) keys.Add(key);
            for (KeyCode key = KeyCode.JoystickButton0; key <= KeyCode.JoystickButton19; key++) keys.Add(key);

            keys.AddRange(new[]
            {
                KeyCode.Space, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl,
                KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.Tab,
                KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Backspace, KeyCode.Insert,
                KeyCode.Delete, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals, KeyCode.LeftBracket,
                KeyCode.RightBracket, KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma,
                KeyCode.Period, KeyCode.Slash, KeyCode.Backslash, KeyCode.CapsLock,
                KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2,
            });
            return keys.ToArray();
        }

        // -- touch ------------------------------------------------------------

        void BuildTouchTab()
        {
            TouchSettings touch = SettingsService.Instance.Data.touch;

            AddStepper("Show Touch Controls", () => TouchModeLabel(touch.mode), step =>
            {
                touch.mode = (touch.mode + step + 3) % 3;
                SaveSettings();
                SetHint("Touch overlay updated.");
            });

            AddStepper("Overlay Size", () => touch.scale.ToString("0.00") + "x", step =>
            {
                touch.scale = Mathf.Clamp(touch.scale + step * 0.05f, 0.6f, 1.8f);
                SaveSettings();
            });

            AddStepper("Overlay Opacity", () => Mathf.RoundToInt(touch.opacity * 100f) + "%", step =>
            {
                touch.opacity = Mathf.Clamp(touch.opacity + step * 0.05f, 0.2f, 1f);
                SaveSettings();
            });

            AddToggle("Vibration", () => SettingsService.Instance.Data.controls.vibration, value =>
            {
                SettingsService.Instance.Data.controls.vibration = value;
                SaveSettings();
            });

            UiKit.Paragraph(_content,
                "The left pad always drives tank controls: up/down walks along the facing, left/right turns "
                + "on the spot. The buttons on the right press the same actions you bind above.");
        }

        static string TouchModeLabel(int mode)
        {
            switch (mode)
            {
                case TouchSettings.ModeAlways: return "Always On";
                case TouchSettings.ModeNever: return "Never";
                default: return "Automatic";
            }
        }

        // -- system -----------------------------------------------------------

        void BuildSystemTab()
        {
            SessionSettings session = SettingsService.Instance.Data.session;

            AddToggle("Skip Main Menu On Start", () => session.skipMenu, value =>
            {
                session.skipMenu = value;
                SaveSettings();
            });

            UiKit.Divider(_content);
            UiKit.Header(_content, "RESET");

            UiKit.Button(_content, "Reset All Settings To Defaults", () =>
            {
                SettingsService.Instance.ResetEverything();
                _pending = SettingsService.Instance.Data.display.Clone();
                RebuildTab();
                SetHint("Everything restored to defaults.");
            }, 56f);

            UiKit.Paragraph(_content,
                "Defaults live in the code that owns them: display, audio, controls and touch in "
                + "Assets/Scripts/Core/SettingsService.cs, bindings in Assets/Scripts/Core/InputService.cs.");
        }

        // -- small row helpers -------------------------------------------------

        void AddStepper(string title, Func<string> getText, Action<int> onStep, float height = 50f)
        {
            UiKit.Stepper(_content, title, getText, step =>
            {
                onStep(step);
                RefreshSteppers();
                RefreshDisplayButtonsIfNeeded();
            }, height);
        }

        void RefreshDisplayButtonsIfNeeded()
        {
            if (_tab == TabDisplay) RefreshDisplayButtons();
        }

        void AddToggle(string title, Func<bool> get, Action<bool> set, float height = 50f)
        {
            var row = UiKit.Row(_content, 16f, height);
            var label = UiKit.Label(row, title, 22, UiKit.TextPrimary, TextAnchor.MiddleLeft);
            UiKit.SetFlexibleWidth(label.rectTransform, 1f);
            UiKit.SetMinWidth(label.rectTransform, 200f);

            Button button = UiKit.Toggle(row, get, value =>
            {
                set(value);
                RefreshSteppers();
                RefreshDisplayButtonsIfNeeded();
            }, 46f);
            UiKit.SetMinWidth(button.GetComponent<RectTransform>(), 180f);
        }

        void SaveSettings()
        {
            SettingsService.Instance.Save();
            SetHint("Saved.");
        }
    }
}
