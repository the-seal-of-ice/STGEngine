namespace STGEngine.Core.Timeline
{
    /// <summary>
    /// Identifies the specific function of an ActionEvent.
    /// </summary>
    public enum ActionType
    {
        // ── Presentation ──
        ShowTitle,
        ScreenEffect,
        BgmControl,
        SePlay,
        BackgroundSwitch,
        CameraScript,
        CameraShake,

        // ── Game Logic ──
        BulletClear,
        ItemDrop,
        AutoCollect,
        ScoreTally,

        // ── Flow Control ──
        WaitCondition,
        BranchJump
    }
}
