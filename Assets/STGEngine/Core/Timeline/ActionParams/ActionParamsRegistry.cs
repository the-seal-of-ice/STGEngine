using System;
using System.Collections.Generic;

namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Maps ActionType → IActionParams concrete type.
    /// Used by the serializer to instantiate the correct params class during deserialization.
    /// </summary>
    public static class ActionParamsRegistry
    {
        private static readonly Dictionary<ActionType, Type> _map = new()
        {
            { ActionType.ShowTitle,        typeof(ShowTitleParams) },
            { ActionType.ScreenEffect,     typeof(ScreenEffectParams) },
            { ActionType.BgmControl,       typeof(BgmControlParams) },
            { ActionType.SePlay,           typeof(SePlayParams) },
            { ActionType.BackgroundSwitch, typeof(BackgroundSwitchParams) },
            { ActionType.BulletClear,      typeof(BulletClearParams) },
            { ActionType.ItemDrop,         typeof(ItemDropParams) },
            { ActionType.AutoCollect,      typeof(AutoCollectParams) },
            { ActionType.ScoreTally,       typeof(ScoreTallyParams) },
            { ActionType.WaitCondition,    typeof(WaitConditionParams) },
            { ActionType.BranchJump,       typeof(BranchJumpParams) },
            { ActionType.CameraScript,    typeof(CameraScriptParams) },
            { ActionType.CameraShake,     typeof(CameraShakeParams) },
        };

        public static Type Resolve(ActionType type) =>
            _map.TryGetValue(type, out var t) ? t : null;

        public static IActionParams CreateDefault(ActionType type)
        {
            var t = Resolve(type);
            return t != null ? (IActionParams)Activator.CreateInstance(t) : null;
        }
    }
}
