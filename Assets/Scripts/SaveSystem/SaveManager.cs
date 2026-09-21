// SnookerKit — JSON persistence to Application.persistentDataPath/snooker/ (CONTRACTS §2/§8/§10).
// Atomic writes (.tmp → previous main becomes .bak → tmp promoted); load chain main → .bak → fresh defaults.
// Every IO path is wrapped: a corrupted save or missing disk must never crash the game (§0.5).
using System;
using System.IO;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Persists small JSON documents (settings, profile) under <c>Application.persistentDataPath/snooker/</c>.
    /// Added to the AppServices object by <see cref="AppServices"/>; also reachable through the static
    /// <see cref="Instance"/> convenience. Load never throws and falls back main → .bak → fresh instance;
    /// Save is atomic and keeps the previous main file as <c>&lt;key&gt;.json.bak</c>.</summary>
    public class SaveManager : MonoBehaviour
    {
        private static SaveManager _instance;

        private string _dir;

        /// <summary>Convenience accessor — assigned in <see cref="Awake"/>, cleared in <see cref="OnDestroy"/>.
        /// Defensive callers may prefer <c>ServiceRegistry.TryGet&lt;SaveManager&gt;</c> instead.</summary>
        public static SaveManager Instance { get { return _instance; } }

        private void Awake()
        {
            // AppServices guarantees a single instance; if a duplicate ever appears, the last one wins.
            _instance = this;
            try
            {
                _dir = Path.Combine(Application.persistentDataPath, "snooker");
            }
            catch (Exception)
            {
                _dir = null; // resolved lazily by EnsureDirectory if this ever fails
            }
            ServiceRegistry.Register<SaveManager>(this);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            ServiceRegistry.Deregister<SaveManager>();
        }

        /// <summary>Loads a JSON document for <paramref name="key"/>, falling back main → .bak → a fresh
        /// <typeparamref name="T"/>. Never throws — corrupted or missing files simply yield defaults.</summary>
        public T Load<T>(string key) where T : class, new()
        {
            string path = PathFor(key);
            if (path != null)
            {
                string mainJson = TryReadFile(path);
                T fromMain = TryFromJson<T>(mainJson);
                if (fromMain != null) return fromMain;

                string bakJson = TryReadFile(path + ".bak");
                T fromBak = TryFromJson<T>(bakJson);
                if (fromBak != null) return fromBak;
            }
            return new T();
        }

        /// <summary>Atomically persists <paramref name="data"/> for <paramref name="key"/>: writes
        /// <c>&lt;key&gt;.json.tmp</c>, promotes the previous main file to <c>&lt;key&gt;.json.bak</c>, then moves the
        /// tmp file into place. Never throws; IO failures are swallowed with best-effort tmp cleanup.</summary>
        public void Save<T>(string key, T data) where T : class
        {
            if (data == null) return;
            string path = PathFor(key);
            if (path == null) return;

            string tmpPath = path + ".tmp";
            try
            {
                EnsureDirectory();
                string json = JsonUtility.ToJson(data);
                if (string.IsNullOrEmpty(json)) return;

                File.WriteAllText(tmpPath, json);

                if (File.Exists(path))
                {
                    string bakPath = path + ".bak";
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(path, bakPath); // previous main becomes the restore point
                }
                if (File.Exists(path)) File.Delete(path); // defensive — normally already renamed away
                File.Move(tmpPath, path);
                tmpPath = null; // promoted successfully; nothing left to clean up
            }
            catch (Exception)
            {
                try
                {
                    if (tmpPath != null && File.Exists(tmpPath)) File.Delete(tmpPath);
                }
                catch (Exception)
                {
                    // Nothing further we can do — the previous save (main/.bak) is still intact.
                }
            }
        }

        /// <summary>Full save path for a key, or null when the key/directory cannot be resolved.</summary>
        private string PathFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                EnsureDirectory();
                return Path.Combine(_dir, key + ".json");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Creates the save directory on demand (persistentDataPath is main-thread-only).</summary>
        private void EnsureDirectory()
        {
            if (_dir == null) _dir = Path.Combine(Application.persistentDataPath, "snooker");
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
        }

        /// <summary>Reads a whole file, returning null on any failure.</summary>
        private static string TryReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Deserializes JSON, returning null on failure or a null payload (so .bak still gets its chance).</summary>
        private static T TryFromJson<T>(string json) where T : class, new()
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
