# SnookerKit Module Contracts — FROZEN

Single source of truth for every module in this repository.
Version: 1.0 (Phase 01). A contract change after freeze requires a note in this file and a lead review.

---

## §0 Hard rules (apply to every file)

1. Namespace `SnookerKit` for all runtime code. `SnookerKit.EditorTools` only for `Assets/Editor/*`.
2. Target: Unity 2022.3 LTS, C# 9, .NET Standard 2.1 profile. No C# 10+ syntax.
3. Classic Input (`UnityEngine.Input`) + uGUI (`UnityEngine.UI.Text`). **Forbidden**: `TMPro`, `UnityEngine.InputSystem`, `UnityEditor` outside `Assets/Editor`, `GameObject.Find` / `Camera.main` in per-frame paths, `Rigidbody.linearVelocity` (2022.3 uses `velocity`), `PhysicsMaterial` (2022.3 uses `PhysicMaterial`).
4. No `.meta` files committed by module agents — the lead generates the full deterministic set with `tools/gen-meta.mjs`.
5. Defensive style: null-check cross-module references via `ServiceRegistry.TryGet<T>`; wrap all file IO and platform API calls in try/catch. The game must never crash from a corrupted save or missing optional service.
6. No per-frame heap allocations in `Update`/`FixedUpdate` hot paths (no LINQ, no closures, no string concat per frame).
7. XML doc comments on all public types and members.
8. Never fabricate metrics. Unavailable platform values render as `"N/A"`.
9. Every `GameEvents` subscription pairs `OnEnable`/`OnDisable`.

---

## §1 Ownership map (Assets/Scripts)

| Module | Files | Owner task |
|---|---|---|
| Core | GameConfig.cs, Contracts.cs, GameEvents.cs, ServiceRegistry.cs, MatchRequest.cs, AppServices.cs, SnookerEnums.cs, BallColor.cs, BootSceneRoot.cs | lead (frozen) |
| SaveSystem | SaveManager.cs, SaveData.cs, SettingsService.cs, StatisticsTracker.cs | 16-e |
| Utilities | Haptics.cs, Tween.cs, MathUtil.cs, RingBuffer.cs | 16-e |
| Performance | PerformanceManager.cs | 16-e |
| Physics | PhysicsWorldConfig.cs, Ball.cs, BallManager.cs, TableBuilder.cs, PocketManager.cs, CueController.cs, TrajectoryPredictor.cs | 16-a |
| Rules / Scoring / Gameplay | RulesManager.cs, ScoringManager.cs, TurnManager.cs, MatchManager.cs, PracticeController.cs, MatchSceneRoot.cs | 16-b |
| AI / Input / Camera | AIShotPlanner.cs, AIController.cs, InputRouter.cs, BallInHandPlacer.cs, CameraManager.cs | 16-c |
| UI / Audio | Theme.cs, ScreenManager.cs, UIInstaller.cs, HomeScreen.cs, HudScreen.cs, SettingsScreen.cs, ProfileScreen.cs, GameToolsPanel.cs, widgets/PowerSliderControl.cs, widgets/SpinPadControl.cs, AudioManager.cs | 16-d |

Scene roots live one MonoBehaviour per file (stable scene-YAML script references):
`UI/HomeSceneRoot.cs`, `UI/SettingsSceneRoot.cs`, `UI/ProfileSceneRoot.cs`, `UI/SceneRootUtil.cs` (owner 16-d), `Core/BootSceneRoot.cs` (lead).

---

## §2 Manager API contracts (pinned signatures)

All managers are MonoBehaviours registered in `ServiceRegistry` in their `Awake` and deregistered in `OnDestroy`, unless noted.

### ServiceRegistry (static)
```csharp
static void Register<T>(T service) where T : class;
static void Deregister<T>() where T : class;
static T Get<T>() where T : class;              // null when absent
static bool TryGet<T>(out T service) where T : class;
```

### BallManager
```csharp
IReadOnlyList<Ball> AllBalls { get; }
Ball CueBall { get; }
bool AllBallsAtRest();
event Action<BallsAtRestArgs> BallsAtRestEvent;  // see GameEvents args
void BuildRack();                                 // 15 reds + 6 colours + cue
ShotContext BeginShotTracking(ShotContext ctx);   // ctx returned with tracking started
ShotContext EndShotTracking();                    // returns completed context (may be the auto-completed one)
void Respot(Ball ball);                           // own spot, +X scan to 40 steps, fallback last scanned
int RedsRemaining();
bool IsOnTable(Ball ball);                        // activeInHierarchy
Vector3 GetSpot(BallColor color);
bool CueBallNeedsPlacement { get; }
void BeginCueBallPlacement(bool fromHand);
bool TryPlaceCueBall(Vector3 worldPos);           // in-D clamp; false when blocked/illegal
void EndCueBallPlacement();
// additive read-only material sharing for TableBuilder:
PhysicMaterial BallMaterial { get; }  PhysicMaterial BedMaterial { get; }  PhysicMaterial CushionMaterial { get; }
```
Notes: `Respot` always returns the ball to the table (last-scanned fallback). Initial cue-ball-in-hand X (manual) = `(BaulkLineX - MaxX) / 2`. `TryPlaceCueBall` clamps into the D.

### TableBuilder
```csharp
void Build();   // idempotent (guarded); child root named "Table"
// static material helpers shared by Physics + UI:
static Material MakeUrp(string name, Color baseColor, float smoothness, float metallic);
static Material MakeTransparentUnlit(string name, Color color);
```
Colliders with exact names (ball-visible, referenced by prediction): `"Baize"` (box, top face exactly at `BedY`), cushions `Cushion_N/S/E/W`, pocket sensors built by PocketManager.

### PocketManager
```csharp
static readonly Vector3[] Pockets;   // 6 entries, y = BedY + 0.012; order: -X+Z, +X+Z, -X-Z, +X-Z corners, then +Z, -Z middles
bool TryGetPocket(Collider sensor, out int index);   // Dictionary<Collider,int> lookup
```
Builds 6 trigger SphereColliders named `PocketSensor_0..5`; on trigger enter of a ball: `ball.Pot(index)`, record `PottedBallRecord` into `Ball.CurrentContext` (if tracking), raise `GameEvents.PottedBall`, drop animation (scale + deactivate).

### CueController
```csharp
float AimAngle { get; }                      // radians, wrapped -pi..pi
void AdjustAimDelta(float delta);            // batches >= 0.0005 rad into AimChanged
void AimAt(float angle);                     // absolute set (AI / assist)
float Power01 { get; }
void SetPower01(float v);                    // clamped 0..1
Vector2 Spin { get; }                        // (x = side, y = vertical; each -1..1)
void SetSpin(Vector2 spin);
bool CanStrike { get; }                      // ballManager && AllBallsAtRest && !CueBallNeedsPlacement && !striking
bool Fire();                                 // false when !CanStrike; applies power+spin via rigidbody impulse
Vector3 AimDirection();
void ShowCue(bool visible);
```

### TrajectoryPredictor
```csharp
void Show(bool visible);
bool TryGetAssistAngle(BallColor? requiredBall, out float aimAngle);
// requiredBall == null → any object ball is a legal candidate.
// returns winning candidate's aim angle (radians); tracks nearest ghost-ball contact.
```
Visuals: `AimLine` (white α0.55 w0.008 dotted), `ObjectLine` (accent (0.35,0.66,0.48) α0.9 w0.01), `DeflectLine` (white α0.35 w0.008), ghost ring quad.

### RulesManager
```csharp
RuleOutcome Evaluate(ShotContext ctx);       // pure — reads managers, mutates only itself
BallColor? RequiredBall { get; }             // null ⇒ "any colour" (after a legal red) — UI renders null as ANY
```
`RuleOutcome { BallColor? RequiredBall; int FoulPoints; string FoulReason; bool TurnContinues; bool FrameOver; bool RespotBlack; List<BallColor> RespotColors; }`
Rules: reds-and-colours sequence, legal-first-contact check, max(4, ball value) fouls, respot colours, transitional any-colour stroke after the final red, respotted black on ties. Practice bypass: no fouls, reds stay down, colours respot, never FrameOver, `Summary="Practice"`.

### ScoringManager
```csharp
int[] Scores { get; }        // [2]
int CurrentBreak { get; }
int BreakOwner { get; }      // player index or -1
void AwardPot(BallColor color);      // break start/extend logic
void EndBreak();
void AwardFoul(int points, string reason = "");   // to opponent + ScoreChanged + FoulCommitted
void ResetFrameScores();
```

### TurnManager
```csharp
bool CanShootNow { get; }        // state == AwaitingShot (AI turns included — AIController waits on this)
bool IsAITurn { get; }
int CurrentPlayerIndex { get; }
```
States: `Idle → AwaitingPlacement → AwaitingShot → BallsMoving → Resolving (+ FrameOver)`.
Applies outcome: respot loop → AwardFoul/AwardPot → ShotResolved → FrameOver / respot-black / turn decision → `TurnChanged` (carries incoming player, `IsAI`, `Reason`, `CanShoot`, `RequiredBall`).

### MatchManager
```csharp
GameMode Mode { get; }
void StartMatch(MatchRequest request);
void StartNextFrame();
void NotifyRespottedBlack();     // frame continues, frame stats not counted
void ConcedeMatch(int playerIndex);
void RestartMatch();  void RestartFrame();  void ConcedeFrame();
string[] PlayerNames { get; }  int[] FramesWon { get; }  int CurrentFrame { get; }  int Frames { get; }
```

### PracticeController
Registered in practice mode only. Bypasses fouls via RulesManager; keeps table alive across frame end.

### AIController / AIShotPlanner
```csharp
// AIShotPlanner
AIPlan PlanShot(BallColor? requiredBall);   // ghost-ball pot search, cut>80° rejected, safety fallback
struct AIPlan { float AimAngle; float Power01; Vector2 Spin; bool Valid; }
// AIController: TurnChanged → coroutine; 0.9–1.6 s think; D sampling for ball-in-hand;
// waits `TurnManager.CanShootNow`, plans, AimAt + SetPower01 + Fire. 4 tiers (Beginner..Expert), difficulty-scaled error.
```

### InputRouter / BallInHandPlacer / CameraManager
```csharp
// InputRouter (Update): single-finger horizontal drag → AdjustAimDelta(dx * 0.0032 * aimSensitivity);
// gated by TurnManager.CanShootNow && !IsAITurn && !CueBallNeedsPlacement && !EventSystem.IsPointerOverGameObject(fingerId);
// aim assist 0..3 → snap 0/2/5/10°, applied as smooth 25%/frame pull; pinch → CameraManager.SetZoom; two-finger orbit → NudgeOrbit.
// BallInHandPlacer: polls CueBallNeedsPlacement; drag ray→plane(y=BallRestHeight)→TryPlaceCueBall; EndCueBallPlacement on touch end after a legal move.
// CameraManager: states Standard / CloseAim / ShotFollow / TopDown; SetZoom(float 0..1); NudgeOrbit(float deltaYawDeg);
// builds "CameraRig" (Camera+AudioListener, MainCamera tag) when Camera.main missing; mutes duplicate AudioListeners (init-time only);
// clear color #0A0D0B, near 0.01, far 60, HDR; adaptive FOV = 2·atan(tan(21°)·(16/9)/aspect) clamped.
```

### AudioManager
```csharp
void Play(SfxKey key);
void PlayImpact(float speed);    // BallBall, speed→volume/pitch, 0.03 s rate limit
void PlayCushion(float speed);
void StartAmbience();            // 0.05 · master, gated by Settings.ambienceOn
void SetVolumes(float master, float sfx);   // AudioListener.volume + stored sfx
```
`Resources.LoadAll<AudioClip>("Audio")` → `Dictionary<SfxKey,AudioClip>` keyed by clip file names:
`cue_strike, ball_ball, cushion, pocket_drop, pot_chime, foul, frame_win, ui_click, ui_back, ambience_loop` (10 WAVs).
8-source round-robin 2D pool + dedicated ambience loop source.

### ScreenManager / UIInstaller
```csharp
// ScreenManager: builds OverlayCanvas (ScreenSpaceOverlay, order 100, CanvasScaler 1920×1080 match 0.5, GraphicRaycaster)
// + SafeRoot (full stretch + SafeAreaFitter) + fullscreen FadeOverlay (blocks raycasts only during fades).
void ShowToast(string message, ToastType type);
void LoadScene(SceneId id);      // wrapped load with fade; coroutine-safe
// UIInstaller: InstallMatchUI / InstallHomeUI / InstallSettingsUI / InstallProfileUI →
// full-stretch screen root + AddComponent<HudScreen/HomeScreen/SettingsScreen/ProfileScreen>; EnsureCanvas provisions Canvas+Scaler+Raycaster up the ancestor chain.
```

### SaveManager / SettingsService / StatisticsTracker / PerformanceManager
```csharp
// SaveManager (static Instance convenience): dir = Application.persistentDataPath/snooker/
T Load<T>(string key) where T : class, new();    // main → .bak → new T(); never throws
void Save<T>(string key, T data);                // atomic: .tmp → delete old → File.Move; .bak kept
// SaveData: [Serializable] SettingsData + ProfileData, public fields only (JsonUtility-safe)
// SettingsData defaults: masterVolume 0.8, sfxVolume 0.9, ambienceOn true, hapticsOn true,
//   aimAssist 1 (0..3), aimSensitivity 1.0, graphicsQuality 2, targetFps 60, showTrajectory true, powerSliderOnLeft false
// ProfileData: playerName "Player", framesPlayed 0, framesWon 0, ballsPotted 0, highestBreak 0, foulsCommitted 0,
//   totalPoints 0, RecentBreaksCap 12 (newest-first)
// SettingsService: loads in Awake; ApplyLoadedSettings (quality clamp via QualitySettings.names, targetFrameRate,
//   AudioListener.volume, AudioManager.SetVolumes via TryGet); SaveSettings persist + re-apply; 9 Set* helpers with 1 s
//   debounced save coroutine; flush on pause/quit/disable.
// StatisticsTracker: OnEnable subscribe GameEvents; pots (practice-aware via MatchManager?.Mode), highest break,
//   break completion (BreakChanged→0 or FrameEnded; lastBreakValue internal; recentBreaks newest-first cap 12), fouls,
//   frames (FrameEnded with RespotBlack=true does NOT count — frame continues), saves on disable + app pause.
// PerformanceManager: RingBufferFloat window of 120 unscaled deltas (first 30 frames skipped);
//   FpsAverage / FpsLow / FrameMsAverage properties; GetBatteryText / GetDeviceText / GetNetworkText (honest N/A).
```

---

## §3 Event pipeline (GameEvents — static)

Raise methods are the ONLY way events fire (raise methods null-check invocation lists). Args are classes with public fields.

| Event | Args fields |
|---|---|
| `MatchStarted` | Mode, PlayerOne, PlayerTwo, AiDifficulty |
| `FrameStarted` | FrameIndex |
| `ShotStarted` | PlayerIndex, Power01, Spin, AimAngle |
| `AimChanged` | AimAngle (≥0.0005 rad batches) |
| `CueStruck` | Power01, Spin, AimAngle |
| `BallBallHit` | Speed |
| `CushionHit` | Speed |
| `PottedBall` | Color, PocketIndex |
| `BallsAtRest` | Context (ShotContext) |
| `ShotResolved` | Outcome (RuleOutcome) |
| `BreakChanged` | Break |
| `ScoreChanged` | Scores (int[] copy), CurrentBreak, Required (BallColor?) |
| `TurnChanged` | PlayerIndex, IsAI, Reason (TurnReason), CanShoot, RequiredBall |
| `FoulCommitted` | Points, Reason, PlayerIndex (incoming player) |
| `FrameEnded` | WinnerIndex, RespotBlack |
| `MatchEnded` | WinnerIndex |
| `ScreenshotCompleted` | Path |

Pipeline: shot fired → `CueStruck`/`ShotStarted` → physics → per-contact events → balls rest → `BallsAtRest(ctx)` → TurnManager resolves via RulesManager → `ShotResolved` → scoring/turn events → HUD. §3.7: `FrameEnded(RespotBlack=true)` does NOT end the frame or count frame stats.

---

## §4 Table dims & physics tuning (GameConfig — authoritative constants)

```csharp
// TableDims (meters; X along length, Z across width, origin at bed centre, cloth top at y=0 in bed space)
BedLength 3.569f   BedWidth 1.778f   MaxX 1.7845f   MaxZ 0.889f
BallDiameter 0.0525f → BallRadius 0.02625f   BallRestHeight (BallRadius + 0.002 above cloth)
BaulkLineX -1.0475f   DRadius 0.292f
Spots: Black X = MaxX - 0.324, Pink X = MaxX / 2, Blue 0, Brown = BaulkLineX, Green/Brown/Yellow Z = -0.292 / 0 / +0.292 on baulk line
CornerPocketMouth 0.086f   MiddlePocketMouth 0.105f   PocketSensorCorner 0.055f   PocketSensorMiddle 0.065f
CushionHeight 0.036f   WoodFrame 0.14f

// PhysicsTuning
Gravity 9.81f (Vector3.down)   FixedDeltaTime 0.008f
SolverIterations 10   SolverVelocityIterations 6   DefaultContactOffset 0.001f   SleepThreshold 0.02f
BallRestitution 0.95f   BallFriction 0.05f   BallDrag 0.05f   BallAngularDrag 0.05f
BedFriction 0.22f   BedBallBounce 0.6f (PhysicMaterial.Combine) → effective ~0.3
CushionFriction 0.12f   CushionBounce 0.72f
ClothDecel 0.42f (m/s² rolling decel applied when grounded ray hits bed — ray length BallRadius + 0.012 margin)
SpinDecay 2.2f (angular velocity damping multiplier when grounded)
MaxShotSpeed 7.5f (m/s at Power01 = 1)   MaxSpinTorque 0.9f
GroundedRayMargin 0.012f   SettleSpeed 0.03f (below → contributes to rest)
RestTimeoutSeconds 5f (shot force-complete)   MaxStrikeWaitSeconds 5f (TurnManager failsafe)
```

`Ball.FixedUpdate`: grounded ray length = `BallRadius + 0.002 lift + 0.012 margin` (bare 0.012 from the rest origin can never reach the bed — decel/spin decay would never fire). Balls use Rigidbody Interpolate + ContinuousDynamic, cached `PhysicMaterial`.

---

## §5–§8 Product specs (PDF sections mirrored)

- **AI (§5/§15)**: 4 tiers. Score = `1.2·cut + 14·d(cue,ghost) + 10·d(ghost,pocket) + 40·blocked`; power clamp `(0.16 + 0.16·(d1 + 0.8·d2), 0.18, 0.95)`; cut > 80° rejected; dual SphereCastNonAlloc clearance excluding target+cue; safety shot fallback when no pot found; error scaled by difficulty (angle σ 0.000/0.004/0.010/0.020 rad, power σ 0/0.02/0.05/0.08).
- **Input (§6/§10–12)**: drag-to-aim 0.0032 rad/px × sensitivity; power via `PowerSliderControl.Build(int widthPx, int heightPx)` (14 blocks, last 3 lerp→gold, 36px knob); spin via `SpinPadControl.Build(Vector2 anchorPos)` (264px circle, 190px ball disc, 36px accent dot behind invisible 88px handle, `ResetPad/Show/Hide/Toggle`); aim assist OFF/LOW/MEDIUM/HIGH = 0/2/5/10° snap, smooth 25%/frame pull.
- **Camera (§7/§16)**: smooth states Standard/CloseAim/ShotFollow/TopDown; no jumps; transitions ≤ 0.6 s; pinch zoom; two-finger orbit; auto-return to Standard.
- **UI (§8/§17–18)**: charcoal `#141A17` panels α 0.92, muted green accent `#3E7C59`, bright accent `#59A97A`, gold `#D9A441`, text `#F2F5F3`; 9-slice RoundedSprite (64×64, 14px radius SDF AA, border 16); Inter font (OFL) with Roboto/Inter/HelveticaNeue/Arial → LegacyRuntime fallback; Game Tools floating panel (FPS/frame time/network/battery/device, honest N/A, screenshot button, fade+scale open, never blocks controls).
- **Audio (§19)**: restrained/realistic; cue strike, ball-ball, cushion, pocket drop, pot chime, foul, frame win, UI click/back, ambience loop. Speed-scaled impacts.
- **Save (§22)**: JSON via JsonUtility, atomic writes, `.bak`, load fallback chain, never crash on corruption.

---

## §10 Known limitations (v1, honest)

- Free-ball rule, miss rule, explicit colour nomination (any colour after a red), push/double-hit/jump fouls: **not implemented** (documented).
- Practice concession awards a nominal frame to stats.
- Network metric is `"N/A (offline)"` — no networking in v1 (architecture-ready per §23).
