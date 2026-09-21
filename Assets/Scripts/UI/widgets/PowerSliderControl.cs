// SnookerKit — HUD power meter widget (frozen, CONTRACTS §6 / PDF §11). 14 blocks + knob on a rounded track.
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Draggable power meter: rounded "PanelLight" track with in-end LOW/HIGH muted captions, 14 blocks
    /// (unfilled #242C27; filled AccentBright with the last 3 lerping to Gold) and a 36px white knob. The HUD wires
    /// PowerChanged (live) and PowerCommitted (release → fire). Images are cached in arrays; updates recolor in place
    /// — no per-frame allocations.</summary>
    public class PowerSliderControl : MonoBehaviour
    {
        private const int BlockCount = 14;
        private const float EndInset = 52f;   // reserved for the LOW/HIGH captions
        private const float BlockGap = 5f;
        private const float KnobSize = 36f;
        private const float DisabledAlpha = 0.45f;

        /// <summary>Track tint ("PanelLight" — slightly lighter than PanelBg so blocks read against it).</summary>
        private static readonly Color TrackColor = new Color(0x2A / 255f, 0x33 / 255f, 0x2D / 255f, 1f);
        /// <summary>Unfilled block color (frozen #242C27).</summary>
        private static readonly Color BlockUnfilled = new Color(0x24 / 255f, 0x2C / 255f, 0x27 / 255f, 1f);

        private readonly Image[] _blocks = new Image[BlockCount];
        private RectTransform _track;
        private RectTransform _knob;
        private CanvasGroup _group;
        private float _xMin;
        private float _xMax;
        private float _blockWidth;
        private float _value;
        private float _visualValue = -1f;
        private bool _interactable = true;

        /// <summary>Current power (0..1) — updated by drags and by Set01.</summary>
        public float Value { get { return _value; } }

        /// <summary>Raised while the user drags (live power, 0..1).</summary>
        public event Action<float> PowerChanged;

        /// <summary>Raised once on pointer release (HUD decides whether to fire).</summary>
        public event Action PowerCommitted;

        /// <summary>Builds the meter under this component's GameObject (size widthPx × heightPx, center-anchored).
        /// Returns the root GameObject for anchoring by the HUD.</summary>
        public GameObject Build(int widthPx, int heightPx)
        {
            if (widthPx < 120) widthPx = 120;
            if (heightPx < 40) heightPx = 40;

            RectTransform rt = transform as RectTransform;
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(widthPx, heightPx);
            rt.anchoredPosition = Vector2.zero;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;

            // Rounded track (the raycast receiver for all pointer input).
            var trackGO = new GameObject("Track", typeof(RectTransform));
            trackGO.transform.SetParent(transform, false);
            _track = (RectTransform)trackGO.transform;
            Stretch(_track);
            Image trackImg = trackGO.AddComponent<Image>();
            trackImg.sprite = UIFactory.RoundedSprite(64, 14);
            trackImg.type = Image.Type.Sliced;
            trackImg.pixelsPerUnitMultiplier = 1f;
            trackImg.color = TrackColor;
            trackImg.raycastTarget = true;

            float halfWidth = widthPx * 0.5f;
            _xMin = -halfWidth + EndInset;
            _xMax = halfWidth - EndInset;
            _blockWidth = ((_xMax - _xMin) - (BlockCount - 1) * BlockGap) / BlockCount;

            MakeCaption(trackGO.transform, "LOW", new Vector2(-halfWidth + EndInset * 0.5f, 0f));
            MakeCaption(trackGO.transform, "HIGH", new Vector2(halfWidth - EndInset * 0.5f, 0f));

            // 14 blocks (geometry fixed at Build time; Set01 only recolors).
            float blockHeight = heightPx * 0.56f;
            for (int i = 0; i < BlockCount; i++)
            {
                var blockGO = new GameObject("Block" + i, typeof(RectTransform));
                blockGO.transform.SetParent(trackGO.transform, false);
                RectTransform brt = (RectTransform)blockGO.transform;
                brt.anchorMin = new Vector2(0.5f, 0.5f);
                brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(_blockWidth, blockHeight);
                brt.anchoredPosition = new Vector2(_xMin + _blockWidth * 0.5f + i * (_blockWidth + BlockGap), 0f);
                Image img = blockGO.AddComponent<Image>();
                img.color = BlockUnfilled;
                img.raycastTarget = false;
                _blocks[i] = img;
            }

            // 36px white knob riding the value position.
            var knobGO = new GameObject("Knob", typeof(RectTransform));
            knobGO.transform.SetParent(trackGO.transform, false);
            _knob = (RectTransform)knobGO.transform;
            _knob.anchorMin = new Vector2(0.5f, 0.5f);
            _knob.anchorMax = new Vector2(0.5f, 0.5f);
            _knob.pivot = new Vector2(0.5f, 0.5f);
            _knob.sizeDelta = new Vector2(KnobSize, KnobSize);
            _knob.anchoredPosition = new Vector2(_xMin, 0f);
            Image knobImg = knobGO.AddComponent<Image>();
            knobImg.sprite = UIFactory.CircleSprite(64);
            knobImg.color = Color.white;
            knobImg.raycastTarget = false;

            // Pointer input (press anywhere on the track; drag continues via pointer capture).
            EventTrigger trigger = trackGO.AddComponent<EventTrigger>();
            AddEntry(trigger, EventTriggerType.PointerDown, e => OnPress((PointerEventData)e));
            AddEntry(trigger, EventTriggerType.Drag, e => OnDrag((PointerEventData)e));
            AddEntry(trigger, EventTriggerType.PointerUp, e => OnRelease((PointerEventData)e));

            UpdateVisuals(_value);
            return gameObject;
        }

        /// <summary>Sets the meter visuals (blocks + knob + Value) without raising any event — visual only.</summary>
        public void Set01(float v)
        {
            _value = Mathf.Clamp01(v);
            UpdateVisuals(_value);
        }

        /// <summary>Dims/disables input (used during AI turns and ball placement). Redundant calls are ignored.</summary>
        public void SetInteractable(bool on)
        {
            if (on == _interactable) return;
            _interactable = on;
            if (_group == null) return;
            _group.alpha = on ? 1f : DisabledAlpha;
            _group.interactable = on;
            _group.blocksRaycasts = on;
        }

        private void OnPress(PointerEventData e)
        {
            float v;
            if (!TryValueFromPointer(e, out v)) return;
            _value = v;
            UpdateVisuals(v);
            if (PowerChanged != null) PowerChanged(v);
        }

        private void OnDrag(PointerEventData e)
        {
            OnPress(e);
        }

        private void OnRelease(PointerEventData e)
        {
            if (PowerCommitted != null) PowerCommitted();
        }

        /// <summary>Maps a screen point onto the track's 0..1 range (overlay canvases pass a null event camera).</summary>
        private bool TryValueFromPointer(PointerEventData e, out float v)
        {
            v = _value;
            if (_track == null || e == null) return false;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, e.position, e.pressEventCamera, out local)) return false;
            float span = _xMax - _xMin;
            if (span <= 0.001f) return false;
            v = Mathf.Clamp01((local.x - _xMin) / span);
            return true;
        }

        /// <summary>Recolors blocks and repositions the knob; skips work when the value did not change.</summary>
        private void UpdateVisuals(float v)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(v, _visualValue)) return;
            _visualValue = v;

            int filled = v <= 0f ? 0 : Mathf.CeilToInt(v * BlockCount);
            for (int i = 0; i < BlockCount; i++)
            {
                Image block = _blocks[i];
                if (block == null) continue;
                if (i >= filled)
                {
                    block.color = BlockUnfilled;
                }
                else if (i >= BlockCount - 3)
                {
                    float t = (i - (BlockCount - 3) + 1) / 3f;
                    block.color = Color.Lerp(UITheme.AccentBright, UITheme.Gold, t);
                }
                else
                {
                    block.color = UITheme.AccentBright;
                }
            }

            if (_knob != null)
            {
                Vector2 p = _knob.anchoredPosition;
                p.x = Mathf.Lerp(_xMin, _xMax, v);
                _knob.anchoredPosition = p;
            }
        }

        /// <summary>Small muted in-end caption (LOW/HIGH).</summary>
        private static void MakeCaption(Transform parent, string content, Vector2 position)
        {
            Text t = UIFactory.Label(parent, content, 16, UITheme.TextMuted, TextAnchor.MiddleCenter);
            RectTransform rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(44f, 24f);
            rt.anchoredPosition = position;
        }

        /// <summary>Adds one EventTrigger entry (build-time helper).</summary>
        private static void AddEntry(EventTrigger trigger, EventTriggerType id, Action<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = id;
            entry.callback.AddListener(new UnityAction<BaseEventData>(callback));
            trigger.triggers.Add(entry);
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
