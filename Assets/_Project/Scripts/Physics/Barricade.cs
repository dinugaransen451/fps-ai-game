using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// Pushable wooden barricade (Member 2). A heavy rigidbody the player can shove with the character
    /// controller (see PlayerController.OnControllerColliderHit) to seal a doorway. While it is stationary it
    /// carves the NavMesh. It is an IBlocker: the Tank breaks it, the Runner re-routes around it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Barricade : MonoBehaviour, IBlocker
    {
        public float health = 160f;
        public float pathPenalty = 80f;

        BoxCollider box;
        NavMeshObstacle obstacle;
        Rigidbody rb;
        Vector3 lastNotified;
        bool broken;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            obstacle = GetComponent<NavMeshObstacle>();
            rb = GetComponent<Rigidbody>();
            lastNotified = transform.position;
        }

        void OnEnable() { BlockerRegistry.Register(this); }
        void OnDisable() { BlockerRegistry.Unregister(this); }

        public bool IsBlocking { get { return !broken; } }
        public float PathPenalty { get { return pathPenalty; } }
        public bool TankCanBreak { get { return true; } }
        public Transform BlockTransform { get { return transform; } }
        public Vector3 BreakPoint { get { return box != null ? box.bounds.center : transform.position; } }

        public Bounds BlockBounds
        {
            get
            {
                if (box == null) return new Bounds(transform.position, Vector3.one);
                Bounds b = box.bounds;
                b.Expand(new Vector3(0.2f, 0f, 0.2f));
                return b;
            }
        }

        void FixedUpdate()
        {
            // tell the pathfinders when a pushed barricade has moved far enough to matter
            if (!broken && (transform.position - lastNotified).sqrMagnitude > 0.09f)
            {
                lastNotified = transform.position;
                BlockerRegistry.NotifyChanged();
            }
        }

        public void TakeBlockDamage(float amount, GameObject by)
        {
            if (broken) return;
            health -= amount;
            GameAudio.PlayAt(GameAudio.WoodBreak, BreakPoint, 0.6f);
            NoiseSystem.Emit(BreakPoint, 16f, NoiseKind.Impact);
            GameFX.Debris(BreakPoint, new Color(0.4f, 0.27f, 0.15f), 2, 1.5f);
            if (health <= 0f) Break();
        }

        void Break()
        {
            broken = true;
            GameAudio.PlayAt(GameAudio.WoodBreak, BreakPoint, 1f);
            GameFX.Debris(BreakPoint, new Color(0.4f, 0.27f, 0.15f), 10, 5f);
            if (obstacle != null) obstacle.enabled = false;
            BlockerRegistry.Unregister(this);
            Destroy(gameObject);
        }

        /// <summary>Builds a barricade (frame + planks + collider + NavMesh obstacle).</summary>
        public static Barricade Create(Transform parent, Vector3 position, float yawDegrees)
        {
            GameObject root = new GameObject("Barricade");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            Material wood = GameMaterials.Surface(SurfaceKind.Wood, 1f, 1f);
            AddVisual(root.transform, new Vector3(-0.8f, 0.65f, 0f), new Vector3(0.14f, 1.3f, 0.2f), wood);
            AddVisual(root.transform, new Vector3(0.8f, 0.65f, 0f), new Vector3(0.14f, 1.3f, 0.2f), wood);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.2f + i * 0.3f;
                float zOff = (i % 2 == 0) ? 0.1f : -0.1f;
                float tilt = (i % 2 == 0) ? 3f : -4f;
                GameObject p = AddVisual(root.transform, new Vector3(0f, y, zOff), new Vector3(1.85f, 0.2f, 0.07f), wood);
                p.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.65f, 0f);
            col.size = new Vector3(1.9f, 1.3f, 0.4f);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 90f;
            body.linearDamping = 4f;
            body.angularDamping = 6f;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            NavMeshObstacle ob = root.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.center = new Vector3(0f, 0.7f, 0f);
            ob.size = new Vector3(2.0f, 1.4f, 0.6f);
            ob.carving = true;
            ob.carveOnlyStationary = true;
            ob.carvingMoveThreshold = 0.1f;
            ob.carvingTimeToStationary = 0.4f;

            return root.AddComponent<Barricade>();
        }

        static GameObject AddVisual(Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Plank";
            Collider c = g.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }
    }
}
