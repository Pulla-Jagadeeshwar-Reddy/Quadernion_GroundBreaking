using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The first floor's "Online Collision Proxies" are one box per mesh. For L-shaped, hollow or
/// grouped meshes (bench rows, railings, cupboards with gaps) that box covers empty space and
/// creates invisible walls. This tool voxelises each oversized proxy against its real mesh and
/// replaces it with a few tight boxes, keeping the CharacterController-friendly box collision.
/// Re-runnable after a floor model change.
/// </summary>
public static class CollisionProxyRefiner
{
    private const string ScenePath = "Assets/Scenes/OnlineFirstFloor.unity";
    private const string ProxyRootName = "Online Collision Proxies";
    private const string RefinedChildPrefix = "Refined Box ";
    private const float VoxelXZ = 0.12f;
    private const float VoxelY = 0.4f;
    private const float FillThreshold = 0.55f;

    [MenuItem("Tools/First Floor/Refine Oversized Collision Boxes")]
    public static void RefineMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorUtility.DisplayDialog("Collision refined", Refine(), "OK");
    }

    /// <summary>Batch entry: -executeMethod CollisionProxyRefiner.RefineFromCommandLine</summary>
    public static void RefineFromCommandLine()
    {
        Debug.Log("[CollisionProxyRefiner] " + Refine());
    }

    public static string Refine()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject proxyRoot = GameObject.Find(ProxyRootName);
        if (proxyRoot == null)
            return "No 'Online Collision Proxies' object found; nothing changed.";

        // Mesh lookup by name for matching proxies back to their source meshes.
        Dictionary<string, List<MeshFilter>> meshesByName = new();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == ProxyRootName || root.name == "RouteContent")
                continue;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;

                if (!meshesByName.TryGetValue(filter.name, out List<MeshFilter> list))
                    meshesByName[filter.name] = list = new List<MeshFilter>();
                list.Add(filter);
            }
        }

        // Remove output from a previous run and restore the originals so results are repeatable:
        // a wall proxy with carved doorway segments goes back to "box off, segments on".
        int cleared = 0;
        foreach (Transform child in proxyRoot.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (child == null || !child.name.StartsWith(RefinedChildPrefix))
                continue;

            Transform owner = child.parent;
            Object.DestroyImmediate(child.gameObject);
            cleared++;
            if (owner == null)
                continue;

            BoxCollider original = owner.GetComponent<BoxCollider>();
            List<BoxCollider> segments = DoorwaySegments(owner);
            foreach (BoxCollider segment in segments)
                segment.enabled = true;
            if (original != null)
                original.enabled = segments.Count == 0;
        }

        // Doorways: the door blocker boxes mark each opening. Wall meshes are solid behind the door
        // leaf, so refined walls must leave these volumes empty (the blocker itself closes them).
        doorOpenings.Clear();
        foreach (BoxCollider blocker in proxyRoot.GetComponentsInChildren<BoxCollider>(true))
        {
            if (!blocker.name.StartsWith("Collision - Door"))
                continue;

            Bounds opening = WorldBounds(blocker);
            Vector3 size = opening.size;
            float thin = Mathf.Min(size.x, size.z);
            opening.size = new Vector3(size.x <= thin ? size.x + 0.8f : size.x + 0.1f, 4.6f, size.z <= thin ? size.z + 0.8f : size.z + 0.1f);
            opening.center = new Vector3(opening.center.x, 0f, opening.center.z);
            doorOpenings.Add(opening);
        }

        int examined = 0;
        int refined = 0;
        int boxesCreated = 0;
        StringBuilder details = new();

        foreach (BoxCollider proxy in proxyRoot.GetComponentsInChildren<BoxCollider>(true).ToArray())
        {
            // Doors are toggled at runtime; stairs go below floor level, and refining them (which
            // drops the floor layer) would delete the steps and let players fall through.
            if (!proxy.name.StartsWith("Collision - ") || proxy.name.StartsWith("Collision - Door") ||
                proxy.name.StartsWith("Collision - Stairs"))
                continue;

            // Room-shell walls were carved into "Doorway Collision Segment" boxes that fill the whole
            // room. Those proxies are disabled but still describe the shell, so refine them too.
            List<BoxCollider> doorwaySegments = DoorwaySegments(proxy.transform);
            if (!proxy.enabled && doorwaySegments.Count == 0)
                continue;

            // Only boxes big enough to make an invisible wall at body height matter.
            Bounds world = WorldBounds(proxy);
            if (world.size.x < 0.9f && world.size.z < 0.9f)
                continue;
            if (world.max.y < 0.25f || world.min.y > 1.7f || world.size.y < 0.3f)
                continue;

            string meshName = proxy.name.Substring("Collision - ".Length);
            MeshFilter source = FindSource(meshesByName, meshName, proxy.transform);
            if (source == null)
                continue;

            examined++;
            List<Bounds> cells = Voxelise(proxy, source, out float fill);
            if (fill >= FillThreshold || cells.Count == 0)
                continue;

            List<Bounds> merged = Merge(cells);
            foreach (Bounds cell in merged)
            {
                GameObject box = new(RefinedChildPrefix + boxesCreated);
                box.transform.SetParent(proxy.transform, false);
                BoxCollider collider = box.AddComponent<BoxCollider>();
                collider.center = cell.center;
                collider.size = cell.size;
                boxesCreated++;
            }

            proxy.enabled = false;
            foreach (BoxCollider segment in doorwaySegments)
                segment.enabled = false;
            refined++;
            if (details.Length < 1500)
                details.AppendLine($"- {meshName}: {world.size.x:0.0}x{world.size.z:0.0} m box was {fill:P0} solid → {merged.Count} boxes");
        }

        string floorReport = FillMissingFloor(scene, proxyRoot.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"Examined {examined} large proxies, refined {refined} into {boxesCreated} tight boxes (cleared {cleared} old).\n{details}{floorReport}";
    }

    private const string FloorFillName = "Floor Fill (generated)";
    private static readonly List<Bounds> doorOpenings = new();

    /// <summary>
    /// Adds thin floor boxes wherever the visible model has a floor surface near y=0 but no
    /// collider, so rooms whose floor is baked into a larger mesh become walkable.
    /// </summary>
    private static string FillMissingFloor(Scene scene, Transform proxyRoot)
    {
        Transform old = proxyRoot.Find(FloorFillName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        // Temporary exact mesh colliders on every visible model mesh.
        List<GameObject> temporary = new();
        Bounds area = new();
        bool any = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == ProxyRootName || root.name == "RouteContent" || root.name == "NetworkManager")
                continue;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled)
                    continue;

                GameObject probe = new("Floor Probe");
                probe.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                probe.transform.localScale = filter.transform.lossyScale;
                probe.layer = 2; // Ignore Raycast, so it never mixes with real collision queries below.
                probe.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                temporary.Add(probe);

                if (!any)
                {
                    area = renderer.bounds;
                    any = true;
                }
                else
                {
                    area.Encapsulate(renderer.bounds);
                }
            }
        }

        Physics.SyncTransforms();
        const float cell = 0.5f;
        int countX = Mathf.CeilToInt(area.size.x / cell);
        int countZ = Mathf.CeilToInt(area.size.z / cell);
        float[,] needY = new float[countX, countZ];
        bool[,] need = new bool[countX, countZ];
        int realLayers = Physics.DefaultRaycastLayers & ~(1 << 2);

        try
        {
            for (int x = 0; x < countX; x++)
            for (int z = 0; z < countZ; z++)
            {
                Vector3 top = new(area.min.x + (x + 0.5f) * cell, 0.6f, area.min.z + (z + 0.5f) * cell);

                // Already has walkable collision near y=0?
                if (Physics.Raycast(top, Vector3.down, out RaycastHit real, 1.2f, realLayers, QueryTriggerInteraction.Ignore) &&
                    real.point.y > -0.4f)
                    continue;

                // Visible floor surface near y=0 under this cell?
                foreach (RaycastHit hit in Physics.RaycastAll(top, Vector3.down, 1.0f, 1 << 2, QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y < -0.25f || hit.point.y > 0.15f || hit.normal.y < 0.8f)
                        continue;
                    need[x, z] = true;
                    needY[x, z] = hit.point.y;
                    break;
                }
            }
        }
        finally
        {
            foreach (GameObject probe in temporary)
                Object.DestroyImmediate(probe);
            Physics.SyncTransforms();
        }

        // Merge needed cells into rectangles.
        GameObject fillRoot = new(FloorFillName);
        fillRoot.transform.SetParent(proxyRoot, false);
        bool[,] used = new bool[countX, countZ];
        int boxes = 0;
        float filledArea = 0f;
        for (int x = 0; x < countX; x++)
        for (int z = 0; z < countZ; z++)
        {
            if (!need[x, z] || used[x, z])
                continue;

            int lengthZ = 1;
            while (z + lengthZ < countZ && need[x, z + lengthZ] && !used[x, z + lengthZ])
                lengthZ++;

            int lengthX = 1;
            while (x + lengthX < countX && ColumnFree(need, used, x + lengthX, z, lengthZ))
                lengthX++;

            float topY = 0f;
            for (int ix = 0; ix < lengthX; ix++)
            for (int iz = 0; iz < lengthZ; iz++)
            {
                used[x + ix, z + iz] = true;
                topY = Mathf.Max(topY, needY[x + ix, z + iz]);
            }

            GameObject box = new($"Floor Fill {boxes}");
            box.transform.SetParent(fillRoot.transform, false);
            box.transform.position = new Vector3(area.min.x + (x + lengthX * 0.5f) * cell, topY - 0.1f, area.min.z + (z + lengthZ * 0.5f) * cell);
            box.AddComponent<BoxCollider>().size = new Vector3(lengthX * cell, 0.2f, lengthZ * cell);
            boxes++;
            filledArea += lengthX * lengthZ * cell * cell;
        }

        return $"\nFloor fill: added {boxes} boxes covering {filledArea:0} m² of visible floor that had no collision.";
    }

    private static bool ColumnFree(bool[,] need, bool[,] used, int x, int z, int lengthZ)
    {
        for (int iz = 0; iz < lengthZ; iz++)
        {
            if (!need[x, z + iz] || used[x, z + iz])
                return false;
        }

        return true;
    }

    /// <summary>World AABB of a box collider, valid even when the collider is disabled.</summary>
    private static Bounds WorldBounds(BoxCollider box)
    {
        Vector3 half = box.size * 0.5f;
        Bounds bounds = new(box.transform.TransformPoint(box.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 local = box.center + new Vector3(
                (corner & 1) == 0 ? -half.x : half.x,
                (corner & 2) == 0 ? -half.y : half.y,
                (corner & 4) == 0 ? -half.z : half.z);
            bounds.Encapsulate(box.transform.TransformPoint(local));
        }

        return bounds;
    }

    private static List<BoxCollider> DoorwaySegments(Transform proxy)
    {
        List<BoxCollider> segments = new();
        foreach (Transform child in proxy)
        {
            if (child.name.StartsWith("Doorway Collision Segment") && child.TryGetComponent(out BoxCollider box))
                segments.Add(box);
        }

        return segments;
    }

    private static MeshFilter FindSource(Dictionary<string, List<MeshFilter>> meshesByName, string name, Transform proxy)
    {
        if (!meshesByName.TryGetValue(name, out List<MeshFilter> candidates))
            return null;

        MeshFilter best = null;
        float bestDistance = 0.5f;
        foreach (MeshFilter candidate in candidates)
        {
            float distance = Vector3.Distance(candidate.transform.position, proxy.position);
            if (distance > bestDistance)
                continue;
            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// Tests a grid of small cells (in the proxy's local space) against the real mesh using a
    /// temporary MeshCollider, and returns the occupied cells in local space.
    /// </summary>
    private static List<Bounds> Voxelise(BoxCollider proxy, MeshFilter source, out float fill)
    {
        List<Bounds> occupied = new();
        fill = 1f;

        GameObject probe = new("Refiner Probe");
        probe.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        probe.transform.localScale = source.transform.lossyScale;
        MeshCollider meshCollider = probe.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = source.sharedMesh;
        Physics.SyncTransforms();

        try
        {
            Vector3 scale = proxy.transform.lossyScale;
            // Fine cells horizontally (keeps door gaps open), coarse along whichever local axis is
            // vertical in the world (imported meshes are often rotated so local Z points up).
            Transform t = proxy.transform;
            float upX = Mathf.Abs(t.right.y), upY = Mathf.Abs(t.up.y), upZ = Mathf.Abs(t.forward.y);
            float sizeX = upX >= upY && upX >= upZ ? VoxelY : VoxelXZ;
            float sizeY = upY > upX && upY >= upZ ? VoxelY : VoxelXZ;
            float sizeZ = upZ > upX && upZ > upY ? VoxelY : VoxelXZ;
            Vector3 localVoxel = new(sizeX / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                sizeY / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                sizeZ / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
            Vector3 min = proxy.center - proxy.size * 0.5f;
            Vector3 max = proxy.center + proxy.size * 0.5f;
            int nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / localVoxel.x));
            int ny = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / localVoxel.y));
            int nz = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / localVoxel.z));
            if ((long)nx * ny * nz > 600000)
                return occupied;

            Vector3 step = new((max.x - min.x) / nx, (max.y - min.y) / ny, (max.z - min.z) / nz);
            Vector3 halfWorld = Vector3.Scale(step * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Collider[] buffer = new Collider[8];
            int total = 0;

            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
            for (int iz = 0; iz < nz; iz++)
            {
                Vector3 localCenter = min + Vector3.Scale(step, new Vector3(ix + 0.5f, iy + 0.5f, iz + 0.5f));
                Vector3 worldCenter = proxy.transform.TransformPoint(localCenter);
                total++;

                // Skip the floor layer: a voxelised floor would be a slab taller than the step
                // height. Flat floor collision is added separately by the floor-fill pass.
                if (worldCenter.y < 0.3f)
                    continue;

                // Keep doorways open up to door height.
                if (worldCenter.y < 2.2f && doorOpenings.Any(opening => opening.Contains(worldCenter)))
                    continue;

                int count = Physics.OverlapBoxNonAlloc(worldCenter, halfWorld, buffer, proxy.transform.rotation,
                    Physics.AllLayers, QueryTriggerInteraction.Collide);
                for (int index = 0; index < count; index++)
                {
                    if (buffer[index] != meshCollider)
                        continue;
                    occupied.Add(new Bounds(localCenter, step));
                    break;
                }
            }

            fill = total > 0 ? occupied.Count / (float)total : 1f;
            return occupied;
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    /// <summary>Greedy merge of cells along x, then identical rows along z, then y.</summary>
    private static List<Bounds> Merge(List<Bounds> cells)
    {
        if (cells.Count == 0)
            return cells;

        Vector3 size = cells[0].size;
        Vector3 origin = cells.Aggregate(cells[0].min, (current, cell) => Vector3.Min(current, cell.min));
        HashSet<Vector3Int> filled = new(cells.Select(cell => Vector3Int.RoundToInt(new Vector3(
            (cell.min.x - origin.x) / size.x, (cell.min.y - origin.y) / size.y, (cell.min.z - origin.z) / size.z))));
        HashSet<Vector3Int> used = new();
        List<Bounds> result = new();

        foreach (Vector3Int start in filled.OrderBy(c => c.y).ThenBy(c => c.z).ThenBy(c => c.x))
        {
            if (used.Contains(start))
                continue;

            int lengthX = 1;
            while (filled.Contains(start + new Vector3Int(lengthX, 0, 0)) && !used.Contains(start + new Vector3Int(lengthX, 0, 0)))
                lengthX++;

            int lengthZ = 1;
            while (RowFree(filled, used, start + new Vector3Int(0, 0, lengthZ), lengthX, 1))
                lengthZ++;

            int lengthY = 1;
            while (SlabFree(filled, used, start + new Vector3Int(0, lengthY, 0), lengthX, lengthZ))
                lengthY++;

            for (int x = 0; x < lengthX; x++)
            for (int y = 0; y < lengthY; y++)
            for (int z = 0; z < lengthZ; z++)
                used.Add(start + new Vector3Int(x, y, z));

            Vector3 min = origin + Vector3.Scale(start, size);
            Vector3 extent = Vector3.Scale(new Vector3(lengthX, lengthY, lengthZ), size);
            result.Add(new Bounds(min + extent * 0.5f, extent));
        }

        return result;
    }

    private static bool RowFree(HashSet<Vector3Int> filled, HashSet<Vector3Int> used, Vector3Int start, int lengthX, int _)
    {
        for (int x = 0; x < lengthX; x++)
        {
            Vector3Int cell = start + new Vector3Int(x, 0, 0);
            if (!filled.Contains(cell) || used.Contains(cell))
                return false;
        }

        return true;
    }

    private static bool SlabFree(HashSet<Vector3Int> filled, HashSet<Vector3Int> used, Vector3Int start, int lengthX, int lengthZ)
    {
        for (int z = 0; z < lengthZ; z++)
        {
            if (!RowFree(filled, used, start + new Vector3Int(0, 0, z), lengthX, 1))
                return false;
        }

        return true;
    }
}
