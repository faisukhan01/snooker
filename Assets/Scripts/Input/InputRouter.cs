// SnookerKit — classic-Input gesture router (CONTRACTS §2/§6). Touch + editor/desktop mouse fallback only —
// classic UnityEngine.Input exclusively (no other input packages), no per-frame heap allocations.
using UnityEngine;
using UnityEngine.EventSystems;

namespace SnookerKit
{
    /// <summary>Routes classic Input to cue aiming and camera gestures. Single-finger horizontal drag aims through
    /// CueController.AdjustAimDelta (0.0032 rad/px × aimSensitivity) while the local player may shoot; an optional
    /// aim-assist magnetism (Settings.aimAssist 0..3 → 0/2/5/10° snap radius) pulls the aim 25%/frame toward
    /// TrajectoryPredictor.TryGetAssistAngle, and only while the finger is down. Two-finger pinch zooms and a
    /// two-finger horizontal drag orbits the CameraManager. Aim is suppressed over UI, during AI turns and while
    /// the cue ball needs placement.</summary>
    public class InputRouter : MonoBehaviour
    {
        private const float RadiansPerPixel = 0.0032f;      // drag-to-aim, scaled by aimSensitivity
        private const float AssistPullPerFrame = 0.25f;     // smooth 25%/frame pull toward the assist angle
        private const float PinchToZoom = 0.002f;           // pinch px → SetZoom 0..1 delta
        private const float ScrollToZoom = 0.08f;           // mouse wheel notch → SetZoom delta (editor/desktop)
        private const float OrbitDegreesPerPixel = 0.2f;    // two-finger horizontal drag → NudgeOrbit degrees
        private const float DeadZonePixels = 0.01f;

        /// <summary>Assist snap radius in degrees per level (OFF/LOW/MEDIUM/HIGH) — CONTRACTS §5.</summary>
        private static readonly float[] AssistSnapDegrees = { 0f, 2f, 5f, 10f };

        private TurnManager _turns;
        private BallManager _balls;
        private CueController _cue;
        private RulesManager _rules;
        private TrajectoryPredictor _predictor;
        private SettingsService _settings;
        private CameraManager _camera;

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private TurnManager Turns { get { if (_turns == null) ServiceRegistry.TryGet(out _turns); return _turns; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CueController Cue { get { if (_cue == null) ServiceRegistry.TryGet(out _cue); return _cue; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private RulesManager Rules { get { if (_rules == null) ServiceRegistry.TryGet(out _rules); return _rules; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private TrajectoryPredictor Predictor { get { if (_predictor == null) ServiceRegistry.TryGet(out _predictor); return _predictor; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private SettingsService Settings { get { if (_settings == null) ServiceRegistry.TryGet(out _settings); return _settings; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CameraManager Cam { get { if (_camera == null) ServiceRegistry.TryGet(out _camera); return _camera; } }

        private int _aimFingerId = -1;      // finger currently driving aim (-1 = none)
        private bool _mouseAiming;          // editor/desktop fallback state
        private bool _mouseOverUI;
        private Vector3 _mouseLast;
        private float _zoom01 = 0.5f;       // accumulated pinch/wheel zoom (0 = closest, 1 = farthest)
        private float _pinchDistance;       // last frame's two-finger spread
        private bool _pinching;

        /// <summary>True while a finger (or the mouse) is down — assist never fights an idle player.</summary>
        private bool PointerDown { get { return _aimFingerId != -1 || _mouseAiming; } }

        private void Update()
        {
            if (Input.touchCount > 0)
            {
                HandleTouches();
            }
            else
            {
                _aimFingerId = -1;
                _pinching = false;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.touchCount == 0) HandleMouse();
#endif
            if (PointerDown) ApplyAimAssist();
        }

        /// <summary>One finger: horizontal drag aims (when it began over the table). Two fingers: pinch zoom +
        /// horizontal orbit. Aim is suspended the moment a second finger lands.</summary>
        private void HandleTouches()
        {
            int count = Input.touchCount;
            if (count == 1)
            {
                _pinching = false;
                Touch touch = Input.GetTouch(0);
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        _aimFingerId = IsOverUI(touch.fingerId) ? -1 : touch.fingerId;
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (touch.fingerId == _aimFingerId) AimByPixels(touch.deltaPosition.x);
                        break;
                    default: // Ended / Canceled
                        if (touch.fingerId == _aimFingerId) _aimFingerId = -1;
                        break;
                }
                return;
            }

            // Two or more fingers — camera gestures take over.
            _aimFingerId = -1;
            Touch a = Input.GetTouch(0);
            Touch b = Input.GetTouch(1);
            float spread = Vector2.Distance(a.position, b.position);
            if (!_pinching)
            {
                _pinching = true;
                _pinchDistance = spread;
            }
            float delta = spread - _pinchDistance;
            _pinchDistance = spread;

            CameraManager cam = Cam;
            if (cam == null) return;

            float orbitDx = (a.deltaPosition.x + b.deltaPosition.x) * 0.5f;
            if (Mathf.Abs(orbitDx) > DeadZonePixels) cam.NudgeOrbit(orbitDx * OrbitDegreesPerPixel);

            if (Mathf.Abs(delta) > DeadZonePixels)
            {
                // Spread fingers (delta > 0) zooms in → the dolly multiplier shrinks toward 0.75×.
                _zoom01 = Mathf.Clamp01(_zoom01 - delta * PinchToZoom);
                cam.SetZoom(_zoom01);
            }
        }

        /// <summary>Editor/desktop fallback: hold LMB and drag to aim, mouse wheel to zoom.</summary>
        private void HandleMouse()
        {
            if (Input.GetMouseButton(0))
            {
                if (!_mouseAiming)
                {
                    _mouseAiming = true;
                    _mouseLast = Input.mousePosition;
                    _mouseOverUI = IsOverUI(-1);
                }
                else
                {
                    if (!_mouseOverUI) AimByPixels(Input.mousePosition.x - _mouseLast.x);
                    _mouseLast = Input.mousePosition;
                }
            }
            else
            {
                _mouseAiming = false;
            }

            Vector2 scroll = Input.mouseScrollDelta;
            if (Mathf.Abs(scroll.y) > DeadZonePixels)
            {
                _zoom01 = Mathf.Clamp01(_zoom01 - scroll.y * ScrollToZoom);
                CameraManager cam = Cam;
                if (cam != null) cam.SetZoom(_zoom01);
            }
        }

        /// <summary>Converts a horizontal pixel delta into a cue aim delta, gated to the local player's live
        /// shot window (CanShootNow, not an AI turn, no cue-ball placement pending).</summary>
        private void AimByPixels(float dx)
        {
            if (Mathf.Abs(dx) < DeadZonePixels) return;

            TurnManager turns = Turns;
            BallManager balls = Balls;
            CueController cue = Cue;
            if (turns == null || balls == null || cue == null) return;
            if (!turns.CanShootNow || turns.IsAITurn || balls.CueBallNeedsPlacement) return;

            cue.AdjustAimDelta(dx * RadiansPerPixel * AimSensitivity);
        }

        /// <summary>Aim-assist magnetism: only while the pointer is down, only within the snap radius of the
        /// winning assist line, applied as a smooth 25%/frame pull through the same AdjustAimDelta path.</summary>
        private void ApplyAimAssist()
        {
            TurnManager turns = Turns;
            BallManager balls = Balls;
            CueController cue = Cue;
            RulesManager rules = Rules;
            TrajectoryPredictor predictor = Predictor;
            if (turns == null || balls == null || cue == null || rules == null || predictor == null) return;
            if (!turns.CanShootNow || turns.IsAITurn || balls.CueBallNeedsPlacement) return;

            int level = AssistLevel;
            if (level <= 0) return;
            float snapRad = AssistSnapDegrees[level] * Mathf.Deg2Rad;

            float assistAngle;
            if (!predictor.TryGetAssistAngle(rules.RequiredBall, out assistAngle)) return;

            float diff = Mathf.DeltaAngle(cue.AimAngle, assistAngle);
            if (Mathf.Abs(diff) <= snapRad) cue.AdjustAimDelta(diff * AssistPullPerFrame);
        }

        /// <summary>aimSensitivity from SettingsService (null-safe; falls back to 1 and rejects nonsense values).</summary>
        private float AimSensitivity
        {
            get
            {
                SettingsService settings = Settings;
                if (settings == null || settings.Settings == null) return 1f;
                float v = settings.Settings.aimSensitivity;
                return v > 0.0001f ? v : 1f;
            }
        }

        /// <summary>aimAssist level clamped to 0..3 (defaults to LOW=1 when settings are unavailable).</summary>
        private int AssistLevel
        {
            get
            {
                SettingsService settings = Settings;
                if (settings == null || settings.Settings == null) return 1;
                return Mathf.Clamp(settings.Settings.aimAssist, 0, AssistSnapDegrees.Length - 1);
            }
        }

        /// <summary>True when the pointer (touch fingerId, or mouse with -1) started over a UI element.</summary>
        private static bool IsOverUI(int fingerId)
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject(fingerId);
        }
    }
}
