using System.Collections.Generic;
using UnityEngine;
using STGEngine.Core.Emitters;
using STGEngine.Core.Modifiers;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// A bullet pattern = one emitter + N modifiers.
    /// This is the primary data unit for the pattern editor.
    /// </summary>
    public class BulletPattern
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Emitter: determines initial bullet distribution.</summary>
        public IEmitter Emitter { get; set; }

        /// <summary>Modifiers: applied in order to alter bullet behavior.</summary>
        public List<IModifier> Modifiers { get; set; } = new();

        /// <summary>Bullet visual scale.</summary>
        public float BulletScale { get; set; } = 0.15f;

        /// <summary>Bullet color (RGBA).</summary>
        public Color BulletColor { get; set; } = new Color(1f, 0.3f, 0.3f, 1f);

        /// <summary>Pattern duration in seconds.</summary>
        public float Duration { get; set; } = 5f;
    }
}
