// SnookerKit — persisted data schemas (CONTRACTS §2). JsonUtility-safe: [Serializable] classes with
// public fields only — JsonUtility cannot serialize properties, so this file deliberately uses fields.
using System.Collections.Generic;

namespace SnookerKit
{
    /// <summary>User settings persisted under the "settings" key. Field defaults are the shipped first-run
    /// experience (CONTRACTS §2); <see cref="SettingsService"/> clamps values at apply time.</summary>
    [System.Serializable]
    public class SettingsData
    {
        /// <summary>Master output volume (0..1), applied to AudioListener.volume.</summary>
        public float masterVolume = 0.8f;

        /// <summary>Sound-effect volume (0..1), forwarded to AudioManager.SetVolumes.</summary>
        public float sfxVolume = 0.9f;

        /// <summary>Whether the quiet ambience loop may play.</summary>
        public bool ambienceOn = true;

        /// <summary>Whether haptic feedback (Handheld.Vibrate) is allowed.</summary>
        public bool hapticsOn = true;

        /// <summary>Aim assist strength 0..3 (Off/Low/Medium/High per <see cref="AimAssistLevel"/>).</summary>
        public int aimAssist = 1;

        /// <summary>Aim drag sensitivity multiplier (clamped 0.1..5 at apply time).</summary>
        public float aimSensitivity = 1.0f;

        /// <summary>QualitySettings level index (clamped to QualitySettings.names.Length - 1 at apply time).</summary>
        public int graphicsQuality = 2;

        /// <summary>Application.targetFrameRate (clamped 30..120 at apply time).</summary>
        public int targetFps = 60;

        /// <summary>Whether the aim trajectory/assist line may render.</summary>
        public bool showTrajectory = true;

        /// <summary>Whether the power slider docks on the left edge (false = right).</summary>
        public bool powerSliderOnLeft = false;
    }

    /// <summary>Career profile persisted under the "profile" key. Practice sessions flow into the same record
    /// by design (pots/points/breaks count; frames do not — see <see cref="StatisticsTracker"/>).</summary>
    [System.Serializable]
    public class ProfileData
    {
        /// <summary>Display name shown on the profile screen.</summary>
        public string playerName = "Player";

        /// <summary>Completed (non-respot-black, non-practice) frames played on this device.</summary>
        public int framesPlayed;

        /// <summary>Frames won: human wins vs AI, or any winner in TwoPlayerLocal.</summary>
        public int framesWon;

        /// <summary>Object balls potted (cue balls are fouls, never counted).</summary>
        public int ballsPotted;

        /// <summary>Best completed break.</summary>
        public int highestBreak;

        /// <summary>Foul events committed (one per FoulCommitted event).</summary>
        public int foulsCommitted;

        /// <summary>Sum of pot points (same counting rule as ballsPotted).</summary>
        public int totalPoints;

        /// <summary>Recent completed breaks, newest first, capped at <see cref="RecentBreaksCap"/>.</summary>
        public List<int> recentBreaks = new List<int>();

        /// <summary>Maximum number of entries kept in <see cref="recentBreaks"/>.</summary>
        public const int RecentBreaksCap = 12;
    }
}
