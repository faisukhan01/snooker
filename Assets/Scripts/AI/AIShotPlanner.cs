// SnookerKit — AI ghost-ball shot planner (CONTRACTS §2/§5). Plain static class; no MonoBehaviour, no allocations.
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>One AI planning result. Angles are radians (aim direction = (cos, 0, sin)); power is 0..1;
    /// spin is (side, vertical), each -1..1.</summary>
    public struct AIPlan
    {
        /// <summary>Cue aim angle in radians (atan2 convention: direction = (cos, 0, sin)).</summary>
        public float AimAngle;
        /// <summary>Strike power (0..1), clamped to the planner's working range.</summary>
        public float Power01;
        /// <summary>Spin applied at strike (x = side, y = vertical; each -1..1).</summary>
        public Vector2 Spin;
        /// <summary>True when a usable plan exists (a clear pot or the safety fallback). False only when the
        /// cue ball or every candidate ball is unavailable.</summary>
        public bool Valid;
    }

    /// <summary>Ghost-ball pot search: legal target balls × PocketManager.Pockets, cut angle &gt; 80° rejected,
    /// dual SphereCastNonAlloc clearance checks (cue→ghost and ghost→pocket, blockers resolved by Ball reference
    /// equality and excluding target + cue), the pinned scoring formula, and a nearest-ball safety fallback when
    /// no clear pot exists. Reads only the same public manager state a human player sees — no hidden advantages.
    /// Scratch: one pre-allocated RaycastHit buffer reused across sweeps; no LINQ, no closures, no per-call heaps.</summary>
    public static class AIShotPlanner
    {
        private const float MaxCutCos = 0.173648f;      // cos(80°) — candidates with a poorer cut are rejected
        private const float ScoreStraightWeight = 1.2f;
        private const float ScoreCueDistance = 14f;
        private const float ScorePocketDistance = 10f;
        private const float ScoreBlockedPenalty = 40f;
        private const float PowerBase = 0.16f;
        private const float PowerScale = 0.16f;
        private const float PowerMin = 0.18f;
        private const float PowerMax = 0.95f;
        private const float SafetyPower = 0.25f;
        private const float RollSpinY = 0.15f;          // slight natural (top) roll for distance shots
        private const float RollSpinDistance = 0.75f;   // d(cue,ghost) + d(ghost,pocket) above this = distance shot
        private const int CastBufferSize = 24;

        /// <summary>Reused sweep buffer — PlanShot runs once per AI turn but must never allocate.</summary>
        private static readonly RaycastHit[] CastBuffer = new RaycastHit[CastBufferSize];

        /// <summary>Plans the best available stroke for the current table state.
        /// requiredBall follows RulesManager.RequiredBall semantics: null ⇒ any object ball is legal.</summary>
        /// <returns>A plan with Valid = true whenever a cue ball and at least one legal target exist.</returns>
        public static AIPlan PlanShot(BallColor? requiredBall)
        {
            AIPlan plan = default(AIPlan);

            BallManager balls;
            if (!ServiceRegistry.TryGet<BallManager>(out balls) || balls == null) return plan;

            Ball cue = balls.CueBall;
            if (cue == null || !balls.IsOnTable(cue)) return plan;
            Vector3 cuePos = cue.transform.position;

            Vector3[] pockets = PocketManager.Pockets; // static readonly per CONTRACTS §2
            IReadOnlyList<Ball> all = balls.AllBalls;
            if (pockets == null || pockets.Length == 0 || all == null)
            {
                return SafetyShot(balls, cuePos, requiredBall);
            }

            bool bestFound = false;
            bool bestClear = false;
            float bestScore = float.MinValue;
            float bestD1 = 0f;
            float bestD2 = 0f;
            Vector3 bestToGhost = Vector3.right;

            for (int bi = 0; bi < all.Count; bi++)
            {
                Ball target = all[bi];
                if (!IsLegalTarget(balls, target, cue, requiredBall)) continue;
                Vector3 targetPos = target.transform.position;

                for (int pi = 0; pi < pockets.Length; pi++)
                {
                    // Work on the ball plane: the contracted pocket y sits at sensor height, not ball height.
                    Vector3 pocket = new Vector3(pockets[pi].x, cuePos.y, pockets[pi].z);
                    Vector3 aimDir = pocket - targetPos;
                    aimDir.y = 0f;
                    float pocketDist = aimDir.magnitude;
                    if (pocketDist < 0.02f) continue;
                    aimDir /= pocketDist;

                    // Ghost ball: the point where the cue ball must contact the target to send it to the pocket.
                    Vector3 ghost = targetPos - aimDir * (2f * GameConfig.TableDims.BallRadius);
                    Vector3 toGhost = ghost - cuePos;
                    toGhost.y = 0f;
                    float d1 = toGhost.magnitude;
                    if (d1 < 0.01f) continue; // cue already at the ghost point — degenerate
                    toGhost /= d1;

                    float cutCos = Vector3.Dot(toGhost, aimDir);
                    if (cutCos < MaxCutCos) continue; // cut steeper than 80°

                    float d2 = Vector3.Distance(ghost, pocket);
                    bool clear = IsPathClear(balls, cuePos, toGhost, d1, target, cue)
                              && IsPathClear(balls, ghost, aimDir, d2, target, cue);

                    // Pinned score: straightness + short cue travel + short pot + clear-path bonus.
                    float score = ScoreStraightWeight * cutCos
                                + ScoreCueDistance / (1f + d1)
                                + ScorePocketDistance / (1f + d2)
                                + (clear ? 0f : ScoreBlockedPenalty);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestFound = true;
                        bestClear = clear;
                        bestD1 = d1;
                        bestD2 = d2;
                        bestToGhost = toGhost;
                    }
                }
            }

            if (bestFound && bestClear)
            {
                plan.AimAngle = Mathf.Atan2(bestToGhost.z, bestToGhost.x);
                plan.Power01 = Mathf.Clamp(PowerBase + PowerScale * (bestD1 + 0.8f * bestD2), PowerMin, PowerMax);
                plan.Spin = (bestD1 + bestD2 > RollSpinDistance)
                    ? new Vector2(0f, RollSpinY)
                    : Vector2.zero;
                plan.Valid = true;
                return plan;
            }

            // No clear pot found → soft safety toward the nearest legal ball.
            return SafetyShot(balls, cuePos, requiredBall);
        }

        /// <summary>Computes the average Z of all legal target balls (same legality rules as PlanShot).
        /// Used by AIController to bias ball-in-hand placement inside the D.</summary>
        /// <returns>True when at least one legal target ball exists.</returns>
        public static bool TryGetTargetCentroidZ(BallColor? requiredBall, out float centroidZ)
        {
            centroidZ = 0f;
            BallManager balls;
            if (!ServiceRegistry.TryGet<BallManager>(out balls) || balls == null) return false;
            IReadOnlyList<Ball> all = balls.AllBalls;
            if (all == null) return false;

            Ball cue = balls.CueBall;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Ball ball = all[i];
                if (!IsLegalTarget(balls, ball, cue, requiredBall)) continue;
                sum += ball.transform.position.z;
                count++;
            }
            if (count == 0) return false;
            centroidZ = sum / count;
            return true;
        }

        /// <summary>Safety fallback: aim the nearest legal ball with soft power; always Valid when a target exists.</summary>
        private static AIPlan SafetyShot(BallManager balls, Vector3 cuePos, BallColor? requiredBall)
        {
            AIPlan plan = default(AIPlan);

            IReadOnlyList<Ball> all = balls.AllBalls;
            if (all == null) return plan;

            Ball cue = balls.CueBall;
            float bestSqr = -1f;
            Vector3 bestPos = Vector3.zero;
            bool found = false;

            for (int i = 0; i < all.Count; i++)
            {
                Ball ball = all[i];
                if (!IsLegalTarget(balls, ball, cue, requiredBall)) continue;
                Vector3 pos = ball.transform.position;
                float sqr = (pos - cuePos).sqrMagnitude;
                if (!found || sqr < bestSqr)
                {
                    found = true;
                    bestSqr = sqr;
                    bestPos = pos;
                }
            }
            if (!found) return plan;

            Vector3 dir = bestPos - cuePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward; // degenerate overlap — pick a stable aim
            plan.AimAngle = Mathf.Atan2(dir.z, dir.x);
            plan.Power01 = SafetyPower;
            plan.Spin = Vector2.zero;
            plan.Valid = true;
            return plan;
        }

        /// <summary>Legality per RulesManager.RequiredBall semantics: on-table object ball matching the requirement
        /// (null ⇒ any object ball). The cue ball itself is never a target.</summary>
        private static bool IsLegalTarget(BallManager balls, Ball ball, Ball cue, BallColor? requiredBall)
        {
            if (ball == null) return false;
            if (cue != null && ReferenceEquals(ball, cue)) return false;
            BallColor color = ball.Color;
            if (color == BallColor.Cue) return false;
            if (requiredBall.HasValue && color != requiredBall.Value) return false;
            return balls.IsOnTable(ball);
        }

        /// <summary>Dual clearance sweep (cue→ghost and ghost→pocket corridors). Blockers are identified by Ball
        /// reference equality against the rack (target + cue excluded); potted balls and cushion/bed geometry
        /// (no Ball component) do not block. The sweep buffer is reused — zero allocations.</summary>
        private static bool IsPathClear(BallManager balls, Vector3 origin, Vector3 direction, float distance, Ball target, Ball cue)
        {
            if (distance <= 0.001f) return true;

            int hits = Physics.SphereCastNonAlloc(
                origin,
                GameConfig.TableDims.BallRadius,
                direction,
                CastBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                Collider collider = CastBuffer[i].collider;
                Ball blocker = collider != null ? collider.GetComponentInParent<Ball>() : null;
                if (blocker == null) continue;                                     // cushion/bed — not a ball blocker
                if (ReferenceEquals(blocker, target) || ReferenceEquals(blocker, cue)) continue;
                if (!balls.IsOnTable(blocker)) continue;                           // potted debris cannot block
                return false;
            }
            return true;
        }
    }
}
