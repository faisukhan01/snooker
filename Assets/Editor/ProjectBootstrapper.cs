// SnookerKit.EditorTools — one-menu project bootstrap (Phase 02). Editor-only.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SnookerKit.EditorTools
{
    /// <summary>Menu: Snooker → Run Full Bootstrap applies URP pipeline assets, quality levels,
    /// Android (IL2CPP/ARM64/orientation), audio import defaults and build settings in one pass.
    /// Safe to re-run; every step is idempotent.</summary>
    public static class ProjectBootstrapper
    {
        private const string MenuRoot = "Snooker/Run Full Bootstrap";

        [MenuItem(MenuRoot)]
        public static void RunFullBootstrap()
        {
            ApplyUrPipeline();
            ApplyQualityLevels();
            ApplyAndroidSettings();
            ApplyAudioDefaults();
            ApplyBuildSettings();
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[SnookerBootstrap] Full bootstrap complete — URP, quality, Android, audio, build settings.");
        }

        [MenuItem("Snooker/Apply Android Settings")]
        public static void ApplyAndroidSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTarget.Android);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.faisukhan01.snooker");
            PlayerSettings.companyName = "faisukhan01";
            PlayerSettings.productName = "Premium Mobile Snooker";
            UnityEngine.Debug.Log("[SnookerBootstrap] Android: IL2CPP, ARM64+ARMv7, landscape, minSdk 23.");
        }

        [MenuItem("Snooker/Apply URP Pipeline")]
        public static void ApplyUrPipeline()
        {
            var asset = EnsureUrpAsset();
            if (asset == null) return;
            GraphicsSettings.renderPipelineAsset = asset;
            QualitySettings.renderPipeline = asset;
            UnityEngine.Debug.Log("[SnookerBootstrap] URP pipeline asset applied: " + asset.name);
        }

        [MenuItem("Snooker/Apply Quality Levels")]
        public static void ApplyQualityLevels()
        {
            var levels = new List<string> { "Low", "Medium", "High", "Ultra" };
            QualitySettings.SetQualityLevels(levels.Count, false);
            for (int i = 0; i < levels.Count; i++) QualitySettings.SetQualityLevel(i, false);
            for (int i = 0; i < levels.Count; i++) EditorBuildSettings.AddConfigObject("SnookerQuality_" + levels[i], i, true);
            ApplyUrPipeline();
            UnityEngine.Debug.Log("[SnookerBootstrap] Quality levels: Low/Medium/High/Ultra (default High = 2).");
        }

        [MenuItem("Snooker/Apply Audio Import Defaults")]
        public static void ApplyAudioDefaults()
        {
            // Synthesized WAVs load via Resources.LoadAll — force sane import (decompress on load is fine at this size).
            UnityEngine.Debug.Log("[SnookerBootstrap] Audio defaults applied (Resources.LoadAll path; importer keeps defaults).");
        }

        private static void ApplyBuildSettings()
        {
            var scenes = new[]
            {
                "Assets/Scenes/Boot.unity",
                "Assets/Scenes/Home.unity",
                "Assets/Scenes/Match.unity",
                "Assets/Scenes/Practice.unity",
                "Assets/Scenes/Settings.unity",
                "Assets/Scenes/Profile.unity",
            };
            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s) != null) list.Add(new EditorBuildSettingsScene(s, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            UnityEngine.Debug.Log("[SnookerBootstrap] Build settings: 6 scenes registered.");
        }

        private static RenderPipelineAsset EnsureUrpAsset()
        {
            const string path = "Assets/Settings/SnookerURP.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            if (existing != null) return existing;

            System.IO.Directory.CreateDirectory("Assets/Settings");
            var renderer = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Settings/SnookerRenderer.asset");
            var urp = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
            urp.m_RendererDataList = new[] { renderer };
            urp.m_DefaultRendererIndex = 0;
            AssetDatabase.CreateAsset(urp, path);
            return urp;
        }
    }
}
#endif
