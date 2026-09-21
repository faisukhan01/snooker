// SnookerKit — turn state machine + rules application (CONTRACTS §2 TurnManager). Subscribes
// CueStruck/BallsAtRest, drives the Idle → AwaitingPlacement → AwaitingShot → BallsMoving → Resolving
// (+ FrameOver) machine and applies RulesManager verdicts via ScoringManager/BallManager/MatchManager.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns whose turn it is and when shots may be fired. Pipe: a strike (<see cref="GameEvents.CueStruck"/>)
    /// opens the tracking window (BallsMoving), the settled table (<see cref="GameEvents.BallsAtRest"/>) is resolved
    /// through <see cref="RulesManager.Evaluate"/>, and the verdict is applied here: respots → scoring →
    /// <see cref="GameEvents.ShotResolved"/> → frame/respot-black decisions → <see cref="GameEvents.TurnChanged"/>.
    /// <see cref="CanShootNow"/> is deliberately true during AI turns too — AIController's bounded wait and
    /// InputRouter both poll it, and gating it on <see cref="IsAITurn"/> deadlocks the AI (real v0 integration bug).
    /// A <see cref="GameConfig.PhysicsTuning.MaxStrikeWaitSeconds"/> watchdog coroutine force-hands stalled turns
    /// over so a blocked placement or an unresponsive player can never freeze the frame.</summary>
    [DisallowMultipleComponent]
    public class TurnManager : MonoBehaviour
    {
        private TurnPhase _phase = TurnPhase.Idle;
        private int _currentPlayerIndex;
        private BallManager _balls; // lazy ServiceRegistry cache (registration order is undefined)
        private Coroutine _watchdog;

        /// <summary>Current state machine phase (HUD/debug introspection; the pinned gameplay surface is
        /// <see cref="CanShootNow"/> / <see cref="IsAITurn"/> / <see cref="CurrentPlayerIndex"/>).</summary>
        public TurnPhase Phase { get { return _phase; } }

        /// <summary>True only while the table is waiting for a strike (phase AwaitingShot). Intentionally NOT
        /// gated on <see cref="IsAITurn"/> — the AI plays through the same window a human uses, so this must be
        /// TRUE during AI turns as well (AIController waits on it before planning/firing).</summary>
        public bool CanShootNow { get { return _phase == TurnPhase.AwaitingShot; } }

        /// <summary>True when the incoming player is the AI opponent: player index 1 while MatchManager reports
        /// MatchVsAI or QuickMatch. Every other mode (and a missing MatchManager) is human-by-default.</summary>
        public bool IsAITurn
        {
            get
            {
                if (_currentPlayerIndex != 1) return false;
                MatchManager match;
                if (ServiceRegistry.TryGet<MatchManager>(out match) && match != null)
                {
                    return match.Mode == GameMode.MatchVsAI || match.Mode == GameMode.QuickMatch;
                }
                return false;
            }
        }

        /// <summary>Player index (0/1) currently at the table. TurnChanged announces the INCOMING index.</summary>
        public int CurrentPlayerIndex { get { return _currentPlayerIndex; } }

        /// <summary>Lazily resolves the BallManager service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls
        {
            get
            {
                if (_balls == null) ServiceRegistry.TryGet<BallManager>(out _balls);
                return _balls;
            }
        }

        private void Awake()
        {
            ServiceRegistry.Register<TurnManager>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<TurnManager>();
            StopAllCoroutines();
            _watchdog = null;
        }

        private void OnEnable()
        {
            GameEvents.CueStruck += OnCueStruck;
            GameEvents.BallsAtRest += OnBallsAtRest;
            if (_watchdog == null) _watchdog = StartCoroutine(Watchdog());
        }

        private void OnDisable()
        {
            GameEvents.CueStruck -= OnCueStruck;
            GameEvents.BallsAtRest -= OnBallsAtRest;
            if (_watchdog != null)
            {
                StopCoroutine(_watchdog);
                _watchdog = null;
            }
        }

        /// <summary>Opens a fresh frame: resets the rules sequence, returns the table to player 0 and raises
        /// TurnChanged(StartOfFrame, CanShoot = true) with <see cref="RulesManager.RequiredBall"/>. Called by
        /// MatchManager after BuildRack (frame start / restart / practice re-rack).</summary>
        public void BeginFrame()
        {
            RulesManager rules;
            if (ServiceRegistry.TryGet<RulesManager>(out rules) && rules != null) rules.ResetFrameState();

            BallManager balls = Balls;
            if (balls != null && balls.CueBallNeedsPlacement)
            {
                balls.EndCueBallPlacement(); // defensive: stale ball-in-hand across a frame restart
            }

            _currentPlayerIndex = 0;
            _phase = TurnPhase.AwaitingShot;
            RaiseTurnChanged(TurnReason.StartOfFrame, true);
        }

        private void Update()
        {
            // Ball-in-hand release: BallInHandPlacer / AIController call BallManager.EndCueBallPlacement
            // directly. When the cue ball is released we open the shot window. Deliberately NO second
            // TurnChanged here — TurnChanged(BallInHandD) already announced the incoming player, and both
            // AIController and InputRouter poll CanShootNow every frame.
            if (_phase != TurnPhase.AwaitingPlacement) return;
            BallManager balls = Balls;
            if (balls != null && !balls.CueBallNeedsPlacement)
            {
                _phase = TurnPhase.AwaitingShot;
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Event flow
        // -------------------------------------------------------------------------------------------------

        /// <summary>Strike committed while AwaitingShot: publishes ShotStarted, moves to BallsMoving and hands
        /// the shot to BallManager.BeginShotTracking (the context carries the cue-ball origin for the resolver).</summary>
        private void OnCueStruck(CueStruckArgs args)
        {
            if (args == null) return;
            if (_phase != TurnPhase.AwaitingShot) return; // ignored outside the shot window

            BallManager balls = Balls;
            Ball cue = balls != null ? balls.CueBall : null;
            if (balls == null || cue == null) return; // defensive: no table yet

            ShotContext ctx = new ShotContext
            {
                PlayerIndex = _currentPlayerIndex,
                Power01 = args.Power01,
                Spin = args.Spin,
                AimAngle = args.AimAngle,
                CueOrigin = cue.transform.position,
            };

            GameEvents.RaiseShotStarted(new ShotStartedArgs
            {
                PlayerIndex = ctx.PlayerIndex,
                Power01 = ctx.Power01,
                Spin = ctx.Spin,
                AimAngle = ctx.AimAngle,
            });

            _phase = TurnPhase.BallsMoving;
            balls.BeginShotTracking(ctx);
        }

        /// <summary>Table settled while BallsMoving: moves to Resolving, asks RulesManager for the verdict and
        /// applies it. Events for other phases (stale context after a frame restart) are ignored.</summary>
        private void OnBallsAtRest(BallsAtRestArgs args)
        {
            if (args == null || _phase != TurnPhase.BallsMoving) return;

            ShotContext ctx = args.Context;
            if (ctx == null)
            {
                // Defensive: a rest event without a context carries no verdict — reopen the shot window.
                _phase = TurnPhase.AwaitingShot;
                RaiseTurnChanged(TurnReason.NoScore, true);
                return;
            }

            _phase = TurnPhase.Resolving;
            RulesManager rules;
            RuleOutcome outcome = ServiceRegistry.TryGet<RulesManager>(out rules) && rules != null
                ? rules.Evaluate(ctx)
                : new RuleOutcome(); // defensive: no rules engine → legal no-score stroke
            ApplyOutcome(outcome, ctx);
        }

        // -------------------------------------------------------------------------------------------------
        // Verdict application (CONTRACTS §2 — fixed order)
        // -------------------------------------------------------------------------------------------------

        /// <summary>Applies the rules verdict in the frozen order: 1) respot loop, 2) foul scoring + ball-in-hand
        /// or handover, 3) pot scoring + continue/handover, 4) ShotResolved, 5) frame-over / respot-black
        /// decisions. Exactly one TurnChanged is raised for the normal paths.</summary>
        private void ApplyOutcome(RuleOutcome outcome, ShotContext ctx)
        {
            if (outcome == null) outcome = new RuleOutcome(); // defensive: malformed verdict
            if (ctx == null) ctx = new ShotContext();

            // ---- 1) respot loop: every colour the verdict asks for goes back on its spot ----
            RespotOutcomeColours(outcome);

            bool announced = false; // true once the TurnChanged for this stroke has been raised

            // ---- 2) foul: pay the penalty, then ball-in-hand or a plain handover ----
            if (outcome.FoulPoints > 0)
            {
                ScoringManager scoring;
                if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
                {
                    scoring.AwardFoul(outcome.FoulPoints, outcome.FoulReason); // offending = current player
                }
                HandoverTable(); // break ends; CurrentPlayerIndex is the incoming player from here on

                if (ctx.CuePotted || ctx.CueOffTable)
                {
                    BallManager balls = Balls;
                    if (balls != null) balls.BeginCueBallPlacement(true);
                    _phase = TurnPhase.AwaitingPlacement;
                    RaiseTurnChanged(TurnReason.BallInHandD, false);
                }
                else
                {
                    _phase = TurnPhase.AwaitingShot;
                    RaiseTurnChanged(TurnReason.Foul, true);
                }
                announced = true;
            }
            // ---- 3) legal pots: award in pot order, then same player continues or the table turns over ----
            else if (ctx.HasObjectPot)
            {
                AwardPotsInPotOrder(ctx);
                if (outcome.TurnContinues && !outcome.FrameOver && !outcome.RespotBlack)
                {
                    _phase = TurnPhase.AwaitingShot;
                    RaiseTurnChanged(TurnReason.PotContinues, true);
                    announced = true;
                }
                // FrameOver / RespotBlack decide below; a legal pot that neither continues nor decides
                // (defensive — unreachable with the current RulesManager) falls through to the fallback.
            }
            // ---- legal stroke with nothing scored: the table changes hands ----
            else
            {
                HandoverTable();
                _phase = TurnPhase.AwaitingShot;
                RaiseTurnChanged(TurnReason.NoScore, true);
                announced = true;
            }

            // ---- 4) verdict published (HUD break/required/toast updates) ----
            GameEvents.RaiseShotResolved(new ShotResolvedArgs { Outcome = outcome });

            // ---- 5) frame decisions ----
            if (outcome.FrameOver)
            {
                _phase = TurnPhase.FrameOver;
                RaiseTurnChanged(TurnReason.FrameOver, false);
                int winner = ResolveFrameWinner(outcome, ctx);
                MatchManager match;
                if (ServiceRegistry.TryGet<MatchManager>(out match) && match != null)
                {
                    match.HandleFrameOver(winner);
                }
            }
            else if (outcome.RespotBlack)
            {
                // CONTRACTS §3.7: frame tied — the black respots and the frame CONTINUES; frame stats are
                // not counted (MatchManager.NotifyRespottedBlack raises the RespotBlack marker event).
                MatchManager match;
                if (ServiceRegistry.TryGet<MatchManager>(out match) && match != null)
                {
                    match.NotifyRespottedBlack();
                }
                BallManager balls = Balls;
                if (balls != null)
                {
                    Ball black = FindLiveBall(balls.AllBalls, BallColor.Black);
                    if (black != null) balls.Respot(black);
                }
                _phase = TurnPhase.AwaitingShot;
                RaiseTurnChanged(TurnReason.RespotBlack, true);
            }
            else if (!announced)
            {
                // Defensive fallback (see step 3): keep the table alive on a malformed verdict.
                HandoverTable();
                _phase = TurnPhase.AwaitingShot;
                RaiseTurnChanged(TurnReason.NoScore, true);
            }
        }

        /// <summary>Awards pot points for every potted object ball in pot order. Cue entries are skipped —
        /// a potted cue is a foul, never a score (ScoringManager filters too; this keeps the rule local).</summary>
        private static void AwardPotsInPotOrder(ShotContext ctx)
        {
            ScoringManager scoring;
            if (!ServiceRegistry.TryGet<ScoringManager>(out scoring) || scoring == null) return;
            List<PottedBallRecord> potted = ctx != null ? ctx.Potted : null;
            if (potted == null) return;
            for (int i = 0; i < potted.Count; i++)
            {
                PottedBallRecord record = potted[i];
                if (record == null || record.Color == BallColor.Cue) continue;
                scoring.AwardPot(record.Color);
            }
        }

        /// <summary>Respots every colour listed in the verdict. Reds never appear (they always stay down);
        /// Respot always lands the ball on the table (own spot + X-scan fallback).</summary>
        private void RespotOutcomeColours(RuleOutcome outcome)
        {
            if (outcome == null || outcome.RespotColors == null || outcome.RespotColors.Count == 0) return;
            BallManager balls = Balls;
            if (balls == null) return;
            IReadOnlyList<Ball> all = balls.AllBalls;
            for (int i = 0; i < outcome.RespotColors.Count; i++)
            {
                Ball ball = FindLiveBall(all, outcome.RespotColors[i]);
                if (ball != null) balls.Respot(ball);
            }
        }

        /// <summary>First non-cue ball of the given colour in the rack list, or null (allocation-free).</summary>
        private static Ball FindLiveBall(IReadOnlyList<Ball> balls, BallColor color)
        {
            if (balls == null) return null;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball ball = balls[i];
                if (ball != null && !ball.IsCue && ball.Color == color) return ball;
            }
            return null;
        }

        /// <summary>Hands the table over: ends the running break and swaps the player index. The next
        /// TurnChanged carries the incoming player.</summary>
        private void HandoverTable()
        {
            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null) scoring.EndBreak();
            _currentPlayerIndex = 1 - _currentPlayerIndex;
        }

        /// <summary>Picks the frame winner: the opponent of the stroker when a foul decided the frame, else
        /// the higher frame score (the deciding black can seal the frame for a leader who did not pot it);
        /// ties resolve to the stroker as a last resort.</summary>
        private static int ResolveFrameWinner(RuleOutcome outcome, ShotContext ctx)
        {
            int stroker = ctx != null ? ctx.PlayerIndex : 0;
            if (stroker != 0 && stroker != 1) stroker = 0;
            if (outcome != null && outcome.FoulPoints > 0) return 1 - stroker;

            ScoringManager scoring;
            if (ServiceRegistry.TryGet<ScoringManager>(out scoring) && scoring != null)
            {
                int[] scores = scoring.Scores; // includes this stroke's points — awarded before this runs
                if (scores != null && scores.Length >= 2)
                {
                    if (scores[0] > scores[1]) return 0;
                    if (scores[1] > scores[0]) return 1;
                }
            }
            return stroker;
        }

        /// <summary>Raises TurnChanged for the current player with <see cref="RulesManager.RequiredBall"/>
        /// attached (null ⇒ "any colour").</summary>
        private void RaiseTurnChanged(TurnReason reason, bool canShoot)
        {
            RulesManager rules;
            BallColor? required = ServiceRegistry.TryGet<RulesManager>(out rules) && rules != null
                ? rules.RequiredBall
                : (BallColor?)null;
            GameEvents.RaiseTurnChanged(new TurnChangedArgs
            {
                PlayerIndex = _currentPlayerIndex,
                IsAI = IsAITurn,
                Reason = reason,
                CanShoot = canShoot,
                RequiredBall = required,
            });
        }

        // -------------------------------------------------------------------------------------------------
        // Watchdog failsafe
        // -------------------------------------------------------------------------------------------------

        /// <summary>Failsafe loop: while the table waits (AwaitingShot / AwaitingPlacement) it clocks wall time;
        /// if no strike arrives within PhysicsTuning.MaxStrikeWaitSeconds the turn is force-handed over so a
        /// blocked D, an unresponsive player or an AI stall can never freeze the frame. Zero per-frame
        /// allocations. Also resets whenever any other phase is active.</summary>
        private IEnumerator Watchdog()
        {
            float elapsed = 0f;
            while (true)
            {
                yield return null;
                if (_phase == TurnPhase.AwaitingShot || _phase == TurnPhase.AwaitingPlacement)
                {
                    elapsed += Time.deltaTime;
                    if (elapsed >= GameConfig.PhysicsTuning.MaxStrikeWaitSeconds)
                    {
                        elapsed = 0f;
                        ForceHandover();
                    }
                }
                else
                {
                    elapsed = 0f;
                }
            }
        }

        /// <summary>Watchdog action: end the break, swap players and re-announce the turn (ball-in-hand stays
        /// ball-in-hand for the incoming player). No-op when the phase moved on between frames.</summary>
        private void ForceHandover()
        {
            if (_phase != TurnPhase.AwaitingShot && _phase != TurnPhase.AwaitingPlacement) return;

            HandoverTable();
            BallManager balls = Balls;
            if (balls != null && balls.CueBallNeedsPlacement)
            {
                _phase = TurnPhase.AwaitingPlacement;
                RaiseTurnChanged(TurnReason.BallInHandD, false);
            }
            else
            {
                _phase = TurnPhase.AwaitingShot;
                RaiseTurnChanged(TurnReason.NoScore, true);
            }
        }
    }
}
