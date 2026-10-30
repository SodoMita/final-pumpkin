using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests
{
    /// <summary>Rebinding rules: defaults, conflicts, persistence, descriptions.</summary>
    public class InputBindingTests
    {
        GameObject _host;
        SettingsService _settings;
        InputService _input;

        [SetUp]
        public void SetUp()
        {
            DeleteSettingsFile();

            _host = new GameObject("InputHost");
            _settings = _host.AddComponent<SettingsService>();
            _settings.Init();

            var inputHost = new GameObject("InputServiceHost");
            inputHost.transform.SetParent(_host.transform);
            _input = inputHost.AddComponent<InputService>();
            _input.Init();
        }

        [TearDown]
        public void TearDown()
        {
            if (_input != null) _input.ClearVirtualInput();
            if (_host != null) Object.DestroyImmediate(_host);
            DeleteSettingsFile();
        }

        static void DeleteSettingsFile()
        {
            string path = Path.Combine(Application.persistentDataPath, SettingsService.FileName);
            if (File.Exists(path)) File.Delete(path);
        }

        [Test]
        public void Defaults_CoverEveryRebindableAction()
        {
            foreach (GameAction action in InputService.RebindableActions)
            {
                List<Binding> bindings = _input.GetBindings(action);
                Assert.Greater(bindings.Count, 0, action + " needs at least one default binding");
            }
        }

        [Test]
        public void MoveForward_HasKeyboardAndGamepadDefaults()
        {
            List<Binding> bindings = _input.GetBindings(GameAction.MoveForward);

            Assert.IsTrue(bindings.Exists(b => !b.IsAxis && b.key == KeyCode.W));
            Assert.IsTrue(bindings.Exists(b => b.IsAxis && b.axis == "Joy LY" && b.dir < 0f));
        }

        [Test]
        public void SetBinding_ReplacesOneSlotOnly()
        {
            int before = _input.GetBindings(GameAction.TurnLeft).Count;

            _input.SetBinding(GameAction.TurnLeft, 0, Binding.FromKey(KeyCode.J));

            List<Binding> after = _input.GetBindings(GameAction.TurnLeft);
            Assert.AreEqual(before, after.Count);
            Assert.AreEqual(KeyCode.J, after[0].key);
        }

        [Test]
        public void Conflict_IsDetectedForADifferentAction()
        {
            Binding space = Binding.FromKey(KeyCode.Space); // default Action binding

            GameAction? conflict = _input.FindConflict(space, GameAction.Run);

            Assert.IsTrue(conflict.HasValue);
            Assert.AreEqual(GameAction.Action, conflict.Value);
        }

        [Test]
        public void Conflict_IgnoresTheActionBeingRebound()
        {
            Binding space = Binding.FromKey(KeyCode.Space);

            GameAction? conflict = _input.FindConflict(space, GameAction.Action);

            Assert.IsFalse(conflict.HasValue, "binding an action to its own key is not a conflict");
        }

        [Test]
        public void NoConflict_ForAFreeKey()
        {
            GameAction? conflict = _input.FindConflict(Binding.FromKey(KeyCode.F7), GameAction.Run);
            Assert.IsFalse(conflict.HasValue);
        }

        [Test]
        public void ResetBinding_RestoresDefaults()
        {
            int original = _input.GetBindings(GameAction.Pause).Count;
            _input.SetBinding(GameAction.Pause, 0, Binding.FromKey(KeyCode.F8));

            _input.ResetBinding(GameAction.Pause);

            List<Binding> bindings = _input.GetBindings(GameAction.Pause);
            Assert.AreEqual(original, bindings.Count);
            Assert.AreEqual(KeyCode.Escape, bindings[0].key);
        }

        [Test]
        public void RemoveLastBinding_KeepsAtLeastOne()
        {
            _input.RemoveLastBinding(GameAction.Aim);
            _input.RemoveLastBinding(GameAction.Aim);
            _input.RemoveLastBinding(GameAction.Aim);

            Assert.GreaterOrEqual(_input.GetBindings(GameAction.Aim).Count, 1);
        }

        [Test]
        public void Bindings_ArePersistedAndReloaded()
        {
            _input.SetBinding(GameAction.Action, 0, Binding.FromKey(KeyCode.F));

            // Relaunch: the previous services must be gone before new ones exist.
            Object.DestroyImmediate(_host);
            _host = null;
            _input = null;

            var host = new GameObject("ReloadHost");
            var settings = host.AddComponent<SettingsService>();
            settings.Init();
            var inputHost = new GameObject("ReloadInput");
            inputHost.transform.SetParent(host.transform, false);
            var reloaded = inputHost.AddComponent<InputService>();
            reloaded.Init();

            Assert.AreEqual(KeyCode.F, reloaded.GetBindings(GameAction.Action)[0].key);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void Describe_RendersKeysAndAxes()
        {
            Assert.AreEqual("Space", InputService.Describe(Binding.FromKey(KeyCode.Space)));
            Assert.AreEqual("L Shift", InputService.Describe(Binding.FromKey(KeyCode.LeftShift)));
            Assert.AreEqual("Joy LY -", InputService.Describe(Binding.FromAxis("Joy LY", -1f)));
            Assert.AreEqual("Pad A", InputService.Describe(Binding.FromKey(KeyCode.JoystickButton0)));
        }

        [Test]
        public void VirtualInput_DrivesTheMovementAxes()
        {
            _input.SetVirtualAnalog(1f, 0f);
            Assert.AreEqual(1f, _input.MoveAxis, 0.001f);

            _input.SetVirtualAnalog(0f, 1f);
            Assert.AreEqual(1f, _input.TurnAxis, 0.001f);

            _input.SetVirtualAnalog(-2f, -2f);
            Assert.AreEqual(-1f, _input.MoveAxis, 0.001f, "analog values are clamped");
            Assert.AreEqual(-1f, _input.TurnAxis, 0.001f);

            _input.ClearVirtualInput();
            Assert.AreEqual(0f, _input.MoveAxis, 0.001f);
        }

        [Test]
        public void VirtualHold_DrivesDiscreteActions()
        {
            _input.SetVirtualHold(GameAction.Aim, true);
            Assert.IsTrue(_input.GetHeld(GameAction.Aim), "held while the finger is down");
            Assert.IsTrue(_input.GetPressed(GameAction.Aim), "the first frame counts as a press");

            // Pressed must fall away on the next poll even if the finger stays down.
            Assert.IsFalse(_input.GetPressed(GameAction.Aim), "press is edge-triggered");

            _input.SetVirtualHold(GameAction.Aim, false);
            Assert.IsFalse(_input.GetHeld(GameAction.Aim));
        }
    }
}
