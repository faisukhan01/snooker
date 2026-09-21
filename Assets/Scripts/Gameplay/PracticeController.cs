// SnookerKit — endless practice driver (CONTRACTS §2 PracticeController). Added by MatchSceneRoot in
// Practice mode only. RulesManager already bypasses fouls there; this component keeps the session alive
// when a frame would otherwise end.
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Registered in practice mode only (MatchSceneRoot decides) and kept alive across frame endings.
    /// When the rules engine would close the frame, MatchManager.HandleFrameOver delegates here instead:
    /// the table re-racks, scores reset and the same frame index restarts — an endless solo session.
    /// Deliberately no GameEvents subscriptions: it is driven by MatchManager (CONTRACTS §0.9 trivially
    /// satisfied, mirroring ScoringManager's zero-subscription design).</summary>
    [DisallowMultipleComponent]
    public class PracticeController : MonoBehaviour
    {
        private void Awake()
        {
            ServiceRegistry.Register<PracticeController>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<PracticeController>();
        }

        /// <summary>A practice frame "ended" — keep the session endless: rebuild the rack, reset frame scores,
        /// raise FrameStarted with the same frame index and reopen the turn window (TurnManager.BeginFrame
        /// returns the table to player 0 and resets the rules sequence). Frozen step order:
        /// BuildRack → ResetFrameScores → FrameStarted → BeginFrame.</summary>
        public void NotifyFrameWouldEnd()
        {
            BallManager balls;
            if (ServiceRegistry.TryGet<BallManager>(out balls) && balls != null) balls.BuildRack();

            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                scoring.ResetFrameScores();
            }

            int frameIndex = 1;
            MatchManager match;
            if (ServiceRegistry.TryGet<MatchManager>(out match) && match != null)
            {
                frameIndex = Mathf.Max(1, match.CurrentFrame);
            }
            GameEvents.RaiseFrameStarted(new FrameStartedArgs { FrameIndex = frameIndex });

            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null) turns.BeginFrame();
        }
    }
}
