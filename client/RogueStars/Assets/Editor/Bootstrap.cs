using System.IO;
using RogueStars.Client.App;
using RogueStars.Client.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RogueStars.Client.Editor
{
    /// <summary>Creates Assets/Scenes/Main.unity (camera + AppBootstrap) and registers it as the only build scene.</summary>
    public static class Bootstrap
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string ServerConfigPath = "Assets/Resources/ServerConfig.asset";

        [MenuItem("Rogue Stars/Create Main Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.063f, 0.063f, 0.1f);
            camera.orthographic = true;

            new GameObject("App", typeof(AppBootstrap));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EnsureServerConfig();
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Created " + ScenePath + " and set it as the build scene.");
        }

        /// <summary>Default server URL asset (read through Resources by ClientSettings).</summary>
        public static void EnsureServerConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<ServerConfig>(ServerConfigPath) != null) return;
            Directory.CreateDirectory("Assets/Resources");
            var cfg = ScriptableObject.CreateInstance<ServerConfig>();
            AssetDatabase.CreateAsset(cfg, ServerConfigPath);
        }

        public const string PlatformConfigPath = "Assets/Resources/PlatformConfig.asset";

        /// <summary>Store/ads selection with placeholder ad unit ids (real values are injected by BuildAndroid).</summary>
        public static void EnsurePlatformConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<PlatformConfig>(PlatformConfigPath) != null) return;
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<PlatformConfig>(), PlatformConfigPath);
        }
    }
}
