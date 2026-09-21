// SnookerKit — AI turn driver (CONTRACTS §2/§5). Plays exclusively through the same public CueController /
// BallManager APIs a human uses: think delay → ball-in-hand D sampling → plan → difficulty-scaled error → fire.
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Drives the AI opponent. Subscribes GameEvents.TurnChanged in OnEnable (unsubscribed in OnDisable);
    /// on an AI handover runs a shot coroutine: 0.9–1.6 s think delay, optional ball-in-hand placement (7 sampled
    /// D candidates, first legal wins), a bounded wait for TurnManager.CanShootNow, one AIShotPlanner.PlanShot,
    /// Gaussian aim/power error per AiDifficulty tier, then AimAt → animated SetPower01 → SetSpin → Fire.
    /// No hidden advantages — every input reaches the table through the public cue/ball APIs.</summary>
    public class AIController : MonoBehaviour
    {
        /// <summary>Aim error σ (radians) per tier: Beginner, Intermediate, Advanced, Expert.</summary>
        private static readonly float[] AngleSigma = { 0f, 0.004f, 0.010f, 0.020f };
        /// <summary>Power error σ (0..1) per tier: Beginner, Intermediate, Advanced, Expert.</summary>
        private static readonly float[] PowerSigma = { 0f, 0.02f, 0.05f, 0.08f };
        /// <summary>Candidate offsets behind the brown spot (baulk-cushion side), all inside the D radius.</summary>
        private static readonly Vector2[] DCandidates =
        {
            new Vector2(0f, 0f),
            new Vector2(-0.05f, 0.09f), new Vector2(-0.05f, -0.09f),
            new Vector2(-0.12f, 0.16f), new Vector2(-0.12f, -0.16f),
            new Vector2(-0.18f, 0.20f), new Vector2(-0.18f, -0.20f),
        };
        /// <summary>Try order: closest to the D centre first; within one radius, the target-ball side first.</summary>
        private static readonly int[] DOrderTargetSide = { 0, 1, 2, 3, 4, 5, 6 };
        private static readonly int[] DOrderOtherSide = { 0, 2, 1, 4, 3, 6, 5 };

        private const float ThinkMinSeconds = 0.9f;
        private const float ThinkMaxSeconds = 1.6f;
        private const float PowerRampSeconds = 0.4f;
        private const float PlacementSettleSeconds = 0.35f;
        private const float PlacementRetrySeconds = 0.25f;
        private const int PlacementAttempts = 3;

        private AiDifficulty _difficulty = AiDifficulty.Intermediate;
        private Coroutine _shotRoutine;
        private bool _rampingPower;

        private TurnManager _turns;
        private BallManager _balls;
        private CueController _cue;
        private RulesManager _rules;

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private TurnManager Turns { get { if (_turns == null) ServiceRegistry.TryGet(out _turns); return _turns; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CueController Cue { get { if (_cue == null) ServiceRegistry.TryGet(out _cue); return _cue; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private RulesManager Rules { get { if (_rules == null) ServiceRegistry.TryGet(out _rules); return _rules; } }

        private void Awake()
        {
            ServiceRegistry.Register<AIController>(this);
        }

        private void OnEnable()
        {
            GameEvents.TurnChanged += OnTurnChanged;
            GameEvents.MatchStarted += OnMatchStarted;
        }

        private void OnDisable()
        {
            GameEvents.TurnChanged -= OnTurnChanged;
            GameEvents.MatchStarted -= OnMatchStarted;
            StopShot();
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<AIController>();
        }

        private void Start()
        {
            // Insurance: catch a turn that was already live before this controller enabled.
            TurnManager turns = Turns;
            if (turns != null && turns.IsAITurn && _shotRoutine == null)
            {
                _shotRoutine = StartCoroutine(ShotRoutine());
            }
        }

        /// <summary>Captures the difficulty tier announced with the match (default Intermediate).</summary>
        private void OnMatchStarted(MatchStartedArgs args)
        {
            if (args != null) _difficulty = args.AiDifficulty;
        }

        /// <summary>Starts the shot coroutine on an AI handover; cancels it the moment the turn leaves the AI.
        /// Started regardless of args.CanShoot: TurnManager flips AwaitingPlacement → AwaitingShot without a
        /// second TurnChanged (ball-in-hand), and the coroutine's bounded CanShootNow wait covers both cases.</summary>
        private void OnTurnChanged(TurnChangedArgs args)
        {
            if (args == null) return;
            if (args.IsAI)
            {
                if (_shotRoutine == null) _shotRoutine = StartCoroutine(ShotRoutine());
            }
            else
            {
                StopShot();
            }
        }

        /// <summary>Stops any running shot coroutine and restores a neutral cue power if we were mid-ramp.</summary>
        private void StopShot()
        {
            if (_shotRoutine != null)
            {
                StopCoroutine(_shotRoutine);
                _shotRoutine = null;
            }
            if (_rampingPower)
            {
                _rampingPower = false;
                CueController cue = Cue;
                if (cue != null) cue.SetPower01(0f);
            }
        }

        /// <summary>Full AI stroke: think → placement → wait → plan → error → aim/ramp/spin → fire.</summary>
        private IEnumerator ShotRoutine()
        {
            // ---- think delay (0.9–1.6 s) ----
            float think = Random.Range(ThinkMinSeconds, ThinkMaxSeconds);
            float timer = 0f;
            while (timer < think)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            BallManager balls = Balls;
            CueController cue = Cue;
            if (balls == null || cue == null) { _shotRoutine = null; yield break; }

            // ---- ball-in-hand: sample 7 D points, first legal placement wins ----
            if (balls.CueBallNeedsPlacement)
            {
                balls.BeginCueBallPlacement(true); // idempotent ensure — BallManager clamps candidates into the D
                bool placed = false;
                for (int attempt = 0; attempt < PlacementAttempts && !placed; attempt++)
                {
                    placed = TryPlaceInD(balls);
                    if (!placed) yield return Wait(PlacementRetrySeconds);
                }
                if (placed)
                {
                    balls.EndCueBallPlacement();
                    yield return Wait(PlacementSettleSeconds);
                }
                // On total failure the placement session stays open (TurnManager remains in AwaitingPlacement)
                // and the bounded wait below trips the PhysicsTuning failsafe instead of firing blind.
            }

            // ---- wait for the shot window, bounded by the TurnManager failsafe ----
            float waited = 0f;
            while (true)
            {
                TurnManager turns = Turns;
                if (turns == null || !turns.IsAITurn) { _shotRoutine = null; yield break; }
                if (turns.CanShootNow) break;
                if (waited >= GameConfig.PhysicsTuning.MaxStrikeWaitSeconds) { _shotRoutine = null; yield break; }
                waited += Time.deltaTime;
                yield return null;
            }

            // ---- plan with RulesManager.RequiredBall semantics (null ⇒ any object ball) ----
            balls = Balls;
            cue = balls != null ? balls.CueBall : null;
            if (balls == null || cue == null) { _shotRoutine = null; yield break; }

            BallColor? required = null;
            RulesManager rules = Rules;
            if (rules != null) required = rules.RequiredBall;

            AIPlan plan = AIShotPlanner.PlanShot(required);
            if (!plan.Valid) { _shotRoutine = null; yield break; }

            // ---- difficulty-scaled Gaussian error (no advantages: same visible state, honest noise) ----
            int tier = Mathf.Clamp((int)_difficulty, 0, AngleSigma.Length - 1);
            float aim = plan.AimAngle + MathUtil.Gaussian(0f, AngleSigma[tier]);
            float power = Mathf.Clamp01(plan.Power01 + MathUtil.Gaussian(0f, PowerSigma[tier]));

            // ---- apply through the public cue API exactly like a human stroke ----
            cue.AimAt(aim);

            _rampingPower = true;
            float ramp = 0f;
            while (ramp < 1f)
            {
                ramp += Time.deltaTime / PowerRampSeconds;
                if (ramp > 1f) ramp = 1f;
                cue.SetPower01(Mathf.Lerp(0f, power, ramp));
                yield return null;
            }
            _rampingPower = false;

            cue.SetSpin(plan.Spin);
            yield return null; // one frame for the cue/trajectory visuals to settle on the final aim
            cue.Fire();

            _shotRoutine = null;
        }

        /// <summary>Samples the 7 candidate points in the D (brown spot + behind-brown offsets, ordered closest to
        /// the D centre first with the target-ball side preferred) and commits the first legal placement.</summary>
        private bool TryPlaceInD(BallManager balls)
        {
            if (balls == null) return false;

            // Bias: which side of the D faces the legal target balls (their Z centroid).
            float centroidZ;
            BallColor? required = null;
            RulesManager rules = Rules;
            if (rules != null) required = rules.RequiredBall;
            bool targetSidePositive = true;
            if (AIShotPlanner.TryGetTargetCentroidZ(required, out centroidZ) && Mathf.Abs(centroidZ) > 0.02f)
            {
                targetSidePositive = centroidZ > 0f;
            }

            Vector3 brown = balls.GetSpot(BallColor.Brown); // D centre on the baulk line
            int[] order = targetSidePositive ? DOrderTargetSide : DOrderOtherSide;
            float y = GameConfig.TableDims.BallRestHeight;

            for (int i = 0; i < order.Length; i++)
            {
                Vector2 offset = DCandidates[order[i]];
                Vector3 candidate = new Vector3(brown.x + offset.x, y, brown.z + offset.y);
                if (balls.TryPlaceCueBall(candidate)) return true;
            }
            return false;
        }

        /// <summary>Allocation-lean scaled-time wait (one iterator allocation per call, never per frame).</summary>
        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
