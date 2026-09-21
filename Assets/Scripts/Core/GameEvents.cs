// SnookerKit — static typed event hub (frozen, CONTRACTS §3). Raise methods are the only way events fire.
using System;

namespace SnookerKit
{
    #region Args

    /// <summary>Raised once a match request has been normalized and managers reset.</summary>
    public class MatchStartedArgs { public GameMode Mode; public string PlayerOne; public string PlayerTwo; public AiDifficulty AiDifficulty; }

    /// <summary>Raised at the start of every frame (including respot-black continuations).</summary>
    public class FrameStartedArgs { public int FrameIndex; }

    /// <summary>Raised the instant a strike is committed.</summary>
    public class ShotStartedArgs { public int PlayerIndex; public float Power01; public Vector2 Spin; public float AimAngle; }

    /// <summary>Raised when accumulated aim delta crosses the 0.0005 rad batch threshold.</summary>
    public class AimChangedArgs { public float AimAngle; }

    /// <summary>Raised on cue strike (audio/haptics/camera hooks).</summary>
    public class CueStruckArgs { public float Power01; public Vector2 Spin; public float AimAngle; }

    /// <summary>Ball-to-ball collision, speed-scaled for audio.</summary>
    public class BallBallHitArgs { public float Speed; }

    /// <summary>Ball-to-cushion collision, speed-scaled for audio.</summary>
    public class CushionHitArgs { public float Speed; }

    /// <summary>An object ball entered a pocket sensor. Cue pots use FoulCommitted instead — never a score.</summary>
    public class PottedBallArgs { public BallColor Color; public int PocketIndex; }

    /// <summary>All balls settled — carries the completed ShotContext to TurnManager. Single resolve source of truth.</summary>
    public class BallsAtRestArgs { public ShotContext Context; }

    /// <summary>Rules engine verdict applied (HUD break/required updates).</summary>
    public class ShotResolvedArgs { public RuleOutcome Outcome; }

    /// <summary>Running break changed (value 0 = break ended).</summary>
    public class BreakChangedArgs { public int Break; }

    /// <summary>Score/required state changed. Scores is a defensive copy.</summary>
    public class ScoreChangedArgs { public int[] Scores; public int CurrentBreak; public BallColor? Required; }

    /// <summary>Turn handed over. PlayerIndex is the INCOMING player; CanShoot gates input + AI wait loops.</summary>
    public class TurnChangedArgs { public int PlayerIndex; public bool IsAI; public TurnReason Reason; public bool CanShoot; public BallColor? RequiredBall; }

    /// <summary>Foul applied. PlayerIndex is the INCOMING (benefiting) player; reason carries the rules text.</summary>
    public class FoulCommittedArgs { public int Points; public string Reason; public int PlayerIndex; }

    /// <summary>Frame decided. RespotBlack=true means the frame CONTINUES (stats must not count it — CONTRACTS §3.7).</summary>
    public class FrameEndedArgs { public int WinnerIndex; public bool RespotBlack; }

    /// <summary>Match decided.</summary>
    public class MatchEndedArgs { public int WinnerIndex; }

    /// <summary>Game Tools screenshot wrote a file (path is persistentDataPath-relative).</summary>
    public class ScreenshotCompletedArgs { public string Path; }

    #endregion

    /// <summary>All cross-module gameplay events. Modules subscribe in OnEnable/unsubscribe in OnDisable (CONTRACTS §0.9).
    /// Raise* methods null-check so ordering of subscriptions never throws.</summary>
    public static class GameEvents
    {
        public static event Action<MatchStartedArgs> MatchStarted;
        public static event Action<FrameStartedArgs> FrameStarted;
        public static event Action<ShotStartedArgs> ShotStarted;
        public static event Action<AimChangedArgs> AimChanged;
        public static event Action<CueStruckArgs> CueStruck;
        public static event Action<BallBallHitArgs> BallBallHit;
        public static event Action<CushionHitArgs> CushionHit;
        public static event Action<PottedBallArgs> PottedBall;
        public static event Action<BallsAtRestArgs> BallsAtRest;
        public static event Action<ShotResolvedArgs> ShotResolved;
        public static event Action<BreakChangedArgs> BreakChanged;
        public static event Action<ScoreChangedArgs> ScoreChanged;
        public static event Action<TurnChangedArgs> TurnChanged;
        public static event Action<FoulCommittedArgs> FoulCommitted;
        public static event Action<FrameEndedArgs> FrameEnded;
        public static event Action<MatchEndedArgs> MatchEnded;
        public static event Action<ScreenshotCompletedArgs> ScreenshotCompleted;

        public static void RaiseMatchStarted(MatchStartedArgs args) { var h = MatchStarted; if (h != null) h(args); }
        public static void RaiseFrameStarted(FrameStartedArgs args) { var h = FrameStarted; if (h != null) h(args); }
        public static void RaiseShotStarted(ShotStartedArgs args) { var h = ShotStarted; if (h != null) h(args); }
        public static void RaiseAimChanged(AimChangedArgs args) { var h = AimChanged; if (h != null) h(args); }
        public static void RaiseCueStruck(CueStruckArgs args) { var h = CueStruck; if (h != null) h(args); }
        public static void RaiseBallBallHit(BallBallHitArgs args) { var h = BallBallHit; if (h != null) h(args); }
        public static void RaiseCushionHit(CushionHitArgs args) { var h = CushionHit; if (h != null) h(args); }
        public static void RaisePottedBall(PottedBallArgs args) { var h = PottedBall; if (h != null) h(args); }
        public static void RaiseBallsAtRest(BallsAtRestArgs args) { var h = BallsAtRest; if (h != null) h(args); }
        public static void RaiseShotResolved(ShotResolvedArgs args) { var h = ShotResolved; if (h != null) h(args); }
        public static void RaiseBreakChanged(BreakChangedArgs args) { var h = BreakChanged; if (h != null) h(args); }
        public static void RaiseScoreChanged(ScoreChangedArgs args) { var h = ScoreChanged; if (h != null) h(args); }
        public static void RaiseTurnChanged(TurnChangedArgs args) { var h = TurnChanged; if (h != null) h(args); }
        public static void RaiseFoulCommitted(FoulCommittedArgs args) { var h = FoulCommitted; if (h != null) h(args); }
        public static void RaiseFrameEnded(FrameEndedArgs args) { var h = FrameEnded; if (h != null) h(args); }
        public static void RaiseMatchEnded(MatchEndedArgs args) { var h = MatchEnded; if (h != null) h(args); }
        public static void RaiseScreenshotCompleted(ScreenshotCompletedArgs args) { var h = ScreenshotCompleted; if (h != null) h(args); }
    }
}
