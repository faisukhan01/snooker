// SnookerKit — cue-ball-in-hand drag placement (CONTRACTS §2/§6). Classic Input only; no per-frame allocations.
using UnityEngine;
using UnityEngine.EventSystems;

namespace SnookerKit
{
    /// <summary>Moves the cue ball while BallManager.CueBallNeedsPlacement is true: a touch (or mouse) drag casts
    /// a ray from CameraManager.ActiveCamera onto the ball plane (y = BallRestHeight) and calls
    /// BallManager.TryPlaceCueBall — a false return (blocked/illegal) simply leaves the ball where it is. When the
    /// drag ends after a successful move, BallManager.EndCueBallPlacement commits. Defensively begins placement on
    /// TurnReason.BallInHandD handovers in case no other module did, and ignores gestures during AI turns so the
    /// player and AIController never tug-of-war over the ball.</summary>
    public class BallInHandPlacer : MonoBehaviour
    {
        private BallManager _balls;
        private TurnManager _turns;
        private CameraManager _camera;

        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private BallManager Balls { get { if (_balls == null) ServiceRegistry.TryGet(out _balls); return _balls; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private TurnManager Turns { get { if (_turns == null) ServiceRegistry.TryGet(out _turns); return _turns; } }
        /// <summary>Lazily re-resolves the service (managers register in Awake — order is undefined).</summary>
        private CameraManager Cam { get { if (_camera == null) ServiceRegistry.TryGet(out _camera); return _camera; } }

        private int _dragFingerId = -1;         // touch currently dragging the cue ball (-1 = none)
        private bool _lastPlaceSucceeded;       // result of the most recent TryPlaceCueBall call
        private bool _mouseDragging;            // editor/desktop fallback state

        private void OnEnable()
        {
            GameEvents.TurnChanged += OnTurnChanged;
        }

        private void OnDisable()
        {
            GameEvents.TurnChanged -= OnTurnChanged;
        }

        /// <summary>Defensive ensure: on a BallInHandD handover, begin placement if it is somehow not begun yet.
        /// BeginCueBallPlacement is treated as idempotent, so AIController and this module can both call it and
        /// ball-in-hand works regardless of which module receives the event first.</summary>
        private void OnTurnChanged(TurnChangedArgs args)
        {
            if (args == null || args.Reason != TurnReason.BallInHandD) return;
            BallManager balls = Balls;
            if (balls != null && balls.CueBallNeedsPlacement) balls.BeginCueBallPlacement(true);
        }

        private void Update()
        {
            BallManager balls = Balls;
            if (balls == null || !balls.CueBallNeedsPlacement)
            {
                ResetDrag();
                return;
            }

            // No tug-of-war: while the AI thinks, AIController owns the placement session.
            TurnManager turns = Turns;
            if (turns != null && turns.IsAITurn)
            {
                ResetDrag();
                return;
            }

            if (Input.touchCount > 0)
            {
                HandleTouches(balls);
            }
            else
            {
                _dragFingerId = -1;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.touchCount == 0) HandleMouse(balls);
#endif
        }

        /// <summary>Touch path: any finger may start the drag (unless it began over UI); while it moves the ball
        /// follows the ray/plane hit; on release the placement ends only if the last move was legal.</summary>
        private void HandleTouches(BallManager balls)
        {
            int count = Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began && _dragFingerId == -1 && !IsOverUI(touch.fingerId))
                {
                    _dragFingerId = touch.fingerId;
                    _lastPlaceSucceeded = false;
                }
                if (touch.fingerId != _dragFingerId) continue;

                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    DragTo(balls, touch.position);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    if (_lastPlaceSucceeded) balls.EndCueBallPlacement();
                    _dragFingerId = -1;
                    _lastPlaceSucceeded = false;
                }
            }
        }

        /// <summary>Editor/desktop fallback mirroring the touch path with the left mouse button.</summary>
        private void HandleMouse(BallManager balls)
        {
            if (Input.GetMouseButton(0))
            {
                if (!_mouseDragging)
                {
                    if (IsOverUI(-1)) return;
                    _mouseDragging = true;
                    _lastPlaceSucceeded = false;
                }
                DragTo(balls, Input.mousePosition);
            }
            else if (_mouseDragging)
            {
                if (_lastPlaceSucceeded) balls.EndCueBallPlacement();
                _mouseDragging = false;
                _lastPlaceSucceeded = false;
            }
        }

        /// <summary>Casts the screen point onto the ball plane (y = BallRestHeight) and asks BallManager to move
        /// the cue ball there. Blocked/illegal targets return false and the ball simply does not move.</summary>
        private void DragTo(BallManager balls, Vector3 screenPosition)
        {
            CameraManager camManager = Cam;
            Camera cam = camManager != null ? camManager.ActiveCamera : null;
            if (cam == null) return;

            Plane plane = new Plane(Vector3.up, new Vector3(0f, GameConfig.TableDims.BallRestHeight, 0f));
            Ray ray = cam.ScreenPointToRay(screenPosition);
            float enter;
            if (plane.Raycast(ray, out enter))
            {
                _lastPlaceSucceeded = balls.TryPlaceCueBall(ray.GetPoint(enter));
            }
        }

        /// <summary>Clears gesture state (without ending placement — BallManager still owns the session).</summary>
        private void ResetDrag()
        {
            _dragFingerId = -1;
            _lastPlaceSucceeded = false;
            _mouseDragging = false;
        }

        /// <summary>True when the pointer (touch fingerId, or mouse with -1) started over a UI element.</summary>
        private static bool IsOverUI(int fingerId)
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject(fingerId);
        }
    }
}
