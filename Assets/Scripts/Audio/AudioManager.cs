// SnookerKit — audio service (frozen, CONTRACTS §2 / PDF §19). Registered in Awake, deregistered in OnDestroy.
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>SFX + ambience playback. Clips are loaded once from Resources/Audio and keyed by SfxKey using the exact
    /// committed file names. Playback goes through an 8-source 2D round-robin pool plus one dedicated looping ambience
    /// source. Missing or failed clip loads only disable that key (logged once) — playback never crashes.</summary>
    public class AudioManager : MonoBehaviour
    {
        /// <summary>Clip file names under Assets/Resources/Audio, index-aligned with SfxKey (frozen CONTRACTS §2).</summary>
        private static readonly string[] ClipNames =
        {
            "cue_strike", "ball_ball", "cushion", "pocket_drop", "pot_chime",
            "foul", "frame_win", "ui_click", "ui_back", "ambience_loop",
        };

        private const int PoolSize = 8;
        private const float ImpactRateSeconds = 0.03f;
        private const float AmbienceVolumeScale = 0.05f;

        private readonly Dictionary<SfxKey, AudioClip> _clips = new Dictionary<SfxKey, AudioClip>(ClipNames.Length);
        private readonly AudioSource[] _pool = new AudioSource[PoolSize];
        private int _nextSource;
        private AudioSource _ambience;
        private float _master = 0.8f;
        private float _sfx = 0.9f;
        private float _lastImpactTime = -999f;
        private float _lastCushionTime = -999f;
        private bool _clipsLoaded;

        private void Awake()
        {
            ServiceRegistry.Register<AudioManager>(this);
            LoadClips();
            BuildSources();

            // Seed volumes from persisted settings when the service is already up (ApplyLoadedSettings also pushes
            // SetVolumes later — this only covers first-frame ordering).
            if (ServiceRegistry.TryGet<SettingsService>(out SettingsService settings) && settings != null && settings.Settings != null)
            {
                _master = Mathf.Clamp01(settings.Settings.masterVolume);
                _sfx = Mathf.Clamp01(settings.Settings.sfxVolume);
            }
            AudioListener.volume = _master;
        }

        private void OnDestroy()
        {
            if (ServiceRegistry.Get<AudioManager>() == this) ServiceRegistry.Deregister<AudioManager>();
        }

        /// <summary>Loads all AudioClips from Resources/Audio and maps them to SfxKey by exact file name.
        /// Any failure or missing clip disables that key only (one aggregate warning) — never throws.</summary>
        private void LoadClips()
        {
            if (_clipsLoaded) return;
            _clipsLoaded = true;

            AudioClip[] all = null;
            try
            {
                all = Resources.LoadAll<AudioClip>("Audio");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("AudioManager: Resources/Audio load failed (" + e.Message + ") — all keys disabled.");
                return;
            }
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("AudioManager: no AudioClips found under Resources/Audio — all keys disabled.");
                return;
            }

            List<string> missing = new List<string>(0);
            for (int i = 0; i < ClipNames.Length; i++)
            {
                AudioClip found = null;
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j] != null && all[j].name == ClipNames[i]) { found = all[j]; break; }
                }
                if (found != null) _clips[(SfxKey)i] = found;
                else missing.Add(ClipNames[i]);
            }
            if (missing.Count > 0)
                Debug.LogWarning("AudioManager: missing clips (keys disabled): " + string.Join(", ", missing.ToArray()));
        }

        /// <summary>Builds the 8-source 2D round-robin pool and the dedicated ambience loop source.</summary>
        private void BuildSources()
        {
            var poolGO = new GameObject("SfxPool");
            poolGO.transform.SetParent(transform, false);
            for (int i = 0; i < PoolSize; i++)
            {
                AudioSource src = poolGO.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f; // 2D
                src.volume = _sfx;
                _pool[i] = src;
            }

            var ambienceGO = new GameObject("Ambience");
            ambienceGO.transform.SetParent(transform, false);
            _ambience = ambienceGO.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f;
            _ambience.volume = 0f;
        }

        /// <summary>Plays a one-shot clip on the next pool source. Disabled/missing keys are silent no-ops.</summary>
        public void Play(SfxKey key)
        {
            AudioClip clip;
            if (!_clips.TryGetValue(key, out clip) || clip == null) return;
            AudioSource src = _pool[_nextSource];
            _nextSource = (_nextSource + 1) % PoolSize;
            if (src == null) return;
            src.pitch = 1f;
            src.volume = _sfx;
            src.clip = clip;
            src.Play();
        }

        /// <summary>Ball-ball impact: volume/pitch scale with impact speed, rate-limited to one per 0.03 s (unscaled).</summary>
        public void PlayImpact(float speed)
        {
            float now = Time.unscaledTime;
            if (now - _lastImpactTime < ImpactRateSeconds) return;
            _lastImpactTime = now;

            AudioClip clip;
            if (!_clips.TryGetValue(SfxKey.BallBall, out clip) || clip == null) return;
            float s01 = Mathf.Clamp01(speed / GameConfig.PhysicsTuning.MaxShotSpeed);
            PlayScaled(clip, _sfx * (0.30f + 0.70f * s01), 0.85f + 0.35f * s01);
        }

        /// <summary>Cushion contact: speed-scaled like PlayImpact but with its own independent rate limit.</summary>
        public void PlayCushion(float speed)
        {
            float now = Time.unscaledTime;
            if (now - _lastCushionTime < ImpactRateSeconds) return;
            _lastCushionTime = now;

            AudioClip clip;
            if (!_clips.TryGetValue(SfxKey.Cushion, out clip) || clip == null) return;
            float s01 = Mathf.Clamp01(speed / GameConfig.PhysicsTuning.MaxShotSpeed);
            PlayScaled(clip, _sfx * (0.20f + 0.55f * s01), 0.80f + 0.25f * s01);
        }

        /// <summary>Starts (or refreshes) the ambience loop at 0.05 · master, gated by Settings.ambienceOn via TryGet.</summary>
        public void StartAmbience()
        {
            if (_ambience == null) return;

            bool on = true;
            if (ServiceRegistry.TryGet<SettingsService>(out SettingsService settings) && settings != null && settings.Settings != null)
                on = settings.Settings.ambienceOn;
            if (!on)
            {
                if (_ambience.isPlaying) _ambience.Stop();
                return;
            }

            AudioClip clip;
            if (!_clips.TryGetValue(SfxKey.AmbienceLoop, out clip) || clip == null) return;
            _ambience.clip = clip;
            _ambience.loop = true;
            _ambience.volume = Mathf.Clamp01(AmbienceVolumeScale * _master);
            if (!_ambience.isPlaying) _ambience.Play();
        }

        /// <summary>Applies volumes: AudioListener.volume = master; stores the sfx scale for pool playback and refreshes ambience.</summary>
        public void SetVolumes(float master, float sfx)
        {
            _master = Mathf.Clamp01(master);
            _sfx = Mathf.Clamp01(sfx);
            AudioListener.volume = _master;
            if (_ambience != null && _ambience.isPlaying) _ambience.volume = Mathf.Clamp01(AmbienceVolumeScale * _master);
        }

        /// <summary>Plays a clip through the round-robin pool with explicit volume/pitch (no allocations).</summary>
        private void PlayScaled(AudioClip clip, float volume, float pitch)
        {
            AudioSource src = _pool[_nextSource];
            _nextSource = (_nextSource + 1) % PoolSize;
            if (src == null) return;
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(volume);
            src.clip = clip;
            src.Play();
        }
    }
}
