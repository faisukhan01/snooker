// SnookerKit — shared enums for the whole game (frozen contract layer).
namespace SnookerKit
{
    /// <summary>Playable match modes (PDF §8). Tournament/online are architecture-ready but not built.</summary>
    public enum GameMode
    {
        /// <summary>Solo practice table: no fouls, reds stay down, colours respot.</summary>
        Practice = 0,
        /// <summary>Single player vs AI opponent.</summary>
        MatchVsAI = 1,
        /// <summary>Two players on one device.</summary>
        TwoPlayerLocal = 2,
        /// <summary>Quick match — short AI frame.</summary>
        QuickMatch = 3,
    }

    /// <summary>AI strength tiers (PDF §15). Error scales with difficulty; no hidden advantages.</summary>
    public enum AiDifficulty
    {
        Beginner = 0,
        Intermediate = 1,
        Advanced = 2,
        Expert = 3,
    }

    /// <summary>Synthesis/audio clip keys — file names in Assets/Resources/Audio (frozen, see CONTRACTS.md).</summary>
    public enum SfxKey
    {
        CueStrike = 0,
        BallBall = 1,
        Cushion = 2,
        PocketDrop = 3,
        PotChime = 4,
        Foul = 5,
        FrameWin = 6,
        UiClick = 7,
        UiBack = 8,
        AmbienceLoop = 9,
    }

    /// <summary>Logical screens (scene names match Assets/Scenes/*).</summary>
    public enum SceneId
    {
        Boot = 0,
        Home = 1,
        Match = 2,
        Practice = 3,
        Settings = 4,
        Profile = 5,
    }

    /// <summary>Legacy screen id kept for ScreenManager stacks (Boot intentionally unused in v1).</summary>
    public enum ScreenId
    {
        Boot = 0,
        Home = 1,
        Match = 2,
        Practice = 3,
        Settings = 4,
        Profile = 5,
    }

    /// <summary>Why a turn changed (HUD banner text derives from this).</summary>
    public enum TurnReason
    {
        StartOfFrame = 0,
        PotContinues = 1,
        Foul = 2,
        NoScore = 3,
        BallInHandD = 4,
        FrameOver = 5,
        RespotBlack = 6,
    }

    /// <summary>Toast severity.</summary>
    public enum ToastType
    {
        Info = 0,
        Good = 1,
        Bad = 2,
    }

    /// <summary>Aim assist strength (PDF §10) — snap degrees per CONTRACTS §5.</summary>
    public enum AimAssistLevel
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    /// <summary>Camera state machine values (PDF §16).</summary>
    public enum CameraState
    {
        Standard = 0,
        CloseAim = 1,
        ShotFollow = 2,
        TopDown = 3,
    }

    /// <summary>TurnManager state machine (CONTRACTS §2).</summary>
    public enum TurnPhase
    {
        Idle = 0,
        AwaitingPlacement = 1,
        AwaitingShot = 2,
        BallsMoving = 3,
        Resolving = 4,
        FrameOver = 5,
    }
}
