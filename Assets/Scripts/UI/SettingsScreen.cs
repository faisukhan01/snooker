// SnookerKit — Settings screen (Task 16-e rebuild; CONTRACTS §2 SettingsService, PDF §20/§21).
using UnityEngine;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Settings screen: GAMEPLAY (aim assist cycle + sensitivity stepper), GRAPHICS (quality + target FPS
    /// cycles), AUDIO (master/sfx steppers) and a TOGGLES row. All reads go through SettingsService.Settings and all
    /// writes through the nine pinned Set* helpers (each change re-reads state so the UI always mirrors the truth).
    ///
    /// NOTE (v1): the "power slider on left" preference has NO Set* helper in the frozen SettingsService contract and
    /// direct field writes are forbidden — this screen therefore intentionally omits that toggle. The HUD still honours
    /// Settings.powerSliderOnLeft when mirroring the spin/power widgets; the toggle lands once a
    /// SetPowerSliderOnLeft helper is pinned (v1.1).</summary>
    public class SettingsScreen : MonoBehaviour
    {
        private static readonly string[] AssistNames = { "OFF", "LOW", "MEDIUM", "HIGH" };
        private static readonly string[] QualityNames = { "LOW", "MEDIUM", "HIGH", "ULTRA" };

        private Text _assistText;
        private Text _qualityText;
        private Text _fpsText;
        private Text _sensText;
        private Text _masterText;
        private Text _sfxText;
        private Button _ambienceBtn;
        private Text _ambienceText;
        private Button _hapticsBtn;
        private Text _hapticsText;
        private Button _trajectoryBtn;
        private Text _trajectoryText;

        private void Start()
        {
            Build();
            RefreshAll();
        }

        private void Build()
        {
            Image card = UIFactory.Panel(transform, "SettingsCard", UITheme.PanelBg, 0.94f, true);
            Place(card, Center(), Center(), Vector2.zero, new Vector2(920f, 960f));
            if (card != null) card.raycastTarget = true;
            Transform parent = card != null ? card.transform : transform;

            Place(UIFactory.Label(parent, "SETTINGS", 54, UITheme.TextPrimary, TextAnchor.MiddleLeft),
                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(420f, 72f));
            Place(UIFactory.Btn(parent, "BACK", BtnStyle.Ghost, GoBack, 28, new Vector2(190f, 72f)),
                  new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(190f, 72f));

            // --- GAMEPLAY ------------------------------------------------------
            SectionHeading(parent, "GAMEPLAY", -150f);
            _assistText = ButtonText(BuildCycleButton(parent, -200f, CycleAssist));
            BuildRowLabel(parent, "AIM ASSIST", -200f);
            _sensText = BuildStepper(parent, -268f, delegate { StepSensitivity(-1); }, delegate { StepSensitivity(1); });
            BuildRowLabel(parent, "AIM SENSITIVITY", -268f);

            // --- GRAPHICS ------------------------------------------------------
            SectionHeading(parent, "GRAPHICS", -340f);
            _qualityText = ButtonText(BuildCycleButton(parent, -390f, CycleQuality));
            BuildRowLabel(parent, "QUALITY", -390f);
            _fpsText = ButtonText(BuildCycleButton(parent, -458f, CycleFps));
            BuildRowLabel(parent, "TARGET FPS", -458f);

            // --- AUDIO ---------------------------------------------------------
            SectionHeading(parent, "AUDIO", -530f);
            _masterText = BuildStepper(parent, -580f, delegate { StepMaster(-1); }, delegate { StepMaster(1); });
            BuildRowLabel(parent, "MASTER VOLUME", -580f);
            _sfxText = BuildStepper(parent, -648f, delegate { StepSfx(-1); }, delegate { StepSfx(1); });
            BuildRowLabel(parent, "SFX VOLUME", -648f);

            // --- TOGGLES -------------------------------------------------------
            SectionHeading(parent, "TOGGLES", -720f);
            _ambienceBtn = UIFactory.Btn(parent, "AMBIENCE", BtnStyle.Ghost, ToggleAmbience, 24, new Vector2(260f, 64f));
            Place(_ambienceBtn, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-282f, -770f), new Vector2(260f, 64f));
            _ambienceText = ButtonText(_ambienceBtn);
            _hapticsBtn = UIFactory.Btn(parent, "HAPTICS", BtnStyle.Ghost, ToggleHaptics, 24, new Vector2(260f, 64f));
            Place(_hapticsBtn, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -770f), new Vector2(260f, 64f));
            _hapticsText = ButtonText(_hapticsBtn);
            _trajectoryBtn = UIFactory.Btn(parent, "TRAJECTORY", BtnStyle.Ghost, ToggleTrajectory, 24, new Vector2(260f, 64f));
            Place(_trajectoryBtn, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(282f, -770f), new Vector2(260f, 64f));
            _trajectoryText = ButtonText(_trajectoryBtn);
        }

        // --- mutation handlers (each writes via a pinned Set* helper, then re-reads state) ---

        private void CycleAssist()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetAimAssist((Mathf.Clamp(ss.Settings.aimAssist, 0, 3) + 1) % 4);
            RefreshAll();
        }

        private void CycleQuality()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetGraphicsQuality((Mathf.Clamp(ss.Settings.graphicsQuality, 0, 3) + 1) % 4);
            RefreshAll();
        }

        private void CycleFps()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetTargetFps(ss.Settings.targetFps <= 30 ? 60 : 30);
            RefreshAll();
        }

        private void StepSensitivity(int dir)
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            int tenths = Mathf.Clamp(Mathf.RoundToInt(ss.Settings.aimSensitivity * 10f) + dir, 5, 20);
            ss.SetAimSensitivity(tenths * 0.1f);
            RefreshAll();
        }

        private void StepMaster(int dir)
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            int pct = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(ss.Settings.masterVolume) * 100f) + dir * 5, 0, 100);
            ss.SetMasterVolume(pct * 0.01f);
            RefreshAll();
        }

        private void StepSfx(int dir)
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            int pct = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(ss.Settings.sfxVolume) * 100f) + dir * 5, 0, 100);
            ss.SetSfxVolume(pct * 0.01f);
            RefreshAll();
        }

        private void ToggleAmbience()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetAmbienceOn(!ss.Settings.ambienceOn);
            RefreshAll();
        }

        private void ToggleHaptics()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetHapticsOn(!ss.Settings.hapticsOn);
            RefreshAll();
        }

        private void ToggleTrajectory()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            ss.SetShowTrajectory(!ss.Settings.showTrajectory);
            RefreshAll();
        }

        /// <summary>Mirrors SettingsService.Settings onto every label/toggle (single source of truth).</summary>
        private void RefreshAll()
        {
            SettingsService ss;
            if (!ServiceRegistry.TryGet(out ss) || ss.Settings == null) return;
            SettingsData s = ss.Settings;

            if (_assistText != null) _assistText.text = AssistNames[Mathf.Clamp(s.aimAssist, 0, 3)];
            if (_qualityText != null) _qualityText.text = QualityNames[Mathf.Clamp(s.graphicsQuality, 0, 3)];
            if (_fpsText != null) _fpsText.text = (s.targetFps <= 30 ? 30 : 60) + " FPS";
            if (_sensText != null) _sensText.text = (Mathf.Clamp(Mathf.RoundToInt(s.aimSensitivity * 10f), 5, 20) * 0.1f).ToString("F1");
            if (_masterText != null) _masterText.text = Mathf.RoundToInt(Mathf.Clamp01(s.masterVolume) * 100f) + "%";
            if (_sfxText != null) _sfxText.text = Mathf.RoundToInt(Mathf.Clamp01(s.sfxVolume) * 100f) + "%";

            StyleToggle(_ambienceBtn, _ambienceText, "AMBIENCE", s.ambienceOn);
            StyleToggle(_hapticsBtn, _hapticsText, "HAPTICS", s.hapticsOn);
            StyleToggle(_trajectoryBtn, _trajectoryText, "TRAJECTORY", s.showTrajectory);
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

        // --- build helpers ---------------------------------------------------

        private static void SectionHeading(Transform parent, string caption, float y)
        {
            Text t = UIFactory.Label(parent, caption, 26, UITheme.AccentBright, TextAnchor.MiddleLeft);
            Place(t, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, y), new Vector2(400f, 30f));
        }

        private static void BuildRowLabel(Transform parent, string caption, float y)
        {
            Text t = UIFactory.Label(parent, caption, 30, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            Place(t, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, y), new Vector2(330f, 64f));
        }

        private static Button BuildCycleButton(Transform parent, float y, System.Action onClick)
        {
            Button b = UIFactory.Btn(parent, "-", BtnStyle.Dark, onClick, 26, new Vector2(280f, 64f));
            Place(b, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, y), new Vector2(280f, 64f));
            return b;
        }

        /// <summary>− / value / + cluster, right-aligned inside the card. Returns the value label.</summary>
        private static Text BuildStepper(Transform parent, float y, System.Action minus, System.Action plus)
        {
            Button minusBtn = UIFactory.Btn(parent, "-", BtnStyle.Dark, minus, 34, new Vector2(72f, 64f));
            Place(minusBtn, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-310f, y), new Vector2(72f, 64f));
            Button plusBtn = UIFactory.Btn(parent, "+", BtnStyle.Dark, plus, 34, new Vector2(72f, 64f));
            Place(plusBtn, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, y), new Vector2(72f, 64f));
            Text value = UIFactory.Label(parent, "-", 30, UITheme.TextPrimary, TextAnchor.MiddleCenter);
            Place(value, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-124f, y), new Vector2(170f, 64f));
            return value;
        }

        private static void StyleToggle(Button b, Text t, string prefix, bool on)
        {
            Image img = ButtonImage(b);
            if (img != null) img.color = on ? UIFactory.WithAlpha(UITheme.AccentBright, 0.92f) : BtnStyle.Ghost.Normal;
            if (t != null)
            {
                t.text = prefix + (on ? " · ON" : " · OFF");
                t.color = on ? UITheme.PanelBg : BtnStyle.Ghost.Text;
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
