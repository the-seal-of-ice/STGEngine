using UnityEngine;
using STGEngine.Core.DataModel;

namespace STGEngine.Runtime.Player
{
    public struct ItemPickupResult
    {
        public int PowerSmallCount;
        public int PowerLargeCount;
        public int PointItemCount;
        public int BombFragmentCount;
        public int LifeFragmentCount;
        public int FullPowerCount;
    }

    /// <summary>
    /// 道具效果应用。纯静态方法，将拾取结果写入 PlayerState。
    /// </summary>
    public static class ItemEffects
    {
        public static void Apply(ItemPickupResult pickup, PlayerState state, PlayerProfile profile)
        {
            // Power
            state.Power += pickup.PowerSmallCount * profile.PowerPerSmallItem
                         + pickup.PowerLargeCount * profile.PowerPerLargeItem;
            if (pickup.FullPowerCount > 0)
                state.Power = profile.MaxPower;
            state.Power = Mathf.Min(state.Power, profile.MaxPower);

            // Score
            state.Score += pickup.PointItemCount * state.PointItemValue;

            // 残机碎片
            state.LifeFragments += pickup.LifeFragmentCount;
            if (state.LifeFragments >= profile.LifeFragmentsPerLife)
            {
                state.Lives++;
                state.LifeFragments -= profile.LifeFragmentsPerLife;
            }

            // Bomb 碎片
            state.BombFragments += pickup.BombFragmentCount;
            if (state.BombFragments >= profile.BombFragmentsPerBomb)
            {
                state.Bombs++;
                state.BombFragments -= profile.BombFragmentsPerBomb;
            }
        }
    }
}
