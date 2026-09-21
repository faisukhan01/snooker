// SnookerKit — aim trajectory guides + aim-assist search (Physics module, CONTRACTS §2).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnookerKit
{
    /// <summary>World-space aim guides ("AimGuides" root): a dotted white AimLine (cue ball → ghost contact or 3 m),
    /// a green ObjectLine (target centre → through-ghost travel direction, 0.6 m), a faint DeflectLine (cue-ball
    /// tangent after impact) and a flat GhostRing quad at the ghost position. All guides recompute in LateUpdate
    /// from CueController.AimAngle + BallManager.CueBall while visible — zero per-frame heap allocations.
    /// TryGetAssistAngle scans live legal object balls × PocketManager.Pockets (ghost-ball math, cut &gt; 80°
    /// rejected, dual SphereCastNonAlloc clearance) and returns the winner's aim angle in the CueController
    /// convention: direction = (sin θ, 0, cos θ), i.e. θ = Atan2(dir.x, dir.z).</summary>
    [DisallowMultipleComponent]
    public class TrajectoryPredictor : MonoBehaviour
    {
        /// <summary>Capacity of the pre-allocated dotted AimLine point buffer.</summary>
        private const int AimPointCapacity = 64;
        /// <summary>Pre-allocated SphereCastNonAlloc buffer size (shared by guide + assist sweeps).</summary>
        private const int CastBufferSize = 24;
        /// <summary>Dash length of the dotted AimLine (m).</summary>
        private const float DashLength = 0.05f;
        /// <summary>Gap between AimLine dashes (m).</summary>
        private const float DashGap = 0.05f;
        /// <summary>cos(80°) — assist candidates with a steeper cut are rejected (frozen CONTRACTS §5).</summary>
        private const float MaxCutCos = 0.173648f;
        /// <summary>Pinned assist score weights (same shape as the AI planner's formula).</summary>
        private const float ScoreStraightWeight = 1.2f;
        private const float ScoreCueDistance = 14f;
        private const float ScorePocketDistance = 10f;
        /// <summary>Scores closer than this are treated as tied → the NEARER ghost contact wins.</summary>
        private const float ScoreEpsilon = 1e-4f;
        /// <summary>AimLine length when no ball is hit (m, frozen CONTRACTS §2).</summary>
        private const float NoHitAimLength = 3f;
        /// <summary>Guide SphereCast distance — spans the full table diagonal.</summary>
        private const float AimCastDistance = 4f;
        /// <summary>ObjectLine length beyond the target centre (m).</summary>
        private const float ObjectLineLength = 0.6f;
        /// <summary>DeflectLine length from the ghost point (m).</summary>
        private const float DeflectLineLength = 0.5f;
        /// <summary>GhostRing lift above the cue-ball centre to avoid z-fighting (m).</summary>
        private const float GhostRingLift = 0.002f;

        private static readonly Color AimLineColor = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color ObjectLineColor = new Color(0.35f, 0.66f, 0.48f, 0.9f);
        private static readonly Color DeflectLineColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color GhostRingColor = new Color(0.349f, 0.663f, 0.478f, 0.8f); // bright accent #59A97A

        private GameObject _guideRoot;
        private LineRenderer _aimLine;
        private LineRenderer _objectLine;
        private LineRenderer _deflectLine;
        private MeshRenderer _ghostRenderer;
        private bool _visible;
        private bool _built;

        private BallManager _balls;
        private CueController _cue;

        private readonly Vector3[] _aimPoints = new Vector3[AimPointCapacity];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[CastBufferSize];

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CueController Cue { get { if (_cue == null) ServiceRegistry.TryGet(out _cue); return _cue; } }

        /// <summary>Registers the predictor in the ServiceRegistry (frozen manager convention).</summary>
        private void Awake()
        {
            ServiceRegistry.Register<TrajectoryPredictor>(this);
        }

        /// <summary>Deregisters from the ServiceRegistry; the guide hierarchy dies with this GameObject.</summary>
        private void OnDestroy()
        {
            ServiceRegistry.Deregister<TrajectoryPredictor>();
            _guideRoot = null;
            _aimLine = null;
            _objectLine = null;
            _deflectLine = null;
            _ghostRenderer = null;
        }

        /// <summary>Builds the AimGuides root once and applies the stored visibility.</summary>
        private void Start()
        {
            if (_built) return;
            _built = true;
            BuildGuides();
            Show(_visible);
        }

        /// <summary>Toggles the AimGuides root. Safe before Start — the flag is applied as soon as guides exist.</summary>
        public void Show(bool visible)
        {
            _visible = visible;
            if (_guideRoot != null && _guideRoot.activeSelf != visible)
            {
                _guideRoot.SetActive(visible);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Guide construction
        // -------------------------------------------------------------------------------------------------

        /// <summary>Creates the AimGuides root with the three world-space LineRenderers and the flat ghost ring
        /// quad. Materials come from TableBuilder.MakeTransparentUnlit; runs once (build-time allocations only).</summary>
        private void BuildGuides()
        {
            _guideRoot = new GameObject("AimGuides");
            _guideRoot.transform.SetParent(transform, false);
            _guideRoot.SetActive(false);

            _aimLine = MakeLine("AimLine", AimLineColor, 0.008f);
            _objectLine = MakeLine("ObjectLine", ObjectLineColor, 0.01f);
            _deflectLine = MakeLine("DeflectLine", DeflectLineColor, 0.008f);

            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "GhostRing";
            ring.transform.SetParent(_guideRoot.transform, false);
            ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // flat on the cloth, normal up
            float diameter = 2f * GameConfig.TableDims.BallRadius;
            ring.transform.localScale = new Vector3(diameter, diameter, 1f);

            _ghostRenderer = ring.GetComponent<MeshRenderer>();
            if (_ghostRenderer != null)
            {
                _ghostRenderer.sharedMaterial = TableBuilder.MakeTransparentUnlit("Guide_GhostRing", GhostRingColor);
                _ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>One world-space guide line: transparent unlit material, white vertex colors (tint lives in the
        /// material), 2 cap vertices, no shadows/probes. Parented under the AimGuides root.</summary>
        private LineRenderer MakeLine(string name, Color color, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_guideRoot.transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 0;
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            lr.startColor = Color.white;
            lr.endColor = Color.white;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lr.sharedMaterial = TableBuilder.MakeTransparentUnlit("Guide_" + name, color);
            return lr;
        }

        // -------------------------------------------------------------------------------------------------
        // Per-frame guide update
        // -------------------------------------------------------------------------------------------------

        /// <summary>Recomputes all guides from the current cue aim + cue-ball position while visible.
        /// Allocation-free: everything writes into pre-allocated buffers.</summary>
        private void LateUpdate()
        {
            if (!_visible) return;

            BallManager balls = Balls;
            CueController cue = Cue;
            Ball cueBall = balls != null ? balls.CueBall : null;
            if (balls == null || cue == null || cueBall == null || !cueBall.gameObject.activeInHierarchy)
            {
                HideAll();
                return;
            }

            Vector3 origin = cueBall.transform.position;
            Vector3 dir = cue.AimDirection();
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f)
            {
                HideAll();
                return;
            }
            dir.Normalize();

            // First ball along the aim (self-hits excluded; cushion/bed hits filtered out by Ball lookup).
            Ball target = null;
            float contactDist = float.MaxValue;
            int hits = Physics.SphereCastNonAlloc(
                origin, GameConfig.TableDims.BallRadius, dir, _castBuffer, AimCastDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                Collider col = _castBuffer[i].collider;
                Ball candidate = col != null ? col.GetComponentInParent<Ball>() : null;
                if (candidate == null || ReferenceEquals(candidate, cueBall)) continue;
                if (candidate.State == BallState.Dropping || candidate.State == BallState.OffTable) continue;
                if (!balls.IsOnTable(candidate)) continue;
                if (_castBuffer[i].distance < contactDist)
                {
                    contactDist = _castBuffer[i].distance;
                    target = candidate;
                }
            }

            bool hitBall = target != null;
            Vector3 ghost = hitBall ? origin + dir * contactDist : origin + dir * NoHitAimLength;

            // Dotted AimLine: cue ball → ghost (or 3 m when nothing is hit).
            if (_aimLine != null)
            {
                int count = BuildDottedPoints(origin, ghost);
                _aimLine.positionCount = count;
                for (int i = 0; i < count; i++) _aimLine.SetPosition(i, _aimPoints[i]);
            }

            // Ghost ring at the contact point.
            if (_ghostRenderer != null)
            {
                _ghostRenderer.enabled = hitBall;
                if (hitBall)
                {
                    Transform ringTransform = _ghostRenderer.transform;
                    ringTransform.position = new Vector3(ghost.x, origin.y + GhostRingLift, ghost.z);
                }
            }

            // Object line + cue deflection tangent from the line of centres at impact.
            bool objectLineDrawn = false;
            if (hitBall)
            {
                Vector3 targetPos = target.transform.position;
                Vector3 through = ghost - targetPos;
                through.y = 0f;
                float throughLen = through.magnitude;
                if (throughLen > 1e-5f)
                {
                    through /= throughLen;
                    SetLine(_objectLine, targetPos, targetPos + through * ObjectLineLength);
                    objectLineDrawn = true;

                    Vector3 tangent = dir - Vector3.Dot(dir, through) * through;
                    tangent.y = 0f;
                    float tangentLen = tangent.magnitude;
                    if (tangentLen > 0.05f)
                    {
                        tangent /= tangentLen;
                        SetLine(_deflectLine, ghost, ghost + tangent * DeflectLineLength);
                    }
                    else
                    {
                        HideLine(_deflectLine); // straight shot — no tangent to show
                    }
                }
            }

            if (!objectLineDrawn) HideLine(_objectLine);
            if (!hitBall) HideLine(_deflectLine);
        }

        /// <summary>Fills the pre-allocated dotted-point buffer (dash/gap pairs) from <paramref name="from"/> to
        /// <paramref name="to"/> and returns the used point count.</summary>
        private int BuildDottedPoints(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-6f || delta.sqrMagnitude < 1e-12f) return 0;
            Vector3 step = delta / dist;

            int count = 0;
            float t = 0f;
            float period = DashLength + DashGap;
            while (t < dist && count + 2 <= AimPointCapacity)
            {
                _aimPoints[count++] = from + step * t;
                _aimPoints[count++] = from + step * Mathf.Min(t + DashLength, dist);
                t += period;
            }
            return count;
        }

        /// <summary>Draws a two-point line (positionCount reset each call — no allocation).</summary>
        private static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
        {
            if (line == null) return;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        /// <summary>Hides a line by collapsing its point count (renders nothing, no SetActive churn).</summary>
        private static void HideLine(LineRenderer line)
        {
            if (line != null) line.positionCount = 0;
        }

        /// <summary>Hides every guide (defensive paths: missing managers, missing cue ball, degenerate aim).</summary>
        private void HideAll()
        {
            HideLine(_aimLine);
            HideLine(_objectLine);
            HideLine(_deflectLine);
            if (_ghostRenderer != null) _ghostRenderer.enabled = false;
        }

        // -------------------------------------------------------------------------------------------------
        // Aim assist
        // -------------------------------------------------------------------------------------------------

        /// <summary>Finds the best pot-able aim for the current table state. requiredBall follows
        /// RulesManager.RequiredBall semantics (null ⇒ any object ball is legal). For every live legal object ball
        /// × PocketManager.Pockets pair: ghost-ball math on the ball plane, cut &gt; 80° rejected, dual
        /// SphereCastNonAlloc clearance (cue→ghost and ghost→pocket; blockers by Ball reference equality,
        /// excluding target + cue). Winner = pinned score 1.2·cutCos + 14/(1+d(cue,ghost)) + 10/(1+d(ghost,pocket));
        /// score ties resolve to the NEAREST ghost contact distance. The returned angle matches the
        /// CueController.AimAngle convention (direction = (sin θ, 0, cos θ), θ = Atan2(dir.x, dir.z)).
        /// Allocation-free. Returns false when no candidate survives.</summary>
        public bool TryGetAssistAngle(BallColor? requiredBall, out float aimAngle)
        {
            aimAngle = 0f;

            BallManager balls = Balls;
            if (balls == null) return false;

            Ball cue = balls.CueBall;
            if (cue == null || !balls.IsOnTable(cue)) return false;

            Vector3[] pockets = PocketManager.Pockets;
            IReadOnlyList<Ball> all = balls.AllBalls;
            if (pockets == null || pockets.Length == 0 || all == null) return false;

            Vector3 cuePos = cue.transform.position;
            float ballRadius = GameConfig.TableDims.BallRadius;

            bool found = false;
            float bestScore = 0f;
            float bestContact = 0f;
            float bestAngle = 0f;

            for (int bi = 0; bi < all.Count; bi++)
            {
                Ball target = all[bi];
                if (!IsCandidate(balls, target, cue, requiredBall)) continue;

                Vector3 targetPos = target.transform.position;
                for (int pi = 0; pi < pockets.Length; pi++)
                {
                    // Work on the ball plane: the contracted pocket y sits at sensor height, not ball height.
                    Vector3 pocket = new Vector3(pockets[pi].x, cuePos.y, pockets[pi].z);
                    Vector3 toPocket = pocket - targetPos;
                    toPocket.y = 0f;
                    float pocketDist = toPocket.magnitude;
                    if (pocketDist < 0.02f) continue;
                    toPocket /= pocketDist;

                    // Ghost ball: where the cue centre must be at contact to send the target to the pocket.
                    Vector3 ghost = targetPos - toPocket * (2f * ballRadius);
                    Vector3 toGhost = ghost - cuePos;
                    toGhost.y = 0f;
                    float d1 = toGhost.magnitude;
                    if (d1 < 0.01f) continue;
                    toGhost /= d1;

                    float cutCos = Vector3.Dot(toGhost, toPocket);
                    if (cutCos < MaxCutCos) continue; // cut steeper than 80°

                    if (!IsPathClear(cuePos, toGhost, d1, target, cue, balls)) continue;
                    if (!IsPathClear(ghost, toPocket, pocketDist, target, cue, balls)) continue;

                    float d2 = Vector3.Distance(ghost, pocket);
                    float score = ScoreStraightWeight * cutCos
                                + ScoreCueDistance / (1f + d1)
                                + ScorePocketDistance / (1f + d2);
                    float candidateAngle = Mathf.Atan2(toGhost.x, toGhost.z);

                    if (!found)
                    {
                        found = true;
                        bestScore = score;
                        bestContact = d1;
                        bestAngle = candidateAngle;
                    }
                    else if (score > bestScore + ScoreEpsilon)
                    {
                        bestScore = score;
                        bestContact = d1;
                        bestAngle = candidateAngle;
                    }
                    else if (score > bestScore - ScoreEpsilon && d1 < bestContact)
                    {
                        // Score tie → the NEARER ghost contact wins.
                        bestContact = d1;
                        bestAngle = candidateAngle;
                    }
                }
            }

            if (!found) return false;
            aimAngle = bestAngle;
            return true;
        }

        /// <summary>Assist legality: live on-table object ball matching the requirement (null ⇒ any object ball).
        /// Mirrors AIShotPlanner.IsLegalTarget — the cue ball itself is never a target.</summary>
        private static bool IsCandidate(BallManager balls, Ball ball, Ball cue, BallColor? requiredBall)
        {
            if (ball == null) return false;
            if (cue != null && ReferenceEquals(ball, cue)) return false;
            if (ball.IsCue || ball.Color == BallColor.Cue) return false;
            if (requiredBall.HasValue && ball.Color != requiredBall.Value) return false;
            if (ball.State == BallState.Dropping || ball.State == BallState.OffTable) return false;
            return balls.IsOnTable(ball);
        }

        /// <summary>Dual clearance sweep corridor. Blockers are identified by Ball reference equality against the
        /// rack (target + cue excluded); cushions/bed (no Ball component) do not block. The sweep buffer is
        /// reused — zero allocations.</summary>
        private bool IsPathClear(Vector3 origin, Vector3 direction, float distance, Ball target, Ball cue, BallManager balls)
        {
            if (distance <= 0.001f) return true;

            int hits = Physics.SphereCastNonAlloc(
                origin,
                GameConfig.TableDims.BallRadius,
                direction,
                _castBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                Collider collider = _castBuffer[i].collider;
                Ball blocker = collider != null ? collider.GetComponentInParent<Ball>() : null;
                if (blocker == null) continue; // cushion/bed — not a ball blocker
                if (ReferenceEquals(blocker, target) || ReferenceEquals(blocker, cue)) continue;
                if (!balls.IsOnTable(blocker)) continue; // potted debris cannot block
                return false;
            }
            return true;
        }
    }
}
