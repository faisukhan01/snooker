# Development Phases — Premium Mobile Snooker

Phase plan mirrored from the PDF master prompt (sections 24). Status reflects the code in this repository.

| Phase | Title | Status |
|---|---|---|
| 01 | Repository analysis and architecture plan | done |
| 02 | Unity/URP/Android project setup | done |
| 03 | Table, balls, cushions, pockets and cue | done |
| 04 | Physics and collision system | done |
| 05 | Aiming, power, spin and touch controls | done |
| 06 | Rules, scoring, turns and fouls | done |
| 07 | AI opponent | done |
| 08 | Professional UI and navigation | done |
| 09 | Performance / Game Tools panel | done |
| 10 | Audio, lighting, effects and animations | done |
| 11 | Optimization and profiling | done |
| 12 | Android testing | pending — requires Unity 2022.3 on a workstation (docs/ANDROID_BUILD.md) |
| 13 | Final visual QA | pending — requires Unity editor (docs/QA_MATRIX.md) |
| 14 | Git commit and verified push | live |

Notes:
- Phases 12/13 cannot be executed inside a headless sandbox (no Unity editor / Android toolchain). Both ship with complete checklists so any workstation can execute them in minutes.
- Phase 14 is the current stage: commits are phase-aligned so `git log` doubles as the delivery record.
