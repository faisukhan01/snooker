// SnookerKit — floating Game Tools panel (CONTRACTS §5–§8 / PDF §17–18). Fully self-contained: attach to any
// scene object and it builds its own top-right anchored root under the nearest canvas (or a standalone overlay
// canvas) in Start. Rounded translucent dark panel (PanelBg α 0.92, ~420 wide), header chevron collapses the
// body via a 0.18 s fade+scale tween (default COLLAPSED), live metrics refreshed on a 0.5 s unscaled coroutine
// (honest N/A where the platform hides values — never fabricated), SCREENSHOT + SETTINGS shortcuts, static
// system-status row, and a header drag that keeps the panel inside canvas bounds. The panel only occupies its
// own small rect, so it never blocks cue, balls or on-screen controls; toasts (overlay order 100) stay above it.
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Floating Game Tools panel (PDF §17–18): device/performance telemetry, screenshot capture and a
    /// Settings shortcut in a draggable, collapsible dark card. Builds itself in Start; no other screen owns or
    /// creates it. Metrics poll PerformanceManager every 0.5 s of unscaled time from a cached coroutine — no
    /// per-frame work, no per-frame allocations. Event subscriptions are paired OnEnable/OnDisable; the metrics
    /// coroutine follows the same lifecycle.</summary>
    public class GameToolsPanel : MonoBehaviour
    {
        // --- layout constants (canvas reference space 1920×1080, matching ScreenManager/UIInstaller) ---
        private const float PanelWidth = 420f;
        private const float HeaderHeight = 88f;
        private const float CollapsedHeight = 88f;
        private const float RowTop = 8f;
        private const float RowHeight = 52f;
        private const int MetricCount = 6;
        private const float ButtonOneY = RowTop + MetricCount * RowHeight + 18f; // SCREENSHOT row top  (338)
        private const float ButtonTwoY = ButtonOneY + 72f + 14f;                 // SETTINGS row top     (424)
        private const float StatusY = ButtonTwoY + 72f + 10f;                    // status row top       (506)
        private const float BodyHeight = StatusY + 36f + 18f;                    // 560
        private const float ExpandedHeight = HeaderHeight + BodyHeight;          // 648
        private const float CollapseSeconds = 0.18f;
        private const float MetricsIntervalSeconds = 0.5f;
        private const float EdgeMargin = 24f;
        private const float DragMargin = 16f;
        private const float MinCanvasDimension = 1f;
        private const string ChevronOpen = "\u25BE";     // ▾ — body visible
        private const string ChevronClosed = "\u25B8";   // ▸ — body collapsed
        private const string StatusText = "OK · offline v1";
        private const string NaText = "N/A";

        private static readonly string[] MetricLabels = { "FPS", "FRAME", "LOW", "DEVICE", "BATTERY", "NETWORK" };

        // --- cached scene/UI references (TryGet null-safety; no lookups after Build) ---
        private RectTransform _root;
        private CanvasGroup _rootGroup;
        private GameObject _bodyGO;
        private RectTransform _bodyRT;
        private CanvasGroup _bodyGroup;
        private Text _chevronText;
        private readonly Text[] _metricValues = new Text[MetricCount];
        private Canvas _canvas;
        private RectTransform _canvasRT;
        private Coroutine _metricsRoutine;
        private Coroutine _animRoutine;
        private WaitForSecondsRealtime _tick;

        // --- state ---
        private bool _built;
        private bool _expanded;
        private float _animValue;          // 0 = collapsed, 1 = expanded (drives fade + scale + height)
        private bool _naSticky;            // avoids rewriting the literal "N/A" every tick while no manager exists
        private Vector2 _dragStartAnchored;
        private float _dragInvScale = 1f;

        private void Start()
        {
            Build();
            StartMetrics();
        }

        private void OnEnable()
        {
            GameEvents.ScreenshotCompleted += OnScreenshotCompleted;
            if (!_built) return;

            // Heal any tween frozen mid-flight by a disable (Tween coroutines die silently with an inactive host).
            if (_chevronText != null) _chevronText.text = _expanded ? ChevronOpen : ChevronClosed;
            if (_bodyGO != null) _bodyGO.SetActive(true);
            ApplyCollapse(_expanded ? 1f : 0f);
            if (!_expanded && _bodyGO != null) _bodyGO.SetActive(false);
            if (_rootGroup != null) _rootGroup.alpha = 1f;
            StartMetrics();
        }

        private void OnDisable()
        {
            GameEvents.ScreenshotCompleted -= OnScreenshotCompleted;
            if (_metricsRoutine != null) { StopCoroutine(_metricsRoutine); _metricsRoutine = null; }
            if (_animRoutine != null) { StopCoroutine(_animRoutine); _animRoutine = null; }
        }

        // --- construction ------------------------------------------------------------

        /// <summary>Resolves (or provisions) the host canvas and builds the top-right panel root once.</summary>
        private void Build()
        {
            if (_built) return;

            Canvas canvas = GetComponentInParent<Canvas>(); // one-time init lookup — never in per-frame paths
            if (canvas == null) canvas = CreateStandaloneCanvas();
            _canvas = canvas;
            _canvasRT = canvas.transform as RectTransform;

            GameObject rootGO = new GameObject("GameToolsRoot", typeof(RectTransform));
            rootGO.transform.SetParent(canvas.transform, false);
            _root = (RectTransform)rootGO.transform;
            _root.anchorMin = new Vector2(1f, 1f);   // top-right anchored
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.sizeDelta = new Vector2(PanelWidth, CollapsedHeight);
            _root.anchoredPosition = new Vector2(-EdgeMargin, -(EdgeMargin + TopSafeInsetCanvasUnits()));

            _rootGroup = rootGO.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 1f;
            _rootGroup.interactable = true;
            _rootGroup.blocksRaycasts = true;

            Image bg = UIFactory.Panel(rootGO.transform, "GameToolsPanelBg", UITheme.PanelBg, 1f, true);
            Transform panelT = bg != null ? bg.transform : rootGO.transform;

            BuildHeader(panelT);
            BuildBody(panelT);

            _tick = new WaitForSecondsRealtime(MetricsIntervalSeconds);
            ApplyCollapse(0f); // default collapsed to the header row
            _built = true;
        }

        /// <summary>Header strip: title, drag raycast catcher and the collapse chevron button.</summary>
        private void BuildHeader(Transform panelT)
        {
            // Transparent strip that receives the drag gesture (labels are raycast-off, button sits on top).
            GameObject headerGO = new GameObject("HeaderStrip", typeof(RectTransform));
            headerGO.transform.SetParent(panelT, false);
            RectTransform hrt = (RectTransform)headerGO.transform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = Vector2.zero;
            hrt.sizeDelta = new Vector2(0f, HeaderHeight);
            Image headerImg = headerGO.AddComponent<Image>();
            headerImg.color = new Color(0f, 0f, 0f, 0f);
            headerImg.raycastTarget = true;

            EventTrigger headerTrigger = headerGO.AddComponent<EventTrigger>();
            AddEntry(headerTrigger, EventTriggerType.BeginDrag, OnPanelDragBegin);
            AddEntry(headerTrigger, EventTriggerType.Drag, OnPanelDragged);

            Text title = UIFactory.Label(panelT, "GAME TOOLS", 34, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            Place(title, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -14f), new Vector2(260f, 60f));
            if (title != null) title.font = FontProvider.Get(FontWeight.SemiBold);

            Button chevron = UIFactory.Btn(panelT, ChevronClosed, BtnStyle.Ghost, ToggleExpanded, 30, new Vector2(72f, 64f));
            Place(chevron, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(72f, 64f));
            _chevronText = chevron != null ? chevron.GetComponentInChildren<Text>(true) : null;
        }

        /// <summary>Body: six metric rows (label left muted / value right primary), two action buttons and the
        /// static system-status line. Hidden until first expand.</summary>
        private void BuildBody(Transform panelT)
        {
            GameObject bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(panelT, false);
            _bodyGO = bodyGO;
            _bodyRT = (RectTransform)bodyGO.transform;
            _bodyRT.anchorMin = new Vector2(0f, 1f);
            _bodyRT.anchorMax = new Vector2(1f, 1f);
            _bodyRT.pivot = new Vector2(0.5f, 1f);
            _bodyRT.anchoredPosition = new Vector2(0f, -HeaderHeight);
            _bodyRT.sizeDelta = new Vector2(0f, BodyHeight);

            _bodyGroup = bodyGO.AddComponent<CanvasGroup>();
            _bodyGroup.alpha = 0f;
            _bodyGroup.interactable = false;
            _bodyGroup.blocksRaycasts = false;

            for (int i = 0; i < MetricLabels.Length; i++)
                _metricValues[i] = BuildMetricRow(bodyGO.transform, i);

            Button shot = UIFactory.Btn(bodyGO.transform, "SCREENSHOT", BtnStyle.Ghost, OnScreenshotClicked, 26, new Vector2(368f, 72f));
            Place(shot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -ButtonOneY), new Vector2(368f, 72f));

            Button settings = UIFactory.Btn(bodyGO.transform, "SETTINGS", BtnStyle.Dark, OnSettingsClicked, 26, new Vector2(368f, 72f));
            Place(settings, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -ButtonTwoY), new Vector2(368f, 72f));

            Text status = UIFactory.Label(bodyGO.transform, StatusText, 22, UITheme.TextMuted, TextAnchor.MiddleLeft);
            Place(status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -StatusY), new Vector2(340f, 36f));

            bodyGO.SetActive(false);
        }

        /// <summary>One metrics row; returns the right-aligned value Text for the refresh loop.</summary>
        private static Text BuildMetricRow(Transform body, int index)
        {
            float y = -(RowTop + index * RowHeight);
            Text label = UIFactory.Label(body, MetricLabels[index], 22, UITheme.TextMuted, TextAnchor.MiddleLeft);
            Place(label, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(120f, 40f));
            Text value = UIFactory.Label(body, NaText, 26, UITheme.TextPrimary, TextAnchor.MiddleRight);
            Place(value, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, y), new Vector2(236f, 40f));
            return value;
        }

        /// <summary>Standalone overlay canvas (order 90, below ScreenManager's 100 so toasts stay on top).</summary>
        private Canvas CreateStandaloneCanvas()
        {
            var go = new GameObject("GameToolsCanvas", typeof(RectTransform));
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return canvas;
        }

        /// <summary>Guarantees an EventSystem exists when the panel provisions its own canvas (init-time only).</summary>
        private static void EnsureEventSystem()
        {
            try
            {
                if (Object.FindObjectOfType<EventSystem>() != null) return;
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Object.DontDestroyOnLoad(go);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("GameToolsPanel.EnsureEventSystem: " + e.Message);
            }
        }

        /// <summary>Top screen notch inset converted to canvas units (0 when the platform reports nothing).</summary>
        private float TopSafeInsetCanvasUnits()
        {
            float inset = 0f;
            try
            {
                Rect sa = Screen.safeArea;
                float sf = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
                inset = Mathf.Max(0f, Screen.height - sa.yMax) / sf;
            }
            catch (System.Exception)
            {
                inset = 0f; // honest default — plain 24 px margin already applied by the caller
            }
            return inset;
        }

        // --- collapse / expand ---------------------------------------------------------

        /// <summary>Flips the expanded state and runs the 0.18 s fade+scale tween (EaseOutCubic).</summary>
        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            if (_chevronText != null) _chevronText.text = _expanded ? ChevronOpen : ChevronClosed;
            if (_bodyGO != null) _bodyGO.SetActive(true);
            StopAnim();
            _animRoutine = Tween.Value(this, _animValue, _expanded ? 1f : 0f, CollapseSeconds, ApplyCollapse, Tween.EaseOutCubic, OnCollapseDone);
        }

        /// <summary>Applies the collapse parameter: body alpha+scale and the animated root height.</summary>
        private void ApplyCollapse(float v)
        {
            _animValue = Mathf.Clamp01(v);
            if (_bodyGroup != null)
            {
                _bodyGroup.alpha = _animValue;
                _bodyGroup.interactable = _animValue > 0.95f;
                _bodyGroup.blocksRaycasts = _animValue > 0.95f;
            }
            if (_bodyRT != null)
            {
                float s = 0.9f + 0.1f * _animValue;
                _bodyRT.localScale = new Vector3(s, s, 1f);
            }
            if (_root != null)
                _root.sizeDelta = new Vector2(PanelWidth, CollapsedHeight + (ExpandedHeight - CollapsedHeight) * _animValue);
        }

        /// <summary>Tween completion: snap exact end state and park the body when fully collapsed.</summary>
        private void OnCollapseDone()
        {
            _animRoutine = null;
            ApplyCollapse(_expanded ? 1f : 0f);
            if (!_expanded && _bodyGO != null) _bodyGO.SetActive(false);
        }

        private void StopAnim()
        {
            if (_animRoutine != null) { StopCoroutine(_animRoutine); _animRoutine = null; }
        }

        // --- metrics refresh (0.5 s unscaled cadence) -----------------------------------

        private void StartMetrics()
        {
            if (_metricsRoutine != null) return;
            if (!isActiveAndEnabled) return;
            if (_tick == null) _tick = new WaitForSecondsRealtime(MetricsIntervalSeconds);
            RefreshMetrics();
            _metricsRoutine = StartCoroutine(MetricsLoop());
        }

        /// <summary>Waits on a cached WaitForSecondsRealtime — zero allocation per tick.</summary>
        private IEnumerator MetricsLoop()
        {
            while (true)
            {
                yield return _tick;
                RefreshMetrics();
            }
        }

        /// <summary>Copies PerformanceManager readouts into the cached value labels. String building happens only
        /// here (twice per second), never per frame. When no manager is registered the labels show honest N/A.</summary>
        private void RefreshMetrics()
        {
            if (_metricValues[0] == null) return;

            PerformanceManager pm;
            if (!ServiceRegistry.TryGet(out pm))
            {
                if (_naSticky) return;
                _naSticky = true;
                for (int i = 0; i < _metricValues.Length; i++)
                    if (_metricValues[i] != null) _metricValues[i].text = NaText;
                return;
            }
            _naSticky = false;

            if (_metricValues[0] != null) _metricValues[0].text = pm.FpsAverage.ToString("F0");
            if (_metricValues[1] != null) _metricValues[1].text = pm.FrameMsAverage.ToString("F1") + " ms";
            if (_metricValues[2] != null) _metricValues[2].text = pm.FpsLow.ToString("F0");
            if (_metricValues[3] != null) _metricValues[3].text = pm.GetDeviceText();
            if (_metricValues[4] != null) _metricValues[4].text = pm.GetBatteryText();
            if (_metricValues[5] != null) _metricValues[5].text = pm.GetNetworkText();
        }

        // --- actions --------------------------------------------------------------------

        /// <summary>SCREENSHOT: delegates to PerformanceManager.CaptureScreenshot; the GameEvents.ScreenshotCompleted
        /// subscription toasts the verified result (honest failure toast when the file never appeared).</summary>
        private void OnScreenshotClicked()
        {
            PerformanceManager pm;
            if (ServiceRegistry.TryGet(out pm))
            {
                pm.CaptureScreenshot();
                return;
            }
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm)) sm.ShowToast("SCREENSHOT N/A", ToastType.Info);
        }

        /// <summary>Screenshot verdict from PerformanceManager: verified file → saved toast + click sfx; null path
        /// → honest failure toast (never fabricates success).</summary>
        private void OnScreenshotCompleted(ScreenshotCompletedArgs args)
        {
            ScreenManager sm;
            if (args != null && !string.IsNullOrEmpty(args.Path))
            {
                if (ServiceRegistry.TryGet(out sm)) sm.ShowToast("SCREENSHOT SAVED", ToastType.Good);
                AudioManager audio;
                if (ServiceRegistry.TryGet(out audio)) audio.Play(SfxKey.UiClick);
                return;
            }
            if (ServiceRegistry.TryGet(out sm)) sm.ShowToast("SCREENSHOT FAILED", ToastType.Bad);
        }

        /// <summary>SETTINGS: fade-load the Settings scene through ScreenManager (direct load as teardown fallback).</summary>
        private void OnSettingsClicked()
        {
            ScreenManager sm;
            if (ServiceRegistry.TryGet(out sm))
            {
                sm.LoadScene(SceneId.Settings);
                return;
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneId.Settings.ToString());
        }

        // --- header drag (never per-frame allocated; clamped inside the host canvas) -----

        private void OnPanelDragBegin(BaseEventData e)
        {
            PointerEventData pe = e as PointerEventData;
            if (pe == null || _root == null) return;
            _dragStartAnchored = _root.anchoredPosition;
            float sf = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            _dragInvScale = 1f / sf; // screen px → canvas units
        }

        private void OnPanelDragged(BaseEventData e)
        {
            PointerEventData pe = e as PointerEventData;
            if (pe == null || _root == null) return;
            Vector2 screenDelta = pe.position - pe.pressPosition;
            MoveRoot(_dragStartAnchored + screenDelta * _dragInvScale);
        }

        /// <summary>Moves the top-right anchored root, clamped so the whole card stays inside the canvas rect.</summary>
        private void MoveRoot(Vector2 target)
        {
            if (_root == null || _canvasRT == null) return;
            float w = _root.sizeDelta.x;
            float h = _root.sizeDelta.y;
            float cw = _canvasRT.rect.width;
            float ch = _canvasRT.rect.height;
            if (cw <= MinCanvasDimension || ch <= MinCanvasDimension) return;

            float loX = Mathf.Min(w - cw + DragMargin, -DragMargin);
            float hiX = Mathf.Max(w - cw + DragMargin, -DragMargin);
            float loY = Mathf.Min(h - ch + DragMargin, -DragMargin);
            float hiY = Mathf.Max(h - ch + DragMargin, -DragMargin);
            _root.anchoredPosition = new Vector2(Mathf.Clamp(target.x, loX, hiX), Mathf.Clamp(target.y, loY, hiY));
        }

        // --- build-time helpers -----------------------------------------------------------

        /// <summary>Anchors/pivots/positions/sizes a component's RectTransform (mirror of the sibling screens).</summary>
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

        /// <summary>Adds one EventTrigger entry (build-time helper; wraps the callback in a UnityAction).</summary>
        private static void AddEntry(EventTrigger trigger, EventTriggerType id, System.Action<BaseEventData> callback)
        {
            if (trigger == null || callback == null) return;
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = id;
            entry.callback.AddListener(new UnityAction<BaseEventData>(callback));
            trigger.triggers.Add(entry);
        }
    }
}
