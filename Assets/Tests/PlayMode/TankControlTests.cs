using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Foundation.Tests
{
    /// <summary>
    /// The control scheme itself, running in a real play-mode session.
    /// Touch/virtual input is used to drive it, which is the same path the
    /// on-screen controls use, so these tests cover both.
    /// </summary>
    public class TankControlTests
    {
        GameObject _ground;
        GameObject _player;
        PlayerTankController _tank;
        ControlSettings _settings;
        float _savedTurnSpeed;
        float _savedAimTurnSpeed;
        float _savedWalkSpeed;
        float _savedRunSpeed;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(SettingsService.Instance, "Bootstrap should have created the services");
            Assert.IsNotNull(InputService.Instance, "Bootstrap should have created the input service");

            _settings = SettingsService.Instance.Data.controls;
            _savedTurnSpeed = _settings.turnSpeed;
            _savedAimTurnSpeed = _settings.aimTurnSpeed;
            _savedWalkSpeed = _settings.walkSpeed;
            _savedRunSpeed = _settings.runSpeed;
            _settings.invertTurn = false;
            _settings.simultaneousRun = true;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(60f, 1f, 60f);

            _player = new GameObject("TestPlayer");
            _player.transform.position = new Vector3(0f, 0.05f, 0f);
            var controller = _player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.minMoveDistance = 0f; // match LevelBuilder: batch runs at thousands of fps
            _tank = _player.AddComponent<PlayerTankController>();

            InputService.Instance.ClearVirtualInput();

            // Let the controller settle onto the ground so tests never measure
            // the initial 5 cm drop as movement.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputService.Instance.ClearVirtualInput();
            _settings.turnSpeed = _savedTurnSpeed;
            _settings.aimTurnSpeed = _savedAimTurnSpeed;
            _settings.walkSpeed = _savedWalkSpeed;
            _settings.runSpeed = _savedRunSpeed;
            _settings.invertTurn = false;

            if (_player != null) Object.Destroy(_player);
            if (_ground != null) Object.Destroy(_ground);
            yield return null;
        }

        /// Everything that could explain "the character did not walk".
        string State(Vector3 start)
        {
            InputService input = InputService.Instance;
            var cc = _player.GetComponent<CharacterController>();
            Vector3 delta = _player.transform.position - start;
            return $"moved={HorizontalDistance(start, _player.transform.position):0.000} " +
                   $"height={delta.y:0.00} MoveAxis={input.MoveAxis:0.00} TurnAxis={input.TurnAxis:0.00} " +
                   $"aim={_tank.Aiming} frozen={_tank.Frozen} grounded={(cc != null && cc.isGrounded)} " +
                   $"walk={_settings.walkSpeed:0.00} run={_settings.runSpeed:0.00} " +
                   $"deltaTime={Time.deltaTime:0.0000} timeScale={Time.timeScale:0.00} " +
                   $"frame={Time.frameCount} pos={_player.transform.position}";
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

        static IEnumerator HoldFor(float move, float turn, float seconds)
        {
            InputService.Instance.SetVirtualAnalog(move, turn);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            InputService.Instance.SetVirtualAnalog(0f, 0f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Idle_DoesNotMove()
        {
            Vector3 start = _player.transform.position;
            yield return HoldFor(0f, 0f, 0.5f);

            Assert.Less(HorizontalDistance(start, _player.transform.position), 0.02f);
        }

        [UnityTest]
        public IEnumerator Forward_WalksAlongTheCharactersFacing()
        {
            Quaternion startRotation = _player.transform.rotation;
            Vector3 start = _player.transform.position;

            yield return HoldFor(1f, 0f, 0.5f);

            Vector3 delta = _player.transform.position - start;
            delta.y = 0f;
            Assert.Greater(delta.magnitude, 0.2f, "holding forward should actually walk: " + State(start));
            Assert.Greater(Vector3.Dot(delta.normalized, _player.transform.forward), 0.9f,
                "movement must follow the character's own facing, not the camera");
            Assert.Less(Quaternion.Angle(startRotation, _player.transform.rotation), 1f,
                "walking forward must not rotate the character");
        }

        [UnityTest]
        public IEnumerator TurnInput_RotatesInPlaceWithoutWalking()
        {
            Vector3 start = _player.transform.position;

            yield return HoldFor(0f, 1f, 0.4f);

            float turned = Mathf.Abs(Mathf.DeltaAngle(0f, _player.transform.eulerAngles.y));
            Assert.Greater(turned, 5f, "holding left should rotate the character");
            Assert.Less(HorizontalDistance(start, _player.transform.position), 0.02f,
                "turning on the spot is allowed; translating is not implied");
        }

        [UnityTest]
        public IEnumerator ForwardAfterTurning_FollowsTheNewFacing()
        {
            yield return HoldFor(0f, 1f, 0.6f);
            Vector3 facing = _player.transform.forward;
            Vector3 start = _player.transform.position;

            yield return HoldFor(1f, 0f, 0.4f);

            Vector3 delta = _player.transform.position - start;
            delta.y = 0f;
            Assert.Greater(delta.magnitude, 0.1f, "walking after a turn should move: " + State(start));
            Assert.Greater(Vector3.Dot(delta.normalized, facing), 0.9f,
                "after a turn, forward must mean the new facing direction");
        }

        [UnityTest]
        public IEnumerator BackInput_ReversesWithoutTurningAround()
        {
            Quaternion startRotation = _player.transform.rotation;
            Vector3 start = _player.transform.position;

            yield return HoldFor(-1f, 0f, 0.5f);

            Vector3 delta = _player.transform.position - start;
            delta.y = 0f;
            Assert.Greater(delta.magnitude, 0.05f, "backpedalling should move: " + State(start));
            Assert.Less(Vector3.Dot(delta.normalized, _player.transform.forward), -0.5f,
                "backpedalling moves backwards while still facing forwards");
            Assert.Less(Quaternion.Angle(startRotation, _player.transform.rotation), 1f);
        }

        [UnityTest]
        public IEnumerator Aim_BlocksMovementButStillTurns()
        {
            InputService.Instance.SetVirtualHold(GameAction.Aim, true);
            yield return null;

            Vector3 start = _player.transform.position;
            yield return HoldFor(1f, 0f, 0.4f);

            Assert.Less(HorizontalDistance(start, _player.transform.position), 0.02f,
                "aiming roots the character in place");
            Assert.IsTrue(_tank.Aiming);

            float yawBefore = _player.transform.eulerAngles.y;
            yield return HoldFor(0f, 1f, 0.3f);
            float turned = Mathf.Abs(Mathf.DeltaAngle(yawBefore, _player.transform.eulerAngles.y));
            Assert.Greater(turned, 3f, "aiming still allows turning");

            InputService.Instance.SetVirtualHold(GameAction.Aim, false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Run_IsFasterThanWalking()
        {
            Vector3 walkStart = _player.transform.position;
            yield return HoldFor(1f, 0f, 0.4f);
            float walkDistance = HorizontalDistance(walkStart, _player.transform.position);

            InputService.Instance.SetVirtualHold(GameAction.Run, true);
            yield return new WaitForSeconds(0.4f); // clear the run hold time
            Vector3 runStart = _player.transform.position;
            yield return HoldFor(1f, 0f, 0.4f);
            float runDistance = HorizontalDistance(runStart, _player.transform.position);

            InputService.Instance.SetVirtualHold(GameAction.Run, false);
            yield return null;

            Assert.Greater(runDistance, walkDistance * 1.1f,
                $"running should cover more ground: walk={walkDistance:0.000} run={runDistance:0.000} " + State(runStart));
        }

        [UnityTest]
        public IEnumerator InvertedTurning_ReversesTheDirection()
        {
            _settings.invertTurn = true;
            float before = _player.transform.eulerAngles.y;

            yield return HoldFor(0f, 1f, 0.4f);

            float delta = Mathf.DeltaAngle(before, _player.transform.eulerAngles.y);
            Assert.Less(delta, 0f, "with inverted turning, left input rotates the other way");

            _settings.invertTurn = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedCamera_NeverSteersTheCharacter()
        {
            var rigHost = new GameObject("TestRig");
            var rig = rigHost.AddComponent<FixedCameraRig>();
            var cameraHost = new GameObject("TestCamera");
            cameraHost.transform.SetParent(rigHost.transform, false);
            cameraHost.transform.position = new Vector3(9f, 3f, 0f); // off to the side
            var camera = cameraHost.AddComponent<Camera>();
            rig.Setup(_player.transform, camera);
            yield return null;

            Quaternion startRotation = _player.transform.rotation;
            Vector3 facing = _player.transform.forward;
            Vector3 start = _player.transform.position;

            yield return HoldFor(1f, 0f, 0.4f);

            Vector3 delta = _player.transform.position - start;
            delta.y = 0f;
            Assert.Greater(delta.magnitude, 0.1f, "the character should still walk: " + State(start));
            Assert.Greater(Vector3.Dot(delta.normalized, facing), 0.9f,
                "movement follows the character, not the camera");
            Assert.Less(Quaternion.Angle(startRotation, _player.transform.rotation), 1f,
                "a camera looking at the player must never rotate it");

            Object.Destroy(rigHost);
            yield return null;
        }
    }
}
