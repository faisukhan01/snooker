// SnookerKit — Boot scene composition root. Ensures persistent services then hands over to Home.
using System.Collections;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Minimal boot: create AppServices, let it warm settings/saves for a beat, load Home.
    /// Boot exists so heavy first-frame work never happens mid-UI.</summary>
    [DefaultExecutionOrder(-2000)]
    public class BootSceneRoot : MonoBehaviour
    {
        private bool _handoffStarted;

        private IEnumerator Start()
        {
            AppServices.Ensure();

            // Give SettingsService a frame to apply quality/volume before the home screen appears.
            yield return null;

            var settings = ServiceRegistry.Get<SettingsService>();
            if (settings != null) settings.ApplyLoadedSettings();

            if (!_handoffStarted)
            {
                _handoffStarted = true;
                var sm = ServiceRegistry.Get<ScreenManager>();
                if (sm != null) sm.LoadScene(SceneId.Home);
                else UnityEngine.SceneManagement.SceneManager.LoadScene(SceneId.Home.ToString());
            }
        }
    }
}
