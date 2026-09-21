// SnookerKit — ball identity + palette (frozen contract layer).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Snooker ball identities. Cue is included so racks/records can express it, but a potted cue is a foul, never a score.</summary>
    public enum BallColor
    {
        Red = 0,
        Yellow = 1,
        Green = 2,
        Brown = 3,
        Blue = 4,
        Pink = 5,
        Black = 6,
        Cue = 7,
    }

    /// <summary>Static palette: point values, rendered colors and display names for every ball. Frozen per CONTRACTS.md.</summary>
    public static class BallPalette
    {
        /// <summary>Point value of potting the ball (Cue scores 0 — cue pots are fouls).</summary>
        public static int Points(BallColor color)
        {
            switch (color)
            {
                case BallColor.Red: return 1;
                case BallColor.Yellow: return 2;
                case BallColor.Green: return 3;
                case BallColor.Brown: return 4;
                case BallColor.Blue: return 5;
                case BallColor.Pink: return 6;
                case BallColor.Black: return 7;
                default: return 0;
            }
        }

        /// <summary>Slightly lifted RGB for readability under warm table lighting.</summary>
        public static Color Rgb(BallColor color)
        {
            switch (color)
            {
                case BallColor.Red: return new Color(0.78f, 0.08f, 0.07f);
                case BallColor.Yellow: return new Color(0.95f, 0.78f, 0.10f);
                case BallColor.Green: return new Color(0.12f, 0.52f, 0.24f);
                case BallColor.Brown: return new Color(0.42f, 0.25f, 0.10f);
                case BallColor.Blue: return new Color(0.10f, 0.30f, 0.80f);
                case BallColor.Pink: return new Color(0.92f, 0.42f, 0.56f);
                case BallColor.Black: return new Color(0.05f, 0.05f, 0.06f);
                default: return new Color(0.94f, 0.94f, 0.91f); // cue
            }
        }

        /// <summary>Human readable name for HUD/rule text.</summary>
        public static string DisplayName(BallColor color)
        {
            switch (color)
            {
                case BallColor.Red: return "Red";
                case BallColor.Yellow: return "Yellow";
                case BallColor.Green: return "Green";
                case BallColor.Brown: return "Brown";
                case BallColor.Blue: return "Blue";
                case BallColor.Pink: return "Pink";
                case BallColor.Black: return "Black";
                default: return "Cue";
            }
        }

        /// <summary>Object balls in colour sequence order (yellow → black). Reds are handled separately by the rules engine.</summary>
        public static readonly BallColor[] ColourSequence =
        {
            BallColor.Yellow, BallColor.Green, BallColor.Brown,
            BallColor.Blue, BallColor.Pink, BallColor.Black,
        };
    }
}
