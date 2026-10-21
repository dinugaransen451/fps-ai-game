using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Fallback zombie body made of blocks with its own procedural walk / run / lunge animation.
    /// Used only if the Blender zombie prefab is not available (e.g. editor setup has not run yet).
    /// </summary>
    public class BlockRig : MonoBehaviour
    {
        public float Speed;
        public float RunBlend;
        public float Attack;
        public float Crouch;

        Transform hips, torso, head, armL, armR, legL, legR;
        float phase;
        float attackS;

        public static BlockRig Create(Transform parent, Color skin, Color cloth, float scale)
        {
            GameObject root = new GameObject("BlockBody");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;
            BlockRig rig = root.AddComponent<BlockRig>();

            Material skinM = GameMaterials.Solid(skin, 0.15f);
            Material clothM = GameMaterials.Solid(cloth, 0.1f);

            rig.hips = new GameObject("Hips").transform;
            rig.hips.SetParent(root.transform, false);
            rig.hips.localPosition = new Vector3(0f, 0.95f, 0f);

            rig.torso = new GameObject("Torso").transform;
            rig.torso.SetParent(rig.hips, false);
            Part(rig.torso, "Chest", new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.28f), clothM);

            rig.head = new GameObject("Head").transform;
            rig.head.SetParent(rig.torso, false);
            rig.head.localPosition = new Vector3(0f, 0.68f, 0.02f);
            Part(rig.head, "Skull", new Vector3(0f, 0.1f, 0f), new Vector3(0.24f, 0.26f, 0.26f), skinM);

            rig.armL = Limb(rig.torso, "ArmL", new Vector3(-0.32f, 0.56f, 0f), new Vector3(0.12f, 0.6f, 0.12f), skinM);
            rig.armR = Limb(rig.torso, "ArmR", new Vector3(0.32f, 0.56f, 0f), new Vector3(0.12f, 0.6f, 0.12f), skinM);
            rig.legL = Limb(rig.hips, "LegL", new Vector3(-0.14f, 0f, 0f), new Vector3(0.17f, 0.95f, 0.17f), clothM);
            rig.legR = Limb(rig.hips, "LegR", new Vector3(0.14f, 0f, 0f), new Vector3(0.17f, 0.95f, 0.17f), clothM);
            return rig;
        }

        static GameObject Part(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Collider c = g.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        // limb pivots at the top (shoulder / hip); the block hangs below the pivot
        static Transform Limb(Transform parent, string name, Vector3 pivotPos, Vector3 size, Material m)
        {
            Transform pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = pivotPos;
            Part(pivot, "Mesh", new Vector3(0f, -size.y * 0.5f, 0f), size, m);
            return pivot;
        }

        void LateUpdate()
        {
            if (hips == null) return;
            float dt = Time.deltaTime;
            attackS = Mathf.MoveTowards(attackS, Attack, dt * 9f);
            float loco = Mathf.Clamp01(Speed / 0.4f);
            phase += dt * Mathf.Clamp(Speed, 0f, 9f) * 2.6f;
            float s = Mathf.Sin(phase);
            float swing = Mathf.Lerp(24f, 48f, RunBlend) * loco;

            legL.localRotation = Quaternion.Euler(-s * swing, 0f, 0f);
            legR.localRotation = Quaternion.Euler(s * swing, 0f, 0f);

            float reach = -75f * (0.6f + 0.4f * loco) - attackS * 40f;
            armL.localRotation = Quaternion.Euler(reach + s * 14f * loco, 0f, 4f);
            armR.localRotation = Quaternion.Euler(reach - s * 14f * loco, 0f, -4f);

            float lean = 8f + 14f * RunBlend * loco + attackS * 18f + Crouch * 25f;
            torso.localRotation = Quaternion.Euler(lean, Mathf.Sin(phase * 0.5f) * 5f * loco, Mathf.Sin(Time.time * 1.7f) * 2f);
            head.localRotation = Quaternion.Euler(-lean * 0.5f + Mathf.Sin(Time.time * 2.3f) * 4f, Mathf.Sin(Time.time * 1.1f) * 10f, 0f);
            hips.localPosition = new Vector3(0f, 0.95f - Crouch * 0.35f - Mathf.Abs(s) * 0.03f * loco, 0f);
        }
    }
}
