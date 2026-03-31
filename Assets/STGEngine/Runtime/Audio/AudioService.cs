using UnityEngine;

namespace STGEngine.Runtime.Audio
{
    /// <summary>
    /// Singleton-style audio service. Wraps IAudioBackend and provides
    /// a stable API for ActionEventPreviewController, game systems, etc.
    /// Created and owned by PatternSandboxSetup (editor) or GameManager (runtime).
    /// </summary>
    public class AudioService
    {
        private readonly IAudioBackend _backend;

        public IAudioBackend Backend => _backend;

        public AudioService(IAudioBackend backend)
        {
            _backend = backend;
        }

        public void PlayBgm(string clipId, float fadeIn = 1f, float fadeOut = 1f, float loopStart = 0f)
            => _backend.PlayBgm(clipId, fadeIn, fadeOut, loopStart);

        public void StopBgm(float fadeOut = 1f) => _backend.StopBgm(fadeOut);
        public void PauseBgm() => _backend.PauseBgm();
        public void ResumeBgm() => _backend.ResumeBgm();
        public void SetBgmTime(float seconds) => _backend.SetBgmTime(seconds);

        public int PlaySe(string clipId, float volume = 1f, float pitch = 1f)
            => _backend.PlaySe(clipId, volume, pitch);

        public void StopSe(int handle) => _backend.StopSe(handle);
        public void StopAllSe() => _backend.StopAllSe();

        public float MasterVolume { get => _backend.MasterVolume; set => _backend.MasterVolume = value; }
        public float BgmVolume { get => _backend.BgmVolume; set => _backend.BgmVolume = value; }
        public float SeVolume { get => _backend.SeVolume; set => _backend.SeVolume = value; }

        public void Tick(float deltaTime) => _backend.Tick(deltaTime);
    }
}
