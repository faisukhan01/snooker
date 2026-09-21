// SnookerKit — one-shot global physics tuning (Physics module, CONTRACTS §4).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Applies the frozen PhysX tuning from GameConfig.PhysicsTuning exactly once per app run.
    /// Scene roots add one instance of this component; the statically-guarded Awake makes duplicate
    /// instances and scene reloads harmless. Must run before any ball simulates (execution order -900).</summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public class PhysicsWorldConfig : MonoBehaviour
    {
        /// <summary>True once the global physics state has been configured for this app run.</summary>
        private static bool _applied;

        /// <summary>Applies gravity, fixed timestep, solver iteration counts, contact offset and sleep
        /// threshold. Guarded by a static flag so the values are written exactly once per app run.</summary>
        private void Awake()
        {
            if (_applied) return;
            _applied = true;

            Physics.gravity = Vector3.down * GameConfig.PhysicsTuning.Gravity;
            Time.fixedDeltaTime = GameConfig.PhysicsTuning.FixedDeltaTime;
            Physics.defaultSolverIterations = GameConfig.PhysicsTuning.SolverIterations;
            Physics.defaultSolverVelocityIterations = GameConfig.PhysicsTuning.SolverVelocityIterations;
            Physics.defaultContactOffset = GameConfig.PhysicsTuning.DefaultContactOffset;
            Physics.sleepThreshold = GameConfig.PhysicsTuning.SleepThreshold;
        }
    }
}
