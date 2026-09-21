// SnookerKit — ball rack construction, shot tracking and respot logic (Physics module, CONTRACTS §2).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns every ball on the table: rack building (15 reds + 6 colours + cue), shared PhysicMaterials,
    /// shot tracking (rest-watch → BallsAtRest), respotting, reds count and cue-ball-in-hand placement.</summary>
    [DisallowMultipleComponent]
    public class BallManager : MonoBehaviour
    {
        private readonly List<Ball> _balls = new List<Ball>(22);
        private Ball _cueBall;
        private GameObject _ballsRoot;
        private bool _needsCuePlacement;

        private PhysicMaterial _ballMaterial;
        private PhysicMaterial _bedMaterial;
        private PhysicMaterial _cushionMaterial;
        private readonly Dictionary<BallColor, Material> _meshMaterials = new Dictionary<BallColor, Material>(8);

        private ShotContext _tracked;
        private ShotContext _completed;
        private Coroutine _restRoutine;

        private readonly BallsAtRestArgs _restArgs = new BallsAtRestArgs();
        private readonly WaitForFixedUpdate _fixedWait = new WaitForFixedUpdate();

        private static Mesh _sphereMesh;

        /// <summary>Raised alongside GameEvents.BallsAtRest for modules that prefer a typed instance event.</summary>
        public event Action<BallsAtRestArgs> BallsAtRestEvent;

        /// <summary>Every live ball (cue, 15 reds, 6 colours) in creation order. Read-only view.</summary>
        public IReadOnlyList<Ball> AllBalls { get { return _balls; } }

        /// <summary>The cue ball, or null before BuildRack.</summary>
        public Ball CueBall { get { return _cueBall; } }

        /// <summary>Ball-to-ball PhysicMaterial (bounciness BallRestitution, friction BallFriction, Average combine).</summary>
        public PhysicMaterial BallMaterial { get { return _ballMaterial; } }

        /// <summary>Cloth bed PhysicMaterial (BedFriction / BedBallBounce) — also used by TableBuilder.</summary>
        public PhysicMaterial BedMaterial { get { return _bedMaterial; } }

        /// <summary>Cushion PhysicMaterial (CushionFriction / CushionBounce) — also used by TableBuilder.</summary>
        public PhysicMaterial CushionMaterial { get { return _cushionMaterial; } }

        /// <summary>True while the cue ball is parked for ball-in-hand placement (TryPlaceCueBall is the only
        /// legal move; EndCueBallPlacement releases the ball).</summary>
        public bool CueBallNeedsPlacement { get { return _needsCuePlacement; } }

        /// <summary>Creates the three shared PhysicMaterials and registers in the ServiceRegistry.</summary>
        private void Awake()
        {
            _ballMaterial = new PhysicMaterial("Ball");
            _ballMaterial.bounciness = GameConfig.PhysicsTuning.BallRestitution;
            _ballMaterial.dynamicFriction = GameConfig.PhysicsTuning.BallFriction;
            _ballMaterial.staticFriction = GameConfig.PhysicsTuning.BallFriction;
            _ballMaterial.frictionCombine = PhysicMaterialCombine.Average;
            _ballMaterial.bounceCombine = PhysicMaterialCombine.Average;

            _bedMaterial = new PhysicMaterial("Bed");
            _bedMaterial.bounciness = GameConfig.PhysicsTuning.BedBallBounce;
            _bedMaterial.dynamicFriction = GameConfig.PhysicsTuning.BedFriction;
            _bedMaterial.staticFriction = GameConfig.PhysicsTuning.BedFriction;
            _bedMaterial.frictionCombine = PhysicMaterialCombine.Average;
            _bedMaterial.bounceCombine = PhysicMaterialCombine.Average;

            _cushionMaterial = new PhysicMaterial("Cushion");
            _cushionMaterial.bounciness = GameConfig.PhysicsTuning.CushionBounce;
            _cushionMaterial.dynamicFriction = GameConfig.PhysicsTuning.CushionFriction;
            _cushionMaterial.staticFriction = GameConfig.PhysicsTuning.CushionFriction;
            _cushionMaterial.frictionCombine = PhysicMaterialCombine.Average;
            _cushionMaterial.bounceCombine = PhysicMaterialCombine.Average;

            ServiceRegistry.Register<BallManager>(this);
        }

        /// <summary>Deregisters, stops tracking and releases the shared materials and per-colour mesh materials.</summary>
        private void OnDestroy()
        {
            StopAllCoroutines();
            ServiceRegistry.Deregister<BallManager>();

            if (_tracked != null)
            {
                _tracked.Completed = true;
                if (Ball.CurrentContext == _tracked) Ball.CurrentContext = null;
                _tracked = null;
            }

            foreach (KeyValuePair<BallColor, Material> kvp in _meshMaterials)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _meshMaterials.Clear();

            if (_ballMaterial != null) Destroy(_ballMaterial);
            if (_bedMaterial != null) Destroy(_bedMaterial);
            if (_cushionMaterial != null) Destroy(_cushionMaterial);
            _ballMaterial = null;
            _bedMaterial = null;
            _cushionMaterial = null;

            _balls.Clear();
            _cueBall = null;
        }

        // -------------------------------------------------------------------------------------------------
        // Rack construction
        // -------------------------------------------------------------------------------------------------

        /// <summary>Builds the full rack: 15 reds in a triangle (apex just behind the pink spot toward the black
        /// end, rows spaced 1.02 × ball diameter), the six colours on their TableDims spots, and the cue ball at
        /// ((BaulkLineX − MaxX) / 2, BallRestHeight, 0). Balls are named "Ball_Red_1".."Ball_Cue", parented under
        /// a "Balls" root. Rebuilds cleanly when called again (frame restarts).</summary>
        public void BuildRack()
        {
            if (_ballsRoot == null)
            {
                _ballsRoot = new GameObject("Balls");
                _ballsRoot.transform.SetParent(transform, false);
            }

            // Clear any previous rack (frame restart) before rebuilding.
            for (int i = 0; i < _balls.Count; i++)
            {
                if (_balls[i] != null) Destroy(_balls[i].gameObject);
            }
            _balls.Clear();
            _cueBall = null;

            Vector3 cuePos = new Vector3(
                (GameConfig.TableDims.BaulkLineX - GameConfig.TableDims.MaxX) * 0.5f,
                GameConfig.TableDims.BallRestHeight, 0f);
            CreateBall("Ball_Cue", BallColor.Cue, 0, cuePos, true);

            for (int i = 0; i < BallPalette.ColourSequence.Length; i++)
            {
                BallColor color = BallPalette.ColourSequence[i];
                CreateBall("Ball_" + BallPalette.DisplayName(color) + "_" + (16 + i), color, 16 + i,
                    GetSpot(color), false);
            }

            float diameter = 2f * GameConfig.TableDims.BallRadius;
            float apexX = GameConfig.TableDims.PinkX + diameter + 0.01f;
            int redId = 1;
            for (int row = 0; row < 5; row++)
            {
                float x = apexX + row * (diameter * 1.02f);
                for (int j = 0; j <= row; j++)
                {
                    float z = (j - row * 0.5f) * (diameter * 1.04f);
                    CreateBall("Ball_Red_" + redId, BallColor.Red, redId,
                        new Vector3(x, GameConfig.TableDims.BallRestHeight, z), false);
                    redId++;
                }
            }
        }

        /// <summary>Creates one ball GameObject: SphereCollider (local radius 0.5 under the uniform BallDiameter
        /// scale → world BallRadius), Rigidbody (tuned by Ball.Awake), Ball component, sphere mesh renderer with
        /// the per-colour URP/Standard material.</summary>
        private Ball CreateBall(string ballName, BallColor color, int id, Vector3 position, bool isCue)
        {
            GameObject go = new GameObject(ballName);
            go.transform.SetParent(_ballsRoot.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * (2f * GameConfig.TableDims.BallRadius);

            SphereCollider col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f; // × uniform BallDiameter scale → world radius = BallRadius
            go.AddComponent<Rigidbody>();

            Ball ball = go.AddComponent<Ball>();
            ball.Id = id;
            ball.Color = color;
            ball.IsCue = isCue;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SphereMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MeshMaterialFor(color);

            _balls.Add(ball);
            if (isCue) _cueBall = ball;
            return ball;
        }

        /// <summary>The built-in sphere mesh (fetched once via a throwaway primitive — shared mesh survives).</summary>
        private static Mesh SphereMesh()
        {
            if (_sphereMesh == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                MeshFilter mf = tmp.GetComponent<MeshFilter>();
                if (mf != null) _sphereMesh = mf.sharedMesh;
                Destroy(tmp);
            }
            return _sphereMesh;
        }

        /// <summary>Per-colour ball render material (created on demand at rack-build time, one per colour).</summary>
        private Material MeshMaterialFor(BallColor color)
        {
            Material mat;
            if (_meshMaterials.TryGetValue(color, out mat) && mat != null) return mat;
            mat = TableBuilder.MakeUrp("Ball_" + BallPalette.DisplayName(color), BallPalette.Rgb(color), 0.82f, 0f);
            _meshMaterials[color] = mat;
            return mat;
        }

        // -------------------------------------------------------------------------------------------------
        // Queries
        // -------------------------------------------------------------------------------------------------

        /// <summary>True when every ball is slower than SettleSpeed and none is Dropping (OffTable balls count
        /// as settled — they are out of play). Allocation-free.</summary>
        public bool AllBallsAtRest()
        {
            float settleSq = GameConfig.PhysicsTuning.SettleSpeed * GameConfig.PhysicsTuning.SettleSpeed;
            for (int i = 0; i < _balls.Count; i++)
            {
                Ball b = _balls[i];
                if (b == null) continue;
                if (b.State == BallState.Dropping) return false;
                Rigidbody rb = b.Body;
                if (rb != null && rb.velocity.sqrMagnitude > settleSq) return false;
            }
            return true;
        }

        /// <summary>Number of reds still legitimately on the table (active, not dropping/off-table).</summary>
        public int RedsRemaining()
        {
            int count = 0;
            for (int i = 0; i < _balls.Count; i++)
            {
                Ball b = _balls[i];
                if (b == null || b.IsCue || b.Color != BallColor.Red) continue;
                if (!IsOnTable(b)) continue;
                if (b.State == BallState.Dropping || b.State == BallState.OffTable) continue;
                count++;
            }
            return count;
        }

        /// <summary>True when the ball exists and its GameObject is active in the hierarchy.</summary>
        public bool IsOnTable(Ball ball)
        {
            return ball != null && ball.gameObject.activeInHierarchy;
        }

        /// <summary>World-space spot for a colour: Black/Pink/Blue/Brown on their TableDims X, Green/Brown/Yellow
        /// on the baulk line (Z = −DRadius / 0 / +DRadius), Red at the rack apex (just behind pink toward black),
        /// Cue at the middle of the D (baulk-line side, x ≤ BaulkLineX). Y is always BallRestHeight.</summary>
        public Vector3 GetSpot(BallColor color)
        {
            float y = GameConfig.TableDims.BallRestHeight;
            switch (color)
            {
                case BallColor.Black:
                    return new Vector3(GameConfig.TableDims.BlackX, y, 0f);
                case BallColor.Pink:
                    return new Vector3(GameConfig.TableDims.PinkX, y, 0f);
                case BallColor.Blue:
                    return new Vector3(0f, y, 0f);
                case BallColor.Brown:
                    return new Vector3(GameConfig.TableDims.BaulkLineX, y, 0f);
                case BallColor.Green:
                    return new Vector3(GameConfig.TableDims.BaulkLineX, y, -GameConfig.TableDims.BaulkLineZ);
                case BallColor.Yellow:
                    return new Vector3(GameConfig.TableDims.BaulkLineX, y, GameConfig.TableDims.BaulkLineZ);
                case BallColor.Red:
                    return new Vector3(GameConfig.TableDims.PinkX + 2f * GameConfig.TableDims.BallRadius + 0.01f, y, 0f);
                default: // Cue — midpoint of the D (the D bulges toward the baulk cushion, x ≤ BaulkLineX)
                    return new Vector3(GameConfig.TableDims.BaulkLineX - GameConfig.TableDims.DRadius * 0.5f, y, 0f);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Shot tracking
        // -------------------------------------------------------------------------------------------------

        /// <summary>Starts tracking <paramref name="ctx"/>: stores it, publishes Ball.CurrentContext and starts
        /// the rest-watch coroutine (all balls under SettleSpeed for RestConfirmFrames consecutive fixed frames,
        /// capped at RestTimeoutSeconds). On completion the context is marked Completed, Ball.CurrentContext is
        /// cleared and BallsAtRest is raised. Returns the same context.</summary>
        public ShotContext BeginShotTracking(ShotContext ctx)
        {
            if (ctx == null) return null;

            if (_tracked != null && _tracked != ctx)
            {
                // Defensive: a previous shot never completed — retire it silently so it is never double-resolved.
                _tracked.Completed = true;
                if (Ball.CurrentContext == _tracked) Ball.CurrentContext = null;
            }

            StopRestRoutine();
            _tracked = ctx;
            _completed = null;
            Ball.CurrentContext = ctx;
            _restRoutine = StartCoroutine(RestWatch(ctx));
            return ctx;
        }

        /// <summary>Stops the rest-watch coroutine and returns the tracked context. If tracking already
        /// auto-completed, returns the stashed completed context; if no shot was ever tracked, returns a fresh
        /// already-completed context — this method never returns null.</summary>
        public ShotContext EndShotTracking()
        {
            StopRestRoutine();

            if (_tracked != null)
            {
                ShotContext tracked = _tracked;
                _tracked = null;
                if (Ball.CurrentContext == tracked) Ball.CurrentContext = null;
                return tracked;
            }
            if (_completed != null) return _completed;

            ShotContext fresh = new ShotContext();
            fresh.Completed = true;
            return fresh;
        }

        /// <summary>Rest-watch: completes the shot after RestConfirmFrames consecutive still fixed frames or the
        /// RestTimeoutSeconds cap, whichever comes first. One WaitForFixedUpdate instance, reused — no per-frame
        /// heap allocations.</summary>
        private IEnumerator RestWatch(ShotContext ctx)
        {
            float elapsed = 0f;
            int stillFrames = 0;
            while (true)
            {
                yield return _fixedWait;
                elapsed += GameConfig.PhysicsTuning.FixedDeltaTime;

                if (AllBallsAtRest()) stillFrames++;
                else stillFrames = 0;

                if (stillFrames >= GameConfig.PhysicsTuning.RestConfirmFrames) break;
                if (elapsed >= GameConfig.PhysicsTuning.RestTimeoutSeconds) break;
            }
            CompleteShot(ctx);
        }

        /// <summary>Marks the context completed, clears Ball.CurrentContext and raises BallsAtRest (typed event +
        /// GameEvents) with a pre-allocated args object. Double-completion is guarded.</summary>
        private void CompleteShot(ShotContext ctx)
        {
            if (ctx == null || _completed == ctx) return;

            ctx.Completed = true;
            if (_tracked == ctx) _tracked = null;
            _completed = ctx;
            if (Ball.CurrentContext == ctx) Ball.CurrentContext = null;

            _restArgs.Context = ctx;
            GameEvents.RaiseBallsAtRest(_restArgs);
            BallsAtRestEvent?.Invoke(_restArgs);
        }

        private void StopRestRoutine()
        {
            if (_restRoutine != null)
            {
                StopCoroutine(_restRoutine);
                _restRoutine = null;
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Respot
        // -------------------------------------------------------------------------------------------------

        /// <summary>Returns a ball to the table: its own spot first, then an X-scan of up to 40 steps (one ball
        /// diameter each) for the first position with no live-ball overlap; the last scanned position is the
        /// fallback so a respotted ball ALWAYS ends up on the table. PlaceAt restores a fully playable state.</summary>
        public void Respot(Ball ball)
        {
            if (ball == null) return;

            Vector3 pos = GetSpot(ball.Color);
            float step = 2f * GameConfig.TableDims.BallRadius;
            for (int i = 0; i < 40; i++)
            {
                if (!OverlapsAny(ball, pos)) break;
                pos.x += step;
            }
            ball.PlaceAt(pos);
        }

        /// <summary>True when any live ball other than <paramref name="ignore"/> overlaps the horizontal disc of
        /// one ball diameter around <paramref name="pos"/>.</summary>
        private bool OverlapsAny(Ball ignore, Vector3 pos)
        {
            float minDist = 2f * GameConfig.TableDims.BallRadius * 1.02f;
            float minDistSq = minDist * minDist;
            for (int i = 0; i < _balls.Count; i++)
            {
                Ball b = _balls[i];
                if (b == null || b == ignore) continue;
                if (b.State == BallState.Dropping || b.State == BallState.OffTable) continue;
                if (!IsOnTable(b)) continue;

                float dx = b.transform.position.x - pos.x;
                float dz = b.transform.position.z - pos.z;
                if (dx * dx + dz * dz < minDistSq) return true;
            }
            return false;
        }

        // -------------------------------------------------------------------------------------------------
        // Cue-ball-in-hand placement
        // -------------------------------------------------------------------------------------------------

        /// <summary>Parks the cue ball for placement: reactivates/restores it if it was potted or off-table,
        /// then freezes it kinematically with the collider disabled so it can be dragged legally without
        /// colliding or falling. <paramref name="fromHand"/> distinguishes foul ball-in-hand from other flows.</summary>
        public void BeginCueBallPlacement(bool fromHand)
        {
            _needsCuePlacement = true;

            Ball cue = _cueBall;
            if (cue == null) return;

            if (!cue.gameObject.activeInHierarchy ||
                cue.State == BallState.Dropping || cue.State == BallState.OffTable)
            {
                cue.PlaceAt(GetSpot(BallColor.Cue));
            }
            ParkCueBall(cue, true);
        }

        /// <summary>Moves the parked cue ball to <paramref name="worldPos"/>, clamped into the D
        /// ({ (x − BaulkLineX)² + z² ≤ DRadius², x ≤ BaulkLineX } — the D bulges toward the baulk cushion).
        /// Returns false when placement mode is off or the spot overlaps a live ball; on success the ball is
        /// placed (and stays parked until EndCueBallPlacement).</summary>
        public bool TryPlaceCueBall(Vector3 worldPos)
        {
            if (!_needsCuePlacement) return false;

            Ball cue = _cueBall;
            if (cue == null) return false;

            float x = worldPos.x;
            float z = worldPos.z;
            if (x > GameConfig.TableDims.BaulkLineX) x = GameConfig.TableDims.BaulkLineX;

            float dx = x - GameConfig.TableDims.BaulkLineX;
            float dist = Mathf.Sqrt(dx * dx + z * z);
            if (dist > GameConfig.TableDims.DRadius)
            {
                float scale = GameConfig.TableDims.DRadius / dist;
                x = GameConfig.TableDims.BaulkLineX + dx * scale;
                z *= scale;
            }

            Vector3 pos = new Vector3(x, GameConfig.TableDims.BallRestHeight, z);
            if (OverlapsAny(cue, pos)) return false;

            cue.PlaceAt(pos);
            ParkCueBall(cue, true); // stay parked until placement ends
            return true;
        }

        /// <summary>Ends cue-ball placement: releases the ball back to full dynamics and clears the flag.</summary>
        public void EndCueBallPlacement()
        {
            _needsCuePlacement = false;

            Ball cue = _cueBall;
            if (cue == null) return;
            ParkCueBall(cue, false);
        }

        /// <summary>Parks (kinematic + collider off) or releases (dynamic + collider on) the cue ball.</summary>
        private void ParkCueBall(Ball cue, bool parked)
        {
            Collider col = cue.GetComponent<Collider>();
            Rigidbody rb = cue.Body;

            if (parked)
            {
                if (col != null) col.enabled = false;
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                if (col != null) col.enabled = true;
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }
            }
        }
    }
}
