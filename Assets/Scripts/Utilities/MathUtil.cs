// SnookerKit — small math helpers for camera/aim/input code (CONTRACTS §2). Angle helpers use shortest-arc
// semantics so wrapping (e.g. 350° → 10°) behaves sensibly; Gaussian uses a cached System.Random (main thread only).
using System;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Static math utilities: shortest-arc angle clamping/deltas, Box–Muller Gaussian noise,
    /// angle-aware MoveTowards, and a tiny sign helper. Only state is the cached RNG.</summary>
    public static class MathUtil
    {
        /// <summary>Clamps <paramref name="angle"/> into the [min, max] arc using shortest-arc semantics: when the
        /// interval wraps through 0° (min &gt; max after normalization) the wrapped span is the valid region, and an
        /// out-of-range angle snaps to the nearer boundary by angular distance. Returns the input unchanged when
        /// already inside (preserves the caller's unwrapped representation).</summary>
        public static float ClampAngle(float angle, float min, float max)
        {
            float a = Mathf.Repeat(angle, 360f);
            float lo = Mathf.Repeat(min, 360f);
            float hi = Mathf.Repeat(max, 360f);
            bool inside = lo <= hi ? (a >= lo && a <= hi) : (a >= lo || a <= hi);
            if (inside) return angle;
            float distToMin = Mathf.Abs(Mathf.DeltaAngle(a, lo));
            float distToMax = Mathf.Abs(Mathf.DeltaAngle(a, hi));
            return distToMin <= distToMax ? min : max;
        }

        /// <summary>Shortest signed delta from <paramref name="from"/> to <paramref name="to"/> in degrees (−180..180).</summary>
        public static float DeltaAngleDeg(float from, float to)
        {
            return Mathf.DeltaAngle(from, to);
        }

        /// <summary>Shortest signed delta from <paramref name="from"/> to <paramref name="to"/> in radians (−π..π).</summary>
        public static float DeltaAngleRad(float from, float to)
        {
            return Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        /// <summary>Normal-distributed sample with spare-value caching (one Box–Muller transform serves two draws,
        /// halving the trig cost). Uses a shared cached <see cref="System.Random"/> — main thread only (CONTRACTS
        /// §0; System.Random is not thread-safe). A non-positive sigma returns the mean unchanged.</summary>
        public static float Gaussian(float mean, float sigma)
        {
            if (sigma <= 0f) return mean;
            if (_hasSpare)
            {
                _hasSpare = false;
                return mean + sigma * (float)_spare;
            }
            double u1 = 1.0 - Rng.NextDouble(); // (0, 1] — keeps Log finite
            double u2 = Rng.NextDouble();
            double radius = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            _spare = radius * Math.Sin(theta);
            _hasSpare = true;
            return mean + sigma * (float)(radius * Math.Cos(theta));
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most
        /// <paramref name="maxDelta"/> along the shortest arc (degrees). Delegates to Mathf.MoveTowardsAngle.</summary>
        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            return Mathf.MoveTowardsAngle(current, target, maxDelta);
        }

        /// <summary>Returns +1 when <paramref name="positive"/> is true, otherwise −1 (aim/orbit sign helper).</summary>
        public static int PlusMinus(bool positive)
        {
            return positive ? 1 : -1;
        }

        private static readonly System.Random Rng = new System.Random();
        private static bool _hasSpare;
        private static double _spare;
    }
}
