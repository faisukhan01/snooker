// SnookerKit — frame-time telemetry + honest device metrics (CONTRACTS §2/§0.8). The sliding window never
// allocates in Update; platform probes are wrapped in try/catch and unavailable values render as "N/A" —
// metrics are never fabricated (§0.8). Registered in ServiceRegistry for the Game Tools panel.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Measures frame timing over a 120-sample unscaled-delta window (first 30 frames skipped as warmup)
    /// and exposes honest battery/device/network text plus screenshot capture for the Game Tools panel.
    /// Properties are computed on demand from the ring buffer — polling sites should throttle (the panel does).</summary>
    public class PerformanceManager : MonoBehaviour
    {
        private const int WindowSize = 120;          // unscaled deltas kept in the sliding window
        private const int WarmupFrames = 30;         // scene-load spikes are unrepresentative — skip them
        private const int ScreenshotSettleFrames = 3; // ScreenCapture.CaptureScreenshot is deferred end-of-frame

        private readonly RingBufferFloat _deltas = new RingBufferFloat(WindowSize);
        private int _framesSeen;

        private void Awake()
        {
            ServiceRegistry.Register<PerformanceManager>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<PerformanceManager>();
        }

        private void Update()
        {
            _framesSeen++;
            if (_framesSeen <= WarmupFrames) return;
            _deltas.Add(Time.unscaledDeltaTime); // zero-alloc; unscaled = real frame cost, timescale-immune
        }

        /// <summary>Average FPS over the sliding window (1/mean delta, guarded div-by-0 → 0 while no data).</summary>
        public float FpsAverage
        {
            get
            {
                float average = _deltas.Average();
                return average > 0f ? 1f / average : 0f;
            }
        }

        /// <summary>Worst-second approximation: the FPS implied by the single slowest frame in the window
        /// (1/max delta, guarded div-by-0 → 0 while no data).</summary>
        public float FpsLow
        {
            get
            {
                float worst = _deltas.Max();
                return worst > 0f ? 1f / worst : 0f;
            }
        }

        /// <summary>Average frame time in milliseconds; 0 while no data has been collected.</summary>
        public float FrameMsAverage
        {
            get { return _deltas.Average() * 1000f; }
        }

        /// <summary>Battery readout, e.g. "86% · Charging" or "86%"; "N/A" when the platform does not expose a
        /// battery level (typical in the editor and on desktop).</summary>
        public string GetBatteryText()
        {
            try
            {
                float level = SystemInfo.batteryLevel; // 0..1, or -1 when unavailable
                if (level < 0f) return "N/A";
                if (level > 1f) level = 1f;
                int percent = Mathf.RoundToInt(level * 100f);
                BatteryStatus status = SystemInfo.batteryStatus;
                bool charging = status == BatteryStatus.Charging || status == BatteryStatus.Full;
                return charging ? percent + "% · Charging" : percent + "%";
            }
            catch (Exception)
            {
                return "N/A";
            }
        }

        /// <summary>Device model plus RAM (rounded GB), e.g. "Pixel 7 · 8 GB"; honest partials or "N/A" when
        /// the platform hides the values.</summary>
        public string GetDeviceText()
        {
            try
            {
                string model = SystemInfo.deviceModel;
                bool hasModel = !string.IsNullOrEmpty(model);
                int memoryMb = SystemInfo.systemMemorySize; // MB, or <= 0 when unknown
                bool hasMemory = memoryMb > 0;
                if (!hasModel && !hasMemory) return "N/A";
                string ram = hasMemory ? Mathf.Max(1, Mathf.RoundToInt(memoryMb / 1024f)) + " GB" : null;
                if (hasModel && ram != null) return model + " · " + ram;
                if (hasModel) return model;
                return ram;
            }
            catch (Exception)
            {
                return "N/A";
            }
        }

        /// <summary>"N/A (offline)" — v1 has no networking, and fabricated latency figures are forbidden (§0.8).</summary>
        public string GetNetworkText()
        {
            return "N/A (offline)";
        }

        /// <summary>Requests a screenshot, written to persistentDataPath as snooker_&lt;ticks&gt;.png. The capture is
        /// deferred by Unity to the end of frame, so the file is verified after a short settle and reported via
        /// GameEvents.ScreenshotCompleted with the full path — or null on any failure (never throws).</summary>
        public void CaptureScreenshot()
        {
            try
            {
                StartCoroutine(CaptureScreenshotRoutine());
            }
            catch (Exception)
            {
                GameEvents.RaiseScreenshotCompleted(new ScreenshotCompletedArgs { Path = null });
            }
        }

        private IEnumerator CaptureScreenshotRoutine()
        {
            string fileName = "snooker_" + DateTime.Now.Ticks + ".png";
            try
            {
                ScreenCapture.CaptureScreenshot(fileName);
            }
            catch (Exception)
            {
                GameEvents.RaiseScreenshotCompleted(new ScreenshotCompletedArgs { Path = null });
                yield break;
            }

            for (int i = 0; i < ScreenshotSettleFrames; i++) yield return null;

            string fullPath = null;
            try
            {
                fullPath = Path.Combine(Application.persistentDataPath, fileName);
                if (!File.Exists(fullPath)) fullPath = null; // honest null — do not report a file that is not there
            }
            catch (Exception)
            {
                fullPath = null;
            }

            GameEvents.RaiseScreenshotCompleted(new ScreenshotCompletedArgs { Path = fullPath });
        }
    }
}
