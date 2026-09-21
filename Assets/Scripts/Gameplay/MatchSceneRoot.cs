// SnookerKit — Match/Practice scene composition root (CONTRACTS §1/§2). The single place where the world
// (physics, table, balls, cue, camera), gameplay managers, input, AI and UI are assembled for BOTH the
// Match and Practice scenes, then handed the pending MatchRequest.
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Composition root for the Match and Practice scenes. Start() consumes
    /// <see cref="MatchRequest.Pending"/> (falling back to Practice when absent) and builds the scene in the
    /// frozen order: persistent services → World children (PhysicsWorldConfig, BallManager, TableBuilder —
    /// built explicitly AFTER BallManager so the table shares its PhysicMaterials — PocketManager,
    /// CueController, TrajectoryPredictor, CameraManager) → gameplay managers → input → AI (match modes only)
    /// → UI → MatchManager.StartMatch. Every step is defensive: failures log at most one warning and
    /// composition never throws.</summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class MatchSceneRoot : MonoBehaviour
    {
        private void Start()
        {
            try
            {
                Compose();
            }
            catch (System.Exception e)
            {
                // Scene composition must never take the app down (CONTRACTS §0.5).
                Debug.LogWarning("MatchSceneRoot: scene composition failed (" + e.Message + ").");
            }
        }

        /// <summary>Full scene assembly in the pinned order. Child order of "World" is critical: BallManager
        /// must exist before TableBuilder.Build() runs, otherwise the table silently loses its shared bed
        /// materials (real v0 bug).</summary>
        private void Compose()
        {
            // 1) Persistent services (SaveManager, SettingsService, PerformanceManager, StatisticsTracker).
            AppServices.Ensure();

            // 2) Consume the handoff request; direct-scene launches get an endless practice session.
            MatchRequest request = MatchRequest.Pending;
            MatchRequest.Pending = null; // clear the static after reading — never consumed twice
            if (request == null)
            {
                request = MatchRequest.Create(GameMode.Practice, AiDifficulty.Intermediate, 0);
            }

            // 3) World children — order is critical (see class doc).
            GameObject world = new GameObject("World");
            world.transform.SetParent(transform, false);

            Attach<PhysicsWorldConfig>(world);
            Attach<BallManager>(world);
            TableBuilder table = Attach<TableBuilder>(world);
            Attach<PocketManager>(world);
            Attach<CueController>(world);
            Attach<TrajectoryPredictor>(world);
            Attach<CameraManager>(world);

            if (table != null)
            {
                table.Build(); // explicit + idempotent (guarded inside TableBuilder)
            }
            else
            {
                Debug.LogWarning("MatchSceneRoot: TableBuilder missing — scene continues without a table mesh.");
            }

            // 4) Gameplay managers. PracticeController exists only in practice mode; MatchManager checks
            //    Mode before delegating frame endings to it.
            Attach<RulesManager>(world);
            Attach<ScoringManager>(world);
            Attach<TurnManager>(world);
            MatchManager match = Attach<MatchManager>(world);
            if (request.Mode == GameMode.Practice)
            {
                Attach<PracticeController>(world);
            }

            // 5) Input (router gates on TurnManager.CanShootNow; placer handles ball-in-hand).
            Attach<InputRouter>(world);
            Attach<BallInHandPlacer>(world);

            // 6) AI opponent for the AI modes only (TwoPlayerLocal is two humans on one device).
            if (request.Mode == GameMode.MatchVsAI || request.Mode == GameMode.QuickMatch)
            {
                Attach<AIController>(world);
            }

            // 7) UI. ScreenManager is created by the menu scene roots and persists with AppServices; when it
            //    is absent (direct scene launch) log one warning and continue — the HUD still installs.
            if (!ServiceRegistry.TryGet<ScreenManager>(out ScreenManager _))
            {
                Debug.LogWarning("MatchSceneRoot: ScreenManager unavailable — continuing without scene-navigation UI.");
            }

            GameObject ui = new GameObject("UI");
            ui.transform.SetParent(transform, false);
            UIInstaller installer = Attach<UIInstaller>(ui);
            if (installer != null) installer.InstallMatchUI();

            // 8) Kick off the match/frame pipeline (resets scoring, raises MatchStarted, builds the rack).
            if (match != null) match.StartMatch(request);
        }

        /// <summary>Adds a component to the given host and returns it (null-checked by callers). Failures log
        /// one warning instead of throwing.</summary>
        private static T Attach<T>(GameObject host) where T : Component
        {
            T component = host.AddComponent<T>();
            if (component == null)
            {
                Debug.LogWarning("MatchSceneRoot: could not add " + typeof(T).Name + ".");
            }
            return component;
        }
    }
}
