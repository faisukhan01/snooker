// SnookerKit — Home / brand screen (Task 16-e rebuild; CONTRACTS §8/§17, PDF §21).
using UnityEngine;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Brand home screen: PLAY NOW, mode grid, AI difficulty and BEST OF selectors.
    /// Self-builds its whole layout in Start under the full-stretch root provisioned by UIInstaller.
    /// Selections persist for the session (static) and flow into MatchRequest.Pending before navigation.</summary>
    public class HomeScreen : MonoBehaviour
    {
        private static readonly string[] DifficultyNames = { "BEGINNER", "INTERMEDIATE", "ADVANCED", "EXPERT" };
        private static readonly int[] FrameChoices = { 1, 3, 5, 7 };

        // Session-persistent menu selections (survive scene navigation within one app run).
        private static AiDifficulty _difficulty = AiDifficulty.Intermediate;
        private static int _frames = 3;

        private readonly Image[] _diffImages = new Image[4];
        private readonly Text[] _diffTexts = new Text[4];
        private readonly Image[] _frameImages = new Image[4];
        private readonly Text[] _frameTexts = new Text[4];

        private Color _ghostNormal = new Color(1f, 1f, 1f, 0.06f);
        private Color _ghostText = new Color(1f, 1f, 1f, 0.9f);

        private void Start()
        {
            Build();
            RefreshSelectors();
        }

        private void Build()
        {
            _ghostNormal = BtnStyle.Ghost.Normal;
            _ghostText = BtnStyle.Ghost.Text;

            Image bg = UIFactory.Panel(transform, "HomeBackground", UITheme.PanelBg, 1f, false);
            Stretch(bg);
            if (bg != null) bg.raycastTarget = true;

            Place(UIFactory.Label(transform, "SNOOKER", 118, UITheme.TextPrimary, TextAnchor.MiddleCenter),
                  Center(), Center(), new Vector2(0f, 400f), new Vector2(1000f, 150f));

            Image underline = UIFactory.Panel(transform, "Underline", UITheme.Gold, 1f, false);
            Place(underline, Center(), Center(), new Vector2(0f, 316f), new Vector2(360f, 6f));
            if (underline != null) underline.raycastTarget = false;

            Place(UIFactory.Label(transform, "PREMIUM MOBILE SNOOKER", 24, UITheme.TextMuted, TextAnchor.MiddleCenter),
                  Center(), Center(), new Vector2(0f, 282f), new Vector2(700f, 34f));

            Place(UIFactory.Btn(transform, "PLAY NOW", BtnStyle.Primary, delegate { Launch(GameMode.MatchVsAI); }, 46, new Vector2(520f, 112f)),
                  Center(), Center(), new Vector2(0f, 178f), new Vector2(520f, 112f));

            Place(UIFactory.Btn(transform, "PRACTICE", BtnStyle.Dark, delegate { Launch(GameMode.Practice); }, 30, new Vector2(310f, 92f)),
                  Center(), Center(), new Vector2(-340f, 40f), new Vector2(310f, 92f));
            Place(UIFactory.Btn(transform, "QUICK MATCH", BtnStyle.Dark, delegate { Launch(GameMode.QuickMatch); }, 30, new Vector2(310f, 92f)),
                  Center(), Center(), new Vector2(0f, 40f), new Vector2(310f, 92f));
            Place(UIFactory.Btn(transform, "TWO PLAYER", BtnStyle.Dark, delegate { Launch(GameMode.TwoPlayerLocal); }, 30, new Vector2(310f, 92f)),
                  Center(), Center(), new Vector2(340f, 40f), new Vector2(310f, 92f));
            Place(UIFactory.Btn(transform, "PROFILE", BtnStyle.Dark, delegate { Navigate(SceneId.Profile); }, 30, new Vector2(310f, 92f)),
                  Center(), Center(), new Vector2(-170f, -92f), new Vector2(310f, 92f));
            Place(UIFactory.Btn(transform, "SETTINGS", BtnStyle.Dark, delegate { Navigate(SceneId.Settings); }, 30, new Vector2(310f, 92f)),
                  Center(), Center(), new Vector2(170f, -92f), new Vector2(310f, 92f));

            Place(UIFactory.Label(transform, "AI LEVEL", 24, UITheme.TextMuted, TextAnchor.MiddleCenter),
                  Center(), Center(), new Vector2(0f, -178f), new Vector2(400f, 30f));
            for (int i = 0; i < 4; i++)
            {
                int idx = i; // closure capture
                Button b = UIFactory.Btn(transform, DifficultyNames[i], BtnStyle.Ghost, delegate { SelectDifficulty(idx); }, 24, new Vector2(250f, 62f));
                Place(b, Center(), Center(), new Vector2(-411f + i * 274f, -238f), new Vector2(250f, 62f));
                _diffImages[i] = ButtonImage(b);
                _diffTexts[i] = ButtonText(b);
            }

            Place(UIFactory.Label(transform, "BEST OF", 24, UITheme.TextMuted, TextAnchor.MiddleCenter),
                  Center(), Center(), new Vector2(0f, -312f), new Vector2(400f, 30f));
            for (int i = 0; i < 4; i++)
            {
                int idx = i; // closure capture
                Button b = UIFactory.Btn(transform, FrameChoices[i].ToString(), BtnStyle.Ghost, delegate { SelectFrames(idx); }, 28, new Vector2(150f, 62f));
                Place(b, Center(), Center(), new Vector2(-261f + i * 174f, -372f), new Vector2(150f, 62f));
                _frameImages[i] = ButtonImage(b);
                _frameTexts[i] = ButtonText(b);
            }
        }

        /// <summary>Builds the request, parks it in MatchRequest.Pending and navigates to the mode's scene.</summary>
        private void Launch(GameMode mode)
        {
            int frames = mode == GameMode.QuickMatch ? 1 : _frames;
            MatchRequest.Pending = MatchRequest.Create(mode, _difficulty, frames);
            Navigate(mode == GameMode.Practice ? SceneId.Practice : SceneId.Match);
        }

        /// <summary>Navigates via ScreenManager with a direct scene-load fallback.</summary>
        private void Navigate(SceneId id)
        {
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm))
            {
                sm.LoadScene(id);
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(id.ToString());
        }

        private void SelectDifficulty(int index)
        {
            _difficulty = (AiDifficulty)index;
            RefreshSelectors();
        }

        private void SelectFrames(int index)
        {
            _frames = FrameChoices[index];
            RefreshSelectors();
        }

        /// <summary>Selected rows get the bright accent tint; the rest stay ghost.</summary>
        private void RefreshSelectors()
        {
            int selDiff = (int)_difficulty;
            for (int i = 0; i < 4; i++)
            {
                bool on = i == selDiff;
                if (_diffImages[i] != null) _diffImages[i].color = on ? UIFactory.WithAlpha(UITheme.AccentBright, 0.92f) : _ghostNormal;
                if (_diffTexts[i] != null) _diffTexts[i].color = on ? UITheme.PanelBg : _ghostText;
            }
            for (int i = 0; i < 4; i++)
            {
                bool on = FrameChoices[i] == _frames;
                if (_frameImages[i] != null) _frameImages[i].color = on ? UIFactory.WithAlpha(UITheme.AccentBright, 0.92f) : _ghostNormal;
                if (_frameTexts[i] != null) _frameTexts[i].color = on ? UITheme.PanelBg : _ghostText;
            }
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

        private static void Stretch(Component g)
        {
            if (g == null) return;
            RectTransform rt = g.transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image ButtonImage(Button b)
        {
            if (b == null) return null;
            Image img = b.image;
            if (img == null) img = b.GetComponent<Image>();
            return img;
        }

        private static Text ButtonText(Button b)
        {
            return b != null ? b.GetComponentInChildren<Text>(true) : null;
        }
    }
}
