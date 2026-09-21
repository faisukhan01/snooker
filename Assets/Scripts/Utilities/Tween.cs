// SnookerKit — static coroutine tween factory (CONTRACTS §2). Nothing global: tweens run on the caller's host
// MonoBehaviour (they die with it), use scaled time, and always deliver the exact end value before onDone fires.
using System;
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Allocation-lean tween helpers built on host.StartCoroutine. If the host is missing, the tween
    /// completes immediately (end value + onDone) and null is returned; if the host dies mid-tween, the
    /// coroutine dies with it — no static state to leak. Callers own raycast blocking for fades.</summary>
    public static class Tween
    {
        /// <summary>Tweens a float from → to over <paramref name="seconds"/> (scaled time). <paramref name="ease"/>
        /// defaults to linear. The final update call always receives exactly <paramref name="to"/>; then onDone fires.</summary>
        public static Coroutine Value(MonoBehaviour host, float from, float to, float seconds, Action<float> onUpdate, Func<float, float> ease = null, Action onDone = null)
        {
            if (host == null)
            {
                if (onUpdate != null) onUpdate(to);
                if (onDone != null) onDone();
                return null;
            }
            try
            {
                return host.StartCoroutine(ValueRoutine(from, to, seconds, onUpdate, ease, onDone));
            }
            catch (Exception)
            {
                return null; // host inactive — cannot run coroutines
            }
        }

        /// <summary>Tweens a Vector3 from → to over <paramref name="seconds"/> (scaled time). <paramref name="ease"/>
        /// defaults to linear. The final update call always receives exactly <paramref name="to"/>; then onDone fires.</summary>
        public static Coroutine Vector(MonoBehaviour host, Vector3 from, Vector3 to, float seconds, Action<Vector3> onUpdate, Func<float, float> ease = null, Action onDone = null)
        {
            if (host == null)
            {
                if (onUpdate != null) onUpdate(to);
                if (onDone != null) onDone();
                return null;
            }
            try
            {
                return host.StartCoroutine(VectorRoutine(from, to, seconds, onUpdate, ease, onDone));
            }
            catch (Exception)
            {
                return null; // host inactive — cannot run coroutines
            }
        }

        /// <summary>Fades a CanvasGroup's alpha from → to over <paramref name="seconds"/> (linear). Stops quietly
        /// if the group is destroyed mid-fade. BlocksRaycasts stays the caller's responsibility.</summary>
        public static Coroutine Fade(MonoBehaviour host, CanvasGroup group, float from, float to, float seconds, Action onDone = null)
        {
            if (host == null || group == null)
            {
                if (group != null) group.alpha = to;
                if (onDone != null) onDone();
                return null;
            }
            try
            {
                return host.StartCoroutine(FadeRoutine(group, from, to, seconds, onDone));
            }
            catch (Exception)
            {
                return null; // host inactive — cannot run coroutines
            }
        }

        private static IEnumerator ValueRoutine(float from, float to, float seconds, Action<float> onUpdate, Func<float, float> ease, Action onDone)
        {
            if (seconds <= 0f)
            {
                if (onUpdate != null) onUpdate(to);
                if (onDone != null) onDone();
                yield break;
            }
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime / seconds;
                bool finished = t >= 1f;
                if (onUpdate != null)
                {
                    if (finished) onUpdate(to); // exact end value — bypasses the ease on the last step
                    else onUpdate(Mathf.LerpUnclamped(from, to, ease != null ? ease(t) : t));
                }
                if (finished) break;
                yield return null;
            }
            if (onDone != null) onDone();
        }

        private static IEnumerator VectorRoutine(Vector3 from, Vector3 to, float seconds, Action<Vector3> onUpdate, Func<float, float> ease, Action onDone)
        {
            if (seconds <= 0f)
            {
                if (onUpdate != null) onUpdate(to);
                if (onDone != null) onDone();
                yield break;
            }
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime / seconds;
                bool finished = t >= 1f;
                if (onUpdate != null)
                {
                    if (finished) onUpdate(to); // exact end value — bypasses the ease on the last step
                    else onUpdate(Vector3.LerpUnclamped(from, to, ease != null ? ease(t) : t));
                }
                if (finished) break;
                yield return null;
            }
            if (onDone != null) onDone();
        }

        private static IEnumerator FadeRoutine(CanvasGroup group, float from, float to, float seconds, Action onDone)
        {
            if (seconds <= 0f)
            {
                group.alpha = to;
                if (onDone != null) onDone();
                yield break;
            }
            float t = 0f;
            while (true)
            {
                if (group == null) yield break; // group destroyed mid-fade — stop quietly
                t += Time.deltaTime / seconds;
                bool finished = t >= 1f;
                group.alpha = finished ? to : Mathf.LerpUnclamped(from, to, t);
                if (finished) break;
                yield return null;
            }
            if (onDone != null) onDone();
        }

        /// <summary>Decelerating cubic ease — fast start, smooth settle into the end value.</summary>
        public static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>Symmetric quadratic ease-in-out.</summary>
        public static float EaseInOutQuad(float t)
        {
            return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
        }

        /// <summary>Back ease with a slight (~10%) overshoot before settling — for playful UI pops.</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
