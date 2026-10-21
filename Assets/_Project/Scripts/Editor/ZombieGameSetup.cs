#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ScreamerAI.EditorTools;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// One-click project setup. Runs automatically the first time the project is opened:
    ///   1. configures the Screamer model, creates materials / animator / prefabs (Member 3's builder)
    ///   2. writes Resources/GameAssets.asset (prefab references the game needs at runtime)
    ///   3. generates the whole level into Scenes/MainLevel.unity (geometry, lights, doors, props, NavMesh bake),
    ///      saving the procedural textures and materials as real assets
    ///   4. adds the scene to Build Settings
    /// Re-run any time from  Tools > Zombie Game.
    /// </summary>
    public static class ZombieGameSetup
    {
        const string Root = "Assets/_Project";
        const string GameAssetsPath = Root + "/Resources/GameAssets.asset";
        const string ScenePath = Root + "/Scenes/MainLevel.unity";
        const string NavMeshPath = Root + "/Scenes/MainLevel_NavMesh.asset";
        const string AutoKey = "ZombieGame.AutoSetupTried";

        // ------------------------------------------------------------------ auto-run
        [InitializeOnLoadMethod]
        static void AutoOnLoad()
        {
            EditorApplication.delayCall += TryAuto;
        }

        static void TryAuto()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAuto;
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath) != null) return;   // already set up
            if (SessionState.GetBool(AutoKey, false)) return;                                // do not loop after a failure
            SessionState.SetBool(AutoKey, true);

            Debug.Log("[ZombieGame] First open detected - running setup (assets, level, NavMesh)...");
            try
            {
                RunAll(false);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ZombieGame] Auto setup failed: " + e + "\nThe game still works: open Assets/_Project/Scenes/MainLevel.unity and press Play (it builds the level at runtime). " +
                               "You can retry with Tools > Zombie Game > Setup Everything.");
            }
        }

        // ------------------------------------------------------------------ menu
        [MenuItem("Tools/Zombie Game/Setup Everything (assets + level scene)")]
        static void MenuSetup()
        {
            RunAll(true);
        }

        [MenuItem("Tools/Zombie Game/Rebuild Level Into Scene")]
        static void MenuRebuildLevel()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildLevelScene(AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath));
        }

        [MenuItem("Tools/Zombie Game/Reset Level To Runtime-Generated (empty scene)")]
        static void MenuResetToRuntime()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject boot = new GameObject("GameBootstrap");
            boot.AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            Debug.Log("[ZombieGame] MainLevel.unity now contains only the GameBootstrap - the level is generated when you press Play.");
        }

        [MenuItem("Tools/Zombie Game/Open Main Level")]
        static void MenuOpen()
        {
            if (!File.Exists(ScenePath)) { RunAll(true); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        // ------------------------------------------------------------------ pipeline
        static void RunAll(bool askToSave)
        {
            if (askToSave && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root + "/Resources");
            EnsureFolder(Root + "/Scenes");
            GameAssets ga = EnsureGameAssets();
            BuildLevelScene(ga);
            AddToBuildSettings();
            Debug.Log("[ZombieGame] Setup complete. Press Play in MainLevel.unity.  Controls: WASD, mouse, LMB shoot, R reload, E doors, F flashlight, Q bottle, G grenade, F1 AI debug.");
        }

        static GameAssets EnsureGameAssets()
        {
            GameObject screamer = null, locker = null, visual = null;
            AudioClip scream = null;
            try
            {
                ScreamerSceneBuilder.BuildGameAssets(out screamer, out locker, out visual, out scream);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ZombieGame] Could not build the Screamer assets (" + e.Message + "). Zombies will use the built-in block bodies instead.");
            }

            GameAssets ga = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, GameAssetsPath);
            }
            ga.screamerPrefab = screamer;
            ga.lockerPrefab = locker;
            ga.zombieVisualPrefab = visual;
            ga.screamClip = scream;
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            return ga;
        }

        static void BuildLevelScene(GameAssets ga)
        {
            EnsureFolder(Root + "/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject boot = new GameObject("GameBootstrap");
            boot.AddComponent<GameBootstrap>();

            LevelRefs refs = LevelBuilder.Build(ga, false);
            SaveGeneratedAssets(refs.root);

            Physics.SyncTransforms();
            try
            {
                refs.surface.BuildNavMesh();
                if (refs.surface.navMeshData != null)
                {
                    AssetDatabase.DeleteAsset(NavMeshPath);
                    AssetDatabase.CreateAsset(refs.surface.navMeshData, NavMeshPath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ZombieGame] Editor NavMesh bake failed (" + e.Message + "). It will be baked when the game starts.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[ZombieGame] Level saved to " + ScenePath);
        }

        static void AddToBuildSettings()
        {
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].path == ScenePath)
                {
                    if (!list[i].enabled) { list[i].enabled = true; EditorBuildSettings.scenes = list.ToArray(); }
                    return;
                }
            }
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ persist procedural materials / textures as assets
        static void SaveGeneratedAssets(GameObject root)
        {
            EnsureFolder(Root + "/Materials/Generated");
            EnsureFolder(Root + "/Textures/Generated");

            Dictionary<Material, Material> matMap = new Dictionary<Material, Material>();
            Dictionary<Texture, Texture> texMap = new Dictionary<Texture, Texture>();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || AssetDatabase.Contains(m)) continue;
                    Material saved;
                    if (!matMap.TryGetValue(m, out saved))
                    {
                        saved = PersistMaterial(m, texMap);
                        matMap[m] = saved;
                    }
                    mats[i] = saved;
                    changed = true;
                }
                if (changed) renderers[r].sharedMaterials = mats;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static Material PersistMaterial(Material m, Dictionary<Texture, Texture> texMap)
        {
            string[] props = m.GetTexturePropertyNames();
            for (int i = 0; i < props.Length; i++)
            {
                Texture t = m.GetTexture(props[i]);
                if (t == null || AssetDatabase.Contains(t)) continue;
                Texture saved;
                if (!texMap.TryGetValue(t, out saved))
                {
                    saved = PersistTexture(t as Texture2D);
                    texMap[t] = saved;
                }
                if (saved != null) m.SetTexture(props[i], saved);
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "/Materials/Generated/" + Sanitize(m.name) + ".mat");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Texture PersistTexture(Texture2D tex)
        {
            if (tex == null) return null;
            string path = Root + "/Textures/Generated/" + Sanitize(tex.name) + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.wrapMode = tex.wrapMode;
                imp.alphaIsTransparency = tex.name.Contains("Blood") || tex.name.Contains("BulletHole");
                imp.mipmapEnabled = true;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Sanitize(string name)
        {
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
