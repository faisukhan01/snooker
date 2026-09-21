// SnookerKit — Profile / career-stats screen (Task 16-e rebuild; CONTRACTS §2 SaveSystem, PDF §20).
using UnityEngine;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Profile screen: player name header, 2-column career stats grid and a RECENT BREAKS chip row.
    /// Data comes from SaveManager.Load&lt;ProfileData&gt;("profile") with a null-safe fresh-data fallback.
    /// Self-builds in Start under the full-stretch root provisioned by UIInstaller.</summary>
    public class ProfileScreen : MonoBehaviour
    {
        private static readonly string[] StatCaptions =
        {
            "MATCHES", "FRAMES WON", "BALLS POTTED", "HIGHEST BREAK", "FOULS", "TOTAL POINTS", "AVERAGE BREAK",
        };

        private const int RecentBreaksCap = 12;

        private void Start()
        {
            Build();
        }

        private void Build()
        {
            Image card = UIFactory.Panel(transform, "ProfileCard", UITheme.PanelBg, 0.94f, true);
            Place(card, Center(), Center(), Vector2.zero, new Vector2(1000f, 980f));
            if (card != null) card.raycastTarget = true;
            Transform parent = card != null ? card.transform : transform;

            Place(UIFactory.Label(parent, "PROFILE", 54, UITheme.TextPrimary, TextAnchor.MiddleLeft),
                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(420f, 72f));
            Place(UIFactory.Btn(parent, "BACK", BtnStyle.Ghost, GoBack, 28, new Vector2(190f, 72f)),
                  new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(190f, 72f));

            ProfileData p = LoadProfile();
            string playerName = p != null && !string.IsNullOrEmpty(p.playerName) ? p.playerName : "PLAYER";
            Place(UIFactory.Label(parent, playerName, 40, UITheme.AccentBright, TextAnchor.MiddleCenter),
                  new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(620f, 52f));

            // 2 x 4 stats grid (7 cells used). Values are computed once — this screen is static per visit.
            string[] values =
            {
                IntOrZero(p != null ? p.framesPlayed : 0),
                IntOrZero(p != null ? p.framesWon : 0),
                IntOrZero(p != null ? p.ballsPotted : 0),
                IntOrZero(p != null ? p.highestBreak : 0),
                IntOrZero(p != null ? p.foulsCommitted : 0),
                IntOrZero(p != null ? p.totalPoints : 0),
                AverageBreak(p),
            };
            for (int i = 0; i < values.Length; i++)
            {
                float x = (i % 2) == 0 ? -240f : 240f;
                float y = -270f - (i / 2) * 150f;
                Image cell = UIFactory.Panel(parent, "StatCell" + i, UIFactory.WithAlpha(UITheme.PanelBg, 0.55f), 1f, true);
                Place(cell, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(420f, 140f));
                if (cell != null) cell.raycastTarget = false;
                Transform cellT = cell != null ? cell.transform : parent;

                Place(UIFactory.Label(cellT, values[i], 54, UITheme.TextPrimary, TextAnchor.MiddleCenter),
                      new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(400f, 76f));
                Place(UIFactory.Label(cellT, StatCaptions[i], 23, UITheme.TextMuted, TextAnchor.MiddleCenter),
                      new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(400f, 28f));
            }

            Place(UIFactory.Label(parent, "RECENT BREAKS", 26, UITheme.TextMuted, TextAnchor.MiddleCenter),
                  new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -822f), new Vector2(500f, 32f));

            BuildRecentBreaks(parent, p);
        }

        /// <summary>Renders up to 12 gold break chips (recentBreaks is newest-first per contract).</summary>
        private void BuildRecentBreaks(Transform parent, ProfileData p)
        {
            int count = p != null && p.recentBreaks != null ? Mathf.Min(RecentBreaksCap, p.recentBreaks.Count) : 0;
            if (count == 0)
            {
                Place(UIFactory.Label(parent, "NO BREAKS RECORDED YET", 24, UITheme.TextMuted, TextAnchor.MiddleCenter),
                      new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -878f), new Vector2(600f, 40f));
                return;
            }

            float totalW = count * 64f + (count - 1) * 10f;
            for (int i = 0; i < count; i++)
            {
                float x = -totalW * 0.5f + 32f + i * 74f;
                Image chip = UIFactory.Panel(parent, "BreakChip" + i, UIFactory.WithAlpha(UITheme.Gold, 0.16f), 1f, true);
                Place(chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, -878f), new Vector2(64f, 64f));
                if (chip != null) chip.raycastTarget = false;
                Transform chipT = chip != null ? chip.transform : parent;
                Place(UIFactory.Label(chipT, p.recentBreaks[i].ToString(), 26, UITheme.Gold, TextAnchor.MiddleCenter),
                      new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 58f));
            }
        }

        /// <summary>Defensive load: SaveManager is contract-guaranteed never to throw, but the screen must
        /// survive even a missing service (fresh ProfileData fallback, CONTRACTS §0.5).</summary>
        private static ProfileData LoadProfile()
        {
            try
            {
                SaveManager sm = SaveManager.Instance;
                if (sm != null)
                {
                    ProfileData p = sm.Load<ProfileData>("profile");
                    if (p != null) return p;
                }
            }
            catch
            {
                // Fall through to a fresh profile — never crash on corrupted/absent saves.
            }
            return new ProfileData();
        }

        private static string IntOrZero(int value)
        {
            return value.ToString();
        }

        private static string AverageBreak(ProfileData p)
        {
            if (p == null) return 0f.ToString("F1");
            return ((float)p.totalPoints / Mathf.Max(1, p.framesPlayed)).ToString("F1");
        }

        private void GoBack()
        {
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm))
            {
                sm.LoadScene(SceneId.Home);
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneId.Home.ToString());
        }

        private static Vector2 Center()
        {
            return new Vector2(0.5f, 0.5f);
        }

        private static T Place<T>(T g, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size) where T : Component
        {
            if (g == null) return null;
            RectTransform rt = g.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = anchor;
                rt.anchorMax = anchor;
                rt.pivot = pivot;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }
            return g;
        }
    }
}
