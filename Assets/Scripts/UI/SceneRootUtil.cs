// SnookerKit — shared boot helpers for the menu scene roots (frozen, CONTRACTS §1).
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Static helpers shared by Home/Settings/Profile scene roots. Every call is defensive: failures log at
    /// most one warning and never throw.</summary>
    public static class SceneRootUtil
    {
        /// <summary>Ensures the always-on services exist: AppServices.Ensure() first, then ScreenManager and
        /// AudioManager as children of AppServices (so they persist across scene loads with it). ScreenManager is
        /// required by the frozen contract; AudioManager is ensured here too because no frozen code path instantiates
        /// it and every scene needs UI sfx/ambience. Logs a single warning on partial failure.</summary>
        public static void EnsureBasics()
        {
            try
            {
                AppServices host = AppServices.Ensure();

                if (!ServiceRegistry.TryGet<ScreenManager>(out ScreenManager _) && host != null)
                    host.gameObject.AddComponent<ScreenManager>();

                if (!ServiceRegistry.TryGet<AudioManager>(out AudioManager _) && host != null)
                    host.gameObject.AddComponent<AudioManager>();

                bool screenOk = ServiceRegistry.TryGet<ScreenManager>(out ScreenManager _) && host != null;
                bool audioOk = ServiceRegistry.TryGet<AudioManager>(out AudioManager _) && host != null;
                if (!screenOk || !audioOk)
                    Debug.LogWarning("SceneRootUtil.EnsureBasics: core UI services incomplete (ScreenManager=" + screenOk + ", AudioManager=" + audioOk + ").");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SceneRootUtil.EnsureBasics failed: " + e.Message);
            }
        }
    }
}
