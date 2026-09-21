// SnookerKit — per-scene screen installer (frozen, CONTRACTS §2). Adds the screen controllers written by 16-e.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnookerKit
{
    /// <summary>Builds full-stretch screen roots ("MatchUI"/"HomeUI"/"SettingsUI"/"ProfileUI") under a canvas and
    /// attaches the matching screen component. EnsureCanvas walks the ancestor chain first so scene roots can be
    /// plain GameObjects — a Canvas + CanvasScaler (1920×1080, match 0.5) + GraphicRaycaster is provisioned on the
    /// installer's GameObject when no ancestor canvas exists.</summary>
    public class UIInstaller : MonoBehaviour
    {
        /// <summary>Installs the in-match HUD screen on a "MatchUI" root.</summary>
        public void InstallMatchUI()
        {
            InstallScreen<HudScreen>("MatchUI");
        }

        /// <summary>Installs the home menu screen on a "HomeUI" root.</summary>
        public void InstallHomeUI()
        {
            InstallScreen<HomeScreen>("HomeUI");
        }

        /// <summary>Installs the settings screen on a "SettingsUI" root.</summary>
        public void InstallSettingsUI()
        {
            InstallScreen<SettingsScreen>("SettingsUI");
        }

        /// <summary>Installs the profile/stats screen on a "ProfileUI" root.</summary>
        public void InstallProfileUI()
        {
            InstallScreen<ProfileScreen>("ProfileUI");
        }

        /// <summary>Creates the named full-stretch root under the resolved canvas and adds the screen component.</summary>
        private GameObject InstallScreen<T>(string rootName) where T : MonoBehaviour
        {
            Canvas canvas = EnsureCanvas(gameObject);

            var rootGO = new GameObject(rootName, typeof(RectTransform));
            rootGO.transform.SetParent(canvas.transform, false);
            RectTransform rt = (RectTransform)rootGO.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            rootGO.AddComponent<T>();
            return rootGO;
        }

        /// <summary>Returns an ancestor Canvas (including the given GameObject itself) or provisions Canvas +
        /// CanvasScaler + GraphicRaycaster on it. Also guarantees an EventSystem exists for the raycaster.</summary>
        private static Canvas EnsureCanvas(GameObject go)
        {
            Canvas canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                EnsureEventSystem();
                return canvas;
            }

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>Creates EventSystem + StandaloneInputModule once (init-time only, DontDestroyOnLoad).</summary>
        private static void EnsureEventSystem()
        {
            try
            {
                if (Object.FindObjectOfType<EventSystem>() != null) return;
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Object.DontDestroyOnLoad(go);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("UIInstaller.EnsureEventSystem: " + e.Message);
            }
        }
    }
}
