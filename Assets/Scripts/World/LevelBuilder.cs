using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Foundation
{
    /// <summary>
    /// Builds the test level entirely from primitives: a floor, three pillars,
    /// a wall, a crate, the capsule player, three fixed cameras, a HUD hint,
    /// the pause overlay and the touch overlay. No prefabs, no scene content.
    /// </summary>
    public static class LevelBuilder
    {
        static readonly Color FloorColor = new Color(0.20f, 0.21f, 0.24f);
        static readonly Color PropColor = new Color(0.28f, 0.27f, 0.26f);
        static readonly Color PlayerColor = new Color(0.62f, 0.63f, 0.66f);
        static readonly Color NoseColor = new Color(0.85f, 0.55f, 0.35f);

        public static void Build(Transform root)
        {
            SetupEnvironment(root);

            Transform player = BuildPlayer(root);
            BuildGeometry(root);
            BuildCameras(root, player);
            BuildZombies(root, player);

            BuildHud(root);
            root.gameObject.AddComponent<PauseScreen>().Build();
            root.gameObject.AddComponent<TouchOverlay>().Build();
        }

        static void SetupEnvironment(Transform root)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.38f, 0.45f);
            RenderSettings.fog = false;

            var lightObject = new GameObject("Sun");
            lightObject.transform.SetParent(root, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
        }

        static Transform BuildPlayer(Transform root)
        {
            var player = new GameObject("Player");
            player.transform.SetParent(root, false);
            player.transform.position = new Vector3(0f, 0.2f, 2f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.02f;
            // Unity drops any Move() smaller than minMoveDistance (default 0.001).
            // At high frame rates a walk step is smaller than that and the character
            // silently stops moving, so let every millimetre through.
            controller.minMoveDistance = 0f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(player.transform, false);

            GameObject body = Primitive(PrimitiveType.Capsule, "Body", visual, false);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            MaterialFactory.Apply(body.GetComponent<Renderer>(), PlayerColor, 0.2f);

            GameObject nose = Primitive(PrimitiveType.Cube, "Nose", visual, false);
            nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.4f);
            nose.transform.localPosition = new Vector3(0f, 1.35f, 0.42f);
            MaterialFactory.Apply(nose.GetComponent<Renderer>(), NoseColor, 0.1f);

            GameObject feet = Primitive(PrimitiveType.Cylinder, "Feet", visual, false);
            feet.transform.localScale = new Vector3(0.6f, 0.12f, 0.6f);
            feet.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            MaterialFactory.Apply(feet.GetComponent<Renderer>(), PropColor, 0.1f);

            var tank = player.AddComponent<PlayerTankController>();
            tank.Visual = visual;
            tank.Feet = feet.transform;

            return player.transform;
        }

        /// The Anny zombies are loaded at runtime from StreamingAssets, so this
        /// only creates the container and the spawner; the meshes appear a few
        /// frames later.
        static void BuildZombies(Transform root, Transform player)
        {
            var container = new GameObject("Zombies");
            container.transform.SetParent(root, false);
            var spawner = container.AddComponent<ZombieSpawner>();
            spawner.Target = player;
        }

        static void BuildGeometry(Transform root)
        {
            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(root, false);

            GameObject floor = Primitive(PrimitiveType.Cube, "Floor", geometry, true);
            floor.transform.localScale = new Vector3(36f, 0.5f, 36f);
            floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            MaterialFactory.Apply(floor.GetComponent<Renderer>(), FloorColor, 0.05f);

            AddBox(geometry, "PillarA", new Vector3(5f, 1.5f, -5f), new Vector3(1.6f, 3f, 1.6f));
            AddBox(geometry, "PillarB", new Vector3(-5.5f, 1.5f, 3.5f), new Vector3(1.6f, 3f, 1.6f));
            AddBox(geometry, "Wall", new Vector3(0f, 1.2f, -10f), new Vector3(12f, 2.4f, 0.6f));
            AddBox(geometry, "Crate", new Vector3(2.5f, 0.6f, 2.5f), new Vector3(1.2f, 1.2f, 1.2f));
        }

        static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            GameObject box = Primitive(PrimitiveType.Cube, name, parent, true);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            MaterialFactory.Apply(box.GetComponent<Renderer>(), PropColor, 0.1f);
        }

        static void BuildCameras(Transform root, Transform player)
        {
            var rigObject = new GameObject("CameraRig");
            rigObject.transform.SetParent(root, false);

            Camera first = CreateCamera(rigObject.transform, "CameraFixed1", new Vector3(0f, 3.2f, 8.5f), 62f);
            Camera second = CreateCamera(rigObject.transform, "CameraFixed2", new Vector3(-9f, 2.4f, -2f), 58f);
            Camera third = CreateCamera(rigObject.transform, "CameraFixed3", new Vector3(9.5f, 4.6f, 1.5f), 66f);

            var rig = rigObject.AddComponent<FixedCameraRig>();
            rig.Setup(player, first, second, third);
        }

        static Camera CreateCamera(Transform parent, string name, Vector3 position, float fov)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = position;

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.040f, 0.055f);
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.enabled = false;
            return camera;
        }

        static void BuildHud(Transform root)
        {
            Canvas canvas = UiKit.CreateCanvas("HUD", 2, root);
            var hintRect = UiKit.NewRect("Hint", canvas.transform);
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.offsetMin = new Vector2(24f, -80f);
            hintRect.offsetMax = new Vector2(-24f, -20f);

            var hint = hintRect.gameObject.AddComponent<UnityEngine.UI.Text>();
            hint.font = UiKit.Font;
            hint.fontSize = 20;
            hint.color = new Color(1f, 1f, 1f, 0.55f);
            hint.alignment = TextAnchor.UpperLeft;
            hint.text = BuildHintText();
        }

        /// Reads the live bindings so the hint stays correct after rebinding.
        static string BuildHintText()
        {
            InputService input = InputService.Instance;
            if (input == null) return "";

            string forward = FirstBinding(input, GameAction.MoveForward);
            string turnLeft = FirstBinding(input, GameAction.TurnLeft);
            string run = FirstBinding(input, GameAction.Run);
            string aim = FirstBinding(input, GameAction.Aim);
            string camera = FirstBinding(input, GameAction.SwapShoulder);
            string pause = FirstBinding(input, GameAction.Pause);

            return $"Tank controls - {forward}/{turnLeft} walk and turn on the spot, " +
                   $"{run} run, {aim} aim, {camera} change camera, {pause} pause.";
        }

        /// Colliders are kept for level geometry and stripped from the player's
        /// visual parts, which must never collide with its own CharacterController.
        static string FirstBinding(InputService input, GameAction action)
        {
            List<Binding> bindings = input.GetBindings(action);
            return bindings.Count > 0 ? InputService.Describe(bindings[0]) : "-";
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);

            if (!keepCollider)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
            }
            return go;
        }
    }
}
