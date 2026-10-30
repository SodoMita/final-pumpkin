using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>
    /// On-screen controls for phones and tablets.
    ///
    /// Left: an analog pad that writes into InputService.SetVirtualAnalog, so the
    /// player reads touch, keyboard and gamepad through the exact same axis.
    /// Right: buttons that hold the same GameAction the keyboard does, so
    /// rebinding also rebinds the touch buttons.
    /// </summary>
    public class TouchOverlay : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _safeArea;
        RectTransform _pad;
        RectTransform _padKnob;
        RectTransform _buttonGrid;
        TouchPad _padInput;

        readonly List<GameObject> _buttons = new List<GameObject>();

        public void Build()
        {
            _canvas = UiKit.CreateCanvas("TouchCanvas", 10, transform);
            _safeArea = UiKit.Stretch(UiKit.NewRect("SafeArea", _canvas.transform));
            _safeArea.gameObject.AddComponent<SafeAreaFitter>();

            BuildPad();
            BuildButtons();

            SettingsService.Instance.SettingsChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (SettingsService.Instance != null) SettingsService.Instance.SettingsChanged -= Refresh;
            if (InputService.Instance != null) InputService.Instance.ClearVirtualInput();
        }

        void Refresh()
        {
            TouchSettings touch = SettingsService.Instance.Data.touch;
            bool visible = ShouldShow(touch);

            _canvas.enabled = visible;
            if (!visible)
            {
                InputService.Instance?.ClearVirtualInput();
                return;
            }

            float scale = Mathf.Clamp(touch.scale, 0.6f, 1.8f);
            var group = _canvas.GetComponent<CanvasGroup>();
            if (group == null) group = _canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp(touch.opacity, 0.2f, 1f);

            Layout(scale);
        }

        static bool ShouldShow(TouchSettings touch)
        {
            if (touch.mode == TouchSettings.ModeAlways) return true;
            if (touch.mode == TouchSettings.ModeNever) return false;
            return Application.isMobilePlatform || Input.touchSupported;
        }

        void Layout(float scale)
        {
            float margin = 28f * scale;
            Vector2 safeSize = _safeArea.rect.size;
            if (safeSize.x <= 0f) safeSize = new Vector2(1920f, 1080f);

            float padSide = 380f * scale;
            _pad.sizeDelta = new Vector2(padSide, padSide);
            _pad.anchorMin = new Vector2(0f, 0f);
            _pad.anchorMax = new Vector2(0f, 0f);
            _pad.pivot = new Vector2(0f, 0f);
            _pad.anchoredPosition = new Vector2(margin, margin);

            float knob = padSide * 0.40f;
            _padKnob.sizeDelta = new Vector2(knob, knob);
            if (_padInput != null)
            {
                _padInput.Radius = padSide * 0.5f;
                _padInput.Knob = _padKnob;
                _padInput.Center = new Vector2(padSide * 0.5f, padSide * 0.5f);
                _padInput.KnobSize = knob;
            }

            float diameter = 170f * scale;
            float spacing = 16f * scale;
            var grid = _buttonGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(diameter, diameter);
            grid.spacing = new Vector2(spacing, spacing);

            var gridSize = new Vector2(diameter * 2f + spacing, diameter * 2f + spacing);
            _buttonGrid.sizeDelta = gridSize;
            _buttonGrid.anchorMin = new Vector2(1f, 0f);
            _buttonGrid.anchorMax = new Vector2(1f, 0f);
            _buttonGrid.pivot = new Vector2(1f, 0f);
            _buttonGrid.anchoredPosition = new Vector2(-margin, margin);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var round = _buttons[i].GetComponent<RoundButtonGraphic>();
                if (round != null) round.Radius = diameter * 0.5f;
            }
        }

        // -- pad --------------------------------------------------------------

        void BuildPad()
        {
            _pad = UiKit.NewRect("TouchPad", _safeArea);
            var background = _pad.gameObject.AddComponent<Image>();
            background.sprite = UiKit.CircleSprite;
            background.color = new Color(1f, 1f, 1f, 0.10f);
            background.raycastTarget = true;

            _padKnob = UiKit.NewRect("Knob", _pad);
            _padKnob.anchorMin = new Vector2(0f, 0f);
            _padKnob.anchorMax = new Vector2(0f, 0f);
            _padKnob.pivot = new Vector2(0.5f, 0.5f);
            var knobImage = _padKnob.gameObject.AddComponent<Image>();
            knobImage.sprite = UiKit.CircleSprite;
            knobImage.color = new Color(1f, 1f, 1f, 0.22f);
            knobImage.raycastTarget = false;

            _padInput = _pad.gameObject.AddComponent<TouchPad>();
            _padInput.Pad = _pad;
            _padInput.Knob = _padKnob;
        }

        // -- buttons ----------------------------------------------------------

        void BuildButtons()
        {
            _buttonGrid = UiKit.NewRect("TouchButtons", _safeArea);
            var group = _buttonGrid.gameObject.AddComponent<GridLayoutGroup>();
            group.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            group.constraintCount = 2;
            group.childAlignment = TextAnchor.LowerRight;

            AddTouchButton(GameAction.Aim, "AIM", new Color(0.78f, 0.74f, 0.45f, 0.55f));
            AddTouchButton(GameAction.Run, "RUN", new Color(0.52f, 0.60f, 0.78f, 0.55f));
            AddTouchButton(GameAction.Action, "ACT", new Color(0.82f, 0.50f, 0.46f, 0.60f));
            AddTouchButton(GameAction.Pause, "II", new Color(0.60f, 0.60f, 0.60f, 0.50f));
        }

        void AddTouchButton(GameAction action, string label, Color tint)
        {
            var rect = UiKit.NewRect("Touch" + action, _buttonGrid);

            var graphic = rect.gameObject.AddComponent<RoundButtonGraphic>();
            graphic.color = tint;
            graphic.raycastTarget = true;

            var textRect = UiKit.Stretch(UiKit.NewRect("Label", rect), 4f);
            var text = textRect.gameObject.AddComponent<Text>();
            text.font = UiKit.Font;
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 0.92f);
            text.raycastTarget = false;
            text.text = label;

            var button = rect.gameObject.AddComponent<TouchActionButton>();
            button.Action = action;
            button.Graphic = graphic;

            _buttons.Add(rect.gameObject);
        }

        // -- widgets ----------------------------------------------------------

        /// Analog stick that writes -1..1 into InputService.
        public class TouchPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public RectTransform Pad;
            public RectTransform Knob;
            public float Radius = 190f;
            public float KnobSize = 76f;
            public Vector2 Center;

            Vector2 _value;

            public void OnPointerDown(PointerEventData eventData)
            {
                UpdateFromScreen(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                UpdateFromScreen(eventData);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _value = Vector2.zero;
                Push();
            }

            void UpdateFromScreen(PointerEventData eventData)
            {
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Pad, eventData.position,
                        eventData.pressEventCamera, out local))
                {
                    return;
                }

                Vector2 offset = local - Center;
                Vector2 normalized = offset / Mathf.Max(Radius * 0.75f, 1f);
                if (normalized.magnitude > 1f) normalized = normalized.normalized;
                _value = normalized;
                Push();
            }

            void Push()
            {
                if (Knob != null)
                {
                    Vector2 limit = _value * (Radius * 0.55f);
                    Knob.anchoredPosition = Center + limit;
                }

                // Pad is anchored bottom-left, so +y local means up on screen,
                // which is forward. Deadzone keeps touch from drifting.
                float move = Mathf.Abs(_value.y) > 0.18f ? _value.y : 0f;
                float turn = Mathf.Abs(_value.x) > 0.18f ? -_value.x : 0f;
                InputService.Instance?.SetVirtualAnalog(move, turn);
            }
        }

        /// Holds a GameAction while a finger is on it.
        public class TouchActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public GameAction Action;
            public RoundButtonGraphic Graphic;

            int _pointers;

            public void OnPointerDown(PointerEventData eventData)
            {
                _pointers++;
                if (_pointers == 1)
                {
                    InputService.Instance?.SetVirtualHold(Action, true);
                    if (Graphic != null) Graphic.Held = true;
                    AudioService.Vibrate();
                }
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _pointers = Mathf.Max(0, _pointers - 1);
                if (_pointers == 0)
                {
                    InputService.Instance?.SetVirtualHold(Action, false);
                    if (Graphic != null) Graphic.Held = false;
                }
            }
        }

        /// Circle drawing that doubles as the button's raycast target.
        public class RoundButtonGraphic : MaskableGraphic
        {
            public float Radius = 85f;

            bool _held;

            public bool Held
            {
                get { return _held; }
                set
                {
                    _held = value;
                    SetVerticesDirty();
                }
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect rect = GetPixelAdjustedRect();
                Vector2 center = rect.center;
                float radius = Mathf.Min(Radius, Mathf.Min(rect.width, rect.height) * 0.5f);
                Color32 color32 = Held ? (Color32)(color * 1.35f) : (Color32)color;

                int segments = 40;
                vh.AddVert(center, color32, Vector2.zero);
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i / (float)segments * Mathf.PI * 2f;
                    var position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    vh.AddVert(position, color32, Vector2.zero);
                }
                for (int i = 1; i <= segments; i++)
                {
                    vh.AddTriangle(0, i, i + 1);
                }
            }

            protected override void OnRectTransformDimensionsChange()
            {
                base.OnRectTransformDimensionsChange();
                SetVerticesDirty();
            }
        }
    }
}
