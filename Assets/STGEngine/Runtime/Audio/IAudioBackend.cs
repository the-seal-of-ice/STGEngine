namespace STGEngine.Runtime.Audio
{
    /// <summary>
    /// Audio playback interface. Abstracts the underlying audio backend
    /// (Unity AudioSource, FMOD, WASAPI, etc.) so upper layers don't
    /// depend on a specific implementation.
    /// </summary>
    public interface IAudioBackend
    {
        // ── BGM (single track, looping) ──

        /// <summary>Play a BGM clip. Fades out current BGM if playing.</summary>
        void PlayBgm(string clipId, float fadeInDuration = 1f, float fadeOutDuration = 1f, float loopStartTime = 0f);

        /// <summary>Stop the current BGM.</summary>
        void StopBgm(float fadeOutDuration = 1f);

        /// <summary>Pause BGM playback (resume with ResumeBgm).</summary>
        void PauseBgm();

        /// <summary>Resume paused BGM.</summary>
        void ResumeBgm();

        /// <summary>Seek BGM to a specific time (for editor timeline sync).</summary>
        void SetBgmTime(float seconds);

        /// <summary>Current BGM playback time in seconds.</summary>
        float BgmTime { get; }

        /// <summary>Whether BGM is currently playing.</summary>
        bool IsBgmPlaying { get; }

        // ── SE (multi-track, one-shot) ──

        /// <summary>Play a sound effect. Returns a handle for optional early stop.</summary>
        int PlaySe(string clipId, float volume = 1f, float pitch = 1f, bool loop = false);

        /// <summary>Stop a specific SE instance by handle.</summary>
        void StopSe(int handle);

        /// <summary>Stop all currently playing SE instances.</summary>
        void StopAllSe();

        // ── Volume ──

        float MasterVolume { get; set; }
        float BgmVolume { get; set; }
        float SeVolume { get; set; }

        // ── Clip info ──

        /// <summary>Get the duration of an audio clip by ID. Returns 0 if not found.</summary>
        float GetClipDuration(string clipId);

        // ── Lifecycle ──

        /// <summary>Called each frame to update fades, pool cleanup, etc.</summary>
        void Tick(float deltaTime);
    }
}
