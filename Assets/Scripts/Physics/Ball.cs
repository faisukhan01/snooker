// SnookerKit — ball rigidbody behaviour (Physics module, CONTRACTS §2/§4).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Simulation state of a single ball. Transitions: Stationary ↔ Rolling (Ball.FixedUpdate),
    /// → Dropping (Ball.Pot, PocketManager animates the shrink), → OffTable (Ball.MarkOffTable).
    /// PlaceAt always restores a ball to Stationary.</summary>
    public enum BallState
    {
        /// <summary>Resting on the bed (or freshly placed, not yet moving).</summary>
        Stationary = 0,
        /// <summary>Rolling / spinning on the bed.</summary>
        Rolling = 1,
        /// <summary>Inside a pocket: kinematic, collider off, shrink animation owned by PocketManager.</summary>
        Dropping = 2,
        /// <summary>Left the table (fell off / explicitly marked): inactive until placed again.</summary>
        OffTable = 3,
    }

    /// <summary>One snooker ball. The GameObject + SphereCollider + Rigidbody are created by BallManager;
    /// this component caches and tunes the rigidbody in Awake, runs the grounded cloth-deceleration probe in
    /// FixedUpdate, the off-table guard in Update, and raises the contact events in OnCollisionEnter.
    /// Pot bookkeeping (PottedBallRecord + PottedBall event) belongs to PocketManager, NOT to Ball.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Ball : MonoBehaviour
    {
        /// <summary>Stable identity assigned by BallManager (cue = 0, reds 1..15, colours 16..21).</summary>
        public int Id { get; set; }

        /// <summary>Ball identity/colour (written once by BallManager at creation; point value via BallPalette).</summary>
        public BallColor Color { get; set; }

        /// <summary>True for the cue ball only.</summary>
        public bool IsCue { get; set; }

        /// <summary>Current simulation state. The setter is protected so external code can only change state
        /// through the intended methods (Pot / MarkOffTable / PlaceAt).</summary>
        public BallState State { get; protected set; }

        /// <summary>Cached Rigidbody, tuned in Awake (Interpolate, ContinuousDynamic, mass 0.17, tuned drag).
        /// The setter is protected — only this component assigns it.</summary>
        public Rigidbody Body { get; protected set; }

        /// <summary>ShotContext of the shot currently being tracked. Set by BallManager.BeginShotTracking,
        /// cleared by BallManager when the shot auto-completes / ends. Ball and PocketManager record into it.</summary>
        public static ShotContext CurrentContext { get; set; }

        // --- cached components / constants ---------------------------------------------------------------

        private Collider _collider;
        private Vector3 _naturalScale;
        private RaycastHit _groundHit;
        private readonly BallBallHitArgs _ballBallArgs = new BallBallHitArgs();
        private readonly CushionHitArgs _cushionArgs = new CushionHitArgs();

        /// <summary>Ball centre below this world Y counts as fallen off the table (bed top is y = 0).</summary>
        private const float OffTableY = -0.05f;
        /// <summary>Absolute world X beyond which a ball is definitely off the table.</summary>
        private const float OffTableAbsX = 2.2f;
        /// <summary>Absolute world Z beyond which a ball is definitely off the table.</summary>
        private const float OffTableAbsZ = 1.3f;

        /// <summary>Caches components and retunes the BallManager-created rigidbody to the pinned simulation
        /// profile. Runs at AddComponent time, i.e. after BallManager.Awake has registered its PhysicMaterial.</summary>
        private void Awake()
        {
            _collider = GetComponent<Collider>();

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>(); // defensive; RequireComponent normally guarantees
            Body = rb;

            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.mass = 0.17f;
            rb.drag = GameConfig.PhysicsTuning.BallDrag;
            rb.angularDrag = GameConfig.PhysicsTuning.BallAngularDrag;
            rb.maxAngularVelocity = 60f;
            rb.solverIterations = 8;
            rb.sleepThreshold = GameConfig.PhysicsTuning.SleepThreshold;

            if (ServiceRegistry.TryGet<BallManager>(out BallManager bm) && bm != null && bm.BallMaterial != null)
            {
                rb.sharedMaterial = bm.BallMaterial;
            }

            _naturalScale = transform.localScale;
            if (_naturalScale.sqrMagnitude < 1e-10f)
            {
                _naturalScale = Vector3.one * (2f * GameConfig.TableDims.BallRadius);
            }

            State = BallState.Stationary;
        }

        /// <summary>Grounded cloth probe + rolling resistance. Ray goes straight down from the ball centre for
        /// BallRadius + BallLift + GroundedRayMargin (the bare margin from the rest origin can never reach the bed).
        /// When grounded and moving: applies ClothDecel opposing velocity (never hard-zeroing it — the bed
        /// PhysicMaterial friction and sleep threshold finish the roll) and SpinDecay damping to angular velocity.
        /// Allocation-free (reused RaycastHit, no LINQ/closures).</summary>
        private void FixedUpdate()
        {
            if (State != BallState.Stationary && State != BallState.Rolling) return;

            Rigidbody rb = Body;
            if (rb == null) return;

            Vector3 v = rb.velocity;
            float speed = v.magnitude;

            float rayLength = GameConfig.TableDims.BallRadius + GameConfig.TableDims.BallLift + GameConfig.PhysicsTuning.GroundedRayMargin;
            bool grounded = Physics.Raycast(
                transform.position, Vector3.down, out _groundHit, rayLength,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (grounded && speed > 0f)
            {
                float decel = GameConfig.PhysicsTuning.ClothDecel * Time.fixedDeltaTime;
                if (speed > decel)
                {
                    // Oppose velocity only while the step cannot cross zero — friction finishes the stop.
                    float newSpeed = speed - decel;
                    rb.velocity = v * (newSpeed / speed);
                }

                Vector3 w = rb.angularVelocity;
                float damp = 1f - Mathf.Min(0.95f, GameConfig.PhysicsTuning.SpinDecay * Time.fixedDeltaTime);
                rb.angularVelocity = new Vector3(w.x * damp, w.y * damp, w.z * damp);

                if (State == BallState.Stationary) State = BallState.Rolling;
            }
            else if (!grounded && speed > 0.05f && State == BallState.Stationary)
            {
                State = BallState.Rolling;
            }

            if (State == BallState.Rolling && speed <= 0.004f && rb.angularVelocity.sqrMagnitude < 0.0025f)
            {
                State = BallState.Stationary;
            }
        }

        /// <summary>Allocation-free off-table guard: balls that escape the rails (through a pocket gap) are
        /// marked off-table before they can fall forever.</summary>
        private void Update()
        {
            if (State == BallState.OffTable || State == BallState.Dropping) return;

            Vector3 p = transform.position;
            if (p.y < OffTableY || p.x > OffTableAbsX || p.x < -OffTableAbsX ||
                p.z > OffTableAbsZ || p.z < -OffTableAbsZ)
            {
                MarkOffTable();
            }
        }

        /// <summary>Contact events: ball-to-ball captures FirstContact (the OBJECT ball colour when the cue
        /// ball is involved in its first ball contact) and raises BallBallHit; cushion colliders (name prefix
        /// "Cushion") raise CushionHit. Both speed-scaled for audio. Runs per contact event, not per frame —
        /// the only string access is the cushion name check here, never in Update/FixedUpdate.</summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || State == BallState.OffTable || State == BallState.Dropping) return;

            float speed = collision.relativeVelocity.magnitude;
            Ball otherBall = collision.collider != null ? collision.collider.GetComponentInParent<Ball>() : null;

            if (otherBall != null && otherBall != this)
            {
                ShotContext ctx = CurrentContext;
                if (ctx != null && !ctx.Completed && !ctx.FirstContact.HasValue && (IsCue || otherBall.IsCue))
                {
                    // Cue ball struck object ball X → record X. Object-object contact without the cue records nothing.
                    ctx.FirstContact = IsCue ? otherBall.Color : Color;
                }

                if (speed > 0.01f)
                {
                    if (ServiceRegistry.TryGet<AudioManager>(out AudioManager am) && am != null) am.PlayImpact(speed);
                    _ballBallArgs.Speed = speed;
                    GameEvents.RaiseBallBallHit(_ballBallArgs);
                }
                return;
            }

            // Static contact: cushions (audio + event); baize/wood are silent.
            Collider col = collision.collider;
            if (col != null)
            {
                string colName = col.name;
                if (!string.IsNullOrEmpty(colName) && colName.StartsWith("Cushion"))
                {
                    if (ServiceRegistry.TryGet<AudioManager>(out AudioManager am) && am != null) am.PlayCushion(speed);
                    _cushionArgs.Speed = speed;
                    GameEvents.RaiseCushionHit(_cushionArgs);
                }
            }
        }

        /// <summary>Pots this ball into pocket <paramref name="pocketIndex"/>. Idempotent. Puts the ball into
        /// the Dropping state (kinematic, collider disabled) — the shrink/deactivate animation and ALL pot
        /// bookkeeping (PottedBallRecord, GameEvents.PottedBall, audio) are owned by PocketManager.</summary>
        public void Pot(int pocketIndex)
        {
            if (State == BallState.Dropping || State == BallState.OffTable) return;

            State = BallState.Dropping;

            Rigidbody rb = Body;
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            if (_collider != null) _collider.enabled = false;
        }

        /// <summary>Marks the ball as off the table: freezes physics, disables the collider and deactivates the
        /// GameObject. Records CueOffTable into the tracked shot context when the cue ball leaves play.
        /// Idempotent; PlaceAt fully reverses it.</summary>
        public void MarkOffTable()
        {
            if (State == BallState.OffTable) return;

            State = BallState.OffTable;

            Rigidbody rb = Body;
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            if (_collider != null) _collider.enabled = false;

            ShotContext ctx = CurrentContext;
            if (ctx != null && !ctx.Completed && IsCue) ctx.CueOffTable = true;

            gameObject.SetActive(false);
        }

        /// <summary>(Re)places the ball at <paramref name="pos"/> in a fully playable state: reactivates the
        /// GameObject, restores the natural scale (pot→respot cycles must never leave a disabled or shrunken
        /// ball live on the table), re-enables the collider, un-freezes physics and returns to Stationary.</summary>
        public void PlaceAt(Vector3 pos)
        {
            gameObject.SetActive(true);
            transform.position = pos;
            transform.localScale = _naturalScale;

            Rigidbody rb = Body;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
            if (_collider != null) _collider.enabled = true;

            State = BallState.Stationary;
        }
    }
}
