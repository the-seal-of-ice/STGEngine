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

        // ── ShowTitle state ──
        private Label _titleLabel;
        private Label _subTitleLabel;
        private VisualElement _titleContainer;
        private VisualElement _titleImage;
        private string _activeTitleEventId;
        private Texture2D _loadedTitleTexture;

        // ── ScreenEffect (shake) state ──
        private float _shakeIntensity;
        private float _shakeEndTime;
        private FreeCameraController _freeCam;

        // ── BulletClear visualization ──
        private ActionEvent _activeClearEvent;

        // Cached segment for event lookup
        private TimelineSegment _segment;

        public ActionEventPreviewController(VisualElement overlayRoot, Camera camera)
        {
            _overlayRoot = overlayRoot;
            if (camera != null)
                _freeCam = camera.GetComponent<FreeCameraController>();
            BuildTitleOverlay();
        }

        /// <summary>Set the current segment for event lookup. Only resets state when segment actually changes.</summary>
        public void SetSegment(TimelineSegment segment)
        {
            if (ReferenceEquals(_segment, segment)) return;
            _segment = segment;
            Reset();
        }

        /// <summary>Call each frame with the current playback time.</summary>
        public void Tick(float currentTime, float deltaTime)
        {
            if (_segment?.Events == null) return;

            // Refresh FreeCameraController reference if needed
            if (_freeCam == null)
            {
                var cam = Camera.main;
                if (cam != null) _freeCam = cam.GetComponent<FreeCameraController>();
            }

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

            // Camera shake via FreeCameraController.ShakeOffset
            if (_freeCam != null)
            {
                if (foundShake)
                {
                    float strength = _shakeIntensity;
                    _freeCam.ShakeOffset = new Vector3(
                        (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f,
                        (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 2f,
                        (Mathf.PerlinNoise(Time.time * 25f, Time.time * 25f) - 0.5f) * 2f
                    ) * strength;
                }
                else
                {
                    _freeCam.ShakeOffset = Vector3.zero;
                }
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

            if (_freeCam != null)
                _freeCam.ShakeOffset = Vector3.zero;

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

            // Image element (hidden by default)
            _titleImage = new VisualElement();
            _titleImage.style.display = DisplayStyle.None;
            _titleImage.pickingMode = PickingMode.Ignore;
            _titleContainer.Add(_titleImage);

            _titleLabel = new Label();
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
            if (ae.Params is not ShowTitleParams p) return;

            _titleContainer.style.display = DisplayStyle.Flex;

            // Rebuild when event changes
            if (_activeTitleEventId != ae.Id)
            {
                _activeTitleEventId = ae.Id;

                // Position anchor
                _titleContainer.style.top = p.Position switch
                {
                    ScreenPosition.TopCenter    => Length.Percent(5),
                    ScreenPosition.TopLeft      => Length.Percent(5),
                    ScreenPosition.TopRight     => Length.Percent(5),
                    ScreenPosition.Center       => Length.Percent(40),
                    ScreenPosition.BottomCenter => Length.Percent(75),
                    _ => Length.Percent(8)
                };
                _titleContainer.style.alignItems = p.Position switch
                {
                    ScreenPosition.TopLeft  => Align.FlexStart,
                    ScreenPosition.TopRight => Align.FlexEnd,
                    _ => Align.Center
                };

                // Offset
                _titleContainer.style.marginLeft = p.Offset.x;
                _titleContainer.style.marginTop = new StyleLength(
                    _titleContainer.style.top.value.value + p.Offset.y);

                // Title label
                _titleLabel.text = p.Text ?? "";
                _titleLabel.style.fontSize = p.FontSize;
                _titleLabel.style.color = p.TitleColor;
                _titleLabel.style.unityFontStyleAndWeight = p.FontStyle switch
                {
                    TitleFontStyle.Bold       => UnityEngine.FontStyle.Bold,
                    TitleFontStyle.Italic     => UnityEngine.FontStyle.Italic,
                    TitleFontStyle.BoldItalic => UnityEngine.FontStyle.BoldAndItalic,
                    _ => UnityEngine.FontStyle.Normal
                };

                // Subtitle label
                _subTitleLabel.text = p.SubText ?? "";
                _subTitleLabel.style.fontSize = p.SubFontSize;
                _subTitleLabel.style.color = p.SubTitleColor;
                _subTitleLabel.style.display = string.IsNullOrEmpty(p.SubText)
                    ? DisplayStyle.None : DisplayStyle.Flex;

                // Image
                if (!string.IsNullOrEmpty(p.ImagePath))
                {
                    _loadedTitleTexture = Resources.Load<Texture2D>(p.ImagePath);
                    if (_loadedTitleTexture != null)
                    {
                        _titleImage.style.backgroundImage = new StyleBackground(_loadedTitleTexture);
                        int w = p.ImageWidth > 0 ? p.ImageWidth : _loadedTitleTexture.width;
                        int h = p.ImageHeight > 0 ? p.ImageHeight : _loadedTitleTexture.height;
                        _titleImage.style.width = w;
                        _titleImage.style.height = h;
                        _titleImage.style.marginBottom = 8;
                        _titleImage.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        _titleImage.style.display = DisplayStyle.None;
                    }
                }
                else
                {
                    _titleImage.style.display = DisplayStyle.None;
                    _loadedTitleTexture = null;
                }
            }

            // Fade
            float localTime = currentTime - ae.StartTime;
            float fadeIn = Mathf.Max(0.01f, p.FadeInDuration);
            float fadeOut = Mathf.Max(0.01f, p.FadeOutDuration);
            float alpha = 1f;
            if (localTime < fadeIn)
                alpha = localTime / fadeIn;
            else if (ae.Duration > fadeOut && localTime > ae.Duration - fadeOut)
                alpha = (ae.Duration - localTime) / fadeOut;

            _titleContainer.style.opacity = Mathf.Clamp01(alpha);
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
