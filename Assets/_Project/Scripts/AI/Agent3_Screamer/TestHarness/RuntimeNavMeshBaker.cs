using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ScreamerAI.TestHarness
{
    /// <summary>
    /// Safety net for the test scene: if no NavMesh exists when the scene starts, bake the attached
    /// NavMeshSurface at runtime. The scene normally ships with a pre-baked NavMesh, in which case this does nothing.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavMeshSurface))]
    public class RuntimeNavMeshBaker : MonoBehaviour
    {
        void Awake()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0) return;

            NavMeshSurface surface = GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            Debug.Log("[Screamer test scene] No NavMesh found - baked one at runtime.");
        }
    }
}
