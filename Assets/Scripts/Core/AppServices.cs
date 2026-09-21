// SnookerKit — always-on service composition root (frozen, CONTRACTS §1/§2).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Created once per active scene by every *SceneRoot. Guarantees exactly one instance of the
    /// persistent services (SaveManager, SettingsService, StatisticsTracker, PerformanceManager) exists and is
    /// registered before gameplay managers Awake. Survives scene loads (DontDestroyOnLoad) with duplicate guard.</summary>
    [DefaultExecutionOrder(-1000)]
    public class AppServices : MonoBehaviour
    {
        private static AppServices _instance;

        /// <summary>True when the persistent service set is live (scene roots can short-circuit re-creation).</summary>
        public static bool Ready { get { return _instance != null; } }

        /// <summary>Ensures the service set exists in the current scene. Safe to call from any Awake.</summary>
        public static AppServices Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("AppServices");
            _instance = go.AddComponent<AppServices>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject); // duplicate guard across scene reloads
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Order matters: saves first (SettingsService reads them), tracker subscribes events last.
            EnsureComponent<SaveManager>();
            EnsureComponent<SettingsService>();
            EnsureComponent<PerformanceManager>();
            EnsureComponent<StatisticsTracker>();
        }

        private void EnsureComponent<T>() where T : Component
        {
            var existing = GetComponent<T>();
            if (existing == null) gameObject.AddComponent<T>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
