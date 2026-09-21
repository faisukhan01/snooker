# Touch Controls Reference

Landscape-first, one-thumb-friendly. All interactions use the classic Input touch pipeline.

## Aiming

| Gesture | Result |
|---|---|
| Single-finger horizontal drag (on the table, not on UI) | Aim left/right — 0.0032 rad/px scaled by the Aim Sensitivity setting (0.5–2.0) |
| Aim assist OFF/LOW/MEDIUM/HIGH | Magnetism pull (25%/frame) toward the best legal line within a 0°/2°/5°/10° window while your finger is down |

## Power

| Gesture | Result |
|---|---|
| Press + drag on the power meter (bottom-right by default) | Sets power 0–100%; the meter shows LOW→HIGH blocks, last three glow gold |
| Release | Strikes (only when every ball is at rest and the cue ball is not in hand) |

The meter is disabled (dimmed) during AI turns, ball movement and placement — accidental shots are
impossible by construction (`CueController.CanStrike` + `TurnManager` gating).

## Spin (English)

| Gesture | Result |
|---|---|
| Drag the dot on the circular pad (bottom-left) | Contact point moves top/back/left/right; feeds real rigidbody angular velocity — no scripted post-shot paths |
| SPIN button (bottom-centre) | Shows/hides the pad |
| Tap the pad centre | Resets to centre contact |

## Ball-in-hand (after a foul that potted the cue ball)

| Gesture | Result |
|---|---|
| Drag on the table | Cue ball follows your finger, clamped inside the D; illegal (overlapping) spots are rejected |
| Release after a legal placement | Locks the ball in and hands control back |

## Camera & system

| Gesture | Result |
|---|---|
| Two-finger pinch | Zoom in/out |
| Two-finger horizontal drag | Orbit the camera |
| CAMERA button | Cycles Standard → Close Aim → Shot Follow → Top Down |
| MENU button | Pause panel: Resume / Restart Frame / Concede Frame / Quit to Home |

## Keyboard (editor / desktop debug)

| Key | Result |
|---|---|
| Mouse drag on table | Aim (same as touch) |
| Mouse wheel | Zoom |
| LMB on buttons | Standard uGUI clicks |
