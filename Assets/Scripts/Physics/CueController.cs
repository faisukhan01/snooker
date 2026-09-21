// SnookerKit — cue aim state, procedural cue visual and strike (Physics module, CONTRACTS §2).
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns the aim angle (radians, wrapped to (−π, π]; direction = (sin θ, 0, cos θ)), power (0..1) and
    /// spin (x = side, y = top/back; each −1..1), the procedural cue visual, and the strike. Aim deltas batch into
    /// GameEvents.AimChanged at ≥ 0.0005 rad (reused args object); Fire() applies velocity + angular velocity to
    /// the cue ball rigidbody and raises GameEvents.CueStruck. Human input (InputRouter) and AI (AIController)
    /// both drive this single public surface — there is no second shot path.</summary>
    [DisallowMultipleComponent]
    public class CueController : MonoBehaviour
    {
        /// <summary>Aim delta (rad) that must accumulate before one batched AimChanged fires (frozen CONTRACTS §2).</summary>
        private const float BatchThreshold = 0.0005f;
        /// <summary>Cue-ball speed at Power01 = 0 (m/s) — even the softest tap rolls.</summary>
        private const float MinShotSpeed = 0.9f;
        /// <summary>Seconds the strike guard stays closed after Fire (double-fire protection).</summary>
        private const float StrikeClearSeconds = 0.05f;
        /// <summary>Base gap between cue tip and ball centre at zero pullback (m).</summary>
        private const float CueGap = 0.12f;
        /// <summary>Pullback preview at Power01 = 0 (m).</summary>
        private const float PullbackMin = 0.02f;
        /// <summary>Pullback preview at Power01 = 1 (m).</summary>
        private const float PullbackMax = 0.22f;
        /// <summary>Angular velocity scale (rad/s) at |spin| = 1 — saturates Ball.Awake's maxAngularVelocity (60).</summary>
        private const float SpinScaleRadPerSec = 60f;

        // Procedural cue visual dimensions (m) — a tapered look from stepped radii, tip at the stick origin.
        private const float TipBandLength = 0.02f;
        private const float TipBandRadius = 0.005f;
        private const float ShaftFrontLength = 0.5f;
        private const float ShaftFrontRadius = 0.0075f;
        private const float ShaftBackLength = 0.6f;
        private const float ShaftBackRadius = 0.0105f;
        private const float ButtLength = 0.35f;
        private const float ButtRadius = 0.0135f;

        /// <summary>Shaft wood #8A5A2B.</summary>
        private static readonly Color ShaftColor = new Color(0.541176f, 0.352941f, 0.168627f);
        /// <summary>Darker butt wood.</summary>
        private static readonly Color ButtColor = new Color(0.243137f, 0.152941f, 0.070588f);
        /// <summary>Light tip band (leather/ferrule).</summary>
        private static readonly Color TipColor = new Color(0.909804f, 0.894118f, 0.847059f);

        private float _aimAngle;
        private float _pendingDelta;
        private float _power;
        private Vector2 _spin;
        private bool _striking;
        private bool _cueVisible;
        private BallManager _balls;
        private GameObject _cueRoot;
        private Transform _cueStick;

        private readonly AimChangedArgs _aimArgs = new AimChangedArgs();
        private readonly CueStruckArgs _struckArgs = new CueStruckArgs();

        /// <summary>Current aim angle in radians, wrapped to (−π, π]. Direction = (sin, 0, cos) — see AimDirection.</summary>
        public float AimAngle { get { return _aimAngle; } }

        /// <summary>Current strike power (0..1).</summary>
        public float Power01 { get { return _power; } }

        /// <summary>Current spin: x = side (right positive), y = vertical (top positive); each −1..1.</summary>
        public Vector2 Spin { get { return _spin; } }

        /// <summary>True when a stroke is legal right now: BallManager present, every ball settled, no cue-ball
        /// placement pending and no strike in flight. Allocation-free.</summary>
        public bool CanStrike
        {
            get
            {
                BallManager balls = Balls;
                return balls != null && balls.AllBallsAtRest() && !balls.CueBallNeedsPlacement && !_striking;
            }
        }

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }

        /// <summary>Registers the controller in the ServiceRegistry (frozen manager convention).</summary>
        private void Awake()
        {
            ServiceRegistry.Register<CueController>(this);
        }

        /// <summary>Re-arms the strike guard — coroutines do not survive a disable cycle (defensive).</summary>
        private void OnEnable()
        {
            _striking = false;
        }

        /// <summary>Stops the strike coroutine and deregisters from the ServiceRegistry.</summary>
        private void OnDestroy()
        {
            StopAllCoroutines();
            ServiceRegistry.Deregister<CueController>();
        }

        /// <summary>Accumulates an aim delta (radians) and applies it in ≥ 0.0005 rad batches; each batch wraps the
        /// angle, moves the aim and raises GameEvents.AimChanged once with a reused args object.</summary>
        public void AdjustAimDelta(float delta)
        {
            _pendingDelta += delta;
            if (Mathf.Abs(_pendingDelta) < BatchThreshold) return;
            ApplyAim(_aimAngle + _pendingDelta);
        }

        /// <summary>Sets the aim absolutely (AI planner / aim assist) and raises GameEvents.AimChanged.</summary>
        public void AimAt(float angle)
        {
            ApplyAim(angle);
        }

        /// <summary>Clamps and stores the strike power (0..1).</summary>
        public void SetPower01(float v)
        {
            _power = Mathf.Clamp01(v);
        }

        /// <summary>Clamps and stores the spin, each axis to −1..1.</summary>
        public void SetSpin(Vector2 spin)
        {
            _spin = new Vector2(Mathf.Clamp(spin.x, -1f, 1f), Mathf.Clamp(spin.y, -1f, 1f));
        }

        /// <summary>Unit aim direction on the table plane: (sin AimAngle, 0, cos AimAngle). θ = 0 aims toward +Z.</summary>
        public Vector3 AimDirection()
        {
            return new Vector3(Mathf.Sin(_aimAngle), 0f, Mathf.Cos(_aimAngle));
        }

        /// <summary>Strikes the cue ball. Returns false when !CanStrike (or the cue rigidbody is missing).
        /// On success: snapshots power/spin/angle, opens the strike guard, sets Body.velocity = AimDirection ×
        /// (0.9 + Power01 × (MaxShotSpeed − 0.9)), applies spin as angular velocity (perpendicular horizontal axis
        /// for top/back spin, Y-axis twist for side spin), raises GameEvents.CueStruck with a reused args object
        /// and schedules the 0.05 s guard release.</summary>
        public bool Fire()
        {
            BallManager balls = Balls;
            if (balls == null || _striking || !balls.AllBallsAtRest() || balls.CueBallNeedsPlacement) return false;

            Ball cue = balls.CueBall;
            Rigidbody rb = cue != null ? cue.Body : null;
            if (rb == null) return false;

            // Snapshot — later input changes must not affect the strike already in flight.
            float power = _power;
            Vector2 spin = _spin;
            float angle = _aimAngle;
            Vector3 dir = AimDirection();

            _striking = true;
            rb.WakeUp();
            rb.velocity = dir * (MinShotSpeed + power * (GameConfig.PhysicsTuning.MaxShotSpeed - MinShotSpeed));

            // Top/back spin rolls about the horizontal axis perpendicular to the aim; side spin twists about Y
            // (positive side = right english → clockwise from above → negative Y by the right-hand rule).
            Vector3 rollAxis = new Vector3(dir.z, 0f, -dir.x);
            rb.angularVelocity = rollAxis * (spin.y * SpinScaleRadPerSec)
                               + Vector3.up * (-spin.x * SpinScaleRadPerSec);

            _struckArgs.Power01 = power;
            _struckArgs.Spin = spin;
            _struckArgs.AimAngle = angle;
            GameEvents.RaiseCueStruck(_struckArgs);

            StartCoroutine(StrikeRelease());
            return true;
        }

        /// <summary>Shows or hides the procedural cue visual. The cue only actually renders while a stroke is
        /// legal (CanStrike) — LateUpdate re-hides it whenever the table is live.</summary>
        public void ShowCue(bool visible)
        {
            _cueVisible = visible;
            if (!visible)
            {
                if (_cueRoot != null && _cueRoot.activeSelf) _cueRoot.SetActive(false);
                return;
            }
            if (CanStrike)
            {
                EnsureCueVisual();
                SyncCueVisual();
            }
        }

        /// <summary>Keeps the visible cue glued behind the cue ball at BallRadius + 0.12 + pullback along the
        /// inverse aim (pullback previews Power01: 0 → 0.02, 1 → 0.22). Hides the visual whenever the table is
        /// live. Allocation-free.</summary>
        private void LateUpdate()
        {
            if (!_cueVisible) return;

            if (!CanStrike)
            {
                if (_cueRoot != null && _cueRoot.activeSelf) _cueRoot.SetActive(false);
                return;
            }

            EnsureCueVisual();
            SyncCueVisual();
        }

        // -------------------------------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------------------------------

        /// <summary>Applies an absolute aim: wraps to (−π, π], flushes the pending delta and raises AimChanged.</summary>
        private void ApplyAim(float angle)
        {
            _aimAngle = WrapAngle(angle);
            _pendingDelta = 0f;

            _aimArgs.AimAngle = _aimAngle;
            GameEvents.RaiseAimChanged(_aimArgs);
        }

        /// <summary>Wraps any angle into (−π, π] (atan2(sin, cos) style).</summary>
        private static float WrapAngle(float angle)
        {
            return Mathf.Atan2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        /// <summary>Strike-guard release coroutine: re-opens Fire after StrikeClearSeconds. Manual timer loop —
        /// no WaitForSeconds allocation.</summary>
        private IEnumerator StrikeRelease()
        {
            float elapsed = 0f;
            while (elapsed < StrikeClearSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _striking = false;
        }

        /// <summary>Builds the procedural cue visual once (lazily): a tapered cylinder shaft (#8A5A2B) with a
        /// darker butt and a light tip band, laid out backward from the stick origin along −Z.</summary>
        private void EnsureCueVisual()
        {
            if (_cueRoot != null) return;

            _cueRoot = new GameObject("Cue");
            _cueRoot.transform.SetParent(transform, false);
            _cueRoot.SetActive(false);

            _cueStick = new GameObject("CueStick").transform;
            _cueStick.SetParent(_cueRoot.transform, false);

            Material tipMaterial = TableBuilder.MakeUrp("Cue_Tip", TipColor, 0.6f, 0f);
            Material shaftMaterial = TableBuilder.MakeUrp("Cue_Shaft", ShaftColor, 0.45f, 0f);
            Material buttMaterial = TableBuilder.MakeUrp("Cue_Butt", ButtColor, 0.35f, 0f);

            MakeCylinder(_cueStick, "Cue_Tip", -TipBandLength * 0.5f, TipBandRadius, TipBandLength, tipMaterial);
            MakeCylinder(_cueStick, "Cue_ShaftFront", -TipBandLength - ShaftFrontLength * 0.5f,
                ShaftFrontRadius, ShaftFrontLength, shaftMaterial);
            MakeCylinder(_cueStick, "Cue_ShaftBack", -TipBandLength - ShaftFrontLength - ShaftBackLength * 0.5f,
                ShaftBackRadius, ShaftBackLength, shaftMaterial);
            MakeCylinder(_cueStick, "Cue_Butt", -TipBandLength - ShaftFrontLength - ShaftBackLength - ButtLength * 0.5f,
                ButtRadius, ButtLength, buttMaterial);
        }

        /// <summary>One cue segment: collider-free cylinder lying along the stick's −Z axis.</summary>
        private static void MakeCylinder(Transform parent, string name, float zCentre, float radius, float length, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis along the stick's Z
            go.transform.localPosition = new Vector3(0f, 0f, zCentre);
            go.transform.localScale = new Vector3(radius * 2f, length, radius * 2f);

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = material;
        }

        /// <summary>Positions the cue root at the cue ball, +Z along the aim, with the stick offset by
        /// BallRadius + 0.12 + pullback behind the ball centre.</summary>
        private void SyncCueVisual()
        {
            BallManager balls = Balls;
            Ball cue = balls != null ? balls.CueBall : null;
            if (cue == null || _cueRoot == null) return;

            Vector3 dir = AimDirection();
            float pullback = PullbackMin + (PullbackMax - PullbackMin) * _power;

            _cueRoot.transform.position = cue.transform.position;
            _cueRoot.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _cueStick.localPosition = new Vector3(0f, 0f, -(GameConfig.TableDims.BallRadius + CueGap + pullback));

            if (!_cueRoot.activeSelf) _cueRoot.SetActive(true);
        }
    }
}
