using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>What the level builder hands back to the game.</summary>
    public class LevelRefs
    {
        public GameObject root;
        public Transform level;          // static geometry (NavMesh is baked from this)
        public Transform dynamicRoot;    // doors, barricades, crates, pickups
        public Transform spawnPoints;    // Director markers
        public Vector3 playerStart;
        public float playerYaw;
        public float[] floorHeights;
        public Bounds[] rampZones;
        public Bounds levelBounds;
        public NavMeshSurface surface;
    }

    /// <summary>
    /// WORLD BUILDER (Member 1): generates the whole building - 2 floors, 5 rooms + corridor + stairwell per floor,
    /// textured walls/floors/ceilings, doorways, narrow service gaps, props, flickering horror lighting, blood decals,
    /// spawn markers, patrol routes - and bakes the NavMesh. Works at runtime and in the editor (Tools > Zombie Game).
    ///
    /// Layout of each floor (x right, z up, 40 x 30 m):
    ///     z=30 +-----------+-----------+-----------+
    ///          |  D Cafe   | E Lab     | F Stairs  |
    ///     z=16 +--door-----+--door-----+--open-----+
    ///          |         main corridor (4 m)       |
    ///     z=12 +--door-----+--door-----+--door-----+
    ///          |  A Lobby  | B Office  | C Storage |
    ///     z=0  +-----------+-----------+-----------+
    ///          x=0         x=12        x=24        x=40
    /// </summary>
    public static class LevelBuilder
    {
        public const float Width = 40f;
        public const float Depth = 30f;
        public const float FloorH = 4f;
        public const float SlabT = 0.3f;
        public const float WallT = 0.3f;
        public const float CeilH = 3.7f;
        public const float DoorH = 2.5f;

        // stair ramp (runs along +z, rises FloorH over 8 m)
        const float RampX0 = 33.9f, RampX1 = 36.1f, RampZ0 = 18f, RampZ1 = 26f;

        static Transform levelT, dynT, lightT, spawnT, patrolT;
        static GameAssets assets;
        static System.Random rng;

        struct Opening
        {
            public float c, w, h;
            public Opening(float c, float w, float h) { this.c = c; this.w = w; this.h = h; }
        }

        // ================================================================== entry point
        public static LevelRefs Build(GameAssets gameAssets, bool bakeNavMesh)
        {
            assets = gameAssets;
            rng = new System.Random(12345);

            GameObject root = new GameObject("World");
            levelT = Child(root.transform, "Level");
            dynT = Child(root.transform, "Dynamic");
            lightT = Child(root.transform, "Lighting");
            spawnT = Child(root.transform, "SpawnPoints");
            patrolT = Child(root.transform, "PatrolRoutes");

            ApplyAtmosphere();

            for (int floor = 0; floor < 2; floor++)
            {
                float yb = floor * FloorH;
                BuildShell(floor, yb);
                BuildStructure(floor, yb);
                if (floor == 0) FurnishFloor1(yb); else FurnishFloor2(yb);
                BuildLights(floor, yb);
                BuildPickups(floor, yb);
                BuildSpawnMarkers(floor, yb);
                BuildDecals(floor, yb);
            }
            BuildStairs();
            BuildPatrolRoutes();
            BuildNarrowVolumes();

            LevelRefs refs = new LevelRefs();
            refs.root = root;
            refs.level = levelT;
            refs.dynamicRoot = dynT;
            refs.spawnPoints = spawnT;
            refs.playerStart = PlayerStart;
            refs.playerYaw = 0f;
            refs.floorHeights = FloorHeights();
            refs.rampZones = RampZones();
            refs.levelBounds = new Bounds(new Vector3(Width * 0.5f, FloorH, Depth * 0.5f), new Vector3(Width, FloorH * 2f, Depth));

            // NavMesh surface on the static geometry
            NavMeshSurface surface = levelT.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.defaultArea = 0;
            refs.surface = surface;
            if (bakeNavMesh)
            {
                Physics.SyncTransforms();
                surface.BuildNavMesh();
            }
            return refs;
        }

        // ---- constants the game needs even when the level was baked into the scene by the editor
        public static readonly Vector3 PlayerStart = new Vector3(6f, 0.05f, 5f);
        public static float[] FloorHeights() { return new float[] { 0f, FloorH }; }

        public static Bounds[] RampZones()
        {
            return new Bounds[] { new Bounds(new Vector3((RampX0 + RampX1) * 0.5f, FloorH * 0.5f, (RampZ0 + RampZ1) * 0.5f), new Vector3(RampX1 - RampX0 + 0.4f, FloorH + 0.2f, RampZ1 - RampZ0 + 0.4f)) };
        }

        public static void ApplyAtmosphere()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.065f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.025f, 0.03f, 0.03f);
            RenderSettings.fogDensity = 0.022f;
        }

        // ================================================================== primitives
        static Transform Child(Transform parent, string name)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        static void Tile(Vector3 size, out float tu, out float tv)
        {
            if (size.y < size.x && size.y < size.z) { tu = size.x * 0.5f; tv = size.z * 0.5f; }
            else { tu = Mathf.Max(size.x, size.z) * 0.5f; tv = size.y * 0.5f; }
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, SurfaceKind kind, bool collider = true)
        {
            float tu, tv;
            Tile(size, out tu, out tv);
            return BoxMat(parent, name, center, size, GameMaterials.Surface(kind, tu, tv), collider);
        }

        static GameObject BoxMat(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider = true)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider)
            {
                Collider c = g.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            return g;
        }

        static GameObject Vis(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Collider c = g.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        // ================================================================== shell: floors, ceilings, outer walls
        static void BuildShell(int floor, float yb)
        {
            string tag = "F" + (floor + 1);
            // floor slabs (the upper floor has a hole for the stairwell)
            List<float[]> pieces = floor == 0 ? FullPiece() : HolePieces();
            for (int i = 0; i < pieces.Count; i++)
            {
                float[] r = pieces[i];
                Vector3 size = new Vector3(r[1] - r[0], SlabT, r[3] - r[2]);
                Vector3 c = new Vector3((r[0] + r[1]) * 0.5f, yb - SlabT * 0.5f, (r[2] + r[3]) * 0.5f);
                Box(levelT, tag + "_FloorSlab_" + i, c, size, floor == 0 ? SurfaceKind.Tiles : SurfaceKind.Tiles);
            }

            // ceiling panels (under the next slab); the first floor's ceiling has the stairwell hole too
            List<float[]> ceil = floor == 0 ? HolePieces() : FullPiece();
            for (int i = 0; i < ceil.Count; i++)
            {
                float[] r = ceil[i];
                Vector3 size = new Vector3(r[1] - r[0], 0.06f, r[3] - r[2]);
                Vector3 c = new Vector3((r[0] + r[1]) * 0.5f, yb + CeilH - 0.03f, (r[2] + r[3]) * 0.5f);
                Box(levelT, tag + "_Ceiling_" + i, c, size, SurfaceKind.CeilingTile, false);
            }

            // roof slab above the top floor
            if (floor == 1)
            {
                Box(levelT, "RoofSlab", new Vector3(Width * 0.5f, yb + CeilH + SlabT * 0.5f, Depth * 0.5f),
                    new Vector3(Width + 0.6f, SlabT, Depth + 0.6f), SurfaceKind.Concrete);
            }
            else
            {
                // underside of the first-floor slab is the 2nd floor; nothing else needed
            }

            // outer walls
            WallX(levelT, tag + "_Wall_S", 0f, 0f, Width, yb, SurfaceKind.Concrete);
            WallX(levelT, tag + "_Wall_N", Depth, 0f, Width, yb, SurfaceKind.Concrete);
            WallZ(levelT, tag + "_Wall_W", 0f, 0f, Depth, yb, SurfaceKind.Concrete);
            WallZ(levelT, tag + "_Wall_E", Width, 0f, Depth, yb, SurfaceKind.Concrete);
        }

        static List<float[]> FullPiece()
        {
            List<float[]> l = new List<float[]>();
            l.Add(new float[] { -0.3f, Width + 0.3f, -0.3f, Depth + 0.3f });
            return l;
        }

        // rectangles {xmin, xmax, zmin, zmax} around the stairwell hole
        static List<float[]> HolePieces()
        {
            List<float[]> l = new List<float[]>();
            l.Add(new float[] { -0.3f, RampX0, -0.3f, Depth + 0.3f });
            l.Add(new float[] { RampX1, Width + 0.3f, -0.3f, Depth + 0.3f });
            l.Add(new float[] { RampX0, RampX1, -0.3f, RampZ0 });
            l.Add(new float[] { RampX0, RampX1, RampZ1, Depth + 0.3f });
            return l;
        }

        // ================================================================== walls with openings
        static void WallX(Transform p, string name, float z, float x0, float x1, float yb, SurfaceKind kind, params Opening[] ops)
        {
            System.Array.Sort(ops, (a, b) => a.c.CompareTo(b.c));
            float cur = x0;
            int seg = 0;
            for (int i = 0; i < ops.Length; i++)
            {
                float l = ops[i].c - ops[i].w * 0.5f, r = ops[i].c + ops[i].w * 0.5f;
                if (l > cur + 0.01f) Box(p, name + "_" + seg++, new Vector3((cur + l) * 0.5f, yb + CeilH * 0.5f, z), new Vector3(l - cur, CeilH, WallT), kind);
                float lh = CeilH - ops[i].h;
                Box(p, name + "_Lintel" + i, new Vector3((l + r) * 0.5f, yb + ops[i].h + lh * 0.5f, z), new Vector3(r - l, lh, WallT), kind);
                cur = r;
            }
            if (x1 > cur + 0.01f) Box(p, name + "_" + seg, new Vector3((cur + x1) * 0.5f, yb + CeilH * 0.5f, z), new Vector3(x1 - cur, CeilH, WallT), kind);
        }

        static void WallZ(Transform p, string name, float x, float z0, float z1, float yb, SurfaceKind kind, params Opening[] ops)
        {
            System.Array.Sort(ops, (a, b) => a.c.CompareTo(b.c));
            float cur = z0;
            int seg = 0;
            for (int i = 0; i < ops.Length; i++)
            {
                float l = ops[i].c - ops[i].w * 0.5f, r = ops[i].c + ops[i].w * 0.5f;
                if (l > cur + 0.01f) Box(p, name + "_" + seg++, new Vector3(x, yb + CeilH * 0.5f, (cur + l) * 0.5f), new Vector3(WallT, CeilH, l - cur), kind);
                float lh = CeilH - ops[i].h;
                Box(p, name + "_Lintel" + i, new Vector3(x, yb + ops[i].h + lh * 0.5f, (l + r) * 0.5f), new Vector3(WallT, lh, r - l), kind);
                cur = r;
            }
            if (z1 > cur + 0.01f) Box(p, name + "_" + seg, new Vector3(x, yb + CeilH * 0.5f, (cur + z1) * 0.5f), new Vector3(WallT, CeilH, z1 - cur), kind);
        }

        // ================================================================== interior walls + doors
        static void BuildStructure(int floor, float yb)
        {
            string tag = "F" + (floor + 1);
            SurfaceKind wall = SurfaceKind.Plaster;
            float dw = 1.4f;   // door width

            // corridor south wall z=12 : doors to A (6), B (18), C (32)
            WallX(levelT, tag + "_CorrS", 12f, 0f, Width, yb, wall,
                new Opening(6f, dw, DoorH), new Opening(18f, dw, DoorH), new Opening(32f, dw, DoorH));
            // corridor north wall z=16 : doors to D (8), E (22); open entrance to the stair hall (35)
            WallX(levelT, tag + "_CorrN", 16f, 0f, Width, yb, wall,
                new Opening(8f, dw, DoorH), new Opening(22f, dw, DoorH), new Opening(35f, 3.0f, DoorH));

            // partitions between the south rooms
            WallZ(levelT, tag + "_A_B", 12f, 0f, 12f, yb, wall, new Opening(6f, 2.0f, DoorH));
            WallZ(levelT, tag + "_B_C", 24f, 0f, 12f, yb, wall, new Opening(6f, 1.3f, DoorH));   // narrow service gap
            // partitions between the north rooms
            WallZ(levelT, tag + "_D_E", 16f, 16f, Depth, yb, wall, new Opening(23f, 1.3f, DoorH)); // narrow service gap
            WallZ(levelT, tag + "_E_F", 28f, 16f, Depth, yb, wall, new Opening(22f, 2.0f, DoorH));

            // doors
            MakeDoor(tag + "_Door_A", new Vector3(6f, yb, 12f), 0f, dw);
            MakeDoor(tag + "_Door_B", new Vector3(18f, yb, 12f), 0f, dw);
            MakeDoor(tag + "_Door_C", new Vector3(32f, yb, 12f), 0f, dw);
            MakeDoor(tag + "_Door_D", new Vector3(8f, yb, 16f), 0f, dw);
            MakeDoor(tag + "_Door_E", new Vector3(22f, yb, 16f), 0f, dw);

            // exit signs
            BoxMat(levelT, tag + "_ExitSign1", new Vector3(35f, yb + 3.1f, 15.8f), new Vector3(0.7f, 0.22f, 0.06f), GameMaterials.Emissive(new Color(0.1f, 1f, 0.3f), 2.5f), false);
            BoxMat(levelT, tag + "_ExitSign2", new Vector3(6f, yb + 3.1f, 11.8f), new Vector3(0.7f, 0.22f, 0.06f), GameMaterials.Emissive(new Color(0.1f, 1f, 0.3f), 2.5f), false);

            // hazard stripes along the corridor edges
            Box(levelT, tag + "_HazardW", new Vector3(20f, yb + 0.01f, 12.45f), new Vector3(39f, 0.02f, 0.5f), SurfaceKind.Hazard, false);
            Box(levelT, tag + "_HazardE", new Vector3(20f, yb + 0.01f, 15.55f), new Vector3(39f, 0.02f, 0.5f), SurfaceKind.Hazard, false);
        }

        static Door MakeDoor(string name, Vector3 center, float yaw, float width)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(dynT, false);
            root.transform.position = center;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Transform pivot = Child(root.transform, "Hinge");
            pivot.localPosition = new Vector3(-width * 0.5f, 0f, 0f);

            GameObject slab = new GameObject("Slab");
            slab.transform.SetParent(pivot, false);
            slab.transform.localPosition = new Vector3(width * 0.5f, 1.25f, 0f);

            float leaf = width - 0.04f;
            Vis(slab.transform, "Leaf", Vector3.zero, new Vector3(leaf, 2.45f, 0.07f), GameMaterials.Surface(SurfaceKind.Metal, 1f, 1.5f));
            Vis(slab.transform, "Handle", new Vector3(leaf * 0.42f, -0.1f, 0.07f), new Vector3(0.18f, 0.04f, 0.06f), GameMaterials.Solid(new Color(0.7f, 0.7f, 0.7f), 0.7f, 0.8f));
            Vis(slab.transform, "Window", new Vector3(0f, 0.55f, 0.04f), new Vector3(0.4f, 0.5f, 0.02f), GameMaterials.Solid(new Color(0.05f, 0.08f, 0.08f), 0.9f));

            BoxCollider col = slab.AddComponent<BoxCollider>();
            col.size = new Vector3(leaf, 2.45f, 0.08f);

            NavMeshObstacle ob = slab.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.size = new Vector3(width, 2.4f, 0.5f);
            ob.carving = true;
            ob.carveOnlyStationary = true;

            Door d = root.AddComponent<Door>();
            d.pivot = pivot;
            d.slab = slab.transform;
            d.slabCollider = col;
            d.obstacle = ob;
            return d;
        }

        // ================================================================== stairs
        static void BuildStairs()
        {
            float cx = (RampX0 + RampX1) * 0.5f;
            float width = RampX1 - RampX0;
            Vector3 a = new Vector3(cx, 0f, RampZ0);
            Vector3 b = new Vector3(cx, FloorH, RampZ1);
            Vector3 dir = (b - a);
            float len = dir.magnitude;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float thick = 0.3f;
            Vector3 mid = (a + b) * 0.5f - (rot * Vector3.up) * (thick * 0.5f);

            GameObject ramp = Box(levelT, "StairRamp", mid, new Vector3(width, thick, len), SurfaceKind.Concrete);
            ramp.transform.rotation = rot;

            // side walls of the stairwell (rise above the upper floor as a railing)
            Box(levelT, "Stair_WallW", new Vector3(RampX0 - 0.1f, 2.5f, (RampZ0 + RampZ1) * 0.5f), new Vector3(0.2f, 5.0f, RampZ1 - RampZ0), SurfaceKind.Plaster);
            Box(levelT, "Stair_WallE", new Vector3(RampX1 + 0.1f, 2.5f, (RampZ0 + RampZ1) * 0.5f), new Vector3(0.2f, 5.0f, RampZ1 - RampZ0), SurfaceKind.Plaster);
            // railing across the south edge of the hole on the upper floor
            Box(levelT, "Stair_RailS", new Vector3(cx, FloorH + 0.5f, RampZ0 - 0.1f), new Vector3(width, 1.0f, 0.2f), SurfaceKind.Metal);
        }

        // ================================================================== props
        static void Prop(string kind, float x, float yb, float z, float yaw, float length = 0f)
        {
            Vector3 pos = new Vector3(x, yb, z);
            Material wood = GameMaterials.Surface(SurfaceKind.Wood, 1f, 1f);
            Material metal = GameMaterials.Surface(SurfaceKind.Metal, 1f, 1f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            switch (kind)
            {
                case "desk":
                {
                    GameObject r = Furniture("Desk", pos, rot, new Vector3(1.6f, 0.8f, 0.8f));
                    Vis(r.transform, "Top", new Vector3(0f, 0.77f, 0f), new Vector3(1.6f, 0.06f, 0.8f), wood);
                    Vis(r.transform, "SideL", new Vector3(-0.77f, 0.37f, 0f), new Vector3(0.06f, 0.74f, 0.76f), wood);
                    Vis(r.transform, "SideR", new Vector3(0.77f, 0.37f, 0f), new Vector3(0.06f, 0.74f, 0.76f), wood);
                    Vis(r.transform, "Monitor", new Vector3(0.2f, 0.98f, -0.1f), new Vector3(0.42f, 0.3f, 0.05f), GameMaterials.Emissive(new Color(0.25f, 0.8f, 0.7f), 0.8f));
                    break;
                }
                case "counter":
                {
                    float L = length > 0f ? length : 3f;
                    GameObject r = Furniture("Counter", pos, rot, new Vector3(L, 1.0f, 0.8f));
                    Vis(r.transform, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(L, 1.0f, 0.8f), wood);
                    Vis(r.transform, "Top", new Vector3(0f, 1.03f, 0f), new Vector3(L + 0.1f, 0.06f, 0.9f), metal);
                    break;
                }
                case "shelf":
                {
                    GameObject r = Furniture("Shelf", pos, rot, new Vector3(1.2f, 2.0f, 0.4f));
                    Vis(r.transform, "Body", new Vector3(0f, 1.0f, 0f), new Vector3(1.2f, 2.0f, 0.4f), wood);
                    for (int i = 0; i < 4; i++)
                    {
                        Vis(r.transform, "Board" + i, new Vector3(0f, 0.45f + i * 0.45f, -0.02f), new Vector3(1.1f, 0.04f, 0.44f), metal);
                        for (int k = 0; k < 3; k++)
                        {
                            Color c = Color.HSVToRGB((float)rng.NextDouble(), 0.35f, 0.45f);
                            Vis(r.transform, "Item", new Vector3(-0.4f + k * 0.4f + R(-0.08f, 0.08f), 0.45f + i * 0.45f + 0.12f, -0.1f), new Vector3(R(0.15f, 0.3f), 0.2f, 0.18f), GameMaterials.Solid(c, 0.2f));
                        }
                    }
                    break;
                }
                case "crate":
                {
                    Furniture("Crate", pos, rot, new Vector3(1f, 1f, 1f));
                    GameObject r = levelT.GetChild(levelT.childCount - 1).gameObject;
                    Vis(r.transform, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f), wood);
                    Vis(r.transform, "StrapA", new Vector3(0f, 0.5f, 0f), new Vector3(1.02f, 0.12f, 1.02f), metal);
                    break;
                }
                case "pcrate":
                {
                    GameObject r = new GameObject("PushableCrate");
                    r.transform.SetParent(dynT, false);
                    r.transform.position = pos + Vector3.up * 0.02f;
                    r.transform.rotation = rot;
                    BoxCollider bc = r.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0f, 0.45f, 0f);
                    bc.size = new Vector3(0.9f, 0.9f, 0.9f);
                    Vis(r.transform, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.9f), wood);
                    Vis(r.transform, "Strap", new Vector3(0f, 0.45f, 0f), new Vector3(0.92f, 0.1f, 0.92f), metal);
                    Rigidbody rb = r.AddComponent<Rigidbody>();
                    rb.mass = 25f;
                    rb.linearDamping = 3f;
                    rb.angularDamping = 5f;
                    rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                    NavMeshObstacle ob = r.AddComponent<NavMeshObstacle>();
                    ob.shape = NavMeshObstacleShape.Box;
                    ob.center = new Vector3(0f, 0.45f, 0f);
                    ob.size = new Vector3(0.95f, 0.9f, 0.95f);
                    ob.carving = true;
                    ob.carveOnlyStationary = true;
                    break;
                }
                case "table":
                {
                    GameObject r = Furniture("Table", pos, rot, new Vector3(1.8f, 0.85f, 0.9f));
                    Vis(r.transform, "Top", new Vector3(0f, 0.81f, 0f), new Vector3(1.8f, 0.07f, 0.9f), wood);
                    float[] sx = { -0.82f, 0.82f };
                    float[] sz = { -0.38f, 0.38f };
                    for (int i = 0; i < 2; i++)
                        for (int j = 0; j < 2; j++)
                            Vis(r.transform, "Leg", new Vector3(sx[i], 0.39f, sz[j]), new Vector3(0.07f, 0.78f, 0.07f), metal);
                    break;
                }
                case "couch":
                {
                    GameObject r = Furniture("Couch", pos, rot, new Vector3(2.0f, 0.95f, 0.9f));
                    Material fabric = GameMaterials.Surface(SurfaceKind.Carpet, 1f, 1f);
                    Vis(r.transform, "Seat", new Vector3(0f, 0.25f, 0f), new Vector3(2.0f, 0.45f, 0.9f), fabric);
                    Vis(r.transform, "Back", new Vector3(0f, 0.7f, 0.33f), new Vector3(2.0f, 0.55f, 0.25f), fabric);
                    Vis(r.transform, "ArmL", new Vector3(-0.9f, 0.45f, 0f), new Vector3(0.2f, 0.6f, 0.9f), fabric);
                    Vis(r.transform, "ArmR", new Vector3(0.9f, 0.45f, 0f), new Vector3(0.2f, 0.6f, 0.9f), fabric);
                    break;
                }
                case "bed":
                {
                    GameObject r = Furniture("Bed", pos, rot, new Vector3(2.0f, 0.7f, 0.95f));
                    Vis(r.transform, "Frame", new Vector3(0f, 0.3f, 0f), new Vector3(2.0f, 0.35f, 0.95f), metal);
                    Vis(r.transform, "Mattress", new Vector3(0f, 0.55f, 0f), new Vector3(1.9f, 0.2f, 0.88f), GameMaterials.Solid(new Color(0.55f, 0.58f, 0.55f), 0.1f));
                    Vis(r.transform, "Stain", new Vector3(0.1f, 0.66f, 0.05f), new Vector3(0.7f, 0.01f, 0.5f), GameMaterials.Solid(new Color(0.3f, 0.03f, 0.03f), 0.5f));
                    break;
                }
                case "rack":
                {
                    GameObject r = Furniture("ServerRack", pos, rot, new Vector3(0.8f, 2.0f, 0.8f));
                    Vis(r.transform, "Body", new Vector3(0f, 1.0f, 0f), new Vector3(0.8f, 2.0f, 0.8f), GameMaterials.Solid(new Color(0.06f, 0.06f, 0.07f), 0.5f, 0.5f));
                    for (int i = 0; i < 6; i++)
                    {
                        Color led = (i % 3 == 0) ? new Color(1f, 0.15f, 0.1f) : new Color(0.1f, 1f, 0.3f);
                        Vis(r.transform, "Led", new Vector3(-0.2f + (i % 2) * 0.3f, 0.5f + i * 0.25f, 0.41f), new Vector3(0.08f, 0.04f, 0.02f), GameMaterials.Emissive(led, 2.5f));
                    }
                    break;
                }
                case "console":
                {
                    float L = length > 0f ? length : 1.6f;
                    GameObject r = Furniture("Console", pos, rot, new Vector3(L, 1.0f, 0.8f));
                    Vis(r.transform, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(L, 1.0f, 0.8f), metal);
                    GameObject screen = Vis(r.transform, "Screen", new Vector3(0f, 1.2f, -0.1f), new Vector3(L * 0.8f, 0.36f, 0.04f), GameMaterials.Emissive(new Color(0.2f, 0.9f, 0.85f), 1.4f));
                    screen.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                    break;
                }
                case "labtable":
                {
                    float L = length > 0f ? length : 3f;
                    GameObject r = Furniture("LabTable", pos, rot, new Vector3(L, 0.95f, 1.1f));
                    Vis(r.transform, "Base", new Vector3(0f, 0.42f, 0f), new Vector3(L - 0.1f, 0.84f, 1.0f), metal);
                    Vis(r.transform, "Top", new Vector3(0f, 0.9f, 0f), new Vector3(L, 0.08f, 1.1f), GameMaterials.Solid(new Color(0.7f, 0.72f, 0.7f), 0.5f));
                    Vis(r.transform, "Vial", new Vector3(0.4f, 1.0f, 0.1f), new Vector3(0.1f, 0.2f, 0.1f), GameMaterials.Emissive(new Color(0.3f, 1f, 0.2f), 1.2f));
                    break;
                }
                case "locker":
                {
                    Locker(pos, rot);
                    break;
                }
                case "barrel":
                {
                    GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    b.name = "Barrel";
                    b.transform.SetParent(levelT, false);
                    b.transform.position = pos + Vector3.up * 0.5f;
                    b.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
                    b.GetComponent<Renderer>().sharedMaterial = GameMaterials.Solid(new Color(0.55f, 0.2f, 0.1f), 0.45f, 0.5f);
                    break;
                }
            }
        }

        static GameObject Furniture(string name, Vector3 pos, Quaternion rot, Vector3 colliderSize)
        {
            GameObject r = new GameObject(name);
            r.transform.SetParent(levelT, false);
            r.transform.position = pos;
            r.transform.rotation = rot;
            BoxCollider bc = r.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            bc.size = colliderSize;
            return r;
        }

        static void Locker(Vector3 pos, Quaternion rot)
        {
            GameObject g;
            if (assets != null && assets.lockerPrefab != null)
            {
                g = Object.Instantiate(assets.lockerPrefab, levelT);
                g.name = "WreckedLocker";
                g.transform.position = pos;
                g.transform.rotation = rot;
            }
            else
            {
                g = Furniture("Locker", pos, rot, new Vector3(0.8f, 1.9f, 0.6f));
                Vis(g.transform, "Body", new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 1.9f, 0.6f), GameMaterials.Surface(SurfaceKind.Metal, 1f, 1f));
                Vis(g.transform, "Door", new Vector3(0f, 0.95f, -0.31f), new Vector3(0.7f, 1.8f, 0.02f), GameMaterials.Solid(new Color(0.2f, 0.28f, 0.3f), 0.4f, 0.4f));
                g.AddComponent<HideSpot>();
            }
        }

        // ================================================================== furnishing
        static void FurnishFloor1(float yb)
        {
            // A - Reception / lobby
            Prop("counter", 3f, yb, 7f, 0f, 4f);
            Prop("couch", 9.6f, yb, 1.6f, 180f);
            Prop("crate", 1f, yb, 1f, 0f);
            Prop("crate", 1f, yb, 2.15f, 8f);
            Prop("locker", 11.55f, yb, 9.0f, 270f);
            Prop("locker", 11.55f, yb, 10.2f, 270f);
            Prop("barrel", 10.8f, yb, 5.2f, 0f);
            Barricade.Create(dynT, new Vector3(11.3f, yb + 0.02f, 6f), 90f);

            // B - Security office
            Prop("desk", 16f, yb, 9.6f, 0f);
            Prop("desk", 20.5f, yb, 9.6f, 180f);
            Prop("console", 18f, yb, 1.2f, 0f, 4f);
            Prop("shelf", 23.6f, yb, 2.5f, 90f);
            Prop("pcrate", 14.6f, yb, 3f, 20f);
            Prop("locker", 13f, yb, 11.55f, 180f);
            Prop("locker", 14.2f, yb, 11.55f, 180f);

            // C - Storage
            float[] sx = { 27f, 30f, 33f, 36f };
            for (int i = 0; i < sx.Length; i++) Prop("shelf", sx[i], yb, 2f, 0f);
            Prop("shelf", 27f, yb, 10f, 0f);
            Prop("shelf", 29.4f, yb, 10f, 0f);
            Prop("shelf", 35.4f, yb, 10f, 0f);
            Prop("shelf", 38f, yb, 10f, 0f);
            Prop("crate", 38.2f, yb, 5f, 0f);
            Prop("crate", 38.2f, yb, 6.1f, 12f);
            Prop("crate", 37.1f, yb, 5f, 0f);
            Prop("pcrate", 28.5f, yb, 6.2f, 0f);
            Prop("barrel", 39.1f, yb, 1.2f, 0f);
            Prop("barrel", 39.1f, yb, 2.1f, 0f);

            // D - Cafeteria
            Prop("table", 4f, yb, 21f, 0f);
            Prop("table", 4f, yb, 26f, 0f);
            Prop("table", 12f, yb, 21f, 0f);
            Prop("table", 12f, yb, 26f, 0f);
            Prop("counter", 8f, yb, 29.1f, 0f, 6f);
            Prop("locker", 0.75f, yb, 27.6f, 90f);
            Prop("locker", 0.75f, yb, 28.8f, 90f);
            Prop("barrel", 14.8f, yb, 17.2f, 0f);

            // E - Laboratory
            Prop("labtable", 19f, yb, 20.5f, 0f);
            Prop("labtable", 24.5f, yb, 18f, 0f);
            Prop("console", 19f, yb, 29.1f, 0f);
            Prop("console", 22f, yb, 29.1f, 0f);
            Prop("console", 25f, yb, 29.1f, 0f);
            Prop("locker", 27.3f, yb, 28.4f, 270f);
            Prop("locker", 27.3f, yb, 27.2f, 270f);
            Prop("barrel", 17f, yb, 28.8f, 0f);
            Prop("barrel", 17f, yb, 27.9f, 0f);
            Barricade.Create(dynT, new Vector3(27.4f, yb + 0.02f, 22f), 90f);

            // F - Stair hall + corridor clutter
            Prop("crate", 38.6f, yb, 28.5f, 0f);
            Prop("crate", 38.6f, yb, 27.4f, 0f);
            Prop("crate", 37.5f, yb, 28.5f, 15f);
            Prop("locker", 39.3f, yb, 19f, 270f);
            Prop("barrel", 29f, yb, 17.2f, 0f);
            Prop("crate", 1f, yb, 15f, 0f);
            Prop("crate", 1f, yb, 13.9f, 10f);
            Prop("barrel", 39f, yb, 15f, 0f);
            Prop("barrel", 39f, yb, 13.2f, 0f);
        }

        static void FurnishFloor2(float yb)
        {
            // G - Ward 1
            Prop("bed", 1.4f, yb, 2.5f, 90f);
            Prop("bed", 1.4f, yb, 5.5f, 90f);
            Prop("bed", 1.4f, yb, 8.5f, 90f);
            Prop("bed", 10.4f, yb, 3f, 90f);
            Prop("desk", 6f, yb, 1.2f, 0f);
            Prop("locker", 11.55f, yb, 9.0f, 270f);
            Prop("locker", 11.55f, yb, 10.2f, 270f);
            Prop("crate", 9.5f, yb, 10.8f, 15f);
            Barricade.Create(dynT, new Vector3(11.3f, yb + 0.02f, 6f), 90f);

            // H - Ward 2
            Prop("bed", 15f, yb, 3f, 0f);
            Prop("bed", 20.5f, yb, 3f, 0f);
            Prop("bed", 15f, yb, 9f, 0f);
            Prop("bed", 20.5f, yb, 9f, 0f);
            Prop("shelf", 23.6f, yb, 2.5f, 90f);
            Prop("pcrate", 22f, yb, 10.5f, 0f);
            Prop("locker", 13f, yb, 11.55f, 180f);

            // I - Records
            float[] sx = { 27f, 30f, 33f, 36f };
            for (int i = 0; i < sx.Length; i++) Prop("shelf", sx[i], yb, 2f, 0f);
            Prop("shelf", 27f, yb, 10f, 0f);
            Prop("shelf", 29.4f, yb, 10f, 0f);
            Prop("shelf", 35.4f, yb, 10f, 0f);
            Prop("shelf", 38f, yb, 10f, 0f);
            Prop("desk", 32f, yb, 6.5f, 90f);
            Prop("crate", 38.2f, yb, 5f, 0f);
            Prop("barrel", 39.1f, yb, 1.2f, 0f);

            // J - Break room
            Prop("couch", 3f, yb, 19.5f, 0f);
            Prop("table", 4f, yb, 24f, 0f);
            Prop("table", 12f, yb, 24f, 0f);
            Prop("couch", 3f, yb, 28f, 180f);
            Prop("counter", 8f, yb, 29.1f, 0f, 6f);
            Prop("locker", 0.75f, yb, 22.6f, 90f);
            Prop("barrel", 14.8f, yb, 17.2f, 0f);

            // K - Server room
            float[] rx = { 18.5f, 20f, 21.5f, 23f, 24.5f };
            for (int i = 0; i < rx.Length; i++) Prop("rack", rx[i], yb, 29.2f, 0f);
            Prop("labtable", 19f, yb, 20.5f, 0f);
            Prop("console", 24.5f, yb, 18f, 0f, 2.4f);
            Prop("locker", 27.3f, yb, 28.4f, 270f);
            Prop("locker", 27.3f, yb, 27.2f, 270f);

            // F - stair hall + corridor clutter
            Prop("crate", 38.6f, yb, 28.5f, 0f);
            Prop("crate", 38.6f, yb, 27.4f, 0f);
            Prop("locker", 39.3f, yb, 19f, 270f);
            Prop("barrel", 29f, yb, 17.2f, 0f);
            Prop("crate", 1f, yb, 15f, 0f);
            Prop("barrel", 39f, yb, 15f, 0f);
        }

        // ================================================================== lights
        static void BuildLights(int floor, float yb)
        {
            Color white = new Color(0.82f, 0.95f, 1f);
            Color red = new Color(1f, 0.12f, 0.08f);
            FlickerLight.Mode B = FlickerLight.Mode.Buzz, F = FlickerLight.Mode.Failing, P = FlickerLight.Mode.Pulse;

            Lamp(5f, 14f, yb, white, 2.6f, 9f, B);
            Lamp(15f, 14f, yb, white, 2.6f, 9f, F);
            Lamp(25f, 14f, yb, white, 2.6f, 9f, B);
            Lamp(35f, 14f, yb, red, 3.4f, 8f, P);
            Lamp(6f, 6f, yb, red, 3.2f, 9f, P);
            Lamp(18f, 6f, yb, white, 2.8f, 9f, B);
            Lamp(28f, 6f, yb, white, 2.4f, 8f, F);
            Lamp(36f, 6f, yb, red, 3.0f, 8f, P);
            Lamp(8f, 23f, yb, white, 2.8f, 9f, B);
            Lamp(12f, 27f, yb, white, 2.4f, 8f, F);
            Lamp(22f, 23f, yb, white, 3.0f, 10f, B);
            Lamp(31f, 23f, yb, red, 3.2f, 9f, P);
        }

        static void Lamp(float x, float z, float yb, Color color, float intensity, float range, FlickerLight.Mode mode)
        {
            GameObject g = new GameObject("Lamp");
            g.transform.SetParent(lightT, false);
            g.transform.position = new Vector3(x, yb + CeilH - 0.5f, z);

            Light l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;

            GameObject fx = BoxMat(g.transform, "Fixture", new Vector3(x, yb + CeilH - 0.08f, z), new Vector3(1.2f, 0.08f, 0.25f), GameMaterials.Emissive(color, 2f), false);

            FlickerLight f = g.AddComponent<FlickerLight>();
            f.mode = mode;
            f.lightSource = l;
            f.fixture = fx.GetComponent<Renderer>();
            f.baseIntensity = intensity;
            f.emissionColor = color;
        }

        // ================================================================== pickups, markers, decals, patrols
        static void BuildPickups(int floor, float yb)
        {
            if (floor == 0)
            {
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(2.5f, yb, 10.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(10.5f, yb, 3.5f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(20.5f, yb, 3.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Grenade, new Vector3(38.5f, yb, 7f), 1);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(26.5f, yb, 1.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(2f, yb, 17.5f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Bottles, new Vector3(14.5f, yb, 28f), 3);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(21.5f, yb, 24f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(26.5f, yb, 28.5f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(29.5f, yb, 28.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Bottles, new Vector3(31f, yb, 18.5f), 3);
            }
            else
            {
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(2.5f, yb, 10.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(9f, yb, 3f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(20.5f, yb, 6.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Grenade, new Vector3(38.5f, yb, 7f), 1);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(26.5f, yb, 1.5f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(2f, yb, 17.5f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Bottles, new Vector3(14.5f, yb, 28f), 3);
                Pickup.Create(dynT, Pickup.PickupType.Grenade, new Vector3(21.5f, yb, 24f), 1);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(26.5f, yb, 26f), 18);
                Pickup.Create(dynT, Pickup.PickupType.Health, new Vector3(29.5f, yb, 28.5f), 35);
                Pickup.Create(dynT, Pickup.PickupType.Ammo, new Vector3(31f, yb, 18.5f), 18);
            }
        }

        static void BuildSpawnMarkers(int floor, float yb)
        {
            string t = "Spawn_F" + (floor + 1) + "_";
            Marker(t + "Lobby", 9.8f, yb, 10.2f);
            Marker(t + "Office", 22f, yb, 10.5f);
            Marker(t + "Storage", 36.5f, yb, 6f);
            Marker(t + "CafeE", 13.5f, yb, 28.5f);
            Marker(t + "CafeW", 2.5f, yb, 24.5f);
            Marker(t + "Lab", 22f, yb, 27.5f);
            Marker(t + "StairsN", 30f, yb, 28f);
            Marker(t + "StairsE", 38.5f, yb, 22f);
            Marker(t + "CorridorE", 38.5f, yb, 14f);
            Marker(t + "CorridorW", 1.5f, yb, 14f);
        }

        static void Marker(string name, float x, float yb, float z)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(spawnT, false);
            g.transform.position = new Vector3(x, yb + 0.05f, z);
        }

        static void BuildDecals(int floor, float yb)
        {
            for (int i = 0; i < 14; i++)
            {
                float x = R(2f, 38f);
                float z = (i % 3 == 0) ? R(12.6f, 15.4f) : R(1.5f, 28.5f);
                if (x > RampX0 - 0.3f && x < RampX1 + 0.3f && z > RampZ0 - 0.3f && z < RampZ1 + 0.3f) continue;
                float size = R(0.8f, 2.2f);
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Blood";
                Collider c = q.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
                q.transform.SetParent(levelT, false);
                q.transform.position = new Vector3(x, yb + 0.012f + i * 0.0004f, z);
                q.transform.rotation = Quaternion.Euler(90f, R(0f, 360f), 0f);
                q.transform.localScale = new Vector3(size, size, 1f);
                q.GetComponent<Renderer>().sharedMaterial = GameMaterials.Decal(SurfaceKind.Blood);
            }

            // smears on the corridor walls
            for (int i = 0; i < 5; i++)
            {
                float x = R(3f, 37f);
                bool south = (i % 2 == 0);
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "BloodWall";
                Collider c = q.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
                q.transform.SetParent(levelT, false);
                Vector3 normal = south ? Vector3.forward : Vector3.back;
                float zWall = south ? 12.16f : 15.84f;
                q.transform.position = new Vector3(x, yb + R(0.9f, 1.8f), zWall);
                q.transform.rotation = Quaternion.LookRotation(-normal);
                float s = R(0.6f, 1.4f);
                q.transform.localScale = new Vector3(s, s, 1f);
                q.GetComponent<Renderer>().sharedMaterial = GameMaterials.Decal(SurfaceKind.Blood);
            }
        }

        public static void BuildPatrolRoutes()
        {
            PatrolRoutes.Routes.Clear();
            for (int floor = 0; floor < 2; floor++)
            {
                float y = floor * FloorH + 0.05f;
                Vector3[] route =
                {
                    new Vector3(3f, y, 14f), new Vector3(14f, y, 14f), new Vector3(26f, y, 14f), new Vector3(37f, y, 14f),
                    new Vector3(35f, y, 17.2f), new Vector3(30.5f, y, 23f), new Vector3(31.5f, y, 19.5f)
                };
                PatrolRoutes.Routes.Add(route);
            }
        }

        // narrow service gaps: tagged with the "Narrow" NavMesh area (index 3); the Tank's area mask excludes it
        static void BuildNarrowVolumes()
        {
            for (int floor = 0; floor < 2; floor++)
            {
                float yb = floor * FloorH;
                NarrowVolume("NarrowGap_B_C_F" + (floor + 1), new Vector3(24f, yb + 1f, 6f));
                NarrowVolume("NarrowGap_D_E_F" + (floor + 1), new Vector3(16f, yb + 1f, 23f));
            }
        }

        static void NarrowVolume(string name, Vector3 center)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(levelT, false);
            g.transform.position = center;
            NavMeshModifierVolume v = g.AddComponent<NavMeshModifierVolume>();
            v.size = new Vector3(1.6f, 3f, 1.9f);
            v.center = Vector3.zero;
            v.area = TankBrain.NarrowArea;
        }
    }
}
