// SnookerKit — UI theme tokens, font provider and widget factory (frozen API, CONTRACTS §2/§8).
// uGUI Text only (UnityEngine.UI) — classic Input only, no external UI packages.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Font weights used by the UI (mapped to bundled Inter files, OS fonts or the legacy runtime font).</summary>
    public enum FontWeight
    {
        /// <summary>Body / caption text.</summary>
        Regular = 0,
        /// <summary>Buttons, titles, toast text.</summary>
        SemiBold = 1,
        /// <summary>Emphasis.</summary>
        Bold = 2,
    }

    /// <summary>Resolves a Font per weight: bundled Resources fonts first (Fonts/Inter-*), then OS fonts
    /// (Roboto → Inter → HelveticaNeue → Arial), finally the LegacyRuntime builtin. Results are cached per weight;
    /// the chain never throws and practically always yields a usable font.</summary>
    public static class FontProvider
    {
        private static readonly string[] ResourceNames = { "Fonts/Inter-Regular", "Fonts/Inter-SemiBold", "Fonts/Inter-Bold" };
        private static readonly string[] OSFallbacks = { "Roboto", "Inter", "HelveticaNeue", "Arial" };
        private const int DefaultCreationSize = 32;

        private static readonly Font[] Cache = new Font[3];
        private static string[] _installedNames;

        /// <summary>Returns a cached Font for the requested weight (never null in practice; never throws).</summary>
        public static Font Get(FontWeight weight)
        {
            int i = (int)weight;
            if (i < 0 || i > 2) i = 0;

            Font f = Cache[i];
            if (f != null) return f;

            try { f = Resources.Load<Font>(ResourceNames[i]); }
            catch (System.Exception) { f = null; }

            if (f == null) f = CreateFromOS();

            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { f = null; }
            }

            Cache[i] = f;
            return f;
        }

        /// <summary>Creates a dynamic font from the first installed OS font that matches the fallback list (contains-match).</summary>
        private static Font CreateFromOS()
        {
            try
            {
                if (_installedNames == null) _installedNames = Font.GetOSInstalledFontNames();
                if (_installedNames == null) return null;

                for (int i = 0; i < OSFallbacks.Length; i++)
                {
                    for (int j = 0; j < _installedNames.Length; j++)
                    {
                        if (string.IsNullOrEmpty(_installedNames[j])) continue;
                        if (_installedNames[j].IndexOf(OSFallbacks[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                            return Font.CreateDynamicFontFromOSFont(OSFallbacks[i], DefaultCreationSize);
                    }
                }
            }
            catch (System.Exception) { /* OS font enumeration unavailable — fall through to LegacyRuntime. */ }
            return null;
        }
    }

    /// <summary>Palette + button styles for one button. Frozen shape (Normal/Pressed/Text); the three named styles
    /// map onto the UITheme tokens.</summary>
    public struct BtnStyle
    {
        /// <summary>Resting background color.</summary>
        public Color Normal;
        /// <summary>Background color while pressed.</summary>
        public Color Pressed;
        /// <summary>Label color.</summary>
        public Color Text;

        /// <summary>Accent-filled primary action.</summary>
        public static BtnStyle Primary
        {
            get
            {
                var s = new BtnStyle();
                s.Normal = UITheme.Accent;
                s.Pressed = Color.Lerp(UITheme.Accent, Color.black, 0.18f);
                s.Text = UITheme.TextPrimary;
                return s;
            }
        }

        /// <summary>Charcoal panel action (secondary).</summary>
        public static BtnStyle Dark
        {
            get
            {
                var s = new BtnStyle();
                s.Normal = UITheme.PanelBg;
                s.Pressed = Color.Lerp(UITheme.PanelBg, Color.black, 0.25f);
                s.Text = UITheme.TextPrimary;
                return s;
            }
        }

        /// <summary>Nearly transparent action (tertiary).</summary>
        public static BtnStyle Ghost
        {
            get
            {
                var s = new BtnStyle();
                s.Normal = WithAlpha(Color.white, 0.06f);
                s.Pressed = WithAlpha(Color.white, 0.12f);
                s.Text = UITheme.TextPrimary;
                return s;
            }
        }
    }

    /// <summary>Design tokens (CONTRACTS §8): charcoal panels, muted green accent, gold highlights, light text.</summary>
    public static class UITheme
    {
        /// <summary>Charcoal panel background (α 0.92).</summary>
        public static readonly Color PanelBg = new Color(0x14 / 255f, 0x1A / 255f, 0x17 / 255f, 0.92f);
        /// <summary>Muted green accent.</summary>
        public static readonly Color Accent = new Color(0x3E / 255f, 0x7C / 255f, 0x59 / 255f, 1f);
        /// <summary>Bright accent (filled meter blocks, positive toast edge).</summary>
        public static readonly Color AccentBright = new Color(0x59 / 255f, 0xA9 / 255f, 0x7A / 255f, 1f);
        /// <summary>Gold highlight (top power blocks, info toast edge).</summary>
        public static readonly Color Gold = new Color(0xD9 / 255f, 0xA4 / 255f, 0x41 / 255f, 1f);
        /// <summary>Primary text.</summary>
        public static readonly Color TextPrimary = new Color(0xF2 / 255f, 0xF5 / 255f, 0xF3 / 255f, 1f);
        /// <summary>Muted text (captions, LOW/HIGH markers).</summary>
        public static readonly Color TextMuted = new Color(0.72f, 0.76f, 0.73f, 0.85f);
        /// <summary>Danger (fouls, bad toast edge).</summary>
        public static readonly Color Danger = new Color(0xC0 / 255f, 0x5B / 255f, 0x4D / 255f, 1f);
    }

    /// <summary>Procedural uGUI widget factory. All sprites are generated deterministically (Texture2D loops, no
    /// Random) and cached in static fields. Default RectTransforms are layout-friendly: Label/Panel stretch to fill
    /// their parent, Btn docks to the top edge and stretches horizontally.</summary>
    public static class UIFactory
    {
        private static readonly Dictionary<int, Sprite> RoundedCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> CircleCache = new Dictionary<int, Sprite>();

        /// <summary>Creates a uGUI Text label (raycast off). Returns the Text so callers can update content.</summary>
        public static Text Label(Transform parent, string content, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);

            Text t = go.AddComponent<Text>();
            t.font = FontProvider.Get(FontWeight.Regular);
            t.fontSize = fontSize < 1 ? 1 : fontSize;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.text = content != null ? content : string.Empty;
            return t;
        }

        /// <summary>Creates a child panel image. When <paramref name="rounded"/> the 9-slice RoundedSprite is used
        /// (Image.Type.Sliced). Final alpha is color.a × alpha so baked token alpha (e.g. PanelBg 0.92) survives.</summary>
        public static Image Panel(Transform parent, string name, Color color, float alpha = 1f, bool rounded = true)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Panel" : name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = true;
            if (rounded)
            {
                img.sprite = RoundedSprite(64, 14);
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            Color c = color;
            c.a = Mathf.Clamp01(color.a * alpha);
            img.color = c;
            return img;
        }

        /// <summary>Creates a rounded button. Default size = top-docked stretch-x, 88px high; an explicit size is
        /// center-anchored. Press animation scales to 0.97 via EventTrigger; clicks play the UI click sfx before the
        /// caller's action runs.</summary>
        public static Button Btn(Transform parent, string label, BtnStyle style, System.Action onClick, int fontSize = 34, Vector2 size = default(Vector2))
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;

            if (size.x <= 0f && size.y <= 0f)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, 88f);
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = Vector2.zero;
            }

            Image img = go.AddComponent<Image>();
            img.sprite = RoundedSprite(64, 14);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = true;

            Button btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = style.Normal;
            Color hi = Color.Lerp(style.Normal, Color.white, 0.08f);
            hi.a = style.Normal.a;
            cb.highlightedColor = hi;
            cb.pressedColor = style.Pressed;
            cb.selectedColor = style.Normal;
            cb.disabledColor = WithAlpha(style.Normal, 0.5f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            // Label (verbatim — screens own their wording) + semi-bold face.
            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)lgo.transform);
            Text t = lgo.AddComponent<Text>();
            t.font = FontProvider.Get(FontWeight.SemiBold);
            t.fontSize = fontSize < 1 ? 1 : fontSize;
            t.color = style.Text;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.text = label != null ? label : string.Empty;

            // Press-scale animation.
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            AddEntry(trigger, EventTriggerType.PointerDown, e => { rt.localScale = new Vector3(0.97f, 0.97f, 1f); });
            AddEntry(trigger, EventTriggerType.PointerUp, e => { rt.localScale = Vector3.one; });
            AddEntry(trigger, EventTriggerType.PointerExit, e => { rt.localScale = Vector3.one; });

            if (onClick != null)
                btn.onClick.AddListener(() => { PlayPress(); onClick(); });

            return btn;
        }

        /// <summary>Returns the cached 9-slice rounded-rect sprite (SDF-ish AA edges, 16px border, 1 px-per-unit so the
        /// border maps to 16 canvas units at pixelsPerUnitMultiplier 1). Deterministic generation, cached per size/radius.</summary>
        public static Sprite RoundedSprite(int sizePx = 64, int radiusPx = 14)
        {
            if (sizePx < 8) sizePx = 8;
            if (radiusPx < 1) radiusPx = 1;
            if (radiusPx > sizePx / 2) radiusPx = sizePx / 2;

            int key = sizePx * 1000 + radiusPx;
            Sprite cached;
            if (RoundedCache.TryGetValue(key, out cached) && cached != null) return cached;

            var tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false);
            tex.name = "RoundedRect_" + sizePx + "_" + radiusPx;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] px = new Color32[sizePx * sizePx];
            float half = sizePx * 0.5f;
            float r = radiusPx;
            for (int y = 0; y < sizePx; y++)
            {
                for (int x = 0; x < sizePx; x++)
                {
                    float ox = x + 0.5f - half;
                    float oy = y + 0.5f - half;
                    float qx = Mathf.Abs(ox) - (half - r);
                    float qy = Mathf.Abs(oy) - (half - r);
                    float dx = Mathf.Max(qx, 0f);
                    float dy = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * sizePx + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            int border = 16;
            if (border > sizePx / 2) border = sizePx / 2;
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, sizePx, sizePx), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            RoundedCache[key] = sprite;
            return sprite;
        }

        /// <summary>Returns the cached anti-aliased circle sprite (deterministic, cached per size).</summary>
        public static Sprite CircleSprite(int sizePx = 64)
        {
            if (sizePx < 8) sizePx = 8;

            Sprite cached;
            if (CircleCache.TryGetValue(sizePx, out cached) && cached != null) return cached;

            var tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false);
            tex.name = "Circle_" + sizePx;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] px = new Color32[sizePx * sizePx];
            float half = sizePx * 0.5f;
            for (int y = 0; y < sizePx; y++)
            {
                for (int x = 0; x < sizePx; x++)
                {
                    float ox = x + 0.5f - half;
                    float oy = y + 0.5f - half;
                    float len = Mathf.Sqrt(ox * ox + oy * oy);
                    float a = Mathf.Clamp01(half - len + 0.5f);
                    px[y * sizePx + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, sizePx, sizePx), new Vector2(0.5f, 0.5f), 1f);
            CircleCache[sizePx] = sprite;
            return sprite;
        }

        /// <summary>Returns <paramref name="c"/> with its alpha replaced (clamped 0..1).</summary>
        public static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        }

        /// <summary>Creates a layout spacer child (LayoutElement with the given preferred height).</summary>
        public static void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height < 0f ? 0f : height;
            le.minHeight = le.preferredHeight;
        }

        /// <summary>Plays the UI click sfx through the registered AudioManager (silent no-op when absent).</summary>
        public static void PlayPress()
        {
            if (ServiceRegistry.TryGet<AudioManager>(out AudioManager audio) && audio != null)
                audio.Play(SfxKey.UiClick);
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

        /// <summary>Adds one EventTrigger entry (build-time helper; wraps the callback in a UnityAction).</summary>
        private static void AddEntry(EventTrigger trigger, EventTriggerType id, System.Action<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = id;
            entry.callback.AddListener(new UnityAction<BaseEventData>(callback));
            trigger.triggers.Add(entry);
        }
    }

    /// <summary>Applies Screen.safeArea to its RectTransform via anchors (offsets zeroed) so UI roots respect notches
    /// and rounded corners. Re-applies on rect-dimension changes and via a cheap 0.5 s poll (orientation swaps on some
    /// platforms do not fire the message for plain MonoBehaviours). Struct compares only — no per-frame allocations.</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private float _pollTimer;

        private void OnEnable()
        {
            _pollTimer = 0f;
            Apply();
        }

        private void Update()
        {
            _pollTimer += Time.unscaledDeltaTime;
            if (_pollTimer < 0.5f) return;
            _pollTimer = 0f;
            Rect sa = Screen.safeArea;
            if (sa != _lastSafeArea || Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        /// <summary>Converts the current safe area into anchor bounds on the attached RectTransform.</summary>
        private void Apply()
        {
            if (this == null) return;
            RectTransform rt = transform as RectTransform;
            if (rt == null) return;

            Rect sa = Screen.safeArea;
            float w = Screen.width;
            float h = Screen.height;
            if (w <= 0f || h <= 0f) return;

            Vector2 min = sa.position;
            Vector2 max = min + sa.size;
            rt.anchorMin = new Vector2(min.x / w, min.y / h);
            rt.anchorMax = new Vector2(max.x / w, max.y / h);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _lastSafeArea = sa;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }
    }
}
