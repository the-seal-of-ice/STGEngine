using System;
using System.Collections.Generic;
using System.Reflection;

namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// Scans assemblies at startup for [TypeTag] attributes and builds
    /// bidirectional tag-to-Type mappings for polymorphic YAML serialization.
    /// </summary>
    public static class TypeRegistry
    {
        private static readonly Dictionary<string, Type> _tagToType = new();
        private static readonly Dictionary<Type, string> _typeToTag = new();
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Scan all loaded assemblies for [TypeTag] types
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system/Unity assemblies for performance
                var name = assembly.GetName().Name;
                if (name.StartsWith("Unity") || name.StartsWith("System")
                    || name.StartsWith("mscorlib") || name.StartsWith("Mono")
                    || name.StartsWith("netstandard"))
                    continue;

                ScanAssembly(assembly);
            }
        }

        /// <summary>
        /// Manually register additional assemblies (e.g. plugin assemblies).
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            EnsureInitialized();
            ScanAssembly(assembly);
        }

        public static Type Resolve(string tag)
        {
            EnsureInitialized();
            if (_tagToType.TryGetValue(tag, out var type))
                return type;
            throw new KeyNotFoundException($"Unknown type tag: '{tag}'");
        }

        public static string GetTag(Type type)
        {
            EnsureInitialized();
            if (_typeToTag.TryGetValue(type, out var tag))
                return tag;
            throw new KeyNotFoundException($"No TypeTag for type: {type.Name}");
        }

        public static bool TryResolve(string tag, out Type type)
        {
            EnsureInitialized();
            return _tagToType.TryGetValue(tag, out type);
        }

        public static bool TryGetTag(Type type, out string tag)
        {
            EnsureInitialized();
            return _typeToTag.TryGetValue(type, out tag);
        }

        /// <summary>
        /// Force re-scan (useful for tests or hot-reload).
        /// </summary>
        public static void Reset()
        {
            _tagToType.Clear();
            _typeToTag.Clear();
            _initialized = false;
        }

        private static void ScanAssembly(Assembly assembly)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<TypeTagAttribute>();
                    if (attr == null) continue;
                    _tagToType[attr.Tag] = type;
                    _typeToTag[type] = attr.Tag;
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Some assemblies may fail to load types; skip silently.
            }
        }
    }
}
