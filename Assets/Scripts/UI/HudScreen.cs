// SnookerKit — Match/Practice HUD (Task 16 rebuild; CONTRACTS §2/§8, PDF §8/§11/§12/§14).
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>In-match heads-up display: player bar with scores/break/required ball, spin pad, power meter,
    /// camera/menu buttons, pause panel, frame/match result overlays. Self-builds in Start under the root
    /// provisioned by <see cref="UIInstaller.InstallMatchUI"/>. All state updates are event-driven (no polling),
    /// every subscription paired OnEnable/OnDisable, zero per-frame allocations.</summary>
    public class HudScreen : MonoBehaviour
    {
        // Top bar widgets (cached — updated from events only).
        private Text _p1Name, _p1Score, _p2Name, _p2Score, _frameChip, _breakLabel, _requiredLabel;
        private Image _p1Edge, _p2Edge;

        // Bottom controls.
        private PowerSliderControl _power;
        private SpinPadControl _spin;
        private CanvasGroup _spinGroup;
        private Text _cameraLabel;

        // Overlays.
        private GameObject _pausePanel;
        private GameObject _bannerPanel;
        private Text _bannerText;
        private GameObject _resultPanel;
        private Text _resultText;

        private bool _menuOpen;
        private Coroutine _bannerRoutine;

        private void Start()
        {
            BuildTopBar();
            BuildBottomControls();
            BuildPausePanel();
            BuildBanner();
            BuildResultPanel();
            RefreshFromManagers();
        }

        private void OnEnable()
        {
            GameEvents.ScoreChanged += OnScoreChanged;
            GameEvents.TurnChanged += OnTurnChanged;
            GameEvents.ShotStarted += OnShotStarted;
            GameEvents.FoulCommitted += OnFoulCommitted;
            GameEvents.FrameEnded += OnFrameEnded;
            GameEvents.MatchEnded += OnMatchEnded;
        }

        private void OnDisable()
        {
            GameEvents.ScoreChanged -= OnScoreChanged;
            GameEvents.TurnChanged -= OnTurnChanged;
            GameEvents.ShotStarted -= OnShotStarted;
            GameEvents.FoulCommitted -= OnFoulCommitted;
            GameEvents.FrameEnded -= OnFrameEnded;
            GameEvents.MatchEnded -= OnMatchEnded;
            if (_bannerRoutine != null) { StopCoroutine(_bannerRoutine); _bannerRoutine = null; }
        }

        // ------------------------------------------------------------------ build

        private void BuildTopBar()
        {
            Image bar = UIFactory.Panel(transform, "PlayerBar", UITheme.PanelBg, 0.92f, true);
            Anchor(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), new Vector2(-48f, 122f));

            _p1Name = UIFactory.Label(bar.transform, "PLAYER 1", 30, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            Anchor(_p1Name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -30f), new Vector2(360f, 40f));
            _p1Score = UIFactory.Label(bar.transform, "0", 58, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            Anchor(_p1Score, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -74f), new Vector2(160f, 66f));

            _frameChip = UIFactory.Label(bar.transform, "FRAME 1", 28, UITheme.TextMuted, TextAnchor.MiddleCenter);
            Anchor(_frameChip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(300f, 36f));
            _requiredLabel = UIFactory.Label(bar.transform, "ON: RED", 30, UITheme.Gold, TextAnchor.MiddleCenter);
            Anchor(_requiredLabel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(300f, 40f));

            _p2Name = UIFactory.Label(bar.transform, "PLAYER 2", 30, UITheme.TextPrimary, TextAnchor.MiddleRight);
            Anchor(_p2Name, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -30f), new Vector2(360f, 40f));
            _p2Score = UIFactory.Label(bar.transform, "0", 58, UITheme.TextPrimary, TextAnchor.MiddleRight);
            Anchor(_p2Score, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -74f), new Vector2(160f, 66f));

            // Active-player accent edges (enabled/disabled, never re-colored per frame).
            _p1Edge = UIFactory.Panel(bar.transform, "P1Edge", UITheme.AccentBright, 1f, false);
            Anchor(_p1Edge, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(10f, 10f), new Vector2(8f, 102f));
            _p2Edge = UIFactory.Panel(bar.transform, "P2Edge", UITheme.AccentBright, 1f, false);
            Anchor(_p2Edge, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 10f), new Vector2(8f, 102f));

            _breakLabel = UIFactory.Label(transform, "", 34, UITheme.Gold, TextAnchor.MiddleCenter);
            Anchor(_breakLabel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(420f, 44f));
        }

        private void BuildBottomControls()
        {
            // Spin pad (bottom-left) and power meter (bottom-right) via the frozen widget APIs.
            _spin = gameObject.AddComponent<SpinPadControl>();
            GameObject spinRoot = _spin.Build(new Vector2(44f, 44f));
            if (spinRoot != null)
            {
                _spinGroup = spinRoot.GetComponent<CanvasGroup>();
                if (_spinGroup == null) _spinGroup = spinRoot.AddComponent<CanvasGroup>();
            }
            _spin.SpinChanged += OnSpinChanged;

            _power = gameObject.AddComponent<PowerSliderControl>();
            _power.Build(640, 130);
            RectTransform prt = _power.transform as RectTransform;
            if (prt != null)
            {
                prt.anchorMin = new Vector2(1f, 0f);
                prt.anchorMax = new Vector2(1f, 0f);
                prt.pivot = new Vector2(1f, 0f);
                prt.anchoredPosition = new Vector2(-44f, 44f);
            }
            _power.PowerChanged += OnPowerChanged;
            _power.PowerCommitted += OnPowerCommitted;

            // Bottom-centre compact buttons: CAMERA · SPIN · MENU.
            Button cam = UIFactory.Btn(transform, "CAMERA", BtnStyle.Dark, delegate { CycleCamera(); }, 24, new Vector2(210f, 72f));
            Anchor(cam, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-250f, 52f), new Vector2(210f, 72f));
            _cameraLabel = ButtonText(cam);

            Button spinBtn = UIFactory.Btn(transform, "SPIN", BtnStyle.Dark, delegate { if (_spin != null) _spin.Toggle(); }, 24, new Vector2(210f, 72f));
            Anchor(spinBtn, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(210f, 72f));

            Button menu = UIFactory.Btn(transform, "MENU", BtnStyle.Dark, delegate { ToggleMenu(); }, 24, new Vector2(210f, 72f));
            Anchor(menu, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(250f, 52f), new Vector2(210f, 72f));
        }

        private void BuildPausePanel()
        {
            _pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            _pausePanel.transform.SetParent(transform, false);
            Image backdrop = _pausePanel.GetComponent<Image>();
            backdrop.color = UIFactory.WithAlpha(Color.black, 0.55f);
            Stretch(_pausePanel.transform as RectTransform);

            Image card = UIFactory.Panel(_pausePanel.transform, "PauseCard", UITheme.PanelBg, 1f, true);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 520f));

            UIFactory.Label(card.transform, "PAUSED", 44, UITheme.TextPrimary, TextAnchor.MiddleCenter);
            Anchor(card.transform.Find("Label") as RectTransform ?? card.transform as RectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(500f, 60f));

            Button resume = UIFactory.Btn(card.transform, "RESUME", BtnStyle.Primary, delegate { ToggleMenu(); }, 30, new Vector2(440f, 84f));
            Anchor(resume, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(440f, 84f));
            Button restart = UIFactory.Btn(card.transform, "RESTART FRAME", BtnStyle.Dark, delegate { RestartFrame(); }, 28, new Vector2(440f, 84f));
            Anchor(restart, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(440f, 84f));
            Button concede = UIFactory.Btn(card.transform, "CONCEDE FRAME", BtnStyle.Dark, delegate { ConcedeFrame(); }, 28, new Vector2(440f, 84f));
            Anchor(concede, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -104f), new Vector2(440f, 84f));
            Button quit = UIFactory.Btn(card.transform, "QUIT TO HOME", BtnStyle.Ghost, delegate { QuitToHome(); }, 28, new Vector2(440f, 84f));
            Anchor(quit, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -204f), new Vector2(440f, 84f));

            _pausePanel.SetActive(false);
        }

        private void BuildBanner()
        {
            _bannerPanel = new GameObject("BannerPanel", typeof(RectTransform), typeof(Image));
            _bannerPanel.transform.SetParent(transform, false);
            Image backdrop = _bannerPanel.GetComponent<Image>();
            backdrop.color = Color.clear;
            backdrop.raycastTarget = false;
            Stretch(_bannerPanel.transform as RectTransform);

            Image card = UIFactory.Panel(_bannerPanel.transform, "BannerCard", UITheme.PanelBg, 1f, true);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(720f, 150f));
            card.raycastTarget = false;
            _bannerText = UIFactory.Label(card.transform, "", 42, UITheme.Gold, TextAnchor.MiddleCenter);
            Anchor(_bannerText, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 120f));
            _bannerPanel.SetActive(false);
        }

        private void BuildResultPanel()
        {
            _resultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
            _resultPanel.transform.SetParent(transform, false);
            Image backdrop = _resultPanel.GetComponent<Image>();
            backdrop.color = UIFactory.WithAlpha(Color.black, 0.66f);
            Stretch(_resultPanel.transform as RectTransform);

            Image card = UIFactory.Panel(_resultPanel.transform, "ResultCard", UITheme.PanelBg, 1f, true);
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 430f));

            _resultText = UIFactory.Label(card.transform, "", 46, UITheme.Gold, TextAnchor.MiddleCenter);
            Anchor(_resultText, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(660f, 130f));

            Button rematch = UIFactory.Btn(card.transform, "REMATCH", BtnStyle.Primary, delegate { Rematch(); }, 30, new Vector2(440f, 88f));
            Anchor(rematch, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -46f), new Vector2(440f, 88f));
            Button home = UIFactory.Btn(card.transform, "HOME", BtnStyle.Dark, delegate { QuitToHome(); }, 30, new Vector2(440f, 88f));
            Anchor(home, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -152f), new Vector2(440f, 88f));

            _resultPanel.SetActive(false);
        }

        // ------------------------------------------------------------------ events

        private void OnScoreChanged(ScoreChangedArgs args)
        {
            if (args == null || args.Scores == null || args.Scores.Length < 2) return;
            SetText(_p1Score, args.Scores[0].ToString());
            SetText(_p2Score, args.Scores[1].ToString());
            SetText(_breakLabel, args.CurrentBreak > 0 ? "BREAK " + args.CurrentBreak : "");
            SetRequired(args.Required);
        }

        private void OnTurnChanged(TurnChangedArgs args)
        {
            if (args == null) return;
            Highlight(args.PlayerIndex);
            bool canPlay = args.CanShoot && !args.IsAI;
            if (_power != null) _power.SetInteractable(canPlay);
            if (_spinGroup != null) _spinGroup.alpha = canPlay ? 1f : 0.45f;
            if (!canPlay && _spin != null) _spin.ResetPad();
        }

        private void OnShotStarted(ShotStartedArgs args)
        {
            if (_power != null) _power.Set01(0f);
            if (_spin != null) _spin.ResetPad();
            if (_power != null) _power.SetInteractable(false);
        }

        private void OnFoulCommitted(FoulCommittedArgs args)
        {
            if (args == null) return;
            AudioManager audio;
            if (ServiceRegistry.TryGet(out audio)) audio.Play(SfxKey.Foul);
            ShowToastSafe("FOUL · " + args.Points + " POINTS" + (string.IsNullOrEmpty(args.Reason) ? "" : " · " + args.Reason.ToUpperInvariant()), ToastType.Bad);
        }

        private void OnFrameEnded(FrameEndedArgs args)
        {
            if (args == null) return;
            if (args.RespotBlack)
            {
                ShowToastSafe("RESPOTTED BLACK — FRAME CONTINUES", ToastType.Info);
                return;
            }
            AudioManager audio;
            if (ServiceRegistry.TryGet(out audio)) audio.Play(SfxKey.FrameWin);
            string name = FrameWinnerName(args.WinnerIndex);
            ShowBanner("FRAME TO " + name);
        }

        private void OnMatchEnded(MatchEndedArgs args)
        {
            if (args == null || _resultPanel == null) return;
            AudioManager audio;
            if (ServiceRegistry.TryGet(out audio)) audio.Play(SfxKey.FrameWin);
            SetText(_resultText, "MATCH WON BY " + FrameWinnerName(args.WinnerIndex));
            _resultPanel.SetActive(true);
            CloseMenu();
        }

        // ------------------------------------------------------------------ widget callbacks

        private void OnPowerChanged(float value)
        {
            CueController cue;
            if (ServiceRegistry.TryGet(out cue)) cue.SetPower01(value);
        }

        private void OnPowerCommitted()
        {
            CueController cue;
            if (ServiceRegistry.TryGet(out cue) && cue.CanStrike) cue.Fire();
        }

        private void OnSpinChanged(Vector2 spin)
        {
            CueController cue;
            if (ServiceRegistry.TryGet(out cue)) cue.SetSpin(spin);
        }

        // ------------------------------------------------------------------ actions

        private void CycleCamera()
        {
            CameraManager cam;
            if (!ServiceRegistry.TryGet(out cam)) return;
            switch (cam.State)
            {
                case CameraState.Standard: cam.SetState(CameraState.CloseAim); break;
                case CameraState.CloseAim: cam.SetState(CameraState.ShotFollow); break;
                case CameraState.ShotFollow: cam.SetState(CameraState.TopDown); break;
                default: cam.SetState(CameraState.Standard); break;
            }
            SetText(_cameraLabel, cam.State.ToString().ToUpperInvariant());
        }

        private void ToggleMenu()
        {
            if (_menuOpen) CloseMenu();
            else
            {
                _menuOpen = true;
                if (_pausePanel != null) _pausePanel.SetActive(true);
                AudioManager audio;
                if (ServiceRegistry.TryGet(out audio)) audio.Play(SfxKey.UiClick);
            }
        }

        private void CloseMenu()
        {
            _menuOpen = false;
            if (_pausePanel != null) _pausePanel.SetActive(false);
        }

        private void RestartFrame()
        {
            CloseMenu();
            MatchManager match;
            if (ServiceRegistry.TryGet(out match)) match.RestartFrame();
        }

        private void ConcedeFrame()
        {
            CloseMenu();
            MatchManager match;
            if (ServiceRegistry.TryGet(out match)) match.ConcedeFrame();
        }

        private void Rematch()
        {
            if (_resultPanel != null) _resultPanel.SetActive(false);
            MatchManager match;
            if (ServiceRegistry.TryGet(out match)) match.RestartMatch();
        }

        private void QuitToHome()
        {
            CloseMenu();
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm))
            {
                sm.LoadScene(SceneId.Home);
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneId.Home.ToString());
        }

        // ------------------------------------------------------------------ helpers

        private void RefreshFromManagers()
        {
            MatchManager match;
            if (ServiceRegistry.TryGet(out match) && match.PlayerNames != null && match.PlayerNames.Length >= 2)
            {
                SetText(_p1Name, match.PlayerNames[0].ToUpperInvariant());
                SetText(_p2Name, match.PlayerNames[1].ToUpperInvariant());
                SetText(_frameChip, "FRAME " + Mathf.Max(1, match.CurrentFrame));
            }

            ScoringManager scoring;
            if (ServiceRegistry.TryGet(out scoring))
            {
                int[] scores = scoring.Scores;
                SetText(_p1Score, scores[0].ToString());
                SetText(_p2Score, scores[1].ToString());
            }

            RulesManager rules;
            BallColor? required = null;
            if (ServiceRegistry.TryGet(out rules)) required = rules.RequiredBall;
            SetRequired(required);

            TurnManager turns;
            if (ServiceRegistry.TryGet(out turns)) Highlight(turns.CurrentPlayerIndex);

            bool canPlay = CanHumanShoot();
            if (_power != null) _power.SetInteractable(canPlay);
            if (_spinGroup != null) _spinGroup.alpha = canPlay ? 1f : 0.45f;
        }

        private static bool CanHumanShoot()
        {
            TurnManager turns;
            if (!ServiceRegistry.TryGet(out turns)) return false;
            return turns.CanShootNow && !turns.IsAITurn;
        }

        private void Highlight(int playerIndex)
        {
            bool p1 = playerIndex == 0;
            if (_p1Edge != null) _p1Edge.gameObject.SetActive(p1);
            if (_p2Edge != null) _p2Edge.gameObject.SetActive(!p1);
        }

        private void SetRequired(BallColor? required)
        {
            SetText(_requiredLabel, required.HasValue ? "ON: " + BallPalette.DisplayName(required.Value).ToUpperInvariant() : "ON: ANY COLOUR");
        }

        private string FrameWinnerName(int winnerIndex)
        {
            if (winnerIndex < 0) return "PLAYER";
            MatchManager match;
            if (ServiceRegistry.TryGet(out match) && match.PlayerNames != null && winnerIndex < match.PlayerNames.Length)
            {
                return match.PlayerNames[winnerIndex].ToUpperInvariant();
            }
            return winnerIndex == 0 ? "PLAYER 1" : "PLAYER 2";
        }

        private void ShowBanner(string message)
        {
            if (_bannerPanel == null) return;
            SetText(_bannerText, message);
            _bannerPanel.SetActive(true);
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(HideBannerAfterDelay(1.7f));
        }

        private IEnumerator HideBannerAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_bannerPanel != null) _bannerPanel.SetActive(false);
            _bannerRoutine = null;
        }

        private static void ShowToastSafe(string message, ToastType type)
        {
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm)) sm.ShowToast(message, type);
        }

        private static void SetText(Text target, string content)
        {
            if (target != null) target.text = content;
        }

        private static Text ButtonText(Button b)
        {
            if (b == null) return null;
            Text t = b.GetComponentInChildren<Text>();
            return t;
        }

        private static Image ButtonImage(Button b)
        {
            if (b == null) return null;
            Image img = b.image;
            if (img == null) img = b.GetComponent<Image>();
            return img;
        }

        private static T Anchor<T>(T g, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size) where T : Component
        {
            if (g == null) return null;
            RectTransform rt = g.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }
            return g;
        }

        private static void Stretch(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

    }
}
