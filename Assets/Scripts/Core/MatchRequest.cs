// SnookerKit — match setup request (frozen, CONTRACTS §2). Menu screens build this; MatchManager consumes it.
namespace SnookerKit
{
    /// <summary>Everything needed to start a match. Player names default sensibly; Frames is best-of.</summary>
    [System.Serializable]
    public class MatchRequest
    {
        /// <summary>Static handoff: Home screen stores the request here before ScreenManager.LoadScene;
        /// MatchSceneRoot consumes and clears it on Start. Never persisted.</summary>
        public static MatchRequest Pending;

        /// <summary>Convenience builder for the common flows.</summary>
        public static MatchRequest Create(GameMode mode, AiDifficulty difficulty, int frames)
        {
            var r = new MatchRequest();
            r.Mode = mode;
            r.Difficulty = difficulty;
            r.Frames = frames;
            if (mode == GameMode.Practice) r.PlayerOne = "Practice";
            return r;
        }

        /// <summary>Playable mode (Practice / MatchVsAI / TwoPlayerLocal / QuickMatch).</summary>
        public GameMode Mode = GameMode.MatchVsAI;

        /// <summary>Display name for player index 0.</summary>
        public string PlayerOne = "Player 1";

        /// <summary>Display name for player index 1 (AI name when Mode = MatchVsAI/QuickMatch).</summary>
        public string PlayerTwo = "Player 2";

        /// <summary>AI strength (ignored in Practice/TwoPlayerLocal).</summary>
        public AiDifficulty Difficulty = AiDifficulty.Intermediate;

        /// <summary>Frames in the match (best-of). Even values play Frames-1; 0/1 = single frame.</summary>
        public int Frames = 1;

        /// <summary>Fills empty names and clamps nonsense so downstream code never null-checks names.</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(PlayerOne)) PlayerOne = "Player 1";
            if (string.IsNullOrEmpty(PlayerTwo)) PlayerTwo = Mode == GameMode.TwoPlayerLocal ? "Player 2" : "AI";
            if (Frames < 0) Frames = 0;
            if (Frames > 35) Frames = 35;
        }
    }
}
