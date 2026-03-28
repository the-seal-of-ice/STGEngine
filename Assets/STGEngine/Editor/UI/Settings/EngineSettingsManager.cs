using System;
using System.IO;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using STGEngine.Core.DataModel;

namespace STGEngine.Editor.UI.Settings
{
    /// <summary>
    /// Aggregates GameplaySettings + EditorSettings and handles load/save.
    /// Provides static access for global use within the editor.
    ///
    /// File layout:
    ///   STGData/settings.yaml       -> GameplaySettings (version-controlled)
    ///   STGData/editor_prefs.yaml   -> EditorSettings   (NOT version-controlled)
    /// </summary>
    public static class EngineSettingsManager
    {
        private static readonly string DataRoot =
            Path.Combine(Application.dataPath, "Resources", "STGData");

        private static readonly string GameplayPath =
            Path.Combine(DataRoot, "settings.yaml");

        private static readonly string EditorPath =
            Path.Combine(DataRoot, "editor_prefs.yaml");

        private static readonly ISerializer YamlWriter = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private static readonly IDeserializer YamlReader = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>[GAMEPLAY] settings shared with runtime.</summary>
        public static GameplaySettings Gameplay { get; private set; } = new();

        /// <summary>[EDITOR] settings for editor only.</summary>
        public static EditorSettings Editor { get; private set; } = new();

        /// <summary>Fired after any setting is modified and saved.</summary>
        public static event Action OnSettingsChanged;

        /// <summary>
        /// Load both settings files. Call once at startup.
        /// Missing files use defaults silently.
        /// </summary>
        public static void Load()
        {
            Gameplay = LoadFile<GameplaySettings>(GameplayPath) ?? new GameplaySettings();
            Editor = LoadFile<EditorSettings>(EditorPath) ?? new EditorSettings();

            Debug.Log($"[EngineSettings] Loaded: TickRate={Gameplay.SimulationTickRate}, " +
                      $"PreviewFps={Editor.PreviewFpsLimit}");
        }

        /// <summary>
        /// Persist both settings files and notify listeners.
        /// </summary>
        public static void Persist()
        {
            Directory.CreateDirectory(DataRoot);
            WriteFile(GameplayPath, Gameplay);
            WriteFile(EditorPath, Editor);
            OnSettingsChanged?.Invoke();
        }

        /// <summary>Modify gameplay settings, persist, and notify.</summary>
        public static void ApplyGameplay(Action<GameplaySettings> modify)
        {
            modify(Gameplay);
            Persist();
        }

        /// <summary>Modify editor settings, persist, and notify.</summary>
        public static void ApplyEditor(Action<EditorSettings> modify)
        {
            modify(Editor);
            Persist();
        }

        private static T LoadFile<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                var yaml = File.ReadAllText(path);
                return YamlReader.Deserialize<T>(yaml);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EngineSettings] Failed to load '{path}': {e.Message}");
                return null;
            }
        }

        private static void WriteFile<T>(string path, T data)
        {
            try
            {
                var yaml = YamlWriter.Serialize(data);
                File.WriteAllText(path, yaml);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EngineSettings] Failed to save '{path}': {e.Message}");
            }
        }
    }
}
