using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The Runner's own pathfinding graph. Walkable points are sampled from the baked NavMesh on a fine grid
    /// (several floors + ramps), linked to their neighbours (links are validated with NavMesh.Raycast, so walls
    /// are respected), and searched with a hand-written A*.
    ///
    /// RE-ROUTING COST: every node that lies inside a closed door or a barricade gets an extra cost
    /// (IBlocker.PathPenalty). If a detour is cheaper than the penalty, A* goes around; if the blocker is the only
    /// way, A* still returns a path through it and the Runner deals with the obstacle when it gets there.
    /// Cost = distance + blocker penalty,  heuristic = straight-line distance (admissible).
    /// </summary>
    public class NavGrid
    {
        public static NavGrid Instance { get; private set; }

        public float Cell { get; private set; }
        public int NodeCount { get { return P.Length; } }
        public long LastSearchExpansions { get; private set; }

        Vector3[] P;
        int[] adjStart;
        int[] adj;
        float[] blockCost;
        bool dirty = true;
        int areaMask;

        // cell lookup
        int nx, nz;
        Vector3 origin;
        List<int>[] cells;

        // A* scratch
        float[] G;
        int[] parent;
        int[] openStamp;
        int[] closedStamp;
        int stamp;
        int[] heapNode = new int[256];
        float[] heapF = new float[256];
        int heapCount;

        // ------------------------------------------------------------------ build
        public static NavGrid Build(Vector3 min, Vector3 max, float cell, float[] floorHeights, Bounds[] rampZones, int areaMask)
        {
            NavGrid g = new NavGrid();
            g.Generate(min, max, cell, floorHeights, rampZones, areaMask);
            Instance = g;
            BlockerRegistry.Changed += g.OnBlockersChanged;
            return g;
        }

        public void Dispose()
        {
            BlockerRegistry.Changed -= OnBlockersChanged;
            if (Instance == this) Instance = null;
        }

        void OnBlockersChanged() { dirty = true; }

        void Generate(Vector3 min, Vector3 max, float cell, float[] floorHeights, Bounds[] rampZones, int mask)
        {
            Cell = cell;
            areaMask = mask;
            origin = min;
            nx = Mathf.CeilToInt((max.x - min.x) / cell);
            nz = Mathf.CeilToInt((max.z - min.z) / cell);
            cells = new List<int>[nx * nz];

            List<Vector3> pts = new List<Vector3>(8192);
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = min.x + (ix + 0.5f) * cell;
                    float z = min.z + (iz + 0.5f) * cell;
                    int ci = iz * nx + ix;

                    for (int f = 0; f < floorHeights.Length; f++) TryAdd(pts, ci, x, floorHeights[f], z, cell);

                    if (rampZones != null)
                    {
                        for (int r = 0; r < rampZones.Length; r++)
                        {
                            Bounds b = rampZones[r];
                            if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                            for (float y = b.min.y; y <= b.max.y + 0.01f; y += 0.5f) TryAdd(pts, ci, x, y, z, cell);
                        }
                    }
                }
            }

            P = pts.ToArray();
            int n = P.Length;

            // links to the forward half of the 8 neighbours, stored both ways
            List<int>[] links = new List<int>[n];
            for (int i = 0; i < n; i++) links[i] = new List<int>(8);

            int[] dx = { 1, 1, 0, -1 };
            int[] dz = { 0, 1, 1, 1 };
            NavMeshHit hit;
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    List<int> here = cells[iz * nx + ix];
                    if (here == null) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int jx = ix + dx[d], jz = iz + dz[d];
                        if (jx < 0 || jz < 0 || jx >= nx || jz >= nz) continue;
                        List<int> there = cells[jz * nx + jx];
                        if (there == null) continue;

                        for (int a = 0; a < here.Count; a++)
                        {
                            int na = here[a];
                            for (int b = 0; b < there.Count; b++)
                            {
                                int nb = there[b];
                                if (Mathf.Abs(P[na].y - P[nb].y) > 0.75f) continue;
                                if (NavMesh.Raycast(P[na], P[nb], out hit, areaMask)) continue;
                                links[na].Add(nb);
                                links[nb].Add(na);
                            }
                        }
                    }
                }
            }

            adjStart = new int[n + 1];
            int total = 0;
            for (int i = 0; i < n; i++) { adjStart[i] = total; total += links[i].Count; }
            adjStart[n] = total;
            adj = new int[total];
            for (int i = 0; i < n; i++)
            {
                List<int> l = links[i];
                for (int k = 0; k < l.Count; k++) adj[adjStart[i] + k] = l[k];
            }

            blockCost = new float[n];
            G = new float[n];
            parent = new int[n];
            openStamp = new int[n];
            closedStamp = new int[n];
            Debug.Log("[NavGrid] " + n + " nodes, " + (total / 2) + " links (cell " + cell + " m)");
        }

        void TryAdd(List<Vector3> pts, int cellIndex, float x, float y, float z, float cell)
        {
            NavMeshHit h;
            if (!NavMesh.SamplePosition(new Vector3(x, y, z), out h, 0.45f, areaMask)) return;

            float px = h.position.x - x, pz = h.position.z - z;
            if (px * px + pz * pz > cell * cell * 0.8f) return;

            List<int> list = cells[cellIndex];
            if (list == null) { list = new List<int>(2); cells[cellIndex] = list; }
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 e = pts[list[i]];
                if (Mathf.Abs(e.y - h.position.y) < 0.3f && Mathf.Abs(e.x - h.position.x) < 0.2f && Mathf.Abs(e.z - h.position.z) < 0.2f) return;
            }
            list.Add(pts.Count);
            pts.Add(h.position);
        }

        // ------------------------------------------------------------------ queries
        public Vector3 NodePos(int i) { return P[i]; }

        public int Nearest(Vector3 p, float maxDist)
        {
            int cx = Mathf.FloorToInt((p.x - origin.x) / Cell);
            int cz = Mathf.FloorToInt((p.z - origin.z) / Cell);
            int rings = Mathf.CeilToInt(maxDist / Cell) + 1;
            int best = -1;
            float bestD = maxDist * maxDist;
            for (int dz = -rings; dz <= rings; dz++)
            {
                for (int dx = -rings; dx <= rings; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) continue;
                    List<int> l = cells[iz * nx + ix];
                    if (l == null) continue;
                    for (int k = 0; k < l.Count; k++)
                    {
                        Vector3 q = P[l[k]];
                        float ddx = q.x - p.x, ddz = q.z - p.z, ddy = (q.y - p.y) * 2f;
                        float d = ddx * ddx + ddz * ddz + ddy * ddy;
                        if (d < bestD) { bestD = d; best = l[k]; }
                    }
                }
            }
            return best;
        }

        /// <summary>A random walkable point near 'center' on the same floor.</summary>
        public bool RandomPointNear(Vector3 center, float radius, out Vector3 point)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                int n = Nearest(new Vector3(center.x + r.x, center.y, center.z + r.y), 1.2f);
                if (n >= 0 && Mathf.Abs(P[n].y - center.y) < 1.2f)
                {
                    point = P[n];
                    return true;
                }
            }
            point = center;
            return false;
        }

        void RefreshBlockCosts()
        {
            dirty = false;
            System.Array.Clear(blockCost, 0, blockCost.Length);
            List<IBlocker> all = BlockerRegistry.All;
            for (int k = 0; k < all.Count; k++)
            {
                IBlocker b = all[k];
                if (b == null || !b.IsBlocking) continue;
                Bounds bb = b.BlockBounds;
                bb.Expand(new Vector3(0.8f, 0.8f, 0.8f));
                float pen = b.PathPenalty;
                for (int i = 0; i < P.Length; i++)
                    if (bb.Contains(P[i]) && pen > blockCost[i]) blockCost[i] = pen;
            }
        }

        public bool IsOnBlocker(Vector3 p)
        {
            int n = Nearest(p, 0.8f);
            if (dirty) RefreshBlockCosts();
            return n >= 0 && blockCost[n] > 0f;
        }

        // ------------------------------------------------------------------ A*
        void Push(int node, float f)
        {
            if (heapCount >= heapNode.Length)
            {
                System.Array.Resize(ref heapNode, heapNode.Length * 2);
                System.Array.Resize(ref heapF, heapF.Length * 2);
            }
            int i = heapCount++;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heapF[p] <= f) break;
                heapNode[i] = heapNode[p];
                heapF[i] = heapF[p];
                i = p;
            }
            heapNode[i] = node;
            heapF[i] = f;
        }

        int Pop()
        {
            int top = heapNode[0];
            heapCount--;
            if (heapCount > 0)
            {
                int node = heapNode[heapCount];
                float f = heapF[heapCount];
                int i = 0;
                while (true)
                {
                    int c = i * 2 + 1;
                    if (c >= heapCount) break;
                    if (c + 1 < heapCount && heapF[c + 1] < heapF[c]) c++;
                    if (heapF[c] >= f) break;
                    heapNode[i] = heapNode[c];
                    heapF[i] = heapF[c];
                    i = c;
                }
                heapNode[i] = node;
                heapF[i] = f;
            }
            return top;
        }

        /// <summary>
        /// Finds a path from 'from' to 'to'. 'result' receives smoothed waypoints (first = from).
        /// 'partial' is true when the goal was unreachable and the path leads to the closest point found.
        /// 'forbidden' (optional) excludes nodes entirely (used for obstacles the agent learned it cannot pass).
        /// </summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result, out bool partial, System.Predicate<int> forbidden = null)
        {
            partial = false;
            result.Clear();
            if (P == null || P.Length == 0) return false;
            if (dirty) RefreshBlockCosts();

            int s = Nearest(from, 2.5f);
            int goal = Nearest(to, 3.0f);
            if (s < 0 || goal < 0) return false;

            Vector3 goalPos = P[goal];
            stamp++;
            heapCount = 0;
            G[s] = 0f;
            parent[s] = -1;
            openStamp[s] = stamp;
            Push(s, Vector3.Distance(P[s], goalPos));

            int best = s;
            float bestH = Vector3.Distance(P[s], goalPos);
            bool found = false;
            long expansions = 0;

            while (heapCount > 0)
            {
                int n = Pop();
                if (closedStamp[n] == stamp) continue;
                closedStamp[n] = stamp;
                expansions++;

                if (n == goal) { found = true; best = n; break; }

                float h = Vector3.Distance(P[n], goalPos);
                if (h < bestH) { bestH = h; best = n; }

                for (int e = adjStart[n]; e < adjStart[n + 1]; e++)
                {
                    int m = adj[e];
                    if (closedStamp[m] == stamp) continue;
                    if (forbidden != null && forbidden(m)) continue;
                    float ng = G[n] + Vector3.Distance(P[n], P[m]) + blockCost[m];
                    if (openStamp[m] != stamp || ng < G[m])
                    {
                        G[m] = ng;
                        parent[m] = n;
                        openStamp[m] = stamp;
                        Push(m, ng + Vector3.Distance(P[m], goalPos));
                    }
                }
            }
            LastSearchExpansions = expansions;

            partial = !found;

            List<Vector3> raw = new List<Vector3>(64);
            for (int n = best; n >= 0; n = parent[n]) raw.Add(P[n]);
            raw.Reverse();
            if (raw.Count == 0) return false;

            // replace the first node by the real start position, the last by the exact goal
            raw[0] = from;
            if (found)
            {
                NavMeshHit gh;
                raw[raw.Count - 1] = NavMesh.SamplePosition(to, out gh, 2f, areaMask) ? gh.position : goalPos;
            }

            Smooth(raw, result);
            return true;
        }

        /// <summary>String-pulling by line of sight: skip waypoints that can be reached in a straight line on the NavMesh.</summary>
        void Smooth(List<Vector3> raw, List<Vector3> result)
        {
            int last = raw.Count - 1;
            result.Add(raw[0]);
            int i = 0;
            NavMeshHit hit;
            while (i < last)
            {
                int k = Mathf.Min(last, i + 14);
                for (; k > i + 1; k--)
                {
                    Vector3 a = raw[i], b = raw[k];
                    float dxz = Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
                    if (Mathf.Abs(a.y - b.y) > 0.3f + 0.6f * dxz) continue;
                    if (!NavMesh.Raycast(a, b, out hit, areaMask)) break;
                }
                result.Add(raw[k]);
                i = k;
            }
        }
    }
}
