// SnookerKit — overlay canvas owner: toast notifications + scene-transition fade (frozen, CONTRACTS §2/§8).
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Registered in Awake (deregistered in OnDestroy). Builds a persistent overlay canvas ("OverlayCanvas",
    /// ScreenSpaceOverlay, sorting order 100, CanvasScaler 1920×1080 match 0.5, GraphicRaycaster) containing a
    /// safe-area root (toasts) and a fullscreen fade overlay that raycast-blocks only while a transition is live.
    /// ScreenManager is expected to live under AppServices so the overlay survives scene loads.</summary>
    public class ScreenManager : MonoBehaviour
    {
        private const float FadeSeconds = 0.22f;
        private const float ToastTotalSeconds = 1.8f;
        private const float ToastFadeIn = 0.15f;
        private const float ToastFadeOut = 0.30f;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private CanvasGroup _fade;
        private RectTransform _toast;
        private CanvasGroup _toastGroup;
        private Text _toastText;
        private Image _toastEdge;
        private Coroutine _fadeRoutine;
        private Coroutine _toastRoutine;
        private bool _transitionActive;

        private void Awake()
        {
            ServiceRegistry.Register<ScreenManager>(this);
            EnsureEventSystem();
            BuildOverlay();
        }

        private void OnDestroy()
        {
            if (ServiceRegistry.Get<ScreenManager>() == this) ServiceRegistry.Deregister<ScreenManager>();
        }

        /// <summary>Shows a rounded dark pill toast top-centre inside the safe area for 1.8 s total
        /// (fade in 0.15 s, hold, fade out 0.30 s). The left edge is colored by type. Re-invocations replace the
        /// current toast (previous coroutine is cancelled, no extra allocation).</summary>
        public void ShowToast(string message, ToastType type)
        {
            if (_toast == null || _toastText == null || _toastEdge == null || _toastGroup == null) return;
            if (!isActiveAndEnabled) return;

            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = null;

            _toastText.text = string.IsNullOrEmpty(message) ? string.Empty : message;
            _toastEdge.color = ToastColor(type);

            _toast.gameObject.SetActive(true);
            _toastGroup.alpha = 0f;
            _toastRoutine = StartCoroutine(ToastRoutine());
        }

        /// <summary>Fades the overlay to black over 0.22 s, loads the scene asynchronously, then fades back.
        /// The overlay raycast-blocks ONLY during the transition. Coroutine-safe: re-entrant calls during a live
        /// transition are ignored (single bool token — no double-load, no queue machinery).</summary>
        public void LoadScene(SceneId id)
        {
            if (_transitionActive) return;
            if (!isActiveAndEnabled)
            {
                // No overlay available (teardown edge) — load directly rather than getting stuck.
                SceneManager.LoadScene(id.ToString());
                return;
            }
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeLoadRoutine(id));
        }

        /// <summary>True while a scene transition fade is in flight (raycast-blocking window).</summary>
        public bool IsTransitioning { get { return _transitionActive; } }

        private void BuildOverlay()
        {
            if (_canvas != null) return;

            var canvasGO = new GameObject("OverlayCanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Safe-area root — toasts live inside it so they never collide with notches.
            var safeGO = new GameObject("SafeRoot", typeof(RectTransform));
            safeGO.transform.SetParent(canvasGO.transform, false);
            _safeRoot = (RectTransform)safeGO.transform;
            Stretch(_safeRoot);
            safeGO.AddComponent<SafeAreaFitter>();

            BuildToast();

            // Fade overlay OUTSIDE the safe root so transitions cover notch regions too.
            var fadeGO = new GameObject("FadeOverlay", typeof(RectTransform));
            fadeGO.transform.SetParent(canvasGO.transform, false);
            Stretch((RectTransform)fadeGO.transform);
            Image img = fadeGO.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 1f);
            img.raycastTarget = true;
            _fade = fadeGO.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.interactable = false;
            _fade.blocksRaycasts = false; // idle: clicks pass through
        }

        /// <summary>Builds the reusable toast pill (rounded dark panel, colored left edge, semi-bold text). Hidden until used.</summary>
        private void BuildToast()
        {
            var go = new GameObject("Toast", typeof(RectTransform));
            go.transform.SetParent(_safeRoot, false);
            _toast = (RectTransform)go.transform;
            _toast.anchorMin = new Vector2(0.5f, 1f);
            _toast.anchorMax = new Vector2(0.5f, 1f);
            _toast.pivot = new Vector2(0.5f, 1f);
            _toast.anchoredPosition = new Vector2(0f, -24f);
            _toast.sizeDelta = new Vector2(160f, 84f);

            Image pill = go.AddComponent<Image>();
            pill.sprite = UIFactory.RoundedSprite(64, 14);
            pill.type = Image.Type.Sliced;
            pill.pixelsPerUnitMultiplier = 1f;
            pill.color = UITheme.PanelBg;
            pill.raycastTarget = false;

            HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(30, 26, 20, 20);
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _toastGroup = go.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.interactable = false;
            _toastGroup.blocksRaycasts = false; // a toast never eats taps

            // Colored left edge (anchored to the pill's left border, excluded from the layout group).
            var edgeGO = new GameObject("Edge", typeof(RectTransform));
            edgeGO.transform.SetParent(go.transform, false);
            RectTransform edgeRT = (RectTransform)edgeGO.transform;
            edgeRT.anchorMin = new Vector2(0f, 0.16f);
            edgeRT.anchorMax = new Vector2(0f, 0.84f);
            edgeRT.pivot = new Vector2(0f, 0.5f);
            edgeRT.anchoredPosition = new Vector2(8f, 0f);
            edgeRT.sizeDelta = new Vector2(6f, 0f);
            _toastEdge = edgeGO.AddComponent<Image>();
            _toastEdge.raycastTarget = false;
            LayoutElement edgeLE = edgeGO.AddComponent<LayoutElement>();
            edgeLE.ignoreLayout = true;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            _toastText = textGO.AddComponent<Text>();
            _toastText.font = FontProvider.Get(FontWeight.SemiBold);
            _toastText.fontSize = 30;
            _toastText.color = UITheme.TextPrimary;
            _toastText.alignment = TextAnchor.MiddleLeft;
            _toastText.raycastTarget = false;
            // Overflow so the pill GROWS with the message (Wrap would clamp preferred width to the initial rect).
            _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _toastText.verticalOverflow = VerticalWrapMode.Overflow;
            LayoutElement le = textGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.minHeight = 44f;

            go.SetActive(false);
        }

        /// <summary>Toast fade choreography on unscaled time (works during paused/loading frames).</summary>
        private IEnumerator ToastRoutine()
        {
            float t = 0f;
            while (t < ToastFadeIn)
            {
                t += Time.unscaledDeltaTime;
                _toastGroup.alpha = Mathf.Clamp01(t / ToastFadeIn);
                yield return null;
            }
            _toastGroup.alpha = 1f;

            float hold = ToastTotalSeconds - ToastFadeIn - ToastFadeOut;
            t = 0f;
            while (t < hold)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            t = 0f;
            while (t < ToastFadeOut)
            {
                t += Time.unscaledDeltaTime;
                _toastGroup.alpha = 1f - Mathf.Clamp01(t / ToastFadeOut);
                yield return null;
            }
            _toastGroup.alpha = 0f;
            _toast.gameObject.SetActive(false);
            _toastRoutine = null;
        }

        /// <summary>Scene transition choreography: fade in → async load → fade out. Blocks raycasts for the whole window only.</summary>
        private IEnumerator FadeLoadRoutine(SceneId id)
        {
            _transitionActive = true;
            if (_fade != null) _fade.blocksRaycasts = true;

            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                SetFadeAlpha(Mathf.Clamp01(t / FadeSeconds));
                yield return null;
            }
            SetFadeAlpha(1f);

            try
            {
                AsyncOperation op = SceneManager.LoadSceneAsync(id.ToString());
                if (op != null)
                {
                    while (!op.isDone) yield return null;
                }
                else
                {
                    Debug.LogWarning("ScreenManager.LoadScene: scene '" + id + "' could not be loaded (invalid name or not in build settings).");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ScreenManager.LoadScene failed for " + id + ": " + e.Message);
            }

            t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                SetFadeAlpha(1f - Mathf.Clamp01(t / FadeSeconds));
                yield return null;
            }
            SetFadeAlpha(0f);
            if (_fade != null) _fade.blocksRaycasts = false;
            _transitionActive = false;
            _fadeRoutine = null;
        }

        private void SetFadeAlpha(float a)
        {
            if (_fade != null) _fade.alpha = a;
        }

        /// <summary>Toast edge color per severity (Good = bright accent, Bad = danger, Info = gold).</summary>
        private static Color ToastColor(ToastType type)
        {
            if (type == ToastType.Good) return UITheme.AccentBright;
            if (type == ToastType.Bad) return UITheme.Danger;
            return UITheme.Gold;
        }

        /// <summary>Ensures an EventSystem exists (init-time only; created once, DontDestroyOnLoad).</summary>
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
                Debug.LogWarning("ScreenManager.EnsureEventSystem: " + e.Message);
            }
        }

        /// <summary>Fills the full parent rect (anchors 0..1, zero offsets).</summary>
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
