// SnookerKit — authoritative table dimensions and physics tuning (frozen, CONTRACTS §4).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>All gameplay constants in one place. Coordinates: X along table length, Z across width,
    /// origin at bed centre, cloth top at y = 0 in bed space (balls rest at BallRestHeight).</summary>
    public static class GameConfig
    {
        /// <summary>Playing-surface metrics in meters (real snooker proportions, slightly game-scaled).</summary>
        public static class TableDims
        {
            /// <summary>Playing length between cushion noses.</summary>
            public const float BedLength = 3.569f;
            /// <summary>Playing width between cushion noses.</summary>
            public const float BedWidth = 1.778f;
            /// <summary>Half-length — legal X range for a resting ball centre is [-MaxX + r, MaxX - r].</summary>
            public const float MaxX = BedLength / 2f;
            /// <summary>Half-width.</summary>
            public const float MaxZ = BedWidth / 2f;
            /// <summary>Ball radius (52.5 mm ball).</summary>
            public const float BallRadius = 0.02625f;
            /// <summary>Vertical lift of ball origin above the cloth (keeps the collider off the bed to avoid drag spikes).</summary>
            public const float BallLift = 0.002f;
            /// <summary>Rest height of a ball centre above the cloth.</summary>
            public const float BallRestHeight = BallRadius + BallLift;
            /// <summary>X of the baulk line (0.737 m from the baulk cushion face).</summary>
            public const float BaulkLineX = -(MaxX - 0.737f);
            /// <summary>D radius (292 mm).</summary>
            public const float DRadius = 0.292f;
            /// <summary>Black spot: 324 mm from the top cushion face.</summary>
            public const float BlackX = MaxX - 0.324f;
            /// <summary>Pink spot: midway between centre spot and top cushion face.</summary>
            public const float PinkX = MaxX / 2f;
            /// <summary>Z offsets of green/brown/yellow on the baulk line (green toward -Z, yellow toward +Z).</summary>
            public const float BaulkLineZ = DRadius;
            /// <summary>Corner pocket mouth width.</summary>
            public const float CornerPocketMouth = 0.086f;
            /// <summary>Middle pocket mouth width (middles are wider but harder angled).</summary>
            public const float MiddlePocketMouth = 0.105f;
            /// <summary>Trigger sensor radius at corner pockets.</summary>
            public const float PocketSensorCorner = 0.055f;
            /// <summary>Trigger sensor radius at middle pockets.</summary>
            public const float PocketSensorMiddle = 0.065f;
            /// <summary>Cushion rail height above cloth.</summary>
            public const float CushionHeight = 0.036f;
            /// <summary>Wood frame width around the bed.</summary>
            public const float WoodFrame = 0.14f;
        }

        /// <summary>PhysX tuning — every value flows into PhysicsWorldConfig / Ball / TableBuilder so identical shots behave identically.</summary>
        public static class PhysicsTuning
        {
            /// <summary>Gravity magnitude applied as Vector3.down.</summary>
            public const float Gravity = 9.81f;
            /// <summary>Fixed timestep (fine steps keep spin/rolling stable at high cue speeds).</summary>
            public const float FixedDeltaTime = 0.008f;
            /// <summary>Solver iteration counts.</summary>
            public const int SolverIterations = 10;
            /// <summary>Velocity iteration counts.</summary>
            public const int SolverVelocityIterations = 6;
            /// <summary>Rigidbody/collider contact offset.</summary>
            public const float DefaultContactOffset = 0.001f;
            /// <summary>Sleep threshold — small enough that slow last inches of a roll still simulate.</summary>
            public const float SleepThreshold = 0.02f;

            /// <summary>Ball-to-ball restitution.</summary>
            public const float BallRestitution = 0.95f;
            /// <summary>Ball PhysicMaterial friction.</summary>
            public const float BallFriction = 0.05f;
            /// <summary>Linear drag on balls.</summary>
            public const float BallDrag = 0.05f;
            /// <summary>Angular drag on balls.</summary>
            public const float BallAngularDrag = 0.05f;

            /// <summary>Cloth friction on the bed material.</summary>
            public const float BedFriction = 0.22f;
            /// <summary>Bed bounce multiplier (combine mode averages with ball material).</summary>
            public const float BedBallBounce = 0.6f;

            /// <summary>Cushion friction.</summary>
            public const float CushionFriction = 0.12f;
            /// <summary>Cushion restitution.</summary>
            public const float CushionBounce = 0.72f;

            /// <summary>Rolling deceleration (m/s²) applied when the grounded ray reaches the bed.</summary>
            public const float ClothDecel = 0.42f;
            /// <summary>Grounded spin damping multiplier per second when rolling.</summary>
            public const float SpinDecay = 2.2f;
            /// <summary>Cue speed at Power01 = 1 (m/s).</summary>
            public const float MaxShotSpeed = 7.5f;
            /// <summary>Maximum spin torque applied at strike (N·m scale).</summary>
            public const float MaxSpinTorque = 0.9f;

            /// <summary>Grounded-ray margin beyond the rest origin (see Ball.FixedUpdate rationale).</summary>
            public const float GroundedRayMargin = 0.012f;
            /// <summary>Speed below which a moving ball contributes to rest detection.</summary>
            public const float SettleSpeed = 0.03f;
            /// <summary>Force-complete a shot after this many seconds even if something micro-jitters.</summary>
            public const float RestTimeoutSeconds = 5f;
            /// <summary>TurnManager failsafe wait for a strike (AI stalled or input blocked).</summary>
            public const float MaxStrikeWaitSeconds = 5f;
            /// <summary>Required consecutive still frames before BallsAtRest fires.</summary>
            public const int RestConfirmFrames = 6;
        }
    }
}
