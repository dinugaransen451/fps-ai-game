using UnityEngine;
using UnityEngine.AI;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>Builds the three zombie types at runtime (components wired up in code, model from GameAssets if present).</summary>
    public static class ZombieFactory
    {
        public static GameObject Spawn(ZombieKind kind, Vector3 position, float yaw, Transform player, GameAssets assets)
        {
            switch (kind)
            {
                case ZombieKind.Tank: return SpawnTank(position, yaw, player, assets);
                case ZombieKind.Screamer: return SpawnScreamer(position, yaw, player, assets);
                default: return SpawnRunner(position, yaw, player, assets);
            }
        }

        // ------------------------------------------------------------------ Runner (Member 4)
        static GameObject SpawnRunner(Vector3 pos, float yaw, Transform player, GameAssets assets)
        {
            GameObject root = new GameObject("Runner");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            CharacterController cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;

            ZombieHealth hp = root.AddComponent<ZombieHealth>();
            hp.kind = ZombieKind.Runner;
            hp.maxHealth = 70f;
            hp.health = 70f;

            AddVisual(root, assets, new Color(1f, 0.78f, 0.74f), 1f, new Color(0.55f, 0.65f, 0.45f), new Color(0.4f, 0.12f, 0.1f));
            root.AddComponent<ZombieAnimDriver>();
            root.AddComponent<AgentMotor>();
            RunnerBrain brain = root.AddComponent<RunnerBrain>();
            brain.player = player;
            return root;
        }

        // ------------------------------------------------------------------ Tank (Member 2)
        static GameObject SpawnTank(Vector3 pos, float yaw, Transform player, GameAssets assets)
        {
            GameObject root = new GameObject("Tank");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.55f;
            col.height = 2.3f;
            col.center = new Vector3(0f, 1.15f, 0f);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.55f;
            agent.height = 2.3f;
            agent.speed = 1f;
            agent.acceleration = 8f;
            agent.angularSpeed = 140f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            ZombieHealth hp = root.AddComponent<ZombieHealth>();
            hp.kind = ZombieKind.Tank;
            hp.maxHealth = 320f;
            hp.health = 320f;

            AddVisual(root, assets, new Color(0.55f, 0.6f, 0.55f), 1.3f, new Color(0.45f, 0.5f, 0.45f), new Color(0.15f, 0.15f, 0.18f));
            root.AddComponent<ZombieAnimDriver>();
            AgentLocomotionAnimator loco = root.AddComponent<AgentLocomotionAnimator>();
            loco.runSpeedReference = 3f;
            TankBrain brain = root.AddComponent<TankBrain>();
            brain.player = player;
            return root;
        }

        // ------------------------------------------------------------------ Screamer (Member 3)
        static GameObject SpawnScreamer(Vector3 pos, float yaw, Transform player, GameAssets assets)
        {
            GameObject go;
            if (assets != null && assets.screamerPrefab != null)
            {
                go = Object.Instantiate(assets.screamerPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
                go.name = "Screamer";
            }
            else
            {
                go = new GameObject("Screamer");
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                CapsuleCollider col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.35f;
                col.height = 1.9f;
                col.center = new Vector3(0f, 0.95f, 0f);
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                NavMeshAgent agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.35f;
                agent.height = 1.9f;
                agent.speed = 1f;
                agent.acceleration = 14f;
                agent.angularSpeed = 240f;
                agent.stoppingDistance = 0.15f;

                BlockRig.Create(go.transform, new Color(0.6f, 0.55f, 0.7f), new Color(0.25f, 0.2f, 0.3f), 0.95f);
                go.AddComponent<ZombieAnimDriver>();
                go.AddComponent<AgentLocomotionAnimator>();
                go.AddComponent<ScreamerBrain>();
            }

            ZombieHealth hp = go.GetComponent<ZombieHealth>();
            if (hp == null) hp = go.AddComponent<ZombieHealth>();
            hp.kind = ZombieKind.Screamer;
            hp.maxHealth = 90f;
            hp.health = 90f;

            ScreamerBrain brain = go.GetComponent<ScreamerBrain>();
            if (brain != null)
            {
                brain.player = player;
                if (brain.screamClip == null) brain.screamClip = (assets != null && assets.screamClip != null) ? assets.screamClip : GameAudio.Roar;
            }
            return go;
        }

        // ------------------------------------------------------------------ visuals
        static void AddVisual(GameObject root, GameAssets assets, Color tint, float scale, Color blockSkin, Color blockCloth)
        {
            if (assets != null && assets.zombieVisualPrefab != null)
            {
                GameObject vis = Object.Instantiate(assets.zombieVisualPrefab, root.transform);
                vis.name = "Body";
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;
                vis.transform.localScale = Vector3.one * scale;

                Renderer[] rs = vis.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < rs.Length; i++)
                {
                    Material[] mats = rs[i].materials;   // instances, so tinting one zombie doesn't tint the rest
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m].SetColor("_BaseColor", tint);
                        mats[m].SetColor("_Color", tint);
                    }
                }

                ZombieProceduralMotion motion = root.AddComponent<ZombieProceduralMotion>();
                motion.animator = vis.GetComponentInChildren<Animator>();
            }
            else
            {
                BlockRig.Create(root.transform, blockSkin, blockCloth, scale);
            }
        }
    }
}
