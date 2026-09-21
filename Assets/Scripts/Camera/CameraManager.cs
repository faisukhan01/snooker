// SnookerKit — broadcast camera rig (CONTRACTS §2/§7). Adopts or builds the single scene camera once at init,
// applies the frozen visual settings + adaptive FOV, and drives the four camera states with eased transitions.
// Init-time only scene scans; zero per-frame heap allocations.
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns the scene Camera. Awake adopts Camera.main (allowed once at init) or builds a "CameraRig"
    /// child (Camera + AudioListener, MainCamera tag), forces clear color #0A0D0B / near 0.01 / far 60 / HDR and
    /// mutes duplicate AudioListeners. Camera states (CameraState): Standard — 3/4 view behind the cue ball;
    /// CloseAim — low behind the cue, blended toward while the human aims; ShotFollow — follows the cue ball while
    /// balls move (look-at centroid of moving balls); TopDown — straight above the table centre with orbit yaw.
    /// All state changes lerp (position + rotation slerp, ≤ 0.6 s, eased) — never snap. ShotFollow auto-returns to
    /// Standard through GameEvents.TurnChanged (subscriptions paired in OnEnable/OnDisable).</summary>
    public class CameraManager : MonoBehaviour
    {
        private const float TransitionSeconds = 0.5f;       // ≤ 0.6 s per CONTRACTS §7
        private const float StandardDistance = 1.9f;
        private const float StandardHeight = 1.35f;
        private const float CloseDistance = 0.9f;
        private const float CloseHeight = 0.45f;
        private const float FollowHeight = 1.55f;
        private const float FollowVelocityPull = 0.12f;     // cue velocity → pull-back distance
        private const float FollowVelocityMax = 0.6f;
        private const float TopDownHeight = 4.2f;
        private const float LookAheadStandard = 0.35f;      // "slight forward" look target past the cue ball
        private const float LookAheadClose = 0.4f;
        private const float ZoomMinMultiplier = 0.75f;      // SetZoom(0) — closest dolly
        private const float ZoomMaxMultiplier = 1.3f;       // SetZoom(1) — farthest dolly
        private const float AimBlendStrength = 0.45f;       // how far Standard dollies toward CloseAim while aiming
        private const float AimActivitySeconds = 0.75f;     // aim-activity decay window
        private const float MovingBallSpeed = 0.05f;        // velocity above this counts as "moving" for the centroid
        private const float BaseHalfFovDegrees = 21f;       // vertical half-FOV at the 16:9 reference aspect
        private const float ReferenceAspect = 16f / 9f;
        private const float MinFovDegrees = 30f;
        private const float MaxFovDegrees = 62f;

        /// <summary>Frozen clear color #0A0D0B (CONTRACTS §2, CameraManager).</summary>
        private static readonly Color ClearColor = new Color(0.0392f, 0.0510f, 0.0431f, 1f);

        private Camera _cam;
        private CameraState _state = CameraState.Standard;
        private float _zoom01 = 0.5f;       // 0 = closest (0.75×), 1 = farthest (1.3×)
        private float _orbitYaw;            // degrees, wrapped -180..180
        private float _lastAspect = -1f;    // cached aspect for the cheap Update FOV check
        private float _aimActivity;         // 1 right after human aim input, decays to 0
        private bool _transitioning;
        private Coroutine _transitionRoutine;

        private BallManager _balls;
        private CueController _cue;
        private TurnManager _turns;

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CueController Cue { get { if (_cue == null) ServiceRegistry.TryGet(out _cue); return _cue; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private TurnManager Turns { get { if (_turns == null) ServiceRegistry.TryGet(out _turns); return _turns; } }

        /// <summary>The adopted (or built) scene camera — null only before Awake / after rig destruction.</summary>
        public Camera ActiveCamera { get { return _cam; } }

        /// <summary>Currently selected camera state.</summary>
        public CameraState State { get { return _state; } }

        private void Awake()
        {
            ServiceRegistry.Register<CameraManager>(this);
            AdoptOrBuildCamera();
            ApplyCameraSettings();
            MuteDuplicateListeners();
            if (_cam != null)
            {
                _lastAspect = _cam.aspect;
                _cam.fieldOfView = ComputeAdaptiveFov(_lastAspect);
            }
            ApplyStatePoseImmediate(); // seed a sensible pose before the first Update
        }

        private void OnEnable()
        {
            GameEvents.CueStruck += OnCueStruck;
            GameEvents.AimChanged += OnAimChanged;
            GameEvents.TurnChanged += OnTurnChanged;
        }

        private void OnDisable()
        {
            GameEvents.CueStruck -= OnCueStruck;
            GameEvents.AimChanged -= OnAimChanged;
            GameEvents.TurnChanged -= OnTurnChanged;
            StopTransition();
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<CameraManager>();
        }

        private void Update()
        {
            if (_cam == null) return;

            // Aim-activity blend decay (Standard dollies toward CloseAim while the human is actively aiming).
            if (_aimActivity > 0f)
            {
                _aimActivity = Mathf.MoveTowards(_aimActivity, 0f, Time.deltaTime / AimActivitySeconds);
            }

            // Adaptive FOV: cheap cached-aspect check, recomputed only when the aspect actually changes.
            float aspect = _cam.aspect;
            if (aspect > 0.05f && !Mathf.Approximately(aspect, _lastAspect))
            {
                _lastAspect = aspect;
                _cam.fieldOfView = ComputeAdaptiveFov(aspect);
            }

            if (!_transitioning) ApplyStatePoseImmediate();
        }

        /// <summary>Switches the camera state with an eased position + rotation transition (never snaps).</summary>
        public void SetState(CameraState state)
        {
            if (state == _state) return;
            _state = state;
            StartTransition();
        }

        /// <summary>Dolly multiplier for Standard/CloseAim: 0 → 0.75× (closest), 1 → 1.3× (farthest).</summary>
        public void SetZoom(float v01)
        {
            _zoom01 = Mathf.Clamp01(v01);
        }

        /// <summary>Rotates the orbit yaw: around the cue ball in Standard/CloseAim, around the table centre in
        /// TopDown. ShotFollow is cinematic and ignores the yaw.</summary>
        public void NudgeOrbit(float deltaYawDeg)
        {
            _orbitYaw = Mathf.Repeat(_orbitYaw + deltaYawDeg + 180f, 360f) - 180f;
        }

        /// <summary>Cue struck → cinematic follow (TopDown stays put so the practice overview is not yanked).</summary>
        private void OnCueStruck(CueStruckArgs args)
        {
            if (_state != CameraState.TopDown) SetState(CameraState.ShotFollow);
        }

        /// <summary>Human aim input keeps the aim-activity blend alive (Standard dollies toward CloseAim).</summary>
        private void OnAimChanged(AimChangedArgs args)
        {
            if (_state != CameraState.Standard) return;
            TurnManager turns = Turns;
            if (turns != null && turns.CanShootNow && !turns.IsAITurn) _aimActivity = 1f;
        }

        /// <summary>Auto-return: once the shot window reopens (balls at rest, turn decided) follow ends.</summary>
        private void OnTurnChanged(TurnChangedArgs args)
        {
            _aimActivity = 0f;
            if (args != null && args.CanShoot && _state == CameraState.ShotFollow)
            {
                SetState(CameraState.Standard);
            }
        }

        /// <summary>Adopt Camera.main once at init (the single allowed use) or build the rig child.</summary>
        private void AdoptOrBuildCamera()
        {
            _cam = Camera.main;
            if (_cam != null) return;

            GameObject rig = new GameObject("CameraRig");
            rig.transform.SetParent(transform, false);
            _cam = rig.AddComponent<Camera>();
            rig.AddComponent<AudioListener>();
            rig.tag = "MainCamera";
        }

        /// <summary>Frozen visual settings: solid #0A0D0B clear, near 0.01, far 60, HDR allowed.</summary>
        private void ApplyCameraSettings()
        {
            if (_cam == null) return;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = ClearColor;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = true;
        }

        /// <summary>Init-time only scene scan: ensure exactly one enabled AudioListener (ours) survives.</summary>
        private void MuteDuplicateListeners()
        {
            if (_cam == null) return;
            AudioListener[] listeners = FindObjectsOfType<AudioListener>(); // never per-frame
            AudioListener mine = _cam.GetComponent<AudioListener>();
            if (mine == null)
            {
                for (int i = 0; i < listeners.Length; i++)
                {
                    if (listeners[i] != null && listeners[i].enabled)
                    {
                        mine = listeners[i];
                        break;
                    }
                }
                if (mine == null) mine = _cam.gameObject.AddComponent<AudioListener>();
            }
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && !ReferenceEquals(listeners[i], mine)) listeners[i].enabled = false;
            }
        }

        /// <summary>fov = 2·atan(tan(21°)·(16/9)/aspect) in degrees, clamped to [30, 62] (CONTRACTS §2).</summary>
        private static float ComputeAdaptiveFov(float aspect)
        {
            float tanHalf = Mathf.Tan(BaseHalfFovDegrees * Mathf.Deg2Rad) * ReferenceAspect / Mathf.Max(aspect, 0.05f);
            float fovDegrees = 2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg;
            return Mathf.Clamp(fovDegrees, MinFovDegrees, MaxFovDegrees);
        }

        /// <summary>Applies the current state pose directly (continuous tracking — inputs are already smooth).</summary>
        private void ApplyStatePoseImmediate()
        {
            if (_cam == null) return;
            Vector3 position, lookAt;
            ComputeStatePose(out position, out lookAt);
            _cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(SafeForward(position, lookAt), Vector3.up));
        }

        /// <summary>Begins (or restarts) the eased transition coroutine for the current state.</summary>
        private void StartTransition()
        {
            if (_cam == null) return;
            StopTransition();
            _transitioning = true;
            _transitionRoutine = StartCoroutine(TransitionRoutine());
        }

        /// <summary>Position lerp + rotation slerp with an eased t; the target pose is recomputed every frame so
        /// states that track moving balls stay accurate during the blend. Duration ≤ 0.6 s — never snaps.</summary>
        private IEnumerator TransitionRoutine()
        {
            Vector3 startPos = _cam.transform.position;
            Quaternion startRot = _cam.transform.rotation;
            float t = 0f;
            while (t < 1f)
            {
                if (_cam == null)
                {
                    _transitioning = false;
                    _transitionRoutine = null;
                    yield break;
                }
                t += Time.deltaTime / TransitionSeconds;
                if (t > 1f) t = 1f;
                float eased = EaseInOutQuad(t);

                Vector3 position, lookAt;
                ComputeStatePose(out position, out lookAt);
                Quaternion targetRot = Quaternion.LookRotation(SafeForward(position, lookAt), Vector3.up);

                _cam.transform.SetPositionAndRotation(
                    Vector3.Lerp(startPos, position, eased),
                    Quaternion.Slerp(startRot, targetRot, eased));
                yield return null;
            }
            _transitioning = false;
            _transitionRoutine = null;
        }

        /// <summary>Cancels any running transition (safe to call from OnDisable / restarts).</summary>
        private void StopTransition()
        {
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }
            _transitioning = false;
        }

        /// <summary>Target pose for the current state. All struct math — zero allocations per call.</summary>
        private void ComputeStatePose(out Vector3 position, out Vector3 lookAt)
        {
            BallManager balls = Balls;
            Ball cueBall = balls != null ? balls.CueBall : null;
            Vector3 cuePos = cueBall != null
                ? cueBall.transform.position
                : new Vector3(0f, GameConfig.TableDims.BallRestHeight, 0f);

            // Aim direction from the cue (horizontal, normalized, safe fallback).
            Vector3 dir = Vector3.right;
            CueController cue = Cue;
            if (cue != null)
            {
                dir = cue.AimDirection();
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-8f) dir = Vector3.right;
                dir.Normalize();
            }

            Quaternion orbit = Quaternion.Euler(0f, _orbitYaw, 0f);
            Vector3 dirOrbit = orbit * dir;
            float zoomMult = Mathf.Lerp(ZoomMinMultiplier, ZoomMaxMultiplier, _zoom01);

            switch (_state)
            {
                case CameraState.CloseAim:
                    position = cuePos - dirOrbit * (CloseDistance * zoomMult) + Vector3.up * (CloseHeight * zoomMult);
                    lookAt = cuePos + dir * LookAheadClose;
                    return;

                case CameraState.ShotFollow:
                {
                    // Smooth follow of the CUE ball, pulled back slightly against its velocity.
                    Vector3 back = Vector3.zero;
                    if (cueBall != null)
                    {
                        Rigidbody rb = cueBall.GetComponent<Rigidbody>();
                        if (rb != null) back = rb.velocity * FollowVelocityPull;
                    }
                    if (back.sqrMagnitude > FollowVelocityMax * FollowVelocityMax)
                    {
                        back = back.normalized * FollowVelocityMax;
                    }
                    position = cuePos + Vector3.up * FollowHeight - back;
                    lookAt = MovingBallCentroid(balls, cuePos);
                    return;
                }

                case CameraState.TopDown:
                    // Straight above the table centre (tiny Z offset avoids the straight-down LookAt degeneracy);
                    // the orbit yaw spins the rig around Y.
                    position = orbit * new Vector3(0f, TopDownHeight, -0.001f);
                    lookAt = Vector3.zero;
                    return;

                default: // Standard — 3/4 view behind the cue ball, blended toward CloseAim while aiming.
                {
                    float blend = AimBlendStrength * _aimActivity;
                    float dist = Mathf.Lerp(StandardDistance * zoomMult, CloseDistance * zoomMult, blend);
                    float height = Mathf.Lerp(StandardHeight * zoomMult, CloseHeight * zoomMult, blend);
                    position = cuePos - dirOrbit * dist + Vector3.up * height;
                    lookAt = cuePos + dir * LookAheadStandard;
                    return;
                }
            }
        }

        /// <summary>Centroid of on-table balls currently moving (velocity probe via cached Rigidbody components);
        /// falls back to the cue position when everything is settled.</summary>
        private static Vector3 MovingBallCentroid(BallManager balls, Vector3 fallback)
        {
            if (balls == null) return fallback;
            System.Collections.Generic.IReadOnlyList<Ball> all = balls.AllBalls;
            if (all == null) return fallback;

            Vector3 sum = Vector3.zero;
            int count = 0;
            float speedSqr = MovingBallSpeed * MovingBallSpeed;
            for (int i = 0; i < all.Count; i++)
            {
                Ball ball = all[i];
                if (ball == null || !balls.IsOnTable(ball)) continue;
                Rigidbody rb = ball.GetComponent<Rigidbody>();
                if (rb == null || rb.velocity.sqrMagnitude < speedSqr) continue;
                sum += ball.transform.position;
                count++;
            }
            return count > 0 ? sum / count : fallback;
        }

        /// <summary>Guard against degenerate look directions (e.g. exactly straight down).</summary>
        private static Vector3 SafeForward(Vector3 position, Vector3 lookAt)
        {
            Vector3 forward = lookAt - position;
            return forward.sqrMagnitude < 1e-8f ? Vector3.down : forward.normalized;
        }

        /// <summary>Smooth ease-in-out quad (same curve as the shared Tween ease; local copy keeps this module
        /// compile-independent of the parallel Utilities surface).</summary>
        private static float EaseInOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : 1f - (-2f * t + 2f) * (-2f * t + 2f) * 0.5f;
        }
    }
}
