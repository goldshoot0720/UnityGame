// Builds Assets/Resources/MoeRegistry.asset from Assets/Characters (FBX models + pose clips)
// and Assets/Generated/<Shared|Game1..Game12> (Unity-AI animations, audio, textures,
// materials). Rebuilds automatically when those folders change and before entering Play mode.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoeGames.EditorTools
{
    [InitializeOnLoad]
    public static class MoeRegistryBuilder
    {
        const string AssetPath = "Assets/Resources/MoeRegistry.asset";
        const string ScenePath = "Assets/Scenes/Main.unity";
        static bool queued;

        static MoeRegistryBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(AssetPath)) Build();
                EnsureMainScene();
            };
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Build(); };
        }

        internal static void Queue()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; Build(); };
        }

        [MenuItem("MoeGames/Rebuild Asset Registry")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var reg = AssetDatabase.LoadAssetAtPath<MoeRegistry>(AssetPath);
            bool fresh = !reg;
            if (fresh) reg = ScriptableObject.CreateInstance<MoeRegistry>();
            reg.characters.Clear();
            reg.animations.Clear();
            reg.audio.Clear();
            reg.textures.Clear();
            reg.materials.Clear();

            for (int i = 0; i < Cast.Ids.Length; i++)
            {
                string path = "Assets/Characters/" + Cast.Fbx[i] + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model) continue;
                var pose = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                reg.characters.Add(new MoeRegistry.CharacterEntry { id = Cast.Ids[i], model = model, pose = pose });
            }

            // Original Phaser Game Agent assets first (so the original soundtrack and art win over
            // same-named Unity-AI assets), then Assets/Generated.
            var roots = new[] { "Assets/PhaserAssets", "Assets/Generated" }.Where(AssetDatabase.IsValidFolder).ToArray();
            foreach (var rootFolder in roots)
            {
                foreach (var guid in AssetDatabase.FindAssets("", new[] { rootFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    string scope = ScopeOf(path);
                    string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (type == typeof(AudioClip))
                    {
                        // Failed AI generations leave empty placeholder WAVs; skip them so the synth fallback plays.
                        if (new FileInfo(path).Length < 2048) continue;
                        reg.audio.Add(new MoeRegistry.NamedAudio { name = name, scope = scope, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) });
                    }
                    else if (type == typeof(Texture2D))
                        reg.textures.Add(new MoeRegistry.NamedTexture { name = name, scope = scope, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) });
                    else if (type == typeof(Material))
                        reg.materials.Add(new MoeRegistry.NamedMaterial { name = name, scope = scope, material = AssetDatabase.LoadAssetAtPath<Material>(path) });
                    else if (type == typeof(AnimationClip))
                        reg.animations.Add(new MoeRegistry.NamedClip { name = name, scope = scope, clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path) });
                    else
                    {
                        // Clips embedded in generated FBX files: register each by the file name
                        // (and "<file>_<clip>" when a file holds several).
                        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
                        for (int k = 0; k < clips.Count; k++)
                            reg.animations.Add(new MoeRegistry.NamedClip { name = k == 0 ? name : name + "_" + clips[k].name.ToLowerInvariant(), scope = scope, clip = clips[k] });
                    }
                }
            }

            if (fresh) AssetDatabase.CreateAsset(reg, AssetPath);
            else EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            MoeRegistry.Reload();
        }

        /// <summary>"Assets/Generated/Game3/Audio/x.wav" or "Assets/PhaserAssets/Game3/x.png" → "Game3"; else "Shared".</summary>
        static string ScopeOf(string path)
        {
            var parts = path.Split('/');
            return parts.Length > 3 ? parts[2] : "Shared";
        }

        [MenuItem("MoeGames/Create Main Scene")]
        public static void EnsureMainScene()
        {
            if (File.Exists(ScenePath)) { AddToBuild(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            // Created additively so whatever scene is open in the editor is left alone; if the
            // editor only has an untouched untitled scene (additive not allowed), replace that.
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
            }
            catch (System.Exception e)
            {
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty)
                {
                    Debug.LogWarning("[MoeGames] Could not create Main scene automatically (" + e.Message + "); use MoeGames > Create Main Scene.");
                    return;
                }
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            AddToBuild();
        }

        static void AddToBuild()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    class MoeRegistryWatcher : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/PhaserAssets/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool Relevant(string p) => p.StartsWith("Assets/Generated/") || p.StartsWith("Assets/PhaserAssets/") || p.StartsWith("Assets/Characters/");
            if (imported.Any(Relevant) || deleted.Any(Relevant) || moved.Any(Relevant) || movedFrom.Any(Relevant))
                MoeRegistryBuilder.Queue();
        }
    }

    /// <summary>Switches the character FBX files to Humanoid rigs so generated (humanoid)
    /// animation clips retarget onto every character.</summary>
    static class CharacterImportSetup
    {
        [MenuItem("MoeGames/Characters/Set FBX Rigs To Humanoid")]
        static void SetHumanoid()
        {
            foreach (var fbx in Cast.Fbx)
            {
                string path = "Assets/Characters/" + fbx + ".fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                if (mi.animationType == ModelImporterAnimationType.Human) continue;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
            ReportAvatars();
            MoeRegistryBuilder.Build();
        }

        [MenuItem("MoeGames/Characters/Report Avatars")]
        static void ReportAvatars()
        {
            for (int i = 0; i < Cast.Fbx.Length; i++)
            {
                string path = "Assets/Characters/" + Cast.Fbx[i] + ".fbx";
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                Debug.Log($"[MoeGames] avatar {Cast.Ids[i]} ({Cast.Fbx[i]}): " + (avatar ? $"human={avatar.isHuman} valid={avatar.isValid}" : "NONE"));
            }
        }
    }
}
