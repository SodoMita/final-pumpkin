using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Cinematic fixed cameras. Cameras are plain children of this object; the
    /// rig activates one at a time and re-aims it at the target. Nothing here
    /// reacts to the mouse, so the camera can never steer the character.
    /// </summary>
    public class FixedCameraRig : MonoBehaviour
    {
        public Transform Target;
        public float LookHeight = 1.0f;
        public bool TrackTarget = true;
        public Camera[] Cameras = new Camera[0];

        int _active;

        public Camera ActiveCamera => Cameras.Length > 0 ? Cameras[Mathf.Clamp(_active, 0, Cameras.Length - 1)] : null;

        public void Setup(Transform target, params Camera[] cameras)
        {
            Target = target;
            Cameras = cameras ?? new Camera[0];
            _active = 0;
            Apply();
        }

        void LateUpdate()
        {
            if (InputService.Instance != null && InputService.Instance.GetPressed(GameAction.SwapShoulder))
            {
                Cycle(1);
            }

            if (!TrackTarget || Target == null) return;

            Vector3 focus = Target.position + Vector3.up * LookHeight;
            for (int i = 0; i < Cameras.Length; i++)
            {
                Camera camera = Cameras[i];
                if (camera == null) continue;
                Vector3 direction = focus - camera.transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
            }
        }

        public void Cycle(int step)
        {
            if (Cameras.Length < 2) return;
            _active = (_active + step % Cameras.Length + Cameras.Length) % Cameras.Length;
            Apply();
        }

        void Apply()
        {
            for (int i = 0; i < Cameras.Length; i++)
            {
                if (Cameras[i] != null) Cameras[i].enabled = i == _active;
            }
        }
    }
}
