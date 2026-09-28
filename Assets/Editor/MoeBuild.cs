// One-click builds for the Releases: WebGL (web), Android (.apk), Windows (.exe) and macOS (.app).
// Outputs go to <project>/Builds/<Platform>/. Call MoeBuild.BuildAll() from the menu or from a
// command (it runs the platforms in sequence and writes Builds/build-report.txt).
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoeGames.EditorTools
{
    public static class MoeBuild
    {
        const string Scene = "Assets/Scenes/Main.unity";
        static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds");

        [MenuItem("MoeGames/Build/Prepare Project Settings")]
        public static void Prepare()
        {
            MoeRegistryBuilder.Build();
            EnsureScene();
            EnsureVariantMaterials();
            PlayerSettings.companyName = "goldshoot0720";
            PlayerSettings.productName = "MoeGames";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.goldshoot0720.moegames");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.goldshoot0720.moegames");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            // Android: 64-bit ARM needs IL2CPP.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            EditorUserBuildSettings.buildAppBundle = false;
            // WebGL: gzip with the JS decompression fallback so it works on any static host (GitHub Pages).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            AssetDatabase.SaveAssets();
        }

        /// <summary>Main scene (empty — App boots itself) registered in Build Settings.</summary>
        static void EnsureScene()
        {
            if (!File.Exists(Scene))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
                try
                {
                    var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(s, Scene);
                    EditorSceneManager.CloseScene(s, true);
                }
                catch (Exception)
                {
                    var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(s, Scene);
                }
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        }

        /// <summary>Materials in Resources keep the Standard shader variants the runtime needs
        /// (emission for glowing effects, GPU instancing for bullets) from being stripped.</summary>
        static void EnsureVariantMaterials()
        {
            const string dir = "Assets/Resources/ShaderKeep";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources", "ShaderKeep");
            var std = Shader.Find("Standard");
            if (!std) return;
            void Make(string name, bool glow, bool inst)
            {
                string path = $"{dir}/{name}.mat";
                if (File.Exists(path)) return;
                var m = new Material(std) { enableInstancing = inst };
                if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
                AssetDatabase.CreateAsset(m, path);
            }
            Make("plain", false, false);
            Make("glow", true, false);
            Make("plain_instanced", false, true);
            Make("glow_instanced", true, true);
        }

        static BuildReport Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target) EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var opts = new BuildPlayerOptions { scenes = new[] { Scene }, locationPathName = path, target = target, targetGroup = group, options = BuildOptions.None };
            return BuildPipeline.BuildPlayer(opts);
        }

        [MenuItem("MoeGames/Build/WebGL")] public static void BuildWebGL() => Report(Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, Path.Combine(Root, "WebGL")));
        [MenuItem("MoeGames/Build/Android APK")] public static void BuildAndroid() => Report(Build(BuildTarget.Android, BuildTargetGroup.Android, Path.Combine(Root, "Android", "MoeGames.apk")));
        [MenuItem("MoeGames/Build/Windows")] public static void BuildWindows() => Report(Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, Path.Combine(Root, "Windows", "MoeGames.exe")));
        [MenuItem("MoeGames/Build/macOS")] public static void BuildMac() => Report(Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, Path.Combine(Root, "Mac", "MoeGames.app")));

        [MenuItem("MoeGames/Build/All Platforms")]
        public static void BuildAll()
        {
            Prepare();
            var sb = new StringBuilder();
            foreach (var (name, fn) in new (string, Func<BuildReport>)[]
            {
                ("WebGL", () => Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, Path.Combine(Root, "WebGL"))),
                ("Android", () => Build(BuildTarget.Android, BuildTargetGroup.Android, Path.Combine(Root, "Android", "MoeGames.apk"))),
                ("Windows", () => Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, Path.Combine(Root, "Windows", "MoeGames.exe"))),
                ("macOS", () => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, Path.Combine(Root, "Mac", "MoeGames.app"))),
            })
            {
                try
                {
                    var r = fn();
                    sb.AppendLine($"{name}: {r.summary.result} {r.summary.totalSize / 1048576.0:F1} MB in {r.summary.totalTime.TotalSeconds:F0}s, errors={r.summary.totalErrors}");
                    foreach (var step in r.steps)
                        foreach (var msg in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                            sb.AppendLine("  " + msg.content.Split('\n')[0]);
                }
                catch (Exception e) { sb.AppendLine($"{name}: EXCEPTION {e.Message}"); }
                File.WriteAllText(Path.Combine(Root, "build-report.txt"), sb.ToString());
            }
            sb.AppendLine("DONE");
            File.WriteAllText(Path.Combine(Root, "build-report.txt"), sb.ToString());
        }

        static void Report(BuildReport r) => Debug.Log($"[MoeGames] build {r.summary.platform}: {r.summary.result} ({r.summary.totalSize / 1048576.0:F1} MB) → {r.summary.outputPath}");
    }
}
