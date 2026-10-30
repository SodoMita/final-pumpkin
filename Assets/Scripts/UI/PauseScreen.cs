using UnityEngine;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>In-level pause overlay: Resume, Options, Main Menu.</summary>
    public class PauseScreen : MonoBehaviour
    {
        Canvas _canvas;
        GameObject _root;
        Button _resumeButton;
        bool _open;

        public bool IsOpen => _open;

        public void Build()
        {
            _canvas = UiKit.CreateCanvas("PauseCanvas", 20, transform);

            _root = UiKit.Stretch(UiKit.NewRect("Root", _canvas.transform)).gameObject;
            var safeArea = UiKit.Stretch(UiKit.NewRect("SafeArea", _root.transform));
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            var dim = UiKit.Background(safeArea, new Color(0f, 0f, 0f, 0.65f));
            dim.GetComponent<Image>().raycastTarget = true;

            var panel = UiKit.NewRect("Panel", safeArea);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(620f, 480f);
            UiKit.AddImage(panel, UiKit.PanelColor, true);

            var column = UiKit.Stretch(UiKit.Column(panel, 14f, 44f));
            UiKit.Label(column, "PAUSED", 40, UiKit.TextPrimary, TextAnchor.MiddleLeft);
            UiKit.Spacer(column, 12f);

            _resumeButton = UiKit.Button(column, "Resume", Close, 62f);
            UiKit.Button(column, "Options", OpenOptions, 62f);
            UiKit.Button(column, "Main Menu", ToMainMenu, 62f);

            _root.SetActive(false);
        }

        void Update()
        {
            if (OptionsScreen.AnyOpen) return;
            if (Time.frameCount == OptionsScreen.ClosedAtFrame) return;
            if (InputService.Instance == null) return;

            if (InputService.Instance.GetPressed(GameAction.Pause))
            {
                if (_open) Close();
                else Open();
            }
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            InputService.Instance.ClearVirtualInput();
            _root.SetActive(true);
            Time.timeScale = 0f;
            if (_resumeButton != null) _resumeButton.Select();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = 1f;
            DestroyOptions();
            _root.SetActive(false);
        }

        void OpenOptions()
        {
            if (OptionsScreen.AnyOpen) return;
            OptionsScreen.Create(transform);
        }

        void DestroyOptions()
        {
            OptionsScreen[] screens = GetComponentsInChildren<OptionsScreen>(true);
            for (int i = 0; i < screens.Length; i++) Destroy(screens[i].gameObject);
        }

        void ToMainMenu()
        {
            Time.timeScale = 1f;
            _open = false;
            GameManager.Instance.ShowMainMenu();
        }

        void OnDestroy()
        {
            // Never leave the game frozen if this overlay is destroyed mid-pause.
            if (_open) Time.timeScale = 1f;
        }
    }
}
