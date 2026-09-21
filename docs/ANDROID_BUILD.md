# Android Build & Test Guide (Phase 12)

Phase 12 requires a workstation with Unity 2022.3 LTS — the sandbox that produced this repository
cannot run the Unity editor or the Android toolchain. Everything else is pre-configured; this guide
takes a fresh workstation from clone to installed APK in about 15 minutes.

## 1. Prerequisites

- Unity 2022.3 LTS with modules: **Android Build Support**, **OpenJDK**, **Android SDK & NDK Tools**.
- An Android device (API 23+) with USB debugging, or an emulator with ARM64 images.
- This repository cloned locally.

## 2. Open & bootstrap

1. Unity Hub → **Add** → select the cloned `snooker` folder → open with 2022.3.
2. Menu **Snooker → Run Full Bootstrap** (or `Assets/Editor/ProjectBootstrapper.cs` menu). This applies:
   - URP pipeline asset (`Assets/Settings/SnookerURP.asset`)
   - Quality levels Low / Medium / High / Ultra (default High)
   - Android: IL2CPP, ARM64 + ARMv7, landscape, minSdk 23, package `com.faisukhan01.snooker`
   - Audio import defaults, 6-scene build settings
   Re-running is always safe (every step is idempotent).

## 3. Editor play test (before touching a device)

- Open `Assets/Scenes/Boot.unity` → press **Play** → expect Home → Match flows.
- In the **Match** scene verify: aiming (drag), power meter, spin pad, AI turn (watch the camera),
  pot/chime/foul audio, pause menu, Game Tools panel metrics.
- Anything broken belongs to Phase 13 QA first — see `docs/QA_MATRIX.md`.

## 4. Build the APK

1. **File → Build Settings → Android → Switch Platform**.
2. Scenes `Boot → Home → Match → Practice → Settings → Profile` should already be listed (verify).
3. Player Settings sanity (bootstrap did these): IL2CPP · ARM64 · IL2CPP release · landscape.
4. **Build** → `snooker-dev.apk` (or Build & Run straight to the device).
5. First IL2CPP build takes several minutes — this is normal.

## 5. On-device test pass

Run the on-device slice of `docs/QA_MATRIX.md`:

| Area | Pass criteria |
|---|---|
| Install/launch | APK installs, app launches to Boot → Home without crash |
| Touch | Drag aim, power meter, spin pad all respond at 60 Hz-class latency |
| Performance | Game Tools FPS matches the device tier (60 on flagships, stable 30+ on budget) |
| Game Tools | Battery/device show real values or honest N/A — never fabricated |
| Save | Change settings, kill the app, relaunch → settings persisted |
| Lifecycle | Home/pause/resume, screen-off/lock/unlock mid-frame: game recovers cleanly |

## 6. Known limitations to verify around

Documented in `README.md` and `docs/CONTRACTS.md` §10: free-ball rule, miss rule, explicit colour
nomination, push/double-hit/jump fouls are **not implemented** in v1. Do not file these as build
failures — they are roadmap items.

## 7. AAB (Play Store) variant

**File → Build Settings → Android → Build App Bundle** with the same settings; signing is the
repository owner's standard Play keystore flow (never commit keystores or credentials).
