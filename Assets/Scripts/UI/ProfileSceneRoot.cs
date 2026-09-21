// SnookerKit — Profile scene composition root (frozen, CONTRACTS §1). One MonoBehaviour per file for stable scene YAML.
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Profile/stats scene entry: ensures core services (AppServices/ScreenManager/AudioManager) then installs
    /// the profile UI via a UIInstaller on a "UI" root GameObject.</summary>
    [DefaultExecutionOrder(-500)]
    public class ProfileSceneRoot : MonoBehaviour
    {
        private void Start()
        {
            SceneRootUtil.EnsureBasics();

            var uiGO = new GameObject("UI");
            uiGO.transform.SetParent(transform, false);
            UIInstaller installer = uiGO.AddComponent<UIInstaller>();
            installer.InstallProfileUI();
        }
    }
}
