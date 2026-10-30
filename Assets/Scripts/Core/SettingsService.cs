using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Foundation
{
    [Serializable]
    public class DisplaySettings
    {
        public const int ModeWindowed = 0;
        public const int ModeBorderless = 1;
        public const int ModeExclusive = 2;

        public int mode = ModeWindowed;
        public int width = 1280;
        public int height = 720;
        public bool vsync = true;
        public int fpsLimit = 0;      // 0 = uncapped
        public int qualityLevel = -1; // -1 = leave Unity's default

        public DisplaySettings Clone()
        {
            return new DisplaySettings
            {
                mode = mode,
                width = width,
                height = height,
                vsync = vsync,
                fpsLimit = fpsLimit,
                qualityLevel = qualityLevel,
            };
        }

        public bool SameAs(DisplaySettings other)
        {
            if (other == null) return false;
            return mode == other.mode && width == other.width && height == other.height
                && vsync == other.vsync && fpsLimit == other.fpsLimit
                && qualityLevel == other.qualityLevel;
        }
    }

    [Serializable]
    public class AudioSettings
    {
        public float master = 0.8f;
        public float music = 0.7f;
        public float sfx = 0.8f;
        public float voice = 0.8f;
        public bool muteWhenUnfocused = false;
    }

    [Serializable]
    public class ControlSettings
    {
        public float turnSpeed = 2.2f;      // radians per second
        public float aimTurnSpeed = 1.4f;   // radians per second
        public float walkSpeed = 2.6f;      // metres per second
        public float runSpeed = 5.0f;       // metres per second
        public bool invertTurn = false;
        public bool simultaneousRun = true;
        public bool vibration = true;
    }

    [Serializable]
    public class TouchSettings
    {
        public const int ModeAuto = 0;
        public const int ModeAlways = 1;
        public const int ModeNever = 2;

        public int mode = ModeAuto;
        public float scale = 1.0f;
        public float opacity = 0.55f;
    }

    [Serializable]
    public class SessionSettings
    {
        public bool skipMenu = false;
    }

    [Serializable]
    public class BindingData
    {
        public string action = "";
        public int key = 0;        // KeyCode value, 0 = none
        public string axis = "";   // Input Manager axis name, empty for buttons
        public float dir = 1f;     // +1 / -1 for axis bindings
    }

    [Serializable]
    public class SettingsData
    {
        public int version = 1;
        public DisplaySettings display = new DisplaySettings();
        /// Last display configuration known to render successfully.
        public DisplaySettings displayFallback = new DisplaySettings();
        /// True while a freshly committed display setting has not survived a frame yet.
        public bool displayNeedsConfirm = false;
        public AudioSettings audio = new AudioSettings();
        public ControlSettings controls = new ControlSettings();
        public TouchSettings touch = new TouchSettings();
        public SessionSettings session = new SessionSettings();
        public List<BindingData> bindings = new List<BindingData>();
    }

    /// <summary>
    /// Owns the settings file, applies settings to the engine and exposes the
    /// staged-resolution flow used by the options screen.
    ///
    /// Display changes are NOT written to disk until <see cref="CommitDisplay"/>
    /// is called, because a resolution that cannot be read is unrecoverable.
    /// Everything else is saved as soon as it changes.
    /// </summary>
    public class SettingsService : MonoBehaviour
    {
        public const string FileName = "settings.json";

        /// Absolute width/height limits. Anything inside this range is accepted,
        /// which is what makes arbitrary resolutions possible.
        public const int MinWidth = 320;
        public const int MinHeight = 240;
        public const int MaxWidth = 16384;
        public const int MaxHeight = 16384;

        public static SettingsService Instance { get; private set; }

        public SettingsData Data { get; private set; } = new SettingsData();
        public DisplaySettings PendingDisplay { get; private set; }
        public bool HasStagedDisplay => _displayStaged;

        public event Action SettingsChanged;
        public event Action DisplayStaged;
        public event Action DisplayCommitted;
        public event Action DisplayReverted;

        string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        bool _displayStaged;
        DisplaySettings _backupDisplay;

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
            Load();
            ApplyAll();
        }

        // ---------------------------------------------------------------- disk

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    SettingsData loaded = JsonUtility.FromJson<SettingsData>(json);
                    if (loaded != null) Data = Repair(loaded);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Could not read {FilePath}: {e.Message}. Using defaults.");
                Data = new SettingsData();
            }

            Data.display = RepairDisplay(Data.display);
            Data.displayFallback = RepairDisplay(Data.displayFallback);

            // The previous session committed a display change and never survived a
            // frame with it (crash, black screen, unplugged monitor). Go back to
            // the last configuration that did render.
            if (Data.displayNeedsConfirm)
            {
                Debug.LogWarning("[Settings] Last resolution was never confirmed; restoring "
                                 + $"{Data.displayFallback.width}x{Data.displayFallback.height}.");
                Data.display = Data.displayFallback.Clone();
                Data.displayNeedsConfirm = false;
                Save();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Settings] Could not write {FilePath}: {e.Message}");
            }
        }

        static SettingsData Repair(SettingsData data)
        {
            if (data.display == null) data.display = new DisplaySettings();
            if (data.displayFallback == null) data.displayFallback = new DisplaySettings();
            if (data.audio == null) data.audio = new AudioSettings();
            if (data.controls == null) data.controls = new ControlSettings();
            if (data.touch == null) data.touch = new TouchSettings();
            if (data.session == null) data.session = new SessionSettings();
            if (data.bindings == null) data.bindings = new List<BindingData>();
            data.display = RepairDisplay(data.display);
            return data;
        }

        static DisplaySettings RepairDisplay(DisplaySettings d)
        {
            if (d == null) d = new DisplaySettings();
            d.mode = Mathf.Clamp(d.mode, 0, 2);
            d.width = Mathf.Clamp(d.width <= 0 ? 1280 : d.width, MinWidth, MaxWidth);
            d.height = Mathf.Clamp(d.height <= 0 ? 720 : d.height, MinHeight, MaxHeight);
            d.fpsLimit = Mathf.Clamp(d.fpsLimit, 0, 1000);
            d.qualityLevel = Mathf.Clamp(d.qualityLevel, -1, Mathf.Max(0, QualitySettings.names.Length - 1));
            return d;
        }

        // -------------------------------------------------------------- apply

        public void ApplyAll()
        {
            ApplyDisplay(Data.display, false);
            ApplyAudio();
            SettingsChanged?.Invoke();
        }

        /// Puts a display configuration on screen without touching the file.
        public void ApplyDisplay(DisplaySettings display, bool isPreview)
        {
            if (!isPreview)
            {
                _displayStaged = false;
                PendingDisplay = null;
            }

            if (Application.isMobilePlatform)
            {
                // Phones own their screen; only frame rate and quality apply.
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            else
            {
                FullScreenMode mode = FullScreenMode.Windowed;
                if (display.mode == DisplaySettings.ModeBorderless) mode = FullScreenMode.FullScreenWindow;
                else if (display.mode == DisplaySettings.ModeExclusive) mode = FullScreenMode.ExclusiveFullScreen;

                int w = Mathf.Clamp(display.width, MinWidth, MaxWidth);
                int h = Mathf.Clamp(display.height, MinHeight, MaxHeight);
                Screen.SetResolution(w, h, mode);
            }

            QualitySettings.vSyncCount = display.vsync ? 1 : 0;
            Application.targetFrameRate = display.fpsLimit <= 0 ? -1 : display.fpsLimit;

            if (display.qualityLevel >= 0 && display.qualityLevel < QualitySettings.names.Length)
            {
                QualitySettings.SetQualityLevel(display.qualityLevel, true);
            }
        }

        public void ApplyAudio()
        {
            AudioService.Instance?.ApplyVolumes(Data.audio);
        }

        // ----------------------------------------------------- staged display

        /// Preview a display configuration. The previous one is kept so it can be
        /// restored with <see cref="RevertDisplay"/>.
        public void StageDisplay(DisplaySettings display)
        {
            if (!_displayStaged)
            {
                _backupDisplay = Data.display.Clone();
                _displayStaged = true;
            }
            PendingDisplay = RepairDisplay(display.Clone());
            ApplyDisplay(PendingDisplay, true);
            DisplayStaged?.Invoke();
        }

        /// Writes the staged configuration to disk. Called when the player
        /// confirms they can actually see the screen.
        public void CommitDisplay()
        {
            if (!_displayStaged || PendingDisplay == null) return;
            Data.displayFallback = Data.display.Clone();
            Data.display = PendingDisplay.Clone();
            Data.displayNeedsConfirm = true;
            _displayStaged = false;
            PendingDisplay = null;
            Save();
            DisplayCommitted?.Invoke();
        }

        /// Throws the staged configuration away and restores the last good one.
        public void RevertDisplay()
        {
            if (!_displayStaged) return;
            Data.displayNeedsConfirm = false;
            ApplyDisplay(_backupDisplay, false);
            _displayStaged = false;
            PendingDisplay = null;
            DisplayReverted?.Invoke();
        }

        /// Called once the game has rendered a few seconds with the committed
        /// display settings: from now on they are considered safe to restore.
        public void ConfirmDisplayRendered()
        {
            if (!Data.displayNeedsConfirm) return;
            Data.displayNeedsConfirm = false;
            Data.displayFallback = Data.display.Clone();
            Save();
        }

        // ------------------------------------------------------------- resets

        public void ResetEverything()
        {
            Data = new SettingsData();
            InputService.Instance?.ResetAllBindings();
            AudioService.Instance?.ApplyVolumes(Data.audio);
            ApplyDisplay(Data.display, false);
            Save();
            SettingsChanged?.Invoke();
        }

        public void ResetDisplay()
        {
            Data.display = new DisplaySettings();
            ApplyDisplay(Data.display, false);
            Save();
            SettingsChanged?.Invoke();
        }

        /// Common desktop resolutions, de-duplicated and sorted largest first.
        public static List<Vector2Int> DetectedResolutions()
        {
            var list = new List<Vector2Int>();
            Resolution[] resolutions = Screen.resolutions;
            for (int i = 0; i < resolutions.Length; i++)
            {
                var size = new Vector2Int(resolutions[i].width, resolutions[i].height);
                if (!list.Contains(size)) list.Add(size);
            }
            if (list.Count == 0)
            {
                list.Add(new Vector2Int(Screen.width, Screen.height));
                list.Add(new Vector2Int(1920, 1080));
                list.Add(new Vector2Int(1280, 720));
            }
            list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
            return list;
        }
    }
}
