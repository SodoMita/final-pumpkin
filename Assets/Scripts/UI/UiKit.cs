using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>
    /// Builds uGUI at runtime so the project needs no prefabs, no scene wiring
    /// and no imported assets. Every screen in the game is made of these calls.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color BackgroundColor = new Color(0.030f, 0.032f, 0.040f, 1f);
        public static readonly Color Overlay = new Color(0.050f, 0.055f, 0.065f, 0.97f);
        public static readonly Color PanelColor = new Color(0.075f, 0.080f, 0.095f, 0.96f);
        public static readonly Color ButtonColor = new Color(0.145f, 0.150f, 0.170f, 1f);
        public static readonly Color ButtonHighlight = new Color(0.220f, 0.215f, 0.190f, 1f);
        public static readonly Color Accent = new Color(0.850f, 0.680f, 0.420f, 1f);
        public static readonly Color TextPrimary = new Color(0.930f, 0.910f, 0.860f, 1f);
        public static readonly Color TextMuted = new Color(0.620f, 0.650f, 0.700f, 1f);
        public static readonly Color Danger = new Color(0.850f, 0.450f, 0.400f, 1f);

        const int ReferenceWidth = 1920;
        const int ReferenceHeight = 1080;

        static Font _font;
        static Sprite _roundedSprite;
        static Sprite _circleSprite;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // Unity 2022+ names the built-in font LegacyRuntime.ttf;
                    // older versions call it Arial.ttf. Try both, never throw.
                    string[] candidates = { "LegacyRuntime.ttf", "Arial.ttf" };
                    for (int i = 0; i < candidates.Length && _font == null; i++)
                    {
                        try
                        {
                            _font = Resources.GetBuiltinResource<Font>(candidates[i]);
                        }
                        catch (Exception)
                        {
                            _font = null;
                        }
                    }
                    if (_font == null) Debug.LogWarning("[UiKit] No built-in font found; UI text will be invisible.");
                }
                return _font;
            }
        }

        // ------------------------------------------------------------- sprites

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite == null) _roundedSprite = BuildRoundedSprite(64, 14);
                return _roundedSprite;
            }
        }

        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite == null) _circleSprite = BuildCircleSprite(128);
                return _circleSprite;
            }
        }

        static Sprite BuildRoundedSprite(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - x, x - (size - 1 - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - y, y - (size - 1 - radius)));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        static Sprite BuildCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ----------------------------------------------------------- canvases

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // -------------------------------------------------------- primitives

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static Image AddImage(RectTransform rect, Color color, bool sliced = false, Sprite sprite = null)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sliced)
            {
                image.sprite = sprite != null ? sprite : RoundedSprite;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        public static RectTransform Background(Transform parent, Color color)
        {
            var rect = Stretch(NewRect("Background", parent));
            var image = AddImage(rect, color);
            image.raycastTarget = true;
            return rect;
        }

        public static Text Label(Transform parent, string text, int size = 24,
            Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rect = NewRect("Label", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = color ?? TextPrimary;
            label.alignment = anchor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static RectTransform Row(Transform parent, float spacing = 16f, float minHeight = 0f)
        {
            var rect = NewRect("Row", parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = true;
            if (minHeight > 0f) SetMinHeight(rect, minHeight);
            return rect;
        }

        public static RectTransform Column(Transform parent, float spacing = 12f, float padding = 0f)
        {
            var rect = NewRect("Column", parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = true;
            return rect;
        }

        public static LayoutElement SetMinHeight(RectTransform rect, float height)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return element;
        }

        public static LayoutElement SetMinWidth(RectTransform rect, float width)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            return element;
        }

        public static LayoutElement SetFlexibleHeight(RectTransform rect, float weight = 1f)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleHeight = weight;
            return element;
        }

        public static LayoutElement SetFlexibleWidth(RectTransform rect, float weight = 1f)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = weight;
            return element;
        }

        // ----------------------------------------------------------- buttons

        public static Button Button(Transform parent, string text, Action onClick,
            float height = 52f, float minWidth = 0f)
        {
            var rect = NewRect("Button", parent);
            if (height > 0f) SetMinHeight(rect, height);
            if (minWidth > 0f) SetMinWidth(rect, minWidth);

            var image = AddImage(rect, ButtonColor, true);
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.12f, 1.05f, 1f);
            colors.pressedColor = new Color(0.85f, 0.82f, 0.75f, 1f);
            colors.selectedColor = new Color(1.15f, 1.12f, 1.05f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.4f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var label = Label(rect, text, 24, TextPrimary, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 12f);

            return button;
        }

        /// A toggle drawn as a normal button that shows its own state.
        public static Button Toggle(Transform parent, Func<bool> getValue, Action<bool> onToggle,
            float height = 44f)
        {
            Button button = null;
            button = Button(parent, "", () =>
            {
                onToggle(!getValue());
                RefreshToggle(button, getValue());
            }, height);
            RefreshToggle(button, getValue());
            return button;
        }

        public static void RefreshToggle(Button button, bool value)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = value ? "On" : "Off";
            var image = button.targetGraphic as Image;
            if (image != null) image.color = value ? ButtonHighlight : ButtonColor;
        }

        /// <summary>
        /// A row with a title on the left and "&lt; value &gt;" on the right.
        /// Used for every enumerated or stepped setting.
        /// </summary>
        public static RectTransform Stepper(Transform parent, string title, Func<string> getText,
            Action<int> onStep, float height = 48f)
        {
            var row = Row(parent, 12f, height);
            SetMinHeight(row, height);

            var label = Label(row, title, 22, TextPrimary, TextAnchor.MiddleLeft);
            SetFlexibleWidth(label.rectTransform, 1f);
            SetMinWidth(label.rectTransform, 200f);

            Button left = Button(row, "<", () => onStep(-1), height, 56f);
            var valueLabel = Label(row, getText(), 22, Accent, TextAnchor.MiddleCenter);
            SetMinWidth(valueLabel.rectTransform, 260f);
            Button right = Button(row, ">", () => onStep(1), height, 56f);

            // Keep a marker so callers can refresh the text.
            var refresher = row.gameObject.AddComponent<StepperRefresher>();
            refresher.Label = valueLabel;
            refresher.GetText = getText;
            return row;
        }

        public static void RefreshAllSteppers(RectTransform root)
        {
            var refreshers = root.GetComponentsInChildren<StepperRefresher>(true);
            for (int i = 0; i < refreshers.Length; i++) refreshers[i].Refresh();
        }

        public static void RefreshStepper(RectTransform row)
        {
            var refresher = row.GetComponent<StepperRefresher>();
            refresher?.Refresh();
        }

        /// Numeric field with - / + buttons and free typing. Used for arbitrary
        /// resolution entry.
        public static InputField NumberField(Transform parent, string title, int value, int min, int max,
            Action<int> onChanged, float height = 44f)
        {
            var row = Row(parent, 10f, height);
            SetMinHeight(row, height);

            var label = Label(row, title, 22, TextPrimary, TextAnchor.MiddleLeft);
            SetFlexibleWidth(label.rectTransform, 1f);
            SetMinWidth(label.rectTransform, 200f);

            Button minus = Button(row, "-", () => { }, height, 52f);
            var field = NumberInput(row, value, min, max, onChanged, height, 150f);
            Button plus = Button(row, "+", () => { }, height, 52f);

            minus.onClick.AddListener(() =>
            {
                field.text = Mathf.Clamp(ParseInt(field.text, value) - 1, min, max).ToString();
            });
            plus.onClick.AddListener(() =>
            {
                field.text = Mathf.Clamp(ParseInt(field.text, value) + 1, min, max).ToString();
            });

            return field;
        }

        public static InputField NumberInput(Transform parent, int value, int min, int max,
            Action<int> onChanged, float height = 44f, float minWidth = 150f)
        {
            var rect = NewRect("InputField", parent);
            SetMinHeight(rect, height);
            SetMinWidth(rect, minWidth);

            var background = AddImage(rect, new Color(0.10f, 0.11f, 0.13f, 1f), true);
            background.raycastTarget = true;

            var textRect = Stretch(NewRect("Text", rect), 10f);
            var text = textRect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = 22;
            text.color = TextPrimary;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            text.raycastTarget = false;

            var placeholderRect = Stretch(NewRect("Placeholder", rect), 10f);
            var placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.font = Font;
            placeholder.fontSize = 22;
            placeholder.color = new Color(0.5f, 0.52f, 0.56f, 1f);
            placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.text = min.ToString();
            placeholder.raycastTarget = false;

            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value.ToString();

            field.onEndEdit.AddListener(raw =>
            {
                int parsed = Mathf.Clamp(ParseInt(raw, value), min, max);
                field.text = parsed.ToString();
                onChanged?.Invoke(parsed);
            });

            return field;
        }

        public static int ParseInt(string raw, int fallback)
        {
            int value;
            return int.TryParse(raw, out value) ? value : fallback;
        }

        // ------------------------------------------------------------ scroll

        public static RectTransform ScrollColumn(Transform parent, out RectTransform content, float spacing = 10f)
        {
            var viewport = NewRect("ScrollView", parent);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var maskImage = AddImage(Stretch(NewRect("Viewport", viewport)), new Color(0, 0, 0, 0.001f));
            maskImage.raycastTarget = true;
            var mask = maskImage.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            content = NewRect("Content", maskImage.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(4, 12, 4, 16);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = true;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = maskImage.rectTransform;
            scroll.content = content;
            return viewport;
        }

        public static void ScrollToTop(RectTransform scrollRect)
        {
            var scroll = scrollRect.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        // ----------------------------------------------------------- helpers

        public static void Clear(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                // Detach first so layout groups do not render the old and the new
                // rows in the same frame; Destroy alone is deferred to end of frame.
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public static void Divider(Transform parent, float height = 1f, Color? color = null)
        {
            var rect = NewRect("Divider", parent);
            SetMinHeight(rect, height);
            AddImage(rect, color ?? new Color(1f, 1f, 1f, 0.10f));
        }

        public static void Spacer(Transform parent, float height)
        {
            var rect = NewRect("Spacer", parent);
            SetMinHeight(rect, height);
        }

        public static void Header(Transform parent, string text)
        {
            Spacer(parent, 8f);
            Label(parent, text, 20, Accent, TextAnchor.MiddleLeft);
        }

        public static void Paragraph(Transform parent, string text, int size = 18)
        {
            var label = Label(parent, text, size, TextMuted, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

    }
}
