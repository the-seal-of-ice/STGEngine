using System;
using System.Collections.Generic;
using UnityEngine;

namespace STGEngine.Runtime.Audio
{
    /// <summary>
    /// Unity AudioSource-based audio backend.
    /// BGM: single AudioSource with manual loop-point support.
    /// SE: pooled AudioSources for concurrent playback with throttling.
    /// </summary>
    public class UnityAudioBackend : IAudioBackend
    {
        private readonly Transform _root;
        private readonly Func<string, AudioClip> _clipResolver;

        // ── BGM ──
        private AudioSource _bgmSource;
        private AudioSource _bgmFadeOutSource; // for cross-fade
        private float _bgmFadeInTarget;
        private float _bgmFadeInRemaining;
        private float _bgmFadeOutRemaining;
        private float _bgmLoopStart;
        private float _bgmLoopEnd;

        // ── SE pool ──
        private readonly List<SeInstance> _sePool = new();
        private int _nextSeHandle = 1;
        private int _maxConcurrentSe = 16;

        /// <summary>Maximum concurrent SE AudioSources. Can be changed at runtime.</summary>
        public int MaxConcurrentSe
        {
            get => _maxConcurrentSe;
            set => _maxConcurrentSe = Mathf.Max(4, value);
        }

        // ── SE throttle: same clip can only trigger once per N ms ──
        private readonly Dictionary<string, float> _seLastPlayTime = new();
        private const float SeThrottleInterval = 0.03f; // 30ms

        // ── Volume ──
        private float _masterVolume = 1f;
        private float _bgmVolume = 0.7f;
        private float _seVolume = 1f;

        private struct SeInstance
        {
            public AudioSource Source;
            public int Handle;
            public bool Active;
        }

        /// <summary>
        /// Create the backend.
        /// </summary>
        /// <param name="root">Parent transform for AudioSource GameObjects.</param>
        /// <param name="clipResolver">Resolves a clip ID to an AudioClip (e.g. via STGCatalog/Resources).</param>
        public UnityAudioBackend(Transform root, Func<string, AudioClip> clipResolver)
        {
            _root = root;
            _clipResolver = clipResolver;
            CreateBgmSources();
            PrewarmSePool(6);
        }

        // ═══ BGM ═══

        public void PlayBgm(string clipId, float fadeInDuration = 1f, float fadeOutDuration = 1f, float loopStartTime = 0f)
        {
            var clip = _clipResolver?.Invoke(clipId);
            if (clip == null)
            {
                Debug.LogWarning($"[Audio] BGM clip not found: '{clipId}'");
                return;
            }

            // Cross-fade: move current to fade-out source
            if (_bgmSource.isPlaying)
            {
                _bgmFadeOutSource.clip = _bgmSource.clip;
                _bgmFadeOutSource.time = _bgmSource.time;
                _bgmFadeOutSource.volume = _bgmSource.volume;
                _bgmFadeOutSource.Play();
                _bgmFadeOutRemaining = fadeOutDuration;
            }

            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            _bgmSource.volume = fadeInDuration > 0.01f ? 0f : _bgmVolume * _masterVolume;
            _bgmSource.Play();

            _bgmFadeInTarget = _bgmVolume * _masterVolume;
            _bgmFadeInRemaining = fadeInDuration;
            _bgmLoopStart = loopStartTime;
            _bgmLoopEnd = clip.length;
        }

        public void StopBgm(float fadeOutDuration = 1f)
        {
            if (!_bgmSource.isPlaying) return;
            if (fadeOutDuration <= 0.01f)
            {
                _bgmSource.Stop();
                return;
            }
            // Use fade-out source for smooth stop
            _bgmFadeOutSource.clip = _bgmSource.clip;
            _bgmFadeOutSource.time = _bgmSource.time;
            _bgmFadeOutSource.volume = _bgmSource.volume;
            _bgmFadeOutSource.Play();
            _bgmFadeOutRemaining = fadeOutDuration;
            _bgmSource.Stop();
        }

        public void PauseBgm() => _bgmSource.Pause();
        public void ResumeBgm() => _bgmSource.UnPause();
        public void SetBgmTime(float seconds) { if (_bgmSource.clip != null) _bgmSource.time = Mathf.Clamp(seconds, 0f, _bgmSource.clip.length); }
        public float BgmTime => _bgmSource.isPlaying ? _bgmSource.time : 0f;
        public bool IsBgmPlaying => _bgmSource.isPlaying;

        // ═══ SE ═══

        public int PlaySe(string clipId, float volume = 1f, float pitch = 1f)
        {
            // Throttle: skip if same clip played too recently
            float now = Time.unscaledTime;
            if (_seLastPlayTime.TryGetValue(clipId, out float lastTime) && now - lastTime < SeThrottleInterval)
                return 0;
            _seLastPlayTime[clipId] = now;

            var clip = _clipResolver?.Invoke(clipId);
            if (clip == null) return 0;

            var src = AcquireSeSource();
            if (src == null) return 0;

            int handle = _nextSeHandle++;
            src.clip = clip;
            src.volume = volume * _seVolume * _masterVolume;
            src.pitch = pitch;
            src.Play();

            _sePool.Add(new SeInstance { Source = src, Handle = handle, Active = true });
            return handle;
        }

        public void StopSe(int handle)
        {
            for (int i = 0; i < _sePool.Count; i++)
            {
                if (_sePool[i].Handle == handle && _sePool[i].Active)
                {
                    _sePool[i].Source.Stop();
                    var inst = _sePool[i];
                    inst.Active = false;
                    _sePool[i] = inst;
                    return;
                }
            }
        }

        public void StopAllSe()
        {
            for (int i = 0; i < _sePool.Count; i++)
            {
                if (_sePool[i].Active)
                {
                    _sePool[i].Source.Stop();
                    var inst = _sePool[i];
                    inst.Active = false;
                    _sePool[i] = inst;
                }
            }
        }

        // ═══ Volume ═══

        public float MasterVolume
        {
            get => _masterVolume;
            set { _masterVolume = Mathf.Clamp01(value); UpdateBgmVolume(); }
        }

        public float BgmVolume
        {
            get => _bgmVolume;
            set { _bgmVolume = Mathf.Clamp01(value); UpdateBgmVolume(); }
        }

        public float SeVolume
        {
            get => _seVolume;
            set => _seVolume = Mathf.Clamp01(value);
        }

        // ═══ Clip Info ═══

        public float GetClipDuration(string clipId)
        {
            var clip = _clipResolver?.Invoke(clipId);
            return clip != null ? clip.length : 0f;
        }

        // ═══ Tick ═══

        public void Tick(float deltaTime)
        {
            // BGM fade-in
            if (_bgmFadeInRemaining > 0f)
            {
                _bgmFadeInRemaining -= deltaTime;
                float t = 1f - Mathf.Clamp01(_bgmFadeInRemaining / Mathf.Max(0.01f, _bgmFadeInRemaining + deltaTime));
                _bgmSource.volume = Mathf.Lerp(0f, _bgmFadeInTarget, t);
                if (_bgmFadeInRemaining <= 0f)
                    _bgmSource.volume = _bgmFadeInTarget;
            }

            // BGM fade-out (cross-fade old track)
            if (_bgmFadeOutRemaining > 0f && _bgmFadeOutSource.isPlaying)
            {
                _bgmFadeOutRemaining -= deltaTime;
                float t = Mathf.Clamp01(_bgmFadeOutRemaining / Mathf.Max(0.01f, _bgmFadeOutRemaining + deltaTime));
                _bgmFadeOutSource.volume *= t;
                if (_bgmFadeOutRemaining <= 0f)
                    _bgmFadeOutSource.Stop();
            }

            // BGM loop-point: when playback passes loop end, jump to loop start
            if (_bgmSource.isPlaying && _bgmLoopStart > 0f && _bgmSource.time >= _bgmLoopEnd - 0.05f)
            {
                _bgmSource.time = _bgmLoopStart;
            }

            // SE pool cleanup
            for (int i = _sePool.Count - 1; i >= 0; i--)
            {
                if (_sePool[i].Active && !_sePool[i].Source.isPlaying)
                {
                    var inst = _sePool[i];
                    inst.Active = false;
                    _sePool[i] = inst;
                }
            }
        }

        // ═══ Internal ═══

        private void CreateBgmSources()
        {
            var bgmGo = new GameObject("BGM_Source");
            bgmGo.transform.SetParent(_root);
            _bgmSource = bgmGo.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;
            _bgmSource.priority = 0;

            var fadeGo = new GameObject("BGM_FadeOut");
            fadeGo.transform.SetParent(_root);
            _bgmFadeOutSource = fadeGo.AddComponent<AudioSource>();
            _bgmFadeOutSource.playOnAwake = false;
            _bgmFadeOutSource.loop = false;
            _bgmFadeOutSource.priority = 1;
        }

        private void PrewarmSePool(int count)
        {
            for (int i = 0; i < count; i++)
                CreateSeSource();
        }

        private readonly List<AudioSource> _seSourcePool = new();

        private AudioSource CreateSeSource()
        {
            var go = new GameObject($"SE_Source_{_seSourcePool.Count}");
            go.transform.SetParent(_root);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.priority = 128;
            _seSourcePool.Add(src);
            return src;
        }

        private AudioSource AcquireSeSource()
        {
            // Find an idle source
            foreach (var src in _seSourcePool)
            {
                if (!src.isPlaying) return src;
            }
            // Create new if under limit
            if (_seSourcePool.Count < MaxConcurrentSe)
                return CreateSeSource();
            // Steal oldest
            return _seSourcePool[0];
        }

        private void UpdateBgmVolume()
        {
            if (_bgmSource != null && _bgmFadeInRemaining <= 0f)
                _bgmSource.volume = _bgmVolume * _masterVolume;
        }
    }
}
