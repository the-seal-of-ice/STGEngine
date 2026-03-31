using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Manages visual previews for ActionEvents during timeline playback.
    /// Handles ShowTitle overlay, ScreenEffect (camera shake), and
    /// BulletClear range visualization.
    /// Driven by PatternSandboxSetup each frame.
    /// </summary>
    public class ActionEventPreviewController
    {
        private readonly VisualElement _overlayRoot;
        private readonly Camera _camera;

        // ── ShowTitle state ──
        private Label _titleLabel;
        private Label _subTitleLabel;
        private VisualElement _titleContainer;
        private string _activeTitleEventId;
        private float _titleFadeOutStart;
        private float _titleFadeOutEnd;

        // ── ScreenEffect (shake) state ──
        private Vector3 _cameraOriginalPos;
        private bool _shaking;
        private float _shakeIntensity;
        private float _shakeEndTime;

        // ── BulletClear visualization ──
        private ActionEvent _activeClearEvent;

        // Cached segment for event lookup
        private TimelineSegment _segment;

        public ActionEventPreviewController(VisualElement overlayRoot, Camera camera)
        {
            _overlayRoot = overlayRoot;
            _camera = camera;
            BuildTitleOverlay();
        }

        /// <summary>Set the current segment for event lookup.</summary>
        public void SetSegment(TimelineSegment segment)
        {
            _segment = segment;
            Reset();
        }

        /// <summary>Call each frame with the current playback time.</summary>
        public void Tick(float currentTime, float deltaTime)
        {
            if (_segment?.Events == null) return;

            bool foundTitle = false;
            bool foundShake = false;
            _activeClearEvent = null;

            foreach (var evt in _segment.Events)
            {
                if (evt is not ActionEvent ae) continue;
                bool active = currentTime >= ae.StartTime && (ae.Duration <= 0f || currentTime < ae.StartTime + ae.Duration);
                if (!active) continue;

                switch (ae.ActionType)
                {
                    case ActionType.ShowTitle:
                        foundTitle = true;
                        UpdateTitleOverlay(ae, currentTime);
                        break;

                    case ActionType.ScreenEffect:
                        if (ae.Params is ScreenEffectParams sfx && sfx.EffectType == ScreenEffectType.Shake)
                        {
                            foundShake = true;
                            _shakeIntensity = sfx.Intensity;
                            _shakeEndTime = ae.StartTime + ae.Duration;
                        }
                        break;

                    case ActionType.BulletClear:
                        _activeClearEvent = ae;
                        break;
                }
            }

            // Hide title if no active ShowTitle event
            if (!foundTitle && _titleContainer != null)
            {
                _titleContainer.style.display = DisplayStyle.None;
                _activeTitleEventId = null;
            }

            // Camera shake
            if (foundShake && _camera != null)
            {
                if (!_shaking)
                {
                    _cameraOriginalPos = _camera.transform.localPosition;
                    _shaking = true;
                }
                float t = 1f - Mathf.Clamp01((currentTime - (_shakeEndTime - 0.5f)) / 0.5f);
                float strength = _shakeIntensity * 0.3f * t;
                _camera.transform.localPosition = _cameraOriginalPos + Random.insideUnitSphere * strength;
            }
            else if (_shaking && _camera != null)
            {
                _camera.transform.localPosition = _cameraOriginalPos;
                _shaking = false;
            }
        }

        /// <summary>Draw BulletClear range gizmos. Call from OnDrawGizmos or similar.</summary>
        public void DrawClearGizmos()
        {
            if (_activeClearEvent?.Params is not BulletClearParams clearParams) return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            switch (clearParams.Shape)
            {
                case ClearShape.Circle:
                    DrawWireCircle(clearParams.Origin, clearParams.Radius, 32);
                    break;
                case ClearShape.Rectangle:
                    Gizmos.DrawWireCube(clearParams.Origin, clearParams.Extents * 2f);
                    break;
                case ClearShape.FullScreen:
                    // No gizmo needed for full screen
                    break;
            }
        }

        /// <summary>Reset all preview state.</summary>
        public void Reset()
        {
            if (_titleContainer != null)
                _titleContainer.style.display = DisplayStyle.None;
            _activeTitleEventId = null;

            if (_shaking && _camera != null)
            {
                _camera.transform.localPosition = _cameraOriginalPos;
                _shaking = false;
            }

            _activeClearEvent = null;
        }

        // ── ShowTitle ──

        private void BuildTitleOverlay()
        {
            _titleContainer = new VisualElement();
            _titleContainer.style.position = Position.Absolute;
            _titleContainer.style.left = 0;
            _titleContainer.style.right = 0;
            _titleContainer.style.top = Length.Percent(8);
            _titleContainer.style.alignItems = Align.Center;
            _titleContainer.style.display = DisplayStyle.None;
            _titleContainer.pickingMode = PickingMode.Ignore;

            _titleLabel = new Label();
            _titleLabel.style.fontSize = 28;
            _titleLabel.style.color = Color.white;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(2, 2),
                blurRadius = 4,
                color = new Color(0, 0, 0, 0.7f)
            };
            _titleLabel.pickingMode = PickingMode.Ignore;
            _titleContainer.Add(_titleLabel);

            _subTitleLabel = new Label();
            _subTitleLabel.style.fontSize = 16;
            _subTitleLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _subTitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subTitleLabel.style.marginTop = 4;
            _subTitleLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(1, 1),
                blurRadius = 3,
                color = new Color(0, 0, 0, 0.6f)
            };
            _subTitleLabel.pickingMode = PickingMode.Ignore;
            _titleContainer.Add(_subTitleLabel);

            _overlayRoot.Add(_titleContainer);
        }

        private void UpdateTitleOverlay(ActionEvent ae, float currentTime)
        {
            if (ae.Params is not ShowTitleParams titleParams) return;

            // Show container
            _titleContainer.style.display = DisplayStyle.Flex;

            // Update text if event changed
            if (_activeTitleEventId != ae.Id)
            {
                _activeTitleEventId = ae.Id;
                _titleLabel.text = titleParams.Text ?? "";
                _subTitleLabel.text = titleParams.SubText ?? "";
                _subTitleLabel.style.display = string.IsNullOrEmpty(titleParams.SubText)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }

            // Fade: first 0.3s fade in, last 0.5s fade out
            float localTime = currentTime - ae.StartTime;
            float alpha = 1f;
            if (localTime < 0.3f)
                alpha = localTime / 0.3f;
            else if (ae.Duration > 0.5f && localTime > ae.Duration - 0.5f)
                alpha = (ae.Duration - localTime) / 0.5f;

            alpha = Mathf.Clamp01(alpha);
            _titleContainer.style.opacity = alpha;
        }

        // ── Gizmo helpers ──

        private static void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float step = 360f / segments;
            Vector3 prev = center + new Vector3(radius, 0, 0);
            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
