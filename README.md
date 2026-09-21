# Premium Mobile Snooker

A premium, realistic, fully playable mobile snooker game for Android built with **Unity 2022.3 LTS + C#**.

Real PhysX table physics, a complete snooker rules engine, a 4-tier AI opponent, touch-first controls
(drag-to-aim, power meter, spin pad), professional uGUI presentation, an honest performance/Game Tools
panel, persistent profiles/statistics and a synthesized audio set — in one coherent, code-driven visual
system. No TextMeshPro, no Input System package, no fabricated metrics: unavailable device values
render as **N/A**.

## Highlights

| Area | What you get |
|---|---|
| Physics | Real rigidbody ball motion (rolling decay, spin, cushion/pocket response), consistent tuning (`GameConfig.PhysicsTuning`) |
| Rules | Reds-and-colours sequencing, legal-contact fouls with max(4, value) penalties, respots, breaks, respotted black on ties |
| AI | Beginner / Intermediate / Advanced / Expert — ghost-ball shot search, safety play, difficulty-scaled error, no cheating |
| Controls | Touch drag aiming with optional assist (OFF/LOW/MEDIUM/HIGH), power slider, circular spin pad, ball-in-hand placement |
| UI | Boot / Home / HUD / Settings / Profile / Match screens with safe-area support and screen transitions |
| Tools | Floating Game Tools panel: FPS, frame time, battery, device info, screenshot — honest N/A where unavailable |
| Saves | Atomic JSON persistence with `.bak` recovery; settings, profile, statistics |
| Audio | 10 synthesized WAV cues (strike, impacts, pockets, UI, ambience) — restrained and realistic |

## Getting started

1. Install **Unity 2022.3 LTS** (Android Build Support + OpenJDK + SDK/NDDK tools).
2. `git clone https://github.com/faisukhan01/snooker.git`
3. Unity Hub → **Add** → select the cloned `snooker` folder → open with 2022.3.
4. Open `Assets/Scenes/Boot.unity` and press Play, or open any scene directly
   (`Home`, `Match`, `Practice`, `Settings`, `Profile`).
5. Android build: **File → Build Settings → Android → Switch Platform**, then follow
   `docs/ANDROID_BUILD.md` (IL2CPP / ARM64 settings are pre-configured in `ProjectSettings`).

## Documentation

- `docs/CONTRACTS.md` — module contracts: the architecture's single source of truth
- `docs/PHASES.md` — the 14 development phases and their status
- `docs/ANDROID_BUILD.md` — step-by-step Android build & test guide
- `docs/QA_MATRIX.md` — full manual test matrix
- `docs/CONTROLS.md` — touch controls reference

## Project layout

```
Assets/
  Scenes/        Boot, Home, Match, Practice, Settings, Profile
  Scripts/       Core, Physics, Rules, Scoring, Gameplay, AI, Input, Camera,
                 UI, Audio, SaveSystem, Performance, Utilities (namespace SnookerKit)
  Editor/        ProjectBootstrapper (one-menu project setup: URP, quality, Android, audio import)
  Resources/     Audio (10 synthesized WAVs)
  Fonts/         Inter (OFL) drop-in folder with runtime fallback
  Art/ Materials/ Prefabs/ Captures/   structured swap folders with READMEs
docs/            contracts, phases, build, QA, controls
tools/           gen-scenes.mjs · gen-meta.mjs · gen-audio.mjs (deterministic generators)
```

## Architecture

Composition-root style: each scene contains a single `*SceneRoot` MonoBehaviour that
builds and wires every manager at runtime through a static `ServiceRegistry`.
Systems communicate through `GameEvents` (typed args, raise methods only).
See `docs/CONTRACTS.md` for every pinned API surface.

## Limitations (v1)

- Free-ball rule, miss rule, explicit colour nomination, push/double-hit/jump fouls are not implemented.
- Tournament and online multiplayer are architecture-ready but not built (offline gameplay first, per spec).
- Network metric in Game Tools shows `N/A (offline)`.

## License

Source is provided for the repository owner's use. Inter font ships under the SIL Open Font License
(see `Assets/Fonts/OFL.txt`).
