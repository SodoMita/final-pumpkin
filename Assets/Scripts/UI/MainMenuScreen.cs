using UnityEngine;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>Main menu: Start, Options, Quit. Built entirely from code.</summary>
    public class MainMenuScreen : MonoBehaviour
    {
        public const string Version = "0.2.0";

        Canvas _canvas;
        Button _firstButton;

        public void Build()
        {
            _canvas = UiKit.CreateCanvas("MainMenuCanvas", 0, transform);
            UiKit.Background(_canvas.transform, UiKit.BackgroundColor);

            var safeArea = UiKit.Stretch(UiKit.NewRect("SafeArea", _canvas.transform));
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            // Centred panel.
            var panel = UiKit.NewRect("Panel", safeArea);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 640f);
            UiKit.AddImage(panel, UiKit.PanelColor, true);

            var column = UiKit.Stretch(UiKit.Column(panel, 16f, 48f));
            column.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;

            UiKit.Label(column, "TANK CONTROLS", 52, UiKit.TextPrimary, TextAnchor.MiddleCenter);
            UiKit.Label(column, "fixed cameras  /  self-relative movement  /  desktop + mobile",
                20, UiKit.TextMuted, TextAnchor.MiddleCenter);
            UiKit.Spacer(column, 24f);

            _firstButton = UiKit.Button(column, "Start", () => GameManager.Instance.StartGame(), 64f);
            UiKit.Button(column, "Options", OpenOptions, 64f);
            UiKit.Button(column, "Quit", () => GameManager.Instance.QuitApplication(), 64f);

            UiKit.Spacer(column, 16f);
            UiKit.Divider(column);
            UiKit.Label(column, $"foundation build {Version} - no content", 18, new Color(0.45f, 0.47f, 0.52f),
                TextAnchor.MiddleCenter);

            _firstButton.Select();
        }

        void OpenOptions()
        {
            OptionsScreen.Create(transform);
        }

        void Update()
        {
            if (OptionsScreen.AnyOpen) return;
            if (InputService.Instance != null && InputService.Instance.GetPressed(GameAction.Pause))
            {
                // Escape on the main menu quits on desktop, does nothing on mobile.
                if (!Application.isMobilePlatform) GameManager.Instance.QuitApplication();
            }
        }
    }
}
