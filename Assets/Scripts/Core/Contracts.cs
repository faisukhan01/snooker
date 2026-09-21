// SnookerKit — cross-module data contracts (frozen, CONTRACTS §2/§3).
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>One potted ball recorded into the active shot context (PocketManager is the only writer).</summary>
    public class PottedBallRecord
    {
        public BallColor Color;
        public int PocketIndex;
    }

    /// <summary>Everything known about the shot currently being tracked. BallManager.BeginShotTracking starts it,
    /// Ball/PocketManager record into it, BallsAtRest carries it to TurnManager. Single source of truth.</summary>
    public class ShotContext
    {
        /// <summary>Player index that played the shot.</summary>
        public int PlayerIndex;
        /// <summary>Power at strike (0..1).</summary>
        public float Power01;
        /// <summary>Spin at strike (side, vertical; each -1..1).</summary>
        public Vector2 Spin;
        /// <summary>Aim angle at strike (radians).</summary>
        public float AimAngle;
        /// <summary>Cue-ball origin at strike.</summary>
        public Vector3 CueOrigin;

        /// <summary>Balls potted this stroke in pot order (cue pots recorded too, as fouls — but see BallPotted note).</summary>
        public readonly List<PottedBallRecord> Potted = new List<PottedBallRecord>();
        /// <summary>First object ball contacted this stroke (null = no contact / foul by rules).</summary>
        public BallColor? FirstContact;
        /// <summary>Cue ball finished in a pocket.</summary>
        public bool CuePotted;
        /// <summary>Cue ball left the table (jumped off).</summary>
        public bool CueOffTable;
        /// <summary>True once BallsAtRest has been raised for this context (defensive double-resolve guard).</summary>
        public bool Completed;

        /// <summary>True when any object ball was potted (cue pots do not count — they are fouls).</summary>
        public bool HasObjectPot
        {
            get
            {
                for (int i = 0; i < Potted.Count; i++)
                {
                    if (Potted[i].Color != BallColor.Cue) return true;
                }
                return false;
            }
        }
    }

    /// <summary>Rules engine verdict for one completed stroke (pure data — RulesManager writes, TurnManager applies).</summary>
    public class RuleOutcome
    {
        /// <summary>Ball-on for the next stroke; null means "any colour" after a legal red (UI renders ANY).</summary>
        public BallColor? RequiredBall;
        /// <summary>Penalty points this stroke (0 = legal, else max(4, involved value)).</summary>
        public int FoulPoints;
        /// <summary>Human readable foul reason for HUD/toast ("" when legal).</summary>
        public string FoulReason = "";
        /// <summary>Same player continues (potted legally).</summary>
        public bool TurnContinues;
        /// <summary>Frame ended with this stroke (no respot-black tie).</summary>
        public bool FrameOver;
        /// <summary>Frame tied → respot black and continue (frame stats not counted, CONTRACTS §3.7).</summary>
        public bool RespotBlack;
        /// <summary>Colours that must respot before the next stroke.</summary>
        public readonly List<BallColor> RespotColors = new List<BallColor>();
    }
}
