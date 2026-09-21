# QA Matrix — Premium Mobile Snooker (Phase 13)

Phase 13 (final visual QA) requires the Unity editor. This is the complete pass/fail matrix,
mirroring PDF §25 (visual checklist) and §26 (testing matrix). Anything marked ⚠ needs a
workstation; everything else was already enforced structurally during the build.

## A. Visual QA checklist (per screen, every supported aspect ratio)

| # | Check | 16:9 | 18:9 | 19.5:9 | 20:9 |
|---|---|---|---|---|---|
| A1 | No overlapping elements | ⚠ | ⚠ | ⚠ | ⚠ |
| A2 | No clipped text (long player names truncate with ellipsis behaviour) | ⚠ | ⚠ | ⚠ | ⚠ |
| A3 | Consistent margins + corner radii (rounded 9-slice everywhere) | ⚠ | ⚠ | ⚠ | ⚠ |
| A4 | Readable typography at arm's length (≥ 24px reference) | ⚠ | ⚠ | ⚠ | ⚠ |
| A5 | Correct contrast (charcoal #141A17 panels vs #F2F5F3 text) | ⚠ | ⚠ | ⚠ | ⚠ |
| A6 | No default Unity UI styling anywhere | ⚠ | ⚠ | ⚠ | ⚠ |
| A7 | No decorative clutter; table is the hero | ⚠ | ⚠ | ⚠ | ⚠ |
| A8 | Buttons have clear pressed/disabled states (0.97 scale + tint) | ⚠ | ⚠ | ⚠ | ⚠ |
| A9 | Smooth, short animations only (≤ 0.6 s transitions) | ⚠ | ⚠ | ⚠ | ⚠ |
| A10 | Safe areas / display cutouts respected (SafeAreaFitter) | ⚠ | ⚠ | ⚠ | ⚠ |

## B. Functional matrix (PDF §26)

| Area | Verify | Status |
|---|---|---|
| Launch | App starts without crashes (Boot → Home) | ⚠ device |
| Menus | Navigation and back behaviour across all 6 scenes | ⚠ device |
| Gameplay | Match starts correctly from every mode entry (Play Now / Quick / Two Player / Practice) | ⚠ device |
| Aim | Touch aiming responds accurately; assist OFF/LOW/MEDIUM/HIGH snaps 0/2/5/10° | ⚠ device |
| Power | Shot power is predictable and repeatable (same input ⇒ same break spread) | ⚠ device |
| Spin | Spin affects physics correctly (top/back/side visibly alter cue-ball behaviour) | ⚠ device |
| Physics | Balls collide/rebound/stop correctly; identical shots behave identically | ⚠ device |
| Pockets | Balls detected + drop animation + removed; cue pots are fouls, never scores | ⚠ device |
| Rules | Legal/foul states update correctly (sequence, max(4,value) penalties, respots) | ⚠ device |
| Scoring | Scores and breaks update correctly; ANY-colour chip after a legal red | ⚠ device |
| AI | AI takes legal, playable shots on all 4 tiers; no stalls beyond the 5 s watchdog | ⚠ device |
| Save | Settings/profile persist across process kill; corrupt file → graceful defaults | ⚠ device |
| Performance | Frame rate stable (60 target, graceful fallback) | ⚠ device |
| Game Tools | Metrics show real data or N/A — network always "N/A (offline)" in v1 | ⚠ device |
| Android | Build installs and launches (see docs/ANDROID_BUILD.md) | ⚠ device |

## C. What was already verified during the build (structural)

- Namespace `SnookerKit` everywhere; no duplicate top-level type names across 52 C# files.
- Forbidden-API surface clean: no TextMeshPro/InputSystem/UnityEditor (outside Editor)/GameObject.Find/
  Camera.main in hot paths/linearVelocity/PhysicsMaterial/Newtonsoft.
- Every `GameEvents` subscription is paired with an unsubscribe (OnEnable/OnDisable).
- Cross-module contract alignment reviewed against `docs/CONTRACTS.md` (the frozen layer).
- Scene YAML + deterministic GUID set: 6 scenes, 10 WAVs, meta set idempotent (`gen-meta.mjs` re-runs create 0).
- Build-order regression from the original program (table before BallManager → null bed materials) is
  guarded by explicit composition ordering in `MatchSceneRoot` + TableBuilder idempotent Build.

## D. Sign-off

Record results per cell above, then tag the commit `qa-passed`. Any red cell: fix, re-run the
affected row, update this matrix.
