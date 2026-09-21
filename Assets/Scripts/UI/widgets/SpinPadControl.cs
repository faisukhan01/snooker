// SnookerKit — circular cue-ball spin pad widget (frozen, CONTRACTS §6 / PDF §12).
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Spin pad: a 348×340 root hosting a 264px dark circle (α 0.97), a generated 256px ring texture
    /// (6px outline at α 0.25), a 190px #F2F5F3 ball disc and a 36px AccentBright dot riding an invisible 88px touch
    /// handle. Dragging the handle (clamped to 0.8 · disc radius) sets Spin in −1..1 (+y = top spin). Muted
    /// TOP/BACK/LEFT/RIGHT captions sit outside the ring. Deterministic textures, cached statics, zero per-frame allocs.</summary>
    public class SpinPadControl : MonoBehaviour
    {
        private const float RootWidth = 348f;
        private const float RootHeight = 340f;
        private const float PadSize = 264f;
        private const float RingSize = 256f;
        private const float DiscSize = 190f;
        private const float DotSize = 36f;
        private const float HandleSize = 88f;
        private const float MaxOffsetFactor = 0.8f;
        private const float PopSeconds = 0.12f;

        private static Sprite _ringSprite; // generated once, deterministic

        private RectTransform _root;
        private RectTransform _handle;
        private float _maxOffset;
        private Vector2 _spin;
        private Coroutine _popRoutine;

        /// <summary>Current spin (x = side, y = vertical/top-back; each −1..1).</summary>
        public Vector2 Spin { get { return _spin; } }

        /// <summary>Raised whenever the spin value changes (drag or ResetPad).</summary>
        public event Action<Vector2> SpinChanged;

        /// <summary>Builds the pad centered at <paramref name="anchorPos"/> in its parent. Returns the root GameObject
        /// so the HUD can re-anchor it.</summary>
        public GameObject Build(Vector2 anchorPos)
        {
            RectTransform rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            _root = rt;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RootWidth, RootHeight);
            rt.anchoredPosition = anchorPos;

            _maxOffset = MaxOffsetFactor * (DiscSize * 0.5f);

            // 264px dark circle (α 0.97).
            var padGO = new GameObject("PadCircle", typeof(RectTransform));
            padGO.transform.SetParent(transform, false);
            RectTransform padRT = (RectTransform)padGO.transform;
            CenterRect(padRT, PadSize);
            Image padImg = padGO.AddComponent<Image>();
            padImg.sprite = UIFactory.CircleSprite(64);
            padImg.color = UIFactory.WithAlpha(UITheme.PanelBg, 0.97f);
            padImg.raycastTarget = false;

            // 256px generated ring (6px outline, α 0.25).
            var ringGO = new GameObject("Ring", typeof(RectTransform));
            ringGO.transform.SetParent(transform, false);
            RectTransform ringRT = (RectTransform)ringGO.transform;
            CenterRect(ringRT, RingSize);
            Image ringImg = ringGO.AddComponent<Image>();
            ringImg.sprite = RingSprite();
            ringImg.color = Color.white;
            ringImg.raycastTarget = false;

            // 190px ball disc.
            var discGO = new GameObject("BallDisc", typeof(RectTransform));
            discGO.transform.SetParent(transform, false);
            RectTransform discRT = (RectTransform)discGO.transform;
            CenterRect(discRT, DiscSize);
            Image discImg = discGO.AddComponent<Image>();
            discImg.sprite = UIFactory.CircleSprite(64);
            discImg.color = UITheme.TextPrimary; // #F2F5F3
            discImg.raycastTarget = false;

            // Captions outside the ring.
            MakeCaption(transform, "TOP", new Vector2(0f, 152f));
            MakeCaption(transform, "BACK", new Vector2(0f, -152f));
            MakeCaption(transform, "LEFT", new Vector2(-152f, 0f));
            MakeCaption(transform, "RIGHT", new Vector2(152f, 0f));

            // Invisible 88px touch handle with the 36px accent dot rendered behind it.
            var handleGO = new GameObject("Handle", typeof(RectTransform));
            handleGO.transform.SetParent(transform, false);
            _handle = (RectTransform)handleGO.transform;
            CenterRect(_handle, HandleSize);
            Image handleImg = handleGO.AddComponent<Image>();
            handleImg.color = new Color(0f, 0f, 0f, 0f);
            handleImg.raycastTarget = true; // the touch target

            var dotGO = new GameObject("Dot", typeof(RectTransform));
            dotGO.transform.SetParent(handleGO.transform, false);
            RectTransform dotRT = (RectTransform)dotGO.transform;
            CenterRect(dotRT, DotSize);
            Image dotImg = dotGO.AddComponent<Image>();
            dotImg.sprite = UIFactory.CircleSprite(64);
            dotImg.color = UITheme.AccentBright;
            dotImg.raycastTarget = false;

            // Pointer input lives on the handle (drag continues via pointer capture after the initial press).
            EventTrigger trigger = handleGO.AddComponent<EventTrigger>();
            AddEntry(trigger, EventTriggerType.PointerDown, e => ApplyPointer((PointerEventData)e));
            AddEntry(trigger, EventTriggerType.Drag, e => ApplyPointer((PointerEventData)e));

            return gameObject;
        }

        /// <summary>Recenters the dot and reports zero spin.</summary>
        public void ResetPad()
        {
            _spin = Vector2.zero;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            if (SpinChanged != null) SpinChanged(_spin);
        }

        /// <summary>Shows the pad with a short 0.92 → 1.0 scale pop (unscaled time).</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            if (_popRoutine != null) StopCoroutine(_popRoutine);
            _popRoutine = StartCoroutine(PopRoutine());
        }

        /// <summary>Hides the pad.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>Toggles pad visibility.</summary>
        public void Toggle()
        {
            if (gameObject.activeSelf) Hide();
            else Show();
        }

        /// <summary>Clamps the pointer into the 0.8 · radius range and updates the spin value.</summary>
        private void ApplyPointer(PointerEventData e)
        {
            if (_root == null || _handle == null || e == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, e.position, e.pressEventCamera, out local)) return;

            Vector2 clamped = local;
            float mag = clamped.magnitude;
            if (mag > _maxOffset) clamped *= _maxOffset / mag;

            _handle.anchoredPosition = clamped;
            Vector2 next = new Vector2(clamped.x / _maxOffset, clamped.y / _maxOffset);
            if (next != _spin)
            {
                _spin = next;
                if (SpinChanged != null) SpinChanged(_spin);
            }
        }

        /// <summary>Short pop-in animation on unscaled time.</summary>
        private IEnumerator PopRoutine()
        {
            RectTransform rt = transform as RectTransform;
            if (rt == null) yield break;

            float t = 0f;
            while (t < PopSeconds)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(0.92f, 1f, Mathf.Clamp01(t / PopSeconds));
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            _popRoutine = null;
        }

        /// <summary>Returns the cached 256px ring sprite (white outline, 6px band at α 0.25, AA edges).</summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int size = 256;
            const float radius = 124f;
            const float halfBand = 3f; // 6px outline
            const float alpha = 0.25f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "SpinPadRing";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(halfBand - Mathf.Abs(dist - radius) + 0.5f) * alpha;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            _ringSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 1f);
            return _ringSprite;
        }

        /// <summary>Small muted caption (TOP/BACK/LEFT/RIGHT).</summary>
        private static void MakeCaption(Transform parent, string content, Vector2 position)
        {
            Text t = UIFactory.Label(parent, content, 16, UITheme.TextMuted, TextAnchor.MiddleCenter);
            RectTransform rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(70f, 22f);
            rt.anchoredPosition = position;
        }

        /// <summary>Centers a rect at the parent's center with a square size.</summary>
        private static void CenterRect(RectTransform rt, float size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Adds one EventTrigger entry (build-time helper).</summary>
        private static void AddEntry(EventTrigger trigger, EventTriggerType id, Action<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = id;
            entry.callback.AddListener(new UnityAction<BaseEventData>(callback));
            trigger.triggers.Add(entry);
        }
    }
}
