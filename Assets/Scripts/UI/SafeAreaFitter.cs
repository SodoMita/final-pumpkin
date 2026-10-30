using UnityEngine;

namespace Foundation
{
    /// <summary>Keeps a full-screen UI root inside Screen.safeArea (notches, punch-holes, gesture bars).</summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _last;
        int _lastWidth;
        int _lastHeight;

        void Awake()
        {
            _rect = GetComponent<RectTransform>();
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != _last || Screen.width != _lastWidth || Screen.height != _lastHeight)
            {
                Apply();
            }
        }

        public void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_rect == null || Screen.width <= 0 || Screen.height <= 0) return;

            Rect safe = Screen.safeArea;
            _last = safe;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
