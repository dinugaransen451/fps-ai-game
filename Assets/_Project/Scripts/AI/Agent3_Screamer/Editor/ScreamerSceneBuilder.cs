#if UNITY_EDITOR
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace ScreamerAI.EditorTools
{
    /// <summary>
    /// Builds everything the Screamer needs inside Unity (import settings, URP materials, Animator Controller,
    /// prefabs, and a demo scene with a baked NavMesh).
    ///
    /// It runs AUTOMATICALLY the first time the project is opened (when Screamer_Demo.unity does not exist yet).
    /// To rebuild any time:  Tools > Screamer > Rebuild Prefabs And Demo Scene.
    /// </summary>
    public static class ScreamerSceneBuilder
    {
        const string Root = "Assets/_Project";
        const string FbxPath = Root + "/Models/Characters/zombie_scream.fbx";
        const string LockerObjPath = Root + "/Models/Props/wrecked_locker.obj";
        const string ZombieDiffusePath = Root + "/Textures/Zombie/world_war_zombie_diffuse.png";
        const string ZombieNormalPath = Root + "/Textures/Zombie/world_war_zombie_normal.png";
        const string LockerAlbedoPath = Root + "/Textures/Props/wrecked_locker_albedo.png";
        const string ScreamWavPath = Root + "/Audio/screamer_scream.wav";

        const string MatFolder = Root + "/Materials";
        const string ZombieMatPath = MatFolder + "/M_Screamer_Zombie.mat";
        const string LockerMatPath = MatFolder + "/M_WreckedLocker.mat";
        const string FloorMatPath = MatFolder + "/M_Demo_Floor.mat";
        const string WallMatPath = MatFolder + "/M_Demo_Wall.mat";
        const string CrateMatPath = MatFolder + "/M_Demo_Crate.mat";
        const string DummyMatPath = MatFolder + "/M_Demo_DummyZombie.mat";

        const string ControllerPath = Root + "/Animations/Screamer.controller";
        const string ScreamerPrefabPath = Root + "/Prefabs/Screamer.prefab";
        const string LockerPrefabPath = Root + "/Prefabs/WreckedLocker.prefab";
        const string DummyPrefabPath = Root + "/Prefabs/DummyZombie.prefab";

        const string ScenePath = Root + "/Scenes/Screamer_Demo.unity";
        const string NavMeshAssetPath = Root + "/Scenes/Screamer_Demo_NavMesh.asset";

        // ------------------------------------------------------------------ auto-run on first open
        [InitializeOnLoadMethod]
        static void AutoBuildOnFirstOpen()
        {
            EditorApplication.delayCall += TryAutoBuild;
        }

        static void TryAutoBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoBuild;   // wait until the editor is idle
                return;
            }
            if (File.Exists(ScenePath)) return;                // already built
            if (!File.Exists(FbxPath)) return;                 // model missing - nothing to build

            Debug.Log("[Screamer] First open detected - building materials, prefabs and the demo scene...");
            try
            {
                BuildAll();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Screamer] Auto-build failed: " + e + "\nYou can retry with Tools > Screamer > Rebuild Prefabs And Demo Scene.");
            }
        }

        [MenuItem("Tools/Screamer/Rebuild Prefabs And Demo Scene")]
        static void MenuRebuild()
        {
            BuildAll();
        }

        [MenuItem("Tools/Screamer/Open Demo Scene")]
        static void MenuOpen()
        {
            if (!File.Exists(ScenePath)) { BuildAll(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        // ------------------------------------------------------------------ main pipeline
        static void BuildAll()
        {
            AssetDatabase.Refresh();

            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Animations");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");

            ConfigureTextures();
            ConfigureModel();

            Material zombieMat = CreateLitMaterial(ZombieMatPath, Color.white, 0.25f, 0f,
                LoadTex(ZombieDiffusePath), LoadTex(ZombieNormalPath));
            Material lockerMat = CreateLitMaterial(LockerMatPath, Color.white, 0.35f, 0.35f, LoadTex(LockerAlbedoPath), null);
            Material floorMat = CreateLitMaterial(FloorMatPath, new Color(0.17f, 0.18f, 0.19f), 0.15f, 0f, null, null);
            Material wallMat = CreateLitMaterial(WallMatPath, new Color(0.30f, 0.32f, 0.31f), 0.1f, 0f, null, null);
            Material crateMat = CreateLitMaterial(CrateMatPath, new Color(0.35f, 0.25f, 0.15f), 0.1f, 0f, null, null);
            Material dummyMat = CreateLitMaterial(DummyMatPath, new Color(0.25f, 0.45f, 0.22f), 0.2f, 0f, null, null);

            RuntimeAnimatorController controller = CreateAnimatorController();
            GameObject screamerPrefab = CreateScreamerPrefab(zombieMat, controller);
            GameObject lockerPrefab = CreateLockerPrefab(lockerMat);
            GameObject dummyPrefab = CreateDummyPrefab(dummyMat);

            BuildScene(screamerPrefab, lockerPrefab, dummyPrefab, floorMat, wallMat, crateMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Screamer] Done. Demo scene: " + ScenePath + "  -  press Play, walk with WASD, and let the Screamer spot you.");
        }

        // ------------------------------------------------------------------ import settings
        static void ConfigureTextures()
        {
            TextureImporter normal = AssetImporter.GetAtPath(ZombieNormalPath) as TextureImporter;
            if (normal != null && normal.textureType != TextureImporterType.NormalMap)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.SaveAndReimport();
            }
        }

        static void ConfigureModel()
        {
            ModelImporter mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi == null)
            {
                Debug.LogWarning("[Screamer] Could not find the zombie FBX at " + FbxPath);
                return;
            }

            bool changed = false;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }
            if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (!mi.importAnimation)
            {
                mi.importAnimation = true;
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ materials
        static Texture2D LoadTex(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material CreateLitMaterial(string path, Color color, float smoothness, float metallic, Texture2D albedo, Texture2D normal)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);

            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (albedo != null)
            {
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_MainTex", albedo);
            }
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ animator controller
        static AnimationClip FindScreamClip()
        {
            AnimationClip best = null;
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            for (int i = 0; i < all.Length; i++)
            {
                AnimationClip c = all[i] as AnimationClip;
                if (c == null || c.name.StartsWith("__preview__")) continue;
                if (best == null || c.length > best.length) best = c;
            }
            return best;
        }

        static Avatar FindAvatar()
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            for (int i = 0; i < all.Length; i++)
            {
                Avatar a = all[i] as Avatar;
                if (a != null) return a;
            }
            return null;
        }

        static RuntimeAnimatorController CreateAnimatorController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Scream", AnimatorControllerParameterType.Trigger);

            AnimationClip clip = FindScreamClip();
            if (clip == null) Debug.LogWarning("[Screamer] No animation clip found inside the FBX - the Animator Controller will be empty.");

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // "Hunch" = the first frame of the scream clip, frozen (speed 0). The procedural layer animates on top of it.
            AnimatorState hunch = sm.AddState("Hunch");
            hunch.motion = clip;
            hunch.speed = 0f;

            AnimatorState scream = sm.AddState("Scream");
            scream.motion = clip;
            sm.defaultState = hunch;

            AnimatorStateTransition toScream = hunch.AddTransition(scream);
            toScream.hasExitTime = false;
            toScream.duration = 0.1f;
            toScream.AddCondition(AnimatorConditionMode.If, 0f, "Scream");

            AnimatorStateTransition back = scream.AddTransition(hunch);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.duration = 0.2f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------ prefabs
        static GameObject CreateScreamerPrefab(Material zombieMat, RuntimeAnimatorController controller)
        {
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) throw new System.Exception("Zombie FBX not found at " + FbxPath);

            GameObject root = new GameObject("Screamer");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);

            SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = new Material[Mathf.Max(1, renderers[i].sharedMaterials.Length)];
                for (int m = 0; m < mats.Length; m++) mats[m] = zombieMat;
                renderers[i].sharedMaterials = mats;
            }

            Animator anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            Avatar avatar = FindAvatar();
            if (avatar != null) anim.avatar = avatar;
            anim.applyRootMotion = false;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.35f;
            col.height = 2f;

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.speed = 1f;
            agent.acceleration = 14f;
            agent.angularSpeed = 240f;
            agent.stoppingDistance = 0.15f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            AudioSource src = root.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.minDistance = 3f;
            src.maxDistance = 60f;
            src.rolloffMode = AudioRolloffMode.Linear;

            ZombieProceduralMotion motion = root.AddComponent<ZombieProceduralMotion>();
            motion.animator = anim;

            ScreamerBrain brain = root.AddComponent<ScreamerBrain>();
            brain.animator = anim;
            brain.motion = motion;
            brain.audioSource = src;
            brain.screamClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ScreamWavPath);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ScreamerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject CreateLockerPrefab(Material lockerMat)
        {
            GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(LockerObjPath);
            if (obj == null) throw new System.Exception("Locker model not found at " + LockerObjPath);

            GameObject root = new GameObject("WreckedLocker");
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(obj, root.transform);

            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] mats = new Material[Mathf.Max(1, renderers[i].sharedMaterials.Length)];
                for (int m = 0; m < mats.Length; m++) mats[m] = lockerMat;
                renderers[i].sharedMaterials = mats;
            }

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.95f, 0f);
            box.size = new Vector3(0.82f, 1.9f, 0.62f);

            root.AddComponent<HideSpot>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LockerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject CreateDummyPrefab(Material dummyMat)
        {
            GameObject root = new GameObject("DummyZombie");

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.95f, 0.7f);
            body.GetComponent<Renderer>().sharedMaterial = dummyMat;
            Object.DestroyImmediate(body.GetComponent<Collider>());

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.95f, 0f);
            col.radius = 0.35f;
            col.height = 1.9f;

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.9f;
            agent.speed = 1f;
            agent.acceleration = 10f;

            root.AddComponent<ZombieAlertReceiver>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, DummyPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ demo scene
        static void BuildScene(GameObject screamerPrefab, GameObject lockerPrefab, GameObject dummyPrefab,
            Material floorMat, Material wallMat, Material crateMat)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- atmosphere
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.07f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.04f);
            RenderSettings.fogDensity = 0.018f;

            GameObject moon = new GameObject("Moonlight");
            Light moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.45f, 0.55f, 0.85f);
            moonLight.intensity = 0.35f;
            moonLight.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(52f, -30f, 0f);

            // ---- environment (everything under here is baked into the NavMesh)
            GameObject env = new GameObject("Environment");
            AddBox(env.transform, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(52f, 0.2f, 52f), floorMat);

            // outer walls
            AddBox(env.transform, "Wall_N", new Vector3(0f, 1.5f, 26f), new Vector3(52.4f, 3f, 0.4f), wallMat);
            AddBox(env.transform, "Wall_S", new Vector3(0f, 1.5f, -26f), new Vector3(52.4f, 3f, 0.4f), wallMat);
            AddBox(env.transform, "Wall_E", new Vector3(26f, 1.5f, 0f), new Vector3(0.4f, 3f, 52.4f), wallMat);
            AddBox(env.transform, "Wall_W", new Vector3(-26f, 1.5f, 0f), new Vector3(0.4f, 3f, 52.4f), wallMat);

            // inner walls - an S-shaped approach so the player pops into view suddenly
            AddBox(env.transform, "Wall_Inner_1", new Vector3(-8f, 1.5f, 5f), new Vector3(16f, 3f, 0.4f), wallMat);
            AddBox(env.transform, "Wall_Inner_2", new Vector3(8f, 1.5f, -5f), new Vector3(16f, 3f, 0.4f), wallMat);
            AddBox(env.transform, "Wall_Inner_3", new Vector3(13f, 1.5f, 13f), new Vector3(0.4f, 3f, 14f), wallMat);
            AddBox(env.transform, "Wall_Inner_4", new Vector3(-15f, 1.5f, -13f), new Vector3(0.4f, 3f, 16f), wallMat);

            // crates
            AddBox(env.transform, "Crate_1", new Vector3(5f, 0.6f, 3f), new Vector3(2f, 1.2f, 2f), crateMat);
            AddBox(env.transform, "Crate_2", new Vector3(-6f, 0.6f, -10f), new Vector3(2f, 1.2f, 2f), crateMat);
            AddBox(env.transform, "Crate_3", new Vector3(17f, 0.6f, -14f), new Vector3(2.4f, 1.2f, 2.4f), crateMat);

            // hide spots (the wrecked locker prop)
            Vector3[] lockerPos =
            {
                new Vector3(-6f, 0f, 9f), new Vector3(6f, 0f, 11f), new Vector3(19f, 0f, -3f), new Vector3(-19f, 0f, -6f),
                new Vector3(-3f, 0f, -17f), new Vector3(10f, 0f, -19f), new Vector3(-20f, 0f, 16f), new Vector3(20f, 0f, 19f)
            };
            float[] lockerYaw = { 20f, -35f, 90f, -80f, 160f, 200f, 45f, -120f };
            for (int i = 0; i < lockerPos.Length; i++)
            {
                GameObject l = (GameObject)PrefabUtility.InstantiatePrefab(lockerPrefab, env.transform);
                l.name = "WreckedLocker_" + (i + 1);
                l.transform.position = lockerPos[i];
                l.transform.rotation = Quaternion.Euler(0f, lockerYaw[i], 0f);
            }

            NavMeshSurface surface = env.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            env.AddComponent<ScreamerAI.TestHarness.RuntimeNavMeshBaker>();

            // ---- atmosphere lights (red emergency lamps)
            AddPointLight("EmergencyLight_1", new Vector3(0f, 2.6f, 0f), new Color(1f, 0.18f, 0.1f), 7f, 16f);
            AddPointLight("EmergencyLight_2", new Vector3(-12f, 2.6f, 14f), new Color(1f, 0.3f, 0.1f), 6f, 15f);
            AddPointLight("EmergencyLight_3", new Vector3(14f, 2.6f, -12f), new Color(1f, 0.18f, 0.1f), 6f, 15f);
            AddPointLight("EmergencyLight_4", new Vector3(-14f, 2.6f, -20f), new Color(0.9f, 0.4f, 0.15f), 5f, 14f);

            // ---- player
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.05f, -21f);
            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;

            GameObject camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 150f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
            camGo.AddComponent<AudioListener>();

            GameObject flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.15f, -0.1f, 0.1f);
            Light flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Spot;
            flash.range = 28f;
            flash.spotAngle = 55f;
            flash.innerSpotAngle = 30f;
            flash.intensity = 25f;
            flash.color = new Color(1f, 0.95f, 0.8f);
            flash.shadows = LightShadows.Soft;

            ScreamerAI.TestHarness.TestPlayerController pc = player.AddComponent<ScreamerAI.TestHarness.TestPlayerController>();
            pc.cameraPivot = camGo.transform;
            pc.flashlight = flash;

            // ---- the Screamer
            GameObject screamer = (GameObject)PrefabUtility.InstantiatePrefab(screamerPrefab);
            screamer.name = "Screamer";
            screamer.transform.position = new Vector3(5f, 0f, 14f);
            screamer.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            ScreamerBrain brain = screamer.GetComponent<ScreamerBrain>();
            brain.player = player.transform;

            // ---- other zombies that hear the scream
            Vector3[] dummyPos =
            {
                new Vector3(18f, 0f, 8f), new Vector3(-12f, 0f, 20f), new Vector3(-20f, 0f, 2f),
                new Vector3(14f, 0f, -22f), new Vector3(-4f, 0f, -2f)
            };
            for (int i = 0; i < dummyPos.Length; i++)
            {
                GameObject d = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                d.name = "DummyZombie_" + (i + 1);
                d.transform.position = dummyPos[i];
            }

            // ---- debug HUD
            GameObject hud = new GameObject("ScreamerDebugOverlay");
            hud.AddComponent<ScreamerAI.TestHarness.ScreamerDebugOverlay>();

            // ---- bake the NavMesh now and keep it as an asset next to the scene
            Physics.SyncTransforms();
            try
            {
                surface.BuildNavMesh();
                if (surface.navMeshData != null)
                {
                    AssetDatabase.DeleteAsset(NavMeshAssetPath);
                    AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Screamer] Editor NavMesh bake failed (" + e.Message + "). The scene will bake one at runtime instead.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ------------------------------------------------------------------ small helpers
        static GameObject AddBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void AddPointLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.position = pos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
