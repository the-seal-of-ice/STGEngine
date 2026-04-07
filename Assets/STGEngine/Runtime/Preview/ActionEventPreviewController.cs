using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Timeline;
using STGEngine.Runtime.Audio;
using STGEngine.Runtime.Scene;
using STGEngine.Core.Scene;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Manages visual previews for ActionEvents during timeline playback.
    /// Handles ShowTitle overlay, ScreenEffect (camera shake + flash),
    /// ScoreTally overlay, BulletClear (gizmos + actual clearing),
    /// BackgroundSwitch delegation, and ItemDrop/AutoCollect delegation.
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

        // ── ScreenEffect (flash) state ──
        private VisualElement _flashOverlay;

        // ── ScoreTally overlay ──
        private VisualElement _tallyContainer;
        private Label _tallyTitle;
        private Label _tallyScore;

        // ── BulletClear visualization + expanding wave ──
        private ActionEvent _activeClearEvent;
        private readonly HashSet<string> _triggeredClearIds = new();
        private System.Func<IReadOnlyList<ActiveEvent>> _activeEventsProvider;

        /// <summary>Tracks an active expanding clear wave.</summary>
        private class ActiveClearWave
        {
            public string EventId;
            public BulletClearParams Params;
            public float StartTime;       // timeline time when wave started
            public float MaxRadius;        // target radius (ToRadius/ToBoundary)
            public Vector3 MaxExtents;     // target extents (Rectangle)
            public bool IsInfinite;        // Infinite range mode
        }
        private readonly List<ActiveClearWave> _activeClearWaves = new();
        // Reusable list to avoid per-frame allocation
        private readonly List<ClearZone> _tempZones = new();

        // ── BackgroundSwitch ──
        private readonly HashSet<string> _triggeredBgIds = new();
        private BackgroundLayer _backgroundLayer;

        // ── ItemDrop / AutoCollect ──
        private readonly HashSet<string> _triggeredItemIds = new();
        private ItemPreviewSystem _itemSystem;

        // ── Audio ──
        private AudioService _audio;
        private readonly HashSet<string> _triggeredAudioIds = new();
        private readonly Dictionary<string, int> _loopingSeHandles = new();
        private float _lastTickTime = -1f;

        // ── Camera Script ──
        private CameraScriptPlayer _cameraScriptPlayer;
        private EditorCameraFrame _editorCameraFrame;
        private ScreenTransitionController _screenTransition;
        private readonly HashSet<string> _triggeredCameraIds = new();

        // Dependencies for frame resolution
        private Runtime.Preview.BossPlaceholder _bossPlaceholder;

        // Cached segment for event lookup
        private TimelineSegment _segment;

        public ActionEventPreviewController(VisualElement overlayRoot, Camera camera)
        {
            _overlayRoot = overlayRoot;
            if (camera != null)
                _freeCam = camera.GetComponent<FreeCameraController>();

            // Camera script player
            if (camera != null)
            {
                _cameraScriptPlayer = camera.GetComponent<CameraScriptPlayer>();
                if (_cameraScriptPlayer == null)
                    _cameraScriptPlayer = camera.gameObject.AddComponent<CameraScriptPlayer>();
                _editorCameraFrame = new EditorCameraFrame(_freeCam);
                _cameraScriptPlayer.Initialize(_editorCameraFrame);

                // Screen transition controller
                _screenTransition = new ScreenTransitionController(overlayRoot);
            }

            BuildTitleOverlay();
            BuildFlashOverlay();
            BuildTallyOverlay();
        }

        /// <summary>Set the audio service for BGM/SE playback.</summary>
        public void SetAudioService(AudioService audio) => _audio = audio;

        /// <summary>Provide access to active previewers for BulletClear actual clearing.</summary>
        public void SetActiveEventsProvider(System.Func<IReadOnlyList<ActiveEvent>> provider)
            => _activeEventsProvider = provider;

        /// <summary>Set the background layer for BackgroundSwitch events.</summary>
        public void SetBackgroundLayer(BackgroundLayer layer) => _backgroundLayer = layer;

        /// <summary>Set the item preview system for ItemDrop/AutoCollect events.</summary>
        public void SetItemSystem(ItemPreviewSystem system) => _itemSystem = system;

        /// <summary>Set the active player for camera script offset calculations.</summary>
        public void SetCameraPlayer(STGEngine.Runtime.Player.IPlayerProvider player)
            => _editorCameraFrame?.SetPlayer(player);

        /// <summary>Set the boss placeholder for camera target resolution.</summary>
        public void SetBossPlaceholder(Runtime.Preview.BossPlaceholder boss) => _bossPlaceholder = boss;

        /// <summary>Set the current segment for event lookup. Only fully resets when segment ID changes.</summary>
        public void SetSegment(TimelineSegment segment)
        {
            if (ReferenceEquals(_segment, segment)) return;

            bool sameLogicalSegment = _segment != null && segment != null
                && _segment.Id == segment.Id;

            _segment = segment;

            if (!sameLogicalSegment)
                Reset();
            // Same logical segment (tempSegment rebuilt): keep _triggeredAudioIds
            // so audio doesn't re-trigger on property edits.
        }

        /// <summary>Call each frame with the current playback time.</summary>
        public void Tick(float currentTime, float deltaTime)
        {
            if (_segment?.Events == null) return;

            // Detect time jump (Seek backward) — clear triggered IDs for events after new time
            if (currentTime < _lastTickTime)
            {
                _triggeredAudioIds.RemoveWhere(id =>
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                            return true;
                    }
                    return false;
                });
                _triggeredClearIds.RemoveWhere(id =>
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                            return true;
                    }
                    return false;
                });
                // Remove clear waves that started after the new time
                _activeClearWaves.RemoveAll(w => w.StartTime >= currentTime);
                _triggeredBgIds.RemoveWhere(id =>
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                            return true;
                    }
                    return false;
                });
                _triggeredItemIds.RemoveWhere(id =>
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                            return true;
                    }
                    return false;
                });
                // Camera script seek reset
                _triggeredCameraIds.RemoveWhere(id =>
                {
                    foreach (var evt in _segment.Events)
                    {
                        if (evt is ActionEvent ae && ae.Id == id && ae.StartTime >= currentTime)
                            return true;
                    }
                    return false;
                });
                if (_cameraScriptPlayer != null && _cameraScriptPlayer.IsActive)
                    _cameraScriptPlayer.Stop();
                // Stop all looping SE handles that were cleared
                foreach (var kvp in new Dictionary<string, int>(_loopingSeHandles))
                {
                    if (!_triggeredAudioIds.Contains(kvp.Key))
                    {
                        _audio?.StopSe(kvp.Value);
                        _loopingSeHandles.Remove(kvp.Key);
                    }
                }
                _audio?.StopBgm(0.05f);
                _audio?.StopAllSe();
            }
            _lastTickTime = currentTime;

            // Refresh FreeCameraController reference if needed
            if (_freeCam == null)
            {
                var cam = Camera.main;
                if (cam != null) _freeCam = cam.GetComponent<FreeCameraController>();
            }

            bool foundTitle = false;
            bool foundShake = false;
            bool foundFlash = false;
            bool foundTally = false;
            _activeClearEvent = null;

            foreach (var evt in _segment.Events)
            {
                if (evt is not ActionEvent ae) continue;

                // Audio events (fire-once, not range-based)
                if (ae.ActionType == ActionType.BgmControl || ae.ActionType == ActionType.SePlay)
                {
                    if (currentTime >= ae.StartTime && _audio != null && !_triggeredAudioIds.Contains(ae.Id))
                    {
                        _triggeredAudioIds.Add(ae.Id);
                        if (ae.ActionType == ActionType.BgmControl && ae.Params is BgmControlParams bgm)
                        {
                            switch (bgm.Action)
                            {
                                case BgmAction.Play:
                                case BgmAction.CrossFade:
                                    _audio.PlayBgm(bgm.BgmId, bgm.FadeInDuration, bgm.FadeOutDuration, bgm.LoopStartTime);
                                    break;
                                case BgmAction.Stop:
                                case BgmAction.FadeOut:
                                    _audio.StopBgm(bgm.FadeOutDuration);
                                    break;
                            }
                        }
                        else if (ae.ActionType == ActionType.SePlay && ae.Params is SePlayParams se)
                        {
                            int handle = _audio.PlaySe(se.SeId, se.Volume, se.Pitch, se.Loop);
                            if (se.Loop && handle != 0)
                                _loopingSeHandles[ae.Id] = handle;
                        }
                    }

                    // Stop looping SE when playhead leaves the block's Duration range
                    if (ae.ActionType == ActionType.SePlay && ae.Params is SePlayParams sep && sep.Loop
                        && _loopingSeHandles.ContainsKey(ae.Id))
                    {
                        bool inRange = ae.Duration > 0f
                            ? (currentTime >= ae.StartTime && currentTime < ae.StartTime + ae.Duration)
                            : currentTime >= ae.StartTime;
                        if (!inRange)
                        {
                            _audio?.StopSe(_loopingSeHandles[ae.Id]);
                            _loopingSeHandles.Remove(ae.Id);
                        }
                    }

                    continue;
                }

                // Fire-once events: BackgroundSwitch, BulletClear (clearing), ItemDrop, AutoCollect
                if (currentTime >= ae.StartTime)
                {
                    switch (ae.ActionType)
                    {
                        case ActionType.BackgroundSwitch:
                            if (!_triggeredBgIds.Contains(ae.Id))
                            {
                                _triggeredBgIds.Add(ae.Id);
                                if (ae.Params is BackgroundSwitchParams bgParams && _backgroundLayer != null)
                                {
                                    _backgroundLayer.SetBackground(
                                        bgParams.BackgroundId,
                                        bgParams.Transition,
                                        bgParams.TransitionDuration,
                                        new Vector2(bgParams.ScrollSpeedX, bgParams.ScrollSpeedY));
                                }
                            }
                            break;

                        case ActionType.BulletClear:
                            if (!_triggeredClearIds.Contains(ae.Id))
                            {
                                _triggeredClearIds.Add(ae.Id);
                                StartClearWave(ae, currentTime);
                            }
                            break;

                        case ActionType.ItemDrop:
                            if (!_triggeredItemIds.Contains(ae.Id))
                            {
                                _triggeredItemIds.Add(ae.Id);
                                if (ae.Params is ItemDropParams itemParams && _itemSystem != null)
                                    _itemSystem.SpawnItems(itemParams, Vector3.zero);
                            }
                            break;

                        case ActionType.AutoCollect:
                            if (!_triggeredItemIds.Contains(ae.Id))
                            {
                                _triggeredItemIds.Add(ae.Id);
                                _itemSystem?.TriggerAutoCollect();
                            }
                            break;

                    case ActionType.CameraShake:
                        if (!_triggeredCameraIds.Contains(ae.Id))
                        {
                            _triggeredCameraIds.Add(ae.Id);
                            if (ae.Params is CameraShakeParams cshk && _cameraScriptPlayer != null)
                                _cameraScriptPlayer.Shake(cshk.Preset);
                        }
                        break;

                    case ActionType.CameraScript:
                        if (!_triggeredCameraIds.Contains(ae.Id))
                        {
                            _triggeredCameraIds.Add(ae.Id);
                            if (ae.Params is CameraScriptParams csp && _cameraScriptPlayer != null)
                            {
                                var frameProvider = ResolveEditorFrameProvider(csp);
                                _cameraScriptPlayer.Play(csp, frameProvider);

                                // Trigger screen transition if configured
                                if (csp.ScreenTransition != Core.Scene.ScreenTransitionType.Cut && _screenTransition != null)
                                    _screenTransition.StartTransition(csp.ScreenTransition, csp.TransitionDuration);
                            }
                        }
                        break;
                    }
                }

                // Range-based active check for visual effects
                bool active = currentTime >= ae.StartTime && (ae.Duration <= 0f || currentTime < ae.StartTime + ae.Duration);
                if (!active) continue;

                switch (ae.ActionType)
                {
                    case ActionType.ShowTitle:
                        foundTitle = true;
                        UpdateTitleOverlay(ae, currentTime);
                        break;

                    case ActionType.ScreenEffect:
                        if (ae.Params is ScreenEffectParams sfx)
                        {
                            if (sfx.EffectType == ScreenEffectType.Shake)
                            {
                                foundShake = true;
                                _shakeIntensity = sfx.Intensity;
                                _shakeEndTime = ae.StartTime + ae.Duration;
                            }
                            else if (sfx.EffectType == ScreenEffectType.FlashWhite
                                  || sfx.EffectType == ScreenEffectType.FlashRed)
                            {
                                foundFlash = true;
                                float localT = currentTime - ae.StartTime;
                                float dur = Mathf.Max(0.01f, ae.Duration);
                                float alpha = (1f - Mathf.Clamp01(localT / dur)) * sfx.Intensity;
                                Color c = sfx.EffectType == ScreenEffectType.FlashWhite
                                    ? new Color(1f, 1f, 1f, alpha)
                                    : new Color(1f, 0.2f, 0.1f, alpha);
                                _flashOverlay.style.backgroundColor = c;
                                _flashOverlay.style.display = DisplayStyle.Flex;
                            }
                        }
                        break;

                    case ActionType.BulletClear:
                        _activeClearEvent = ae;
                        break;

                    case ActionType.ScoreTally:
                        foundTally = true;
                        UpdateTallyOverlay(ae, currentTime);
                        break;
                }
            }

            // Hide title if no active ShowTitle event
            if (!foundTitle && _titleContainer != null)
            {
                _titleContainer.style.display = DisplayStyle.None;
                _activeTitleEventId = null;
            }

            // Hide flash overlay if no active flash event
            if (!foundFlash && _flashOverlay != null)
                _flashOverlay.style.display = DisplayStyle.None;

            // Hide tally overlay if no active ScoreTally event
            if (!foundTally && _tallyContainer != null)
                _tallyContainer.style.display = DisplayStyle.None;

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

            // ── Update expanding clear waves ──
            UpdateClearWaves(currentTime);

            // Tick subsystems
            _backgroundLayer?.Tick(deltaTime);
            _itemSystem?.Tick(deltaTime);
            _screenTransition?.Tick(deltaTime);
        }

        /// <summary>Draw BulletClear range gizmos + item gizmos. Call from OnDrawGizmos.</summary>
        public void DrawGizmos()
        {
            // BulletClear: draw expanding wave front for each active wave
            foreach (var wave in _activeClearWaves)
            {
                float elapsed = _lastTickTime - wave.StartTime;
                if (elapsed < 0f) continue;
                float speed = wave.Params.ExpandSpeed;

                // Compute current radius (same formula as UpdateClearWaves)
                float r;
                if (wave.IsInfinite)
                {
                    float accel = speed * 2f;
                    r = speed * elapsed + 0.5f * accel * elapsed * elapsed;
                }
                else
                {
                    r = Mathf.Min(elapsed * speed, wave.MaxRadius);
                }

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                switch (wave.Params.Shape)
                {
                    case ClearShape.FullScreen:
                    case ClearShape.Circle:
                        DrawWireCircle(wave.Params.Origin, r, 32);
                        // Draw target radius as dim outline (not for Infinite)
                        if (!wave.IsInfinite && r < wave.MaxRadius)
                        {
                            Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
                            DrawWireCircle(wave.Params.Origin, wave.MaxRadius, 32);
                        }
                        break;
                    case ClearShape.Rectangle:
                        float maxDim = wave.MaxExtents.magnitude;
                        float t = maxDim > 0f ? Mathf.Clamp01(r / maxDim) : 1f;
                        Vector3 ext = wave.MaxExtents * t;
                        Gizmos.DrawWireCube(wave.Params.Origin, ext * 2f);
                        break;
                }
            }

            // Also draw static gizmo for the range-based active clear event (legacy)
            if (_activeClearEvent?.Params is BulletClearParams clearParams && _activeClearWaves.Count == 0)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                switch (clearParams.Shape)
                {
                    case ClearShape.Circle:
                        DrawWireCircle(clearParams.Origin, clearParams.Radius, 32);
                        break;
                    case ClearShape.Rectangle:
                        Gizmos.DrawWireCube(clearParams.Origin, clearParams.Extents * 2f);
                        break;
                }
            }

            // Item gizmos
            _itemSystem?.DrawGizmos();
        }

        /// <summary>Draw BulletClear range gizmos. Legacy alias for DrawGizmos.</summary>
        public void DrawClearGizmos() => DrawGizmos();

        /// <summary>Reset all preview state.</summary>
        public void Reset()
        {
            if (_titleContainer != null)
                _titleContainer.style.display = DisplayStyle.None;
            _activeTitleEventId = null;

            if (_flashOverlay != null)
                _flashOverlay.style.display = DisplayStyle.None;

            if (_tallyContainer != null)
                _tallyContainer.style.display = DisplayStyle.None;

            if (_freeCam != null)
                _freeCam.ShakeOffset = Vector3.zero;

            _activeClearEvent = null;
            _activeClearWaves.Clear();
            _triggeredAudioIds.Clear();
            _triggeredClearIds.Clear();
            _triggeredBgIds.Clear();
            _triggeredItemIds.Clear();
            _triggeredCameraIds.Clear();
            if (_cameraScriptPlayer != null && _cameraScriptPlayer.IsActive)
                _cameraScriptPlayer.Stop();
            _screenTransition?.Stop();
            _loopingSeHandles.Clear();
            _lastTickTime = -1f;
            _audio?.StopBgm(0.1f);
            _audio?.StopAllSe();
            _itemSystem?.Reset();
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
            _titleContainer.style.marginTop = p.Offset.y;

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
                var tex = Resources.Load<Texture2D>(p.ImagePath);
                if (tex != null)
                {
                    _titleImage.style.backgroundImage = new StyleBackground(tex);
                    _titleImage.style.width = p.ImageWidth > 0 ? p.ImageWidth : tex.width;
                    _titleImage.style.height = p.ImageHeight > 0 ? p.ImageHeight : tex.height;
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
            }

            // Animation
            float localTime = currentTime - ae.StartTime;
            float fadeIn = Mathf.Max(0.01f, p.FadeInDuration);
            float fadeOut = Mathf.Max(0.01f, p.FadeOutDuration);

            // Progress: 0→1 during fade-in, 1 during hold, 1→0 during fade-out
            float inT = Mathf.Clamp01(localTime / fadeIn);
            float outT = 1f;
            if (ae.Duration > fadeOut && localTime > ae.Duration - fadeOut)
                outT = Mathf.Clamp01((ae.Duration - localTime) / fadeOut);

            // Reset transforms each frame (some animations modify these)
            _titleContainer.style.opacity = 1f;
            _titleContainer.style.translate = new Translate(0, 0);
            _titleContainer.style.scale = new Scale(Vector3.one);

            switch (p.Animation)
            {
                case TitleAnimationType.FadeIn:
                    _titleContainer.style.opacity = inT * outT;
                    break;

                case TitleAnimationType.SlideLeft:
                {
                    // Slide in from right, slide out to left
                    float slideIn = (1f - inT) * 300f;   // pixels from right
                    float slideOut = (1f - outT) * -300f; // pixels to left
                    float x = slideIn + slideOut;
                    _titleContainer.style.translate = new Translate(x, 0);
                    _titleContainer.style.opacity = Mathf.Min(inT, outT);
                    break;
                }

                case TitleAnimationType.SlideRight:
                {
                    // Slide in from left, slide out to right
                    float slideIn = (1f - inT) * -300f;
                    float slideOut = (1f - outT) * 300f;
                    float x = slideIn + slideOut;
                    _titleContainer.style.translate = new Translate(x, 0);
                    _titleContainer.style.opacity = Mathf.Min(inT, outT);
                    break;
                }

                case TitleAnimationType.Expand:
                {
                    // Scale from 0 to 1, then 1 to 0
                    float s = inT * outT;
                    _titleContainer.style.scale = new Scale(new Vector3(s, s, 1f));
                    _titleContainer.style.opacity = s;
                    break;
                }

                case TitleAnimationType.TypeWriter:
                {
                    // Reveal characters one by one during fade-in period
                    string fullText = p.Text ?? "";
                    int charCount = Mathf.FloorToInt(inT * fullText.Length);
                    _titleLabel.text = fullText.Substring(0, Mathf.Clamp(charCount, 0, fullText.Length));
                    _titleContainer.style.opacity = outT;
                    break;
                }
            }
        }

        // ── Flash Overlay (FlashWhite / FlashRed) ──

        private void BuildFlashOverlay()
        {
            _flashOverlay = new VisualElement();
            _flashOverlay.style.position = Position.Absolute;
            _flashOverlay.style.left = 0;
            _flashOverlay.style.right = 0;
            _flashOverlay.style.top = 0;
            _flashOverlay.style.bottom = 0;
            _flashOverlay.style.display = DisplayStyle.None;
            _flashOverlay.pickingMode = PickingMode.Ignore;
            _overlayRoot.Add(_flashOverlay);
        }

        // ── ScoreTally Overlay ──

        private void BuildTallyOverlay()
        {
            _tallyContainer = new VisualElement();
            _tallyContainer.style.position = Position.Absolute;
            _tallyContainer.style.left = 0;
            _tallyContainer.style.right = 0;
            _tallyContainer.style.top = 0;
            _tallyContainer.style.bottom = 0;
            _tallyContainer.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _tallyContainer.style.alignItems = Align.Center;
            _tallyContainer.style.justifyContent = Justify.Center;
            _tallyContainer.style.display = DisplayStyle.None;
            _tallyContainer.pickingMode = PickingMode.Ignore;

            _tallyTitle = new Label();
            _tallyTitle.style.fontSize = 36;
            _tallyTitle.style.color = new Color(1f, 0.95f, 0.6f);
            _tallyTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tallyTitle.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            _tallyTitle.style.textShadow = new TextShadow
            {
                offset = new Vector2(2, 2),
                blurRadius = 6,
                color = new Color(0, 0, 0, 0.8f)
            };
            _tallyTitle.pickingMode = PickingMode.Ignore;
            _tallyContainer.Add(_tallyTitle);

            _tallyScore = new Label();
            _tallyScore.style.fontSize = 22;
            _tallyScore.style.color = Color.white;
            _tallyScore.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tallyScore.style.marginTop = 16;
            _tallyScore.style.textShadow = new TextShadow
            {
                offset = new Vector2(1, 1),
                blurRadius = 3,
                color = new Color(0, 0, 0, 0.6f)
            };
            _tallyScore.pickingMode = PickingMode.Ignore;
            _tallyContainer.Add(_tallyScore);

            _overlayRoot.Add(_tallyContainer);
        }

        private void UpdateTallyOverlay(ActionEvent ae, float currentTime)
        {
            if (ae.Params is not ScoreTallyParams p) return;

            _tallyContainer.style.display = DisplayStyle.Flex;

            // Title text based on TallyType
            _tallyTitle.text = p.Type switch
            {
                TallyType.SpellCardBonus => "Spell Card Bonus!",
                TallyType.ChapterClear  => "Chapter Clear!",
                TallyType.StageClear    => "Stage Clear!",
                _ => "Clear!"
            };

            // Placeholder score (no real score in preview mode)
            _tallyScore.text = p.Type switch
            {
                TallyType.SpellCardBonus => "Bonus: 1,000,000",
                _ => "Score: 999,999,999"
            };

            // Fade-in animation
            float localT = currentTime - ae.StartTime;
            float fadeIn = 0.3f;
            float opacity = Mathf.Clamp01(localT / fadeIn);
            _tallyContainer.style.opacity = opacity;
        }

        // ── BulletClear expanding wave ──

        /// <summary>
        /// Resolve the appropriate ICameraFrameProvider for editor preview
        /// based on CameraScriptParams configuration.
        /// </summary>
        private ICameraFrameProvider ResolveEditorFrameProvider(CameraScriptParams csp)
        {
            switch (csp.ReferenceTarget)
            {
                case Core.Scene.CameraReferenceTarget.BoundaryCenter:
                {
                    // In editor preview, use SandboxBoundary center + height offset
                    var boundary = Object.FindAnyObjectByType<SandboxBoundary>();
                    if (boundary != null)
                    {
                        Vector3 center = boundary.transform.position + Vector3.up * csp.BoundaryCenterHeight;
                        return new Runtime.Scene.FixedWorldFrame(center);
                    }
                    return _editorCameraFrame;
                }

                case Core.Scene.CameraReferenceTarget.Boss:
                {
                    if (_bossPlaceholder != null && _bossPlaceholder.IsVisible)
                    {
                        return new Runtime.Scene.TargetTransformFrame(
                            _bossPlaceholder.transform,
                            csp.FrameMode);
                    }
                    return _editorCameraFrame;
                }

                case Core.Scene.CameraReferenceTarget.WorldFixed:
                    return new Runtime.Scene.FixedWorldFrame(csp.FixedWorldPosition);

                case Core.Scene.CameraReferenceTarget.Player:
                default:
                    return _editorCameraFrame;
            }
        }

        private void StartClearWave(ActionEvent ae, float currentTime)
        {
            if (ae.Params is not BulletClearParams p) return;

            // ExpandSpeed = 0 means instant clear (no wave)
            if (p.ExpandSpeed <= 0f)
            {
                ExecuteInstantClear(p);
                return;
            }

            float maxRadius;
            bool isInfinite = false;

            switch (p.Range)
            {
                case ClearRange.ToBoundary:
                    // Compute radius needed to cover entire sandbox boundary from origin
                    maxRadius = ComputeBoundaryRadius(p.Origin);
                    break;
                case ClearRange.Infinite:
                    // No cap — accelerates until all bullets gone
                    maxRadius = float.MaxValue;
                    isInfinite = true;
                    break;
                default: // ToRadius
                    maxRadius = p.Shape == ClearShape.FullScreen ? ComputeBoundaryRadius(p.Origin) : p.Radius;
                    break;
            }

            _activeClearWaves.Add(new ActiveClearWave
            {
                EventId = ae.Id,
                Params = p,
                StartTime = currentTime,
                MaxRadius = maxRadius,
                MaxExtents = p.Extents,
                IsInfinite = isInfinite
            });
        }

        /// <summary>
        /// Compute the radius from origin needed to fully cover the sandbox boundary.
        /// Returns the distance from origin to the farthest corner of the boundary box.
        /// </summary>
        private static float ComputeBoundaryRadius(Vector3 origin)
        {
            var boundary = Object.FindAnyObjectByType<SandboxBoundary>();
            if (boundary == null) return 100f; // fallback
            return BoundaryRadiusFrom(origin, boundary);
        }

        /// <summary>
        /// Compute the estimated duration for a BulletClear event based on its params
        /// and the current sandbox boundary. Returns 0 if instant or cannot compute.
        /// Call this when creating a BulletClear event or when its params change.
        /// </summary>
        public static float ComputeClearDuration(BulletClearParams p)
        {
            if (p == null || p.ExpandSpeed <= 0f) return 0f;

            var boundary = Object.FindAnyObjectByType<SandboxBoundary>();
            if (boundary == null) return 0f;

            float targetRadius;
            switch (p.Range)
            {
                case ClearRange.ToBoundary:
                    targetRadius = BoundaryRadiusFrom(p.Origin, boundary);
                    break;
                case ClearRange.Infinite:
                    // For Infinite, estimate time to cover boundary (wave accelerates)
                    // r = v*t + 0.5*a*t^2, a = 2v → r = v*t + v*t^2 = v*t*(1+t)
                    // Solve for t: v*t^2 + v*t - r = 0 → t = (-1 + sqrt(1 + 4r/v)) / 2
                    targetRadius = BoundaryRadiusFrom(p.Origin, boundary);
                    float v = p.ExpandSpeed;
                    float disc = 1f + 4f * targetRadius / v;
                    return (-1f + Mathf.Sqrt(disc)) * 0.5f;
                default: // ToRadius
                    targetRadius = p.Radius;
                    break;
            }

            return targetRadius / p.ExpandSpeed;
        }

        private static float BoundaryRadiusFrom(Vector3 origin, SandboxBoundary boundary)
        {
            Vector3 center = boundary.transform.position;
            Vector3 half = boundary.HalfExtents;

            float maxDist = 0f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + new Vector3(half.x * x, half.y * y, half.z * z);
                float dist = Vector3.Distance(origin, corner);
                if (dist > maxDist) maxDist = dist;
            }
            return maxDist;
        }

        /// <summary>Instant clear (ExpandSpeed = 0): same as old behavior.</summary>
        private void ExecuteInstantClear(BulletClearParams clearParams)
        {
            var activeEvents = _activeEventsProvider?.Invoke();
            if (activeEvents == null) return;

            foreach (var active in activeEvents)
            {
                if (active.Previewer == null) continue;
                active.Previewer.ClearAllBullets();
            }
        }

        /// <summary>
        /// Each frame: grow active waves, build ClearZone list, push to all previewers.
        /// Waves that have reached max size are kept (bullets stay cleared).
        /// Waves are removed on Reset/Seek.
        /// </summary>
        private void UpdateClearWaves(float currentTime)
        {
            if (_activeClearWaves.Count == 0) return;

            var activeEvents = _activeEventsProvider?.Invoke();
            if (activeEvents == null) return;

            foreach (var active in activeEvents)
            {
                if (active.Previewer == null) continue;

                _tempZones.Clear();
                Vector3 previewerPos = active.Previewer.transform.position;

                foreach (var wave in _activeClearWaves)
                {
                    float elapsed = currentTime - wave.StartTime;
                    if (elapsed < 0f) continue;

                    float speed = wave.Params.ExpandSpeed;
                    Vector3 localOrigin = wave.Params.Origin - previewerPos;

                    // Compute current radius based on range mode
                    float currentRadius;
                    if (wave.IsInfinite)
                    {
                        // Accelerating expansion: r = speed * t + 0.5 * accel * t^2
                        // Use acceleration = speed * 2 (doubles speed every second)
                        float accel = speed * 2f;
                        currentRadius = speed * elapsed + 0.5f * accel * elapsed * elapsed;
                    }
                    else
                    {
                        currentRadius = Mathf.Min(elapsed * speed, wave.MaxRadius);
                    }

                    if (wave.Params.Shape == ClearShape.Rectangle)
                    {
                        float maxDim = wave.MaxExtents.magnitude;
                        float t = maxDim > 0f ? Mathf.Clamp01(currentRadius / maxDim) : 1f;
                        _tempZones.Add(new ClearZone
                        {
                            ShapeType = 2,
                            Origin = localOrigin,
                            Radius = 0f,
                            Extents = wave.MaxExtents * t
                        });
                    }
                    else
                    {
                        _tempZones.Add(new ClearZone
                        {
                            ShapeType = 1,
                            Origin = localOrigin,
                            Radius = currentRadius,
                            Extents = Vector3.zero
                        });
                    }
                }

                active.Previewer.SetClearZones(_tempZones);
            }
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
