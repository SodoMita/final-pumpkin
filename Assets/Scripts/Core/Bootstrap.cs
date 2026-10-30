using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Entry point. Runs before the first scene loads and creates the whole
    /// service stack, so the game does not depend on anything being wired into
    /// a scene. The only scene in the project is an empty bootstrap scene that
    /// exists purely so builds have something to load.
    /// </summary>
    public static class Bootstrap
    {
        public const string RootName = "[Foundation]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            if (Object.FindFirstObjectByType<GameManager>() != null) return;

            var root = new GameObject(RootName);
            Object.DontDestroyOnLoad(root);

            // Order matters: each service is available to the next one's Init.
            root.AddComponent<AudioService>();
            root.AddComponent<InputService>();
            var settings = root.AddComponent<SettingsService>();
            var game = root.AddComponent<GameManager>();

            root.AddComponent<AudioListener>();

            settings.Init();
            InputService.Instance.Init();
            game.Init();
        }
    }

    /// <summary>Owns the two screens the foundation has: menu and level.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public enum State
        {
            MainMenu,
            InLevel,
        }

        public State CurrentState { get; private set; } = State.MainMenu;

        GameObject _menuRoot;
        GameObject _levelRoot;
        float _displayConfirmTimer;

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
            if (SettingsService.Instance.Data.session.skipMenu) StartGame();
            else ShowMainMenu();
        }

        void Update()
        {
            // A display change is only trusted after the game has actually rendered
            // with it for a moment; otherwise the next launch restores the previous one.
            SettingsData data = SettingsService.Instance != null ? SettingsService.Instance.Data : null;
            if (data == null || !data.displayNeedsConfirm)
            {
                _displayConfirmTimer = 0f;
                return;
            }

            _displayConfirmTimer += Time.unscaledDeltaTime;
            if (_displayConfirmTimer >= 3f)
            {
                SettingsService.Instance.ConfirmDisplayRendered();
                _displayConfirmTimer = 0f;
            }
        }

        // ------------------------------------------------------------- screens

        public void ShowMainMenu()
        {
            Time.timeScale = 1f;
            InputService.Instance.ClearVirtualInput();
            DestroyLevel();

            _menuRoot = new GameObject("MainMenu");
            _menuRoot.transform.SetParent(transform, false);
            _menuRoot.AddComponent<MainMenuScreen>().Build();
            CurrentState = State.MainMenu;
        }

        public void StartGame()
        {
            Time.timeScale = 1f;
            InputService.Instance.ClearVirtualInput();
            DestroyMenu();
            DestroyLevel();

            _levelRoot = new GameObject("Level");
            LevelBuilder.Build(_levelRoot.transform);
            CurrentState = State.InLevel;
        }

        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void DestroyMenu()
        {
            if (_menuRoot != null) Destroy(_menuRoot);
            _menuRoot = null;
        }

        void DestroyLevel()
        {
            if (_levelRoot != null) Destroy(_levelRoot);
            _levelRoot = null;
            Time.timeScale = 1f;
        }
    }
}
