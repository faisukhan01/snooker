// SnookerKit — settings load/apply/persist service (CONTRACTS §2). Set* helpers apply immediately and queue a
// 1 s debounced save so slider spam never hammers the disk; pause/quit/disable flush right away (mobile lifecycle).
using System;
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns the live <see cref="SettingsData"/>, applies it to engine/platform state, and persists it
    /// through <see cref="SaveManager"/> under the "settings" key. Added to the AppServices object by
    /// <see cref="AppServices"/>; registered in <see cref="ServiceRegistry"/> for cross-module access.</summary>
    public class SettingsService : MonoBehaviour
    {
        private const float SaveDebounceSeconds = 1f;
        private const float MinAimSensitivity = 0.1f;
        private const float MaxAimSensitivity = 5f;
        private const int MinTargetFps = 30;
        private const int MaxTargetFps = 120;

        private SettingsData _settings = new SettingsData();
        private Coroutine _saveDebounce;

        /// <summary>Live settings object. Replaced once in Awake when a saved copy exists; consumers read it
        /// freely, and change values through the Set* helpers so changes apply and persist.</summary>
        public SettingsData Settings { get { return _settings; } }

        private void Awake()
        {
            LoadFromDisk();
            ApplyLoadedSettings();
            ServiceRegistry.Register<SettingsService>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<SettingsService>();
        }

        /// <summary>Pushes the current settings into engine/platform state. Each subsystem (quality, frame rate,
        /// audio) is isolated in its own try/catch — one failure never blocks the others.</summary>
        public void ApplyLoadedSettings()
        {
            try
            {
                int maxLevel = QualitySettings.names != null ? QualitySettings.names.Length - 1 : 0;
                if (maxLevel < 0) maxLevel = 0;
                int quality = Mathf.Clamp(_settings.graphicsQuality, 0, maxLevel);
                _settings.graphicsQuality = quality; // keep the stored value honest (clamped)
                QualitySettings.SetQualityLevel(quality, true);
            }
            catch (Exception)
            {
                // Quality system unavailable on this platform — skip.
            }

            try
            {
                Application.targetFrameRate = ClampTargetFps(_settings.targetFps);
            }
            catch (Exception)
            {
                // targetFrameRate is only a hint; some platforms ignore it.
            }

            try
            {
                AudioListener.volume = Mathf.Clamp01(_settings.masterVolume);
            }
            catch (Exception)
            {
                // No audio device — skip.
            }

            try
            {
                AudioManager audioManager;
                if (ServiceRegistry.TryGet<AudioManager>(out audioManager) && audioManager != null)
                {
                    audioManager.SetVolumes(_settings.masterVolume, _settings.sfxVolume);
                }
            }
            catch (Exception)
            {
                // AudioManager is optional; master volume still applied via AudioListener.volume above.
            }
        }

        /// <summary>Persists the current settings ("settings" key) and re-applies them immediately.</summary>
        public void SaveSettings()
        {
            SaveManager saveManager = ResolveSaveManager();
            if (saveManager != null) saveManager.Save("settings", _settings);
            ApplyLoadedSettings();
        }

        /// <summary>Sets master volume (0..1). Applies immediately; persists via the 1 s debounce.</summary>
        public void SetMasterVolume(float v)
        {
            _settings.masterVolume = Mathf.Clamp01(v);
            ApplyVolumes();
            QueueDebouncedSave();
        }

        /// <summary>Sets sfx volume (0..1). Applies immediately; persists via the 1 s debounce.</summary>
        public void SetSfxVolume(float v)
        {
            _settings.sfxVolume = Mathf.Clamp01(v);
            ApplyVolumes();
            QueueDebouncedSave();
        }

        /// <summary>Enables/disables the ambience loop. Turning it on starts the loop right away (AudioManager
        /// re-reads this flag through its own gate); v1 pins no stop API, so turning it off takes effect at the
        /// next gate read. Persists via the 1 s debounce.</summary>
        public void SetAmbienceOn(bool on)
        {
            _settings.ambienceOn = on;
            if (on)
            {
                try
                {
                    AudioManager audioManager;
                    if (ServiceRegistry.TryGet<AudioManager>(out audioManager) && audioManager != null)
                    {
                        audioManager.StartAmbience(); // expected to no-op safely when already playing
                    }
                }
                catch (Exception)
                {
                    // Ambience is optional decoration — never fatal.
                }
            }
            QueueDebouncedSave();
        }

        /// <summary>Enables/disables haptics. <see cref="Haptics"/> checks this flag on every call, so it applies
        /// immediately by construction; persists via the 1 s debounce.</summary>
        public void SetHapticsOn(bool on)
        {
            _settings.hapticsOn = on;
            QueueDebouncedSave();
        }

        /// <summary>Sets aim assist level, clamped 0..3 (see <see cref="AimAssistLevel"/>). Takes an int so UI
        /// callers may pass either an int or a cast enum (implicit enum→int conversion). Read per use by input;
        /// persists via the 1 s debounce.</summary>
        public void SetAimAssist(int level)
        {
            _settings.aimAssist = Mathf.Clamp(level, 0, 3);
            QueueDebouncedSave();
        }

        /// <summary>Sets aim drag sensitivity, clamped 0.1..5. Read per use by input; persists via the 1 s debounce.</summary>
        public void SetAimSensitivity(float s)
        {
            _settings.aimSensitivity = Mathf.Clamp(s, MinAimSensitivity, MaxAimSensitivity);
            QueueDebouncedSave();
        }

        /// <summary>Sets the quality level, clamped to the platform's available levels, and applies it immediately;
        /// persists via the 1 s debounce.</summary>
        public void SetGraphicsQuality(int q)
        {
            try
            {
                int maxLevel = QualitySettings.names != null ? QualitySettings.names.Length - 1 : 0;
                if (maxLevel < 0) maxLevel = 0;
                int quality = Mathf.Clamp(q, 0, maxLevel);
                _settings.graphicsQuality = quality;
                QualitySettings.SetQualityLevel(quality, true);
            }
            catch (Exception)
            {
                // Quality system unavailable — keep the stored value as-is.
            }
            QueueDebouncedSave();
        }

        /// <summary>Sets the target frame rate, clamped 30..120, and applies it immediately; persists via the
        /// 1 s debounce.</summary>
        public void SetTargetFps(int fps)
        {
            _settings.targetFps = ClampTargetFps(fps);
            try
            {
                Application.targetFrameRate = _settings.targetFps;
            }
            catch (Exception)
            {
                // targetFrameRate is only a hint; some platforms ignore it.
            }
            QueueDebouncedSave();
        }

        /// <summary>Shows/hides the aim trajectory line. Consumers read the flag per use; persists via the 1 s debounce.</summary>
        public void SetShowTrajectory(bool on)
        {
            _settings.showTrajectory = on;
            QueueDebouncedSave();
        }

        /// <summary>Loads the persisted settings. SaveManager is a sibling component on the AppServices object,
        /// but this is deliberately null-safe (falls back to the registry, then to field defaults).</summary>
        private void LoadFromDisk()
        {
            SaveManager saveManager = ResolveSaveManager();
            if (saveManager == null) return;
            SettingsData loaded = saveManager.Load<SettingsData>("settings"); // never throws, never null
            if (loaded != null) _settings = loaded;
        }

        /// <summary>Applies just the volume pair (shared by both volume setters).</summary>
        private void ApplyVolumes()
        {
            try
            {
                AudioListener.volume = Mathf.Clamp01(_settings.masterVolume);
            }
            catch (Exception)
            {
                // No audio device — skip.
            }

            try
            {
                AudioManager audioManager;
                if (ServiceRegistry.TryGet<AudioManager>(out audioManager) && audioManager != null)
                {
                    audioManager.SetVolumes(_settings.masterVolume, _settings.sfxVolume);
                }
            }
            catch (Exception)
            {
                // AudioManager is optional.
            }
        }

        /// <summary>(Re)starts the 1 s debounced save; repeated calls inside the window collapse into one write.</summary>
        private void QueueDebouncedSave()
        {
            if (_saveDebounce != null)
            {
                StopCoroutine(_saveDebounce);
                _saveDebounce = null;
            }
            if (!isActiveAndEnabled)
            {
                SaveSettings(); // cannot run coroutines while disabled — write now instead
                return;
            }
            _saveDebounce = StartCoroutine(DebouncedSaveRoutine());
        }

        private IEnumerator DebouncedSaveRoutine()
        {
            yield return new WaitForSeconds(SaveDebounceSeconds);
            _saveDebounce = null;
            SaveSettings();
        }

        /// <summary>Cancels a pending debounced save and writes immediately (pause/quit/disable flush).
        /// Never starts a coroutine, so it is safe during teardown.</summary>
        private void FlushNow()
        {
            if (_saveDebounce != null)
            {
                StopCoroutine(_saveDebounce);
                _saveDebounce = null;
            }
            SaveSettings();
        }

        /// <summary>SaveManager via static Instance first, registry fallback second (Awake-order safe).</summary>
        private SaveManager ResolveSaveManager()
        {
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager == null) ServiceRegistry.TryGet<SaveManager>(out saveManager);
            return saveManager;
        }

        private static int ClampTargetFps(int fps)
        {
            if (fps < MinTargetFps) return MinTargetFps;
            if (fps > MaxTargetFps) return MaxTargetFps;
            return fps;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushNow();
        }

        private void OnApplicationQuit()
        {
            FlushNow();
        }

        private void OnDisable()
        {
            FlushNow();
        }
    }
}
