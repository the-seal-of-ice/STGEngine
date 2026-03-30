using UnityEngine;

namespace STGEngine.Core.Timeline
{
    public enum ItemType { PowerSmall, PowerLarge, PointItem, BombFragment, LifeFragment, FullPower }
    public enum DropPattern { AtPosition, FromBoss, RandomSpread, ArcSpread }

    public class ItemDropParams : IActionParams
    {
        public ItemType Type { get; set; } = ItemType.PointItem;
        public int Count { get; set; } = 1;
        public DropPattern Pattern { get; set; } = DropPattern.FromBoss;
        /// <summary>World position for AtPosition mode.</summary>
        public Vector3 Position { get; set; } = Vector3.zero;
        /// <summary>Spread radius for RandomSpread / ArcSpread.</summary>
        public float SpreadRadius { get; set; } = 3f;
    }
}
