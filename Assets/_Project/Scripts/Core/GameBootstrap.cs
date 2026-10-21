using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The only object the game scene really needs. On Start it makes sure the level exists (builds it if the
    /// scene has none, or reuses the one baked by the editor), then creates the player, the navigation graph,
    /// the Director, the HUD and the ambience.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Objective")]
        public int killsRequired = 20;
        public int totalToSpawn = 26;

        NavGrid grid;

        void Start()
        {
            GameAssets assets = GameAssets.Load();

            // ---------------------------------------------------------------- level
            GameObject world = GameObject.Find("World");
            Transform spawnPoints;
            Vector3 start = LevelBuilder.PlayerStart;
            float yaw = 0f;

            if (world == null)
            {
                LevelRefs refs = LevelBuilder.Build(assets, true);
                spawnPoints = refs.spawnPoints;
                start = refs.playerStart;
            }
            else
            {
                spawnPoints = world.transform.Find("SpawnPoints");
                Transform level = world.transform.Find("Level");
                NavMeshSurface surface = level != null ? level.GetComponent<NavMeshSurface>() : null;
                if (NavMesh.CalculateTriangulation().vertices.Length == 0 && surface != null)
                {
                    Debug.Log("[GameBootstrap] No baked NavMesh found - baking now.");
                    Physics.SyncTransforms();
                    surface.BuildNavMesh();
                }
                LevelBuilder.BuildPatrolRoutes();
                LevelBuilder.ApplyAtmosphere();
            }

            // navigation graph for the Runner's A* is built in a moment (needs the doors un-carved first)
            StartCoroutine(BuildGridRoutine());

            // ---------------------------------------------------------------- player
            GameObject player = PlayerController.Create(start, yaw);
            PlayerStats stats = player.GetComponent<PlayerStats>();
            PlayerController pc = player.GetComponent<PlayerController>();

            // ---------------------------------------------------------------- game systems
            GameObject systems = new GameObject("GameSystems");
            GameManager gm = systems.AddComponent<GameManager>();
            gm.killsRequired = killsRequired;
            gm.playerController = pc;
            gm.playerStats = stats;

            Director director = systems.AddComponent<Director>();
            director.player = player.transform;
            director.stats = stats;
            director.spawnPointsParent = spawnPoints;
            director.playerCamera = pc.cam;
            director.assets = assets;
            director.totalToSpawn = totalToSpawn;

            GameHUD hud = systems.AddComponent<GameHUD>();
            hud.stats = stats;
            hud.controller = pc;
            hud.weapon = player.GetComponent<PlayerWeapon>();
            hud.interaction = player.GetComponent<PlayerInteraction>();
            hud.director = director;
            hud.cam = pc.cam;

            GameAudio.StartAmbience(systems);
        }

        // The A* graph must be sampled from the NavMesh with every door / barricade / crate obstacle switched off,
        // otherwise their carved holes would be missing from the graph for good. Closed doors and barricades are
        // handled afterwards as re-routing COSTS (see NavGrid), not as missing nodes.
        IEnumerator BuildGridRoutine()
        {
            NavMeshObstacle[] all = FindObjectsByType<NavMeshObstacle>();
            List<NavMeshObstacle> wasOn = new List<NavMeshObstacle>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled)
                {
                    wasOn.Add(all[i]);
                    all[i].enabled = false;
                }
            }

            yield return null;
            yield return null;

            grid = NavGrid.Build(Vector3.zero, new Vector3(LevelBuilder.Width, 0f, LevelBuilder.Depth), 0.5f,
                LevelBuilder.FloorHeights(), LevelBuilder.RampZones(), NavMesh.AllAreas);

            for (int i = 0; i < wasOn.Count; i++)
                if (wasOn[i] != null) wasOn[i].enabled = true;
        }

        void OnDestroy()
        {
            if (grid != null) grid.Dispose();
        }
    }
}
