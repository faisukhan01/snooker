// SnookerKit — snooker rules engine (CONTRACTS §2 RulesManager). Pure evaluation: reads other
// managers via ServiceRegistry, mutates only its own phase/required state. TurnManager applies outcomes.
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Snooker rules engine: reds-and-colours sequence, legal-first-contact checks, max(4, value)
    /// fouls, colour respots, the transitional "any colour" stroke after the final red, and frame-end /
    /// respotted-black decisions. <see cref="Evaluate"/> is pure — it reads BallManager, ScoringManager and
    /// MatchManager through ServiceRegistry and mutates only its own internal sequence state; the returned
    /// <see cref="RuleOutcome"/> is applied by TurnManager. Practice mode bypasses fouls and frame end.</summary>
    public class RulesManager : MonoBehaviour
    {
        /// <summary>Internal sequence phase. Invariant: Reds ⇔ RequiredBall == Red; ColoursAfterRed ⇔
        /// RequiredBall == null ("any colour"); ColoursOnly ⇔ a specific colour (yellow → black).</summary>
        private enum RedsAndColoursPhase
        {
            /// <summary>Reds are the ball on; colours may not be potted.</summary>
            Reds = 0,
            /// <summary>A red was just potted (including the transitional stroke after the final red) — any colour is on.</summary>
            ColoursAfterRed = 1,
            /// <summary>Reds exhausted — colours run yellow → black and stay down.</summary>
            ColoursOnly = 2,
        }

        /// <summary>Per-stroke classification scratch — rebuilt on every Evaluate, so a fresh allocation
        /// per shot (never per frame) is acceptable and keeps the flow readable.</summary>
        private sealed class StrokeSummary
        {
            /// <summary>Match-rule foul detected this stroke.</summary>
            public bool Foul;
            /// <summary>Penalty value: max(4, highest-value ball involved in the infringement).</summary>
            public int FoulValue;
            /// <summary>Human readable reason of the highest-value infringement.</summary>
            public string FoulReason = "";
            /// <summary>At least one match-legal object ball was potted.</summary>
            public bool LegalPot;
            /// <summary>Sum of points from match-legal pots (used for the respot-black tie check).</summary>
            public int LegalPotPoints;
            /// <summary>Any object ball potted regardless of legality (practice flow uses this).</summary>
            public bool AnyPot;
            /// <summary>At least one red object ball was potted.</summary>
            public bool RedPotted;
            /// <summary>At least one colour object ball was potted.</summary>
            public bool ColourPotted;
            /// <summary>The black was among the potted object balls.</summary>
            public bool BlackPotted;
            /// <summary>Potted colour object balls in pot order (reds never respot — not collected).</summary>
            public readonly List<BallColor> PottedColours = new List<BallColor>(8);
        }

        private RedsAndColoursPhase _phase = RedsAndColoursPhase.Reds;
        private BallColor? _requiredBall = BallColor.Red;

        /// <summary>Ball-on for the next stroke. null ⇒ "any colour" after a legal red — UI renders ANY.
        /// Refreshed by <see cref="Evaluate"/> after every stroke.</summary>
        public BallColor? RequiredBall { get { return _requiredBall; } }

        /// <summary>Resets sequence state for a fresh frame (Reds phase, red ball on). Called by TurnManager.BeginFrame.</summary>
        public void ResetFrameState()
        {
            _phase = RedsAndColoursPhase.Reds;
            _requiredBall = BallColor.Red;
        }

        private void Awake()
        {
            ServiceRegistry.Register<RulesManager>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<RulesManager>();
        }

        /// <summary>Evaluates one completed stroke and returns the verdict for TurnManager to apply.
        /// Pure with respect to other managers: reads BallManager (reds remaining), ScoringManager
        /// (frame scores for the tie check) and MatchManager (practice bypass) via ServiceRegistry and
        /// mutates only its own phase/required state, which is advanced to the next stroke's ball-on.
        /// Foul value is max(4, highest-value ball involved in the infringement: potted illegally, wrong
        /// first contact, cue potted, cue off table, no contact). Colours potted on a foul stroke respot;
        /// reds always stay down. A legally potted black with no reds remaining ends the frame unless the
        /// effective scores (including this stroke's points, which TurnManager awards after Evaluate) are
        /// tied — then the black respots and the frame continues (CONTRACTS §3.7).</summary>
        public RuleOutcome Evaluate(ShotContext ctx)
        {
            var outcome = new RuleOutcome();
            BallColor? currentRequired = _requiredBall;
            outcome.RequiredBall = currentRequired;

            if (ctx == null) return outcome; // defensive: nothing known — state unchanged

            StrokeSummary summary = ClassifyStroke(ctx, currentRequired);

            bool practice = IsPracticeMode();
            BallManager balls = ServiceRegistry.Get<BallManager>();
            int redsRemaining = balls != null ? balls.RedsRemaining() : 0;

            if (practice)
            {
                // Practice bypass (CONTRACTS §2): no fouls, reds stay down, colours respot, never
                // FrameOver. A stroke that would be a foul under match rules carries the "Practice"
                // marker in FoulReason so the HUD can surface it without penalty points.
                outcome.FoulPoints = 0;
                outcome.FoulReason = summary.Foul ? "Practice" : "";
                for (int i = 0; i < summary.PottedColours.Count; i++)
                {
                    outcome.RespotColors.Add(summary.PottedColours[i]); // practice: colours always respot
                }
                ProgressSequence(ctx, outcome, currentRequired, redsRemaining, summary, true);
                outcome.RequiredBall = _requiredBall;
                return outcome;
            }

            if (summary.Foul)
            {
                outcome.FoulPoints = summary.FoulValue;
                outcome.FoulReason = summary.FoulReason;
                outcome.TurnContinues = false;

                // Every colour potted on a foul stroke respots; reds always stay down.
                for (int i = 0; i < summary.PottedColours.Count; i++)
                {
                    outcome.RespotColors.Add(summary.PottedColours[i]);
                }

                // Sequence state after the foul: reds potted on the foul stay down (redsRemaining is
                // already post-stroke), respotted colours change nothing. The incoming player plays
                // the state as it stands. After the final red goes down on a foul the next stroke is
                // still the transitional "any colour" one.
                if (_phase == RedsAndColoursPhase.Reds)
                {
                    if (redsRemaining > 0)
                    {
                        _requiredBall = BallColor.Red;
                    }
                    else
                    {
                        _phase = RedsAndColoursPhase.ColoursAfterRed;
                        _requiredBall = null;
                    }
                }
                else if (_phase == RedsAndColoursPhase.ColoursAfterRed)
                {
                    _requiredBall = null; // any colour still on for the incoming player
                }
                // ColoursOnly: the required colour respots and stays on.

                outcome.RequiredBall = _requiredBall;
                return outcome;
            }

            ProgressSequence(ctx, outcome, currentRequired, redsRemaining, summary, false);
            outcome.RequiredBall = _requiredBall;
            return outcome;
        }

        /// <summary>Classifies one stroke against match rules: first-contact legality, pot legality per
        /// ball (pot order preserved) and cue-ball infringements. Keeps the highest-value infringement.</summary>
        private StrokeSummary ClassifyStroke(ShotContext ctx, BallColor? currentRequired)
        {
            var summary = new StrokeSummary();
            int onValue = currentRequired.HasValue ? BallPalette.Points(currentRequired.Value) : 0;

            if (!ctx.FirstContact.HasValue)
            {
                MarkFoul(summary, onValue, "No ball contacted");
            }
            else if (!IsContactLegal(ctx.FirstContact.Value, currentRequired))
            {
                MarkFoul(
                    summary,
                    Mathf.Max(onValue, BallPalette.Points(ctx.FirstContact.Value)),
                    string.Format("First contact on {0}", BallPalette.DisplayName(ctx.FirstContact.Value)));
            }

            for (int i = 0; i < ctx.Potted.Count; i++)
            {
                PottedBallRecord record = ctx.Potted[i];
                if (record == null || record.Color == BallColor.Cue) continue; // cue pots arrive via ctx.CuePotted

                if (IsPotLegal(record.Color, currentRequired))
                {
                    summary.LegalPot = true;
                    summary.LegalPotPoints += BallPalette.Points(record.Color);
                }
                else
                {
                    MarkFoul(
                        summary,
                        Mathf.Max(onValue, BallPalette.Points(record.Color)),
                        string.Format("{0} potted when not on", BallPalette.DisplayName(record.Color)));
                }

                summary.AnyPot = true;
                if (record.Color == BallColor.Red)
                {
                    summary.RedPotted = true;
                }
                else
                {
                    summary.ColourPotted = true;
                    summary.PottedColours.Add(record.Color);
                    if (record.Color == BallColor.Black) summary.BlackPotted = true;
                }
            }

            if (ctx.CuePotted) MarkFoul(summary, onValue, "Cue ball potted");
            if (ctx.CueOffTable) MarkFoul(summary, onValue, "Cue ball off table");

            return summary;
        }

        /// <summary>Advances the sequence state for a non-foul stroke (or a practice stroke, where fouls
        /// never apply): phase transitions, colour respots, colours-only walk and the black tie check.</summary>
        private void ProgressSequence(
            ShotContext ctx,
            RuleOutcome outcome,
            BallColor? currentRequired,
            int redsRemaining,
            StrokeSummary summary,
            bool practice)
        {
            bool potted = practice ? summary.AnyPot : summary.LegalPot;
            if (!potted)
            {
                // Legal stroke with no pot — the ball on is unchanged.
                outcome.TurnContinues = false;
                _requiredBall = currentRequired;
                return;
            }

            outcome.TurnContinues = true;

            if (_phase == RedsAndColoursPhase.Reds)
            {
                if (summary.RedPotted)
                {
                    // Red(s) down — the next stroke is "any colour" (RequiredBall null), which also
                    // covers the transitional stroke after the final red: null for exactly one stroke.
                    _phase = RedsAndColoursPhase.ColoursAfterRed;
                    _requiredBall = null;
                }
                else
                {
                    // Reachable in practice only (fouls are off): a colour dropped while reds are the
                    // ball on — it respots (added by the practice branch) and reds remain on.
                    _requiredBall = BallColor.Red;
                }
                return;
            }

            if (_phase == RedsAndColoursPhase.ColoursAfterRed)
            {
                if (!practice)
                {
                    // Colours respot for as long as reds-and-colours is in play.
                    for (int i = 0; i < summary.PottedColours.Count; i++)
                    {
                        outcome.RespotColors.Add(summary.PottedColours[i]);
                    }
                }
                if (redsRemaining > 0)
                {
                    _phase = RedsAndColoursPhase.Reds;
                    _requiredBall = BallColor.Red;
                }
                else
                {
                    _phase = RedsAndColoursPhase.ColoursOnly;
                    _requiredBall = BallColor.Yellow;
                }
                return;
            }

            // ColoursOnly: the potted colour stays down — except the black, which either decides the
            // frame or is respotted on a tie (frame continues, stats not counted — CONTRACTS §3.7).
            if (summary.BlackPotted && !practice)
            {
                _requiredBall = BallColor.Black;
                int score0;
                int score1;
                EffectiveScores(ctx, summary.LegalPotPoints, out score0, out score1);
                if (score0 == score1)
                {
                    outcome.RespotBlack = true;
                    outcome.RespotColors.Add(BallColor.Black); // fresh black respot before the next stroke
                    outcome.TurnContinues = true;
                }
                else
                {
                    outcome.FrameOver = true;
                    outcome.TurnContinues = false;
                }
                return;
            }

            // Non-black colour stays down and the sequence advances. In practice the black respots
            // (added by the practice branch) and NextColour clamps at black — an endless table.
            _requiredBall = NextColour(currentRequired);
        }

        /// <summary>Whether the struck object ball is a legal first contact for the current phase.</summary>
        private bool IsContactLegal(BallColor contact, BallColor? currentRequired)
        {
            if (contact == BallColor.Cue) return false; // the cue ball cannot be its own first contact
            switch (_phase)
            {
                case RedsAndColoursPhase.Reds:
                    return contact == BallColor.Red;
                case RedsAndColoursPhase.ColoursAfterRed:
                    return contact != BallColor.Red; // any colour is on
                default:
                    return currentRequired.HasValue && contact == currentRequired.Value;
            }
        }

        /// <summary>Whether potting this ball is legal for the current phase (cue pots are fouls, never scores).</summary>
        private bool IsPotLegal(BallColor color, BallColor? currentRequired)
        {
            if (color == BallColor.Cue) return false;
            switch (_phase)
            {
                case RedsAndColoursPhase.Reds:
                    return color == BallColor.Red;
                case RedsAndColoursPhase.ColoursAfterRed:
                    return color != BallColor.Red;
                default:
                    return currentRequired.HasValue && color == currentRequired.Value;
            }
        }

        /// <summary>Records an infringement, keeping the highest-value one (first wins on ties).</summary>
        private static void MarkFoul(StrokeSummary summary, int involvedValue, string reason)
        {
            if (!summary.Foul || involvedValue > summary.FoulValue)
            {
                summary.FoulValue = Mathf.Max(4, involvedValue);
                summary.FoulReason = reason;
            }
            summary.Foul = true;
        }

        /// <summary>Effective frame scores including this stroke's legal pot points — Evaluate runs before
        /// TurnManager awards the pots, so the respot-black / frame-decided math must add them here.</summary>
        private static void EffectiveScores(ShotContext ctx, int pendingPoints, out int score0, out int score1)
        {
            score0 = 0;
            score1 = 0;
            ScoringManager scoring = ServiceRegistry.Get<ScoringManager>();
            if (scoring != null)
            {
                int[] live = scoring.Scores;
                if (live != null && live.Length >= 2)
                {
                    score0 = live[0];
                    score1 = live[1];
                }
            }
            int potter = ctx.PlayerIndex;
            if (potter == 1) score1 += pendingPoints;
            else score0 += pendingPoints; // index 0 and defensive clamp for out-of-range indices
        }

        /// <summary>Next colour in the official sequence; clamps at black (practice respot-black loop).</summary>
        private static BallColor NextColour(BallColor? current)
        {
            if (!current.HasValue) return BallColor.Yellow;
            BallColor[] sequence = BallPalette.ColourSequence;
            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] != current.Value) continue;
                if (i + 1 < sequence.Length) return sequence[i + 1];
                break;
            }
            return BallColor.Black;
        }

        /// <summary>True while MatchManager reports practice mode (TryGet, null-safe — defaults to match rules).</summary>
        private static bool IsPracticeMode()
        {
            MatchManager match;
            return ServiceRegistry.TryGet<MatchManager>(out match) && match != null && match.Mode == GameMode.Practice;
        }
    }
}
