using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Scene
{
    /// <summary>
    /// 画面过渡效果控制器。使用 UI Toolkit overlay 实现跳切、交叉渐变、淡入淡出等效果。
    /// 与 ActionEventPreviewController 的 flash overlay 模式一致。
    /// </summary>
    public class ScreenTransitionController
    {
        private readonly VisualElement _overlayRoot;
        private VisualElement _transitionOverlay;

        private ScreenTransitionType _activeType = ScreenTransitionType.Cut;
        private float _duration;
        private float _elapsed;
        private bool _isActive;

        public bool IsActive => _isActive;

        public ScreenTransitionController(VisualElement overlayRoot)
        {
            _overlayRoot = overlayRoot;
            BuildOverlay();
        }

        private void BuildOverlay()
        {
            _transitionOverlay = new VisualElement();
            _transitionOverlay.style.position = Position.Absolute;
            _transitionOverlay.style.left = 0;
            _transitionOverlay.style.right = 0;
            _transitionOverlay.style.top = 0;
            _transitionOverlay.style.bottom = 0;
            _transitionOverlay.style.display = DisplayStyle.None;
            _transitionOverlay.pickingMode = PickingMode.Ignore;
            _overlayRoot.Add(_transitionOverlay);
        }

        /// <summary>开始画面过渡效果。</summary>
        public void StartTransition(ScreenTransitionType type, float duration)
        {
            if (type == ScreenTransitionType.Cut)
            {
                // Cut = no visual transition
                _isActive = false;
                _transitionOverlay.style.display = DisplayStyle.None;
                return;
            }

            _activeType = type;
            _duration = Mathf.Max(0.01f, duration);
            _elapsed = 0f;
            _isActive = true;
            _transitionOverlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>每帧更新。</summary>
        public void Tick(float deltaTime)
        {
            if (!_isActive) return;

            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            switch (_activeType)
            {
                case ScreenTransitionType.CrossFade:
                    // Alpha: 0 → peak(0.5) → 0 (triangle shape)
                    float crossAlpha = t < 0.5f ? t * 2f : (1f - t) * 2f;
                    _transitionOverlay.style.backgroundColor = new Color(0f, 0f, 0f, crossAlpha * 0.7f);
                    break;

                case ScreenTransitionType.FadeToBlack:
                    // Alpha: 0 → 1 → 1 → 0 (hold at peak)
                    float blackAlpha;
                    if (t < 0.4f) blackAlpha = t / 0.4f;
                    else if (t < 0.6f) blackAlpha = 1f;
                    else blackAlpha = (1f - t) / 0.4f;
                    _transitionOverlay.style.backgroundColor = new Color(0f, 0f, 0f, blackAlpha);
                    break;

                case ScreenTransitionType.FadeToWhite:
                    float whiteAlpha;
                    if (t < 0.4f) whiteAlpha = t / 0.4f;
                    else if (t < 0.6f) whiteAlpha = 1f;
                    else whiteAlpha = (1f - t) / 0.4f;
                    _transitionOverlay.style.backgroundColor = new Color(1f, 1f, 1f, whiteAlpha);
                    break;

                case ScreenTransitionType.Wipe:
                    // Horizontal wipe using clip-path simulation via width
                    // First half: black bar sweeps right; second half: reveals from left
                    _transitionOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 1f);
                    if (t < 0.5f)
                    {
                        float wipeT = t * 2f;
                        _transitionOverlay.style.left = 0;
                        _transitionOverlay.style.right = Length.Percent((1f - wipeT) * 100f);
                    }
                    else
                    {
                        float wipeT = (t - 0.5f) * 2f;
                        _transitionOverlay.style.left = Length.Percent(wipeT * 100f);
                        _transitionOverlay.style.right = 0;
                    }
                    break;
            }

            if (t >= 1f)
            {
                _isActive = false;
                _transitionOverlay.style.display = DisplayStyle.None;
                // Reset wipe positioning
                _transitionOverlay.style.left = 0;
                _transitionOverlay.style.right = 0;
            }
        }

        /// <summary>立即停止过渡效果。</summary>
        public void Stop()
        {
            _isActive = false;
            _transitionOverlay.style.display = DisplayStyle.None;
            _transitionOverlay.style.left = 0;
            _transitionOverlay.style.right = 0;
        }
    }
}
