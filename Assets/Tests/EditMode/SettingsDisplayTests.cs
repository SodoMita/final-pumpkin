using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests
{
    /// <summary>
    /// The staged-resolution state machine. These are the tests that matter most:
    /// a bug here means a player can lose their screen.
    /// </summary>
    public class SettingsDisplayTests
    {
        GameObject _host;
        SettingsService _settings;
        string _filePath;

        [SetUp]
        public void SetUp()
        {
            _filePath = Path.Combine(Application.persistentDataPath, SettingsService.FileName);
            DeleteSettingsFile();
            _host = new GameObject("SettingsHost");
            _settings = _host.AddComponent<SettingsService>();
            _settings.Init();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            DeleteSettingsFile();
        }

        static void DeleteSettingsFile()
        {
            string path = Path.Combine(Application.persistentDataPath, SettingsService.FileName);
            if (File.Exists(path)) File.Delete(path);
        }

        [Test]
        public void Staging_DoesNotTouchSavedData()
        {
            DisplaySettings original = _settings.Data.display.Clone();
            var candidate = new DisplaySettings { width = 1024, height = 768, vsync = false };

            _settings.StageDisplay(candidate);

            Assert.IsTrue(_settings.HasStagedDisplay, "staging should be tracked");
            Assert.IsTrue(_settings.Data.display.SameAs(original), "saved data must not change while staged");
            Assert.AreEqual(1024, _settings.PendingDisplay.width);
        }

        [Test]
        public void Staging_DoesNotWriteTheFile()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1000, height = 700 });
            Assert.IsFalse(File.Exists(_filePath), "nothing may reach disk before confirmation");
        }

        [Test]
        public void Revert_RestoresPreviousDisplayAndClearsStaging()
        {
            DisplaySettings original = _settings.Data.display.Clone();
            _settings.StageDisplay(new DisplaySettings { width = 640, height = 480 });

            _settings.RevertDisplay();

            Assert.IsFalse(_settings.HasStagedDisplay);
            Assert.IsNull(_settings.PendingDisplay);
            Assert.IsTrue(_settings.Data.display.SameAs(original));
            Assert.IsFalse(File.Exists(_filePath), "a reverted preview must not be persisted");
        }

        [Test]
        public void Commit_WritesDataAndDisk()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1024, height = 768, fpsLimit = 60 });

            _settings.CommitDisplay();

            Assert.IsFalse(_settings.HasStagedDisplay);
            Assert.AreEqual(1024, _settings.Data.display.width);
            Assert.AreEqual(768, _settings.Data.display.height);
            Assert.AreEqual(60, _settings.Data.display.fpsLimit);
            Assert.IsTrue(File.Exists(_filePath), "a confirmed change must be saved");
        }

        [Test]
        public void Commit_SetsTheUnconfirmedFlag()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1024, height = 768 });
            _settings.CommitDisplay();

            Assert.IsTrue(_settings.Data.displayNeedsConfirm,
                "a new resolution is not trusted until the game renders with it");
            Assert.GreaterOrEqual(_settings.Data.displayFallback.width, SettingsService.MinWidth,
                "fallback stays usable");
        }

        [Test]
        public void ConfirmRendered_ClearsTheFlag()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1024, height = 768 });
            _settings.CommitDisplay();

            _settings.ConfirmDisplayRendered();

            Assert.IsFalse(_settings.Data.displayNeedsConfirm);
            Assert.AreEqual(1024, _settings.Data.displayFallback.width);
        }

        [Test]
        public void UnconfirmedResolution_IsRolledBackOnNextLaunch()
        {
            // Simulate: the player committed 700x500, the game never rendered, then crashed.
            _settings.Data.displayFallback = new DisplaySettings { width = 1280, height = 720 };
            _settings.Data.display = new DisplaySettings { width = 700, height = 500 };
            _settings.Data.displayNeedsConfirm = true;
            _settings.Save();

            // A relaunch means the old service is gone before the new one exists.
            Object.DestroyImmediate(_host);
            _host = null;

            var secondHost = new GameObject("SettingsHost2");
            var second = secondHost.AddComponent<SettingsService>();
            second.Init();

            Assert.AreEqual(1280, second.Data.display.width, "must fall back to the last good resolution");
            Assert.AreEqual(720, second.Data.display.height);
            Assert.IsFalse(second.Data.displayNeedsConfirm);

            Object.DestroyImmediate(secondHost);
        }

        [Test]
        public void ArbitraryResolution_IsAcceptedWithinLimits()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1111, height = 777 });

            Assert.AreEqual(1111, _settings.PendingDisplay.width);
            Assert.AreEqual(777, _settings.PendingDisplay.height);
        }

        [Test]
        public void OutOfRangeResolution_IsClamped()
        {
            _settings.StageDisplay(new DisplaySettings { width = 20, height = 999999 });

            Assert.AreEqual(SettingsService.MinWidth, _settings.PendingDisplay.width);
            Assert.AreEqual(SettingsService.MaxHeight, _settings.PendingDisplay.height);
        }

        [Test]
        public void ResetDisplay_ReturnsToDefaultsAndSaves()
        {
            _settings.StageDisplay(new DisplaySettings { width = 1024, height = 768 });
            _settings.CommitDisplay();

            _settings.ResetDisplay();

            Assert.AreEqual(new DisplaySettings().width, _settings.Data.display.width);
            Assert.IsTrue(File.Exists(_filePath));
        }
    }
}
