// SnookerKit — match / frame orchestration (CONTRACTS §2 MatchManager). Consumes a MatchRequest, resets
// scoring, starts frames through BallManager + TurnManager and decides frame/match outcomes. TurnManager
// calls HandleFrameOver / NotifyRespottedBlack; menu code drives Concede* / Restart*.
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns the match shell around the frames: mode, player names, best-of length, frames won and
    /// the current frame index. Deliberately has no GameEvents subscriptions — it is driven by TurnManager
    /// (HandleFrameOver / NotifyRespottedBlack) and by menu actions, so CONTRACTS §0.9 is trivially satisfied.
    /// In Practice mode frame endings delegate to <see cref="PracticeController"/> so the table never dies.</summary>
    [DisallowMultipleComponent]
    public class MatchManager : MonoBehaviour
    {
        private readonly int[] _framesWon = { 0, 0 };
        private readonly string[] _playerNames = { "Player 1", "Player 2" };
        private MatchRequest _request;
        private GameMode _mode = GameMode.MatchVsAI; // conservative default (StatisticsTracker convention)
        private int _currentFrame = 1;
        private int _frames;

        /// <summary>Playable mode of the running match (Practice / MatchVsAI / TwoPlayerLocal / QuickMatch).</summary>
        public GameMode Mode { get { return _mode; } }

        /// <summary>Display names per player index. Shared internal array — treat as read-only (names are
        /// immutable for the lifetime of the match; zero-alloc access for HUD event handlers).</summary>
        public string[] PlayerNames { get { return _playerNames; } }

        /// <summary>Frames won per player index. A defensive copy is returned on every access (mirrors
        /// ScoringManager.Scores) — callers can never mutate the internal tally.</summary>
        public int[] FramesWon { get { return new[] { _framesWon[0], _framesWon[1] }; } }

        /// <summary>1-based index of the frame in progress (practice keeps its single frame forever).</summary>
        public int CurrentFrame { get { return _currentFrame; } }

        /// <summary>Best-of length from the MatchRequest (0/1 = single frame; a win needs &gt; Frames / 2).</summary>
        public int Frames { get { return _frames; } }

        private void Awake()
        {
            ServiceRegistry.Register<MatchManager>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<MatchManager>();
        }

        // -------------------------------------------------------------------------------------------------
        // Match lifecycle
        // -------------------------------------------------------------------------------------------------

        /// <summary>Normalizes and stores the request, resets scores/frames-won, announces MatchStarted and
        /// opens frame 1 (BuildRack → BeginFrame → FrameStarted). Null requests fall back to a sane default.</summary>
        public void StartMatch(MatchRequest request)
        {
            if (request == null) request = MatchRequest.Create(GameMode.MatchVsAI, AiDifficulty.Intermediate, 1);
            request.Normalize();

            _request = request;
            _mode = request.Mode;
            _playerNames[0] = request.PlayerOne;
            _playerNames[1] = request.PlayerTwo;
            _frames = request.Frames;

            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                scoring.ResetFrameScores();
            }

            _framesWon[0] = 0;
            _framesWon[1] = 0;
            _currentFrame = 1;

            GameEvents.RaiseMatchStarted(new MatchStartedArgs
            {
                Mode = _mode,
                PlayerOne = _playerNames[0],
                PlayerTwo = _playerNames[1],
                AiDifficulty = request.Difficulty,
            });

            StartFrameInternal();
        }

        /// <summary>Advances to the next frame: resets frame scores, rebuilds the rack, reopens the turn
        /// window and raises FrameStarted.</summary>
        public void StartNextFrame()
        {
            _currentFrame++;
            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                scoring.ResetFrameScores();
            }
            StartFrameInternal();
        }

        /// <summary>Frame decided (TurnManager on outcome.FrameOver; ConcedeFrame). In practice the ending
        /// delegates to <see cref="PracticeController.NotifyFrameWouldEnd"/> — the session is endless. In match
        /// modes the win is tallied and FrameEnded(RespotBlack = false) raised; reaching more than half the
        /// best-of length ends the match, otherwise the next frame starts.</summary>
        public void HandleFrameOver(int winnerIndex)
        {
            if (_mode == GameMode.Practice)
            {
                PracticeController practice;
                if (ServiceRegistry.TryGet<PracticeController>(out practice) && practice != null)
                {
                    practice.NotifyFrameWouldEnd();
                    return;
                }
                Debug.LogWarning("MatchManager: practice mode without PracticeController — re-racking directly.");
                ReRackPracticeFrame(); // defensive fallback: keep the endless table alive
                return;
            }

            if (winnerIndex != 0 && winnerIndex != 1) winnerIndex = 0; // defensive clamp (callers pass 0/1)

            _framesWon[winnerIndex]++;
            GameEvents.RaiseFrameEnded(new FrameEndedArgs { WinnerIndex = winnerIndex, RespotBlack = false });

            if (_frames > 0 && _framesWon[winnerIndex] > _frames / 2)
            {
                GameEvents.RaiseMatchEnded(new MatchEndedArgs { WinnerIndex = winnerIndex });
            }
            else
            {
                StartNextFrame();
            }
        }

        /// <summary>Respotted-black marker (TurnManager on outcome.RespotBlack, CONTRACTS §3.7): raises
        /// FrameEnded(WinnerIndex = -1, RespotBlack = true) — the frame CONTINUES and the event is a HUD/audio
        /// marker only. Frames played/won are NOT counted here; StatisticsTracker ignores RespotBlack events.</summary>
        public void NotifyRespottedBlack()
        {
            GameEvents.RaiseFrameEnded(new FrameEndedArgs { WinnerIndex = -1, RespotBlack = true });
        }

        /// <summary>Player <paramref name="playerIndex"/> concedes the match: the opponent wins immediately.</summary>
        public void ConcedeMatch(int playerIndex)
        {
            if (playerIndex != 0 && playerIndex != 1) playerIndex = 0; // defensive clamp
            GameEvents.RaiseMatchEnded(new MatchEndedArgs { WinnerIndex = 1 - playerIndex });
        }

        /// <summary>Restarts the whole match from the stored (re-normalized) request; falls back to a sane
        /// default when no request was ever stored.</summary>
        public void RestartMatch()
        {
            MatchRequest request = _request != null
                ? _request
                : MatchRequest.Create(GameMode.MatchVsAI, AiDifficulty.Intermediate, 1);
            StartMatch(request);
        }

        /// <summary>Re-plays the current frame in place: scores reset, rack rebuilt, turn window reopened,
        /// FrameStarted re-raised with the same index.</summary>
        public void RestartFrame()
        {
            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                scoring.ResetFrameScores();
            }

            BallManager balls;
            if (ServiceRegistry.TryGet<BallManager>(out balls) && balls != null) balls.BuildRack();

            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null) turns.BeginFrame();

            GameEvents.RaiseFrameStarted(new FrameStartedArgs { FrameIndex = _currentFrame });
        }

        /// <summary>The player at the table concedes the current frame. In practice this awards a nominal
        /// frame (see below); otherwise the opponent is handed the frame through <see cref="HandleFrameOver"/>.</summary>
        public void ConcedeFrame()
        {
            int current = 0;
            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null)
            {
                int index = turns.CurrentPlayerIndex;
                if (index == 0 || index == 1) current = index;
            }

            if (_mode == GameMode.Practice)
            {
                // Documented v1 behaviour (CONTRACTS §10): a practice concession awards a nominal frame to
                // player 0's session tally so the concession input has visible effect. The frame itself is
                // still never counted as played — StatisticsTracker gates practice frames out entirely.
                _framesWon[0]++;
            }

            HandleFrameOver(1 - current);
        }

        // -------------------------------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------------------------------

        /// <summary>Shared frame-opening sequence: BuildRack → TurnManager.BeginFrame → FrameStarted.
        /// Every step is TryGet-defensive; a missing manager logs one warning and the rest still runs.</summary>
        private void StartFrameInternal()
        {
            BallManager balls;
            if (ServiceRegistry.TryGet<BallManager>(out balls) && balls != null)
            {
                balls.BuildRack();
            }
            else
            {
                Debug.LogWarning("MatchManager: BallManager unavailable — frame starting without a rack.");
            }

            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null)
            {
                turns.BeginFrame();
            }
            else
            {
                Debug.LogWarning("MatchManager: TurnManager unavailable — no turn window for this frame.");
            }

            GameEvents.RaiseFrameStarted(new FrameStartedArgs { FrameIndex = _currentFrame });
        }

        /// <summary>Inline practice re-rack fallback, mirroring PracticeController.NotifyFrameWouldEnd's
        /// frozen order (BuildRack → ResetFrameScores → FrameStarted → BeginFrame). Only runs when the
        /// PracticeController component is missing, which composition should never allow.</summary>
        private void ReRackPracticeFrame()
        {
            BallManager balls;
            if (ServiceRegistry.TryGet<BallManager>(out balls) && balls != null) balls.BuildRack();

            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                scoring.ResetFrameScores();
            }

            GameEvents.RaiseFrameStarted(new FrameStartedArgs { FrameIndex = Mathf.Max(1, _currentFrame) });

            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null) turns.BeginFrame();
        }
    }
}
