// SnookerKit — frame scoring + break tracking (CONTRACTS §2 ScoringManager).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Two-player frame scores, the running break and foul payment. Scoring is deliberately dumb:
    /// RulesManager decides legality, TurnManager applies the verdict by calling AwardPot/AwardFoul, and
    /// this component only accumulates points and raises BreakChanged / ScoreChanged / FoulCommitted.
    /// Has no GameEvents subscriptions by design — nothing here needs to listen (CONTRACTS §0.9 is
    /// trivially satisfied: zero subscriptions, zero unsubscriptions needed).</summary>
    public class ScoringManager : MonoBehaviour
    {
        private readonly int[] _scores = { 0, 0 };
        private int _currentBreak;
        private int _breakOwner = -1;

        /// <summary>Frame scores per player index (0/1). A defensive copy is returned on every access —
        /// callers can never mutate internal state.</summary>
        public int[] Scores { get { return new[] { _scores[0], _scores[1] }; } }

        /// <summary>Running break of the player at the table (0 after EndBreak / ResetFrameScores).</summary>
        public int CurrentBreak { get { return _currentBreak; } }

        /// <summary>Player index owning the running break, or -1 when no break is live.</summary>
        public int BreakOwner { get { return _breakOwner; } }

        private void Awake()
        {
            ServiceRegistry.Register<ScoringManager>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<ScoringManager>();
        }

        /// <summary>Awards the pot value of one legally potted ball to the player at the table (TurnManager's
        /// CurrentPlayerIndex, TryGet null-safe). Starts a new break when the scoring player differs from the
        /// break owner (BreakChanged effectively fires 0 → value), otherwise extends the running break. Always
        /// raises BreakChanged with the new break value and ScoreChanged with the required ball from RulesManager.</summary>
        public void AwardPot(BallColor color)
        {
            if (color == BallColor.Cue) return; // cue pots are fouls — never a score (CONTRACTS §3)
            int points = BallPalette.Points(color);
            if (points <= 0) return; // defensive: nothing to award

            int player = CurrentPlayerSafe();
            if (_breakOwner != player)
            {
                _breakOwner = player; // new visit at the table
                _currentBreak = 0;
            }
            _currentBreak += points;
            _scores[player] += points;

            GameEvents.RaiseBreakChanged(new BreakChangedArgs { Break = _currentBreak });
            GameEvents.RaiseScoreChanged(new ScoreChangedArgs
            {
                Scores = Scores,
                CurrentBreak = _currentBreak,
                Required = RequiredFromRules(),
            });
        }

        /// <summary>Ends the running break: CurrentBreak back to 0 and BreakOwner cleared, announced via
        /// BreakChanged(0). Called by TurnManager whenever the table changes hands.</summary>
        public void EndBreak()
        {
            _currentBreak = 0;
            _breakOwner = -1;
            GameEvents.RaiseBreakChanged(new BreakChangedArgs { Break = 0 });
        }

        /// <summary>Pays foul points to the opponent of the offending player. The offending player is
        /// TurnManager.CurrentPlayerIndex at resolve time (the foul is awarded before the turn handover),
        /// so the benefiting player is the incoming one — echoed in FoulCommittedArgs.PlayerIndex. Raises
        /// ScoreChanged then FoulCommitted. Does not touch the break: TurnManager ends breaks on handover.</summary>
        public void AwardFoul(int points, string reason = "")
        {
            if (points <= 0) return; // defensive: no negative/zero penalties
            int offending = CurrentPlayerSafe();
            int benefiting = 1 - offending;
            _scores[benefiting] += points;

            GameEvents.RaiseScoreChanged(new ScoreChangedArgs
            {
                Scores = Scores,
                CurrentBreak = _currentBreak,
                Required = RequiredFromRules(),
            });
            GameEvents.RaiseFoulCommitted(new FoulCommittedArgs
            {
                Points = points,
                Reason = reason ?? "",
                PlayerIndex = benefiting,
            });
        }

        /// <summary>Zeroes frame scores and the running break (frame start / restart). Raises ScoreChanged
        /// so the HUD clears at frame start; the ScoreChangedArgs already carries CurrentBreak = 0.</summary>
        public void ResetFrameScores()
        {
            _scores[0] = 0;
            _scores[1] = 0;
            _currentBreak = 0;
            _breakOwner = -1;
            GameEvents.RaiseScoreChanged(new ScoreChangedArgs
            {
                Scores = Scores,
                CurrentBreak = 0,
                Required = RequiredFromRules(),
            });
        }

        /// <summary>Player index at the table, clamped defensively to 0 when TurnManager is absent.</summary>
        private static int CurrentPlayerSafe()
        {
            TurnManager turns;
            if (ServiceRegistry.TryGet<TurnManager>(out turns) && turns != null)
            {
                int index = turns.CurrentPlayerIndex;
                if (index == 0 || index == 1) return index;
            }
            return 0;
        }

        /// <summary>Required ball from RulesManager (null ⇒ "any colour"), null-safe.</summary>
        private static BallColor? RequiredFromRules()
        {
            RulesManager rules;
            return ServiceRegistry.TryGet<RulesManager>(out rules) ? rules.RequiredBall : (BallColor?)null;
        }
    }
}
