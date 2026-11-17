using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Scene polish applied by RouteSceneBuilder on every rebuild, so it survives a new floor model:
/// z-fighting fixes on the floor model (tiny offsets on its child objects, the .blend is untouched),
/// the roof over the corridors, and the night lighting (skybox, moonlight, fog, ambient).
/// </summary>
public static class FloorPolish
{
    private const string FloorRootName = "Main";
    private const string RoofDataPath = "Assets/Routes/Shared/Models/roof_mesh.json";
    private const string RoofMeshPath = "Assets/Routes/Shared/Models/RoofMesh.asset";
    private const string RoofObjectName = "Roof (MAIN-ROOF)";
    private const string SkyMaterialPath = "Assets/Routes/Shared/Materials/M_NightSky.mat";
    private const string RoofMaterialPath = "Assets/Routes/Shared/Materials/M_Route_Ceiling.mat";

    // ------------------------------------------------------------------ z-fighting

    private struct Face
    {
        public int Owner;
        public int Sign;
        public float MinA, MaxA, MinB, MaxB;
    }

    /// <summary>
    /// Finds faces of different floor objects lying in the same axis-aligned plane and overlapping,
    /// then nudges one object of each pair a few millimetres so the surfaces no longer fight.
    /// Every run starts from the model's own transforms, so it is safe to repeat.
    /// </summary>
    public static string FixZFighting()
    {
        GameObject floor = GameObject.Find(FloorRootName);
        if (floor == null)
            return "Floor model 'Main' not found; z-fighting fix skipped.";
        // GB_ZFIX=off skips the nudges entirely. Checked: the nudges (a few mm, collision included)
        // leave the walk grid and stair boarding unchanged.
        bool skip = System.Environment.GetEnvironmentVariable("GB_ZFIX") == "off";

        List<MeshRenderer> renderers = floor.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null && !r.gameObject.name.StartsWith("Collision - ") &&
                        Base(r.gameObject.name) != "Cylinder") // pipe joins sit inside each other, never seen
            .ToList();

        // Back to the model's own placement first.
        foreach (MeshRenderer renderer in renderers)
        {
            Transform source = PrefabUtility.GetCorrespondingObjectFromSource(renderer.transform);
            if (source == null)
                continue;
            renderer.transform.localPosition = source.localPosition;
            renderer.transform.localScale = source.localScale;
        }

        if (skip)
            return "Z-fighting: skipped (GB_ZFIX=off), floor model at its own placement.";


        FindPairs(renderers, out Dictionary<(int, int), (int axis, int signA, int signB)> pairInfo);
        int fixedPairs = 0;
        StringBuilder log = new();
        log.AppendLine($"Overlapping coplanar pairs before: {pairInfo.Count}");

        // Ceiling light panels face up and sit on the ceiling: mirror them to face the room, just below.
        HashSet<int> moved = new();
        for (int index = 0; index < renderers.Count; index++)
        {
            MeshRenderer renderer = renderers[index];
            if (!Base(renderer.name).Equals("Roof"))
                continue;

            Bounds old = renderer.bounds;
            Transform t = renderer.transform;
            Vector3 localUp = t.InverseTransformDirection(Vector3.up);
            int axis = MaxAxis(localUp);
            Vector3 scale = t.localScale;
            scale[axis] = -scale[axis];
            t.localScale = scale;
            Bounds now = renderer.bounds;
            t.position += Vector3.up * (old.center.y - now.center.y - 0.004f);
            moved.Add(index);
            log.AppendLine($"  flipped light panel {renderer.name}");
        }

        // Floors: tiles a little above the room shells, the corridor in between.
        for (int index = 0; index < renderers.Count; index++)
        {
            string name = Base(renderers[index].name);
            float lift = name == "Floor" ? 0.003f : name == "Pathway" ? 0.0015f : 0f;
            if (lift <= 0f)
                continue;

            renderers[index].transform.position += Vector3.up * lift;
            moved.Add(index);
        }

        // Door handles stand off the door leaf.
        Dictionary<string, MeshRenderer> doors = renderers.Where(r => Base(r.name) == "Door")
            .GroupBy(r => Suffix(r.name)).ToDictionary(g => g.Key, g => g.First());
        for (int index = 0; index < renderers.Count; index++)
        {
            MeshRenderer handle = renderers[index];
            string name = Base(handle.name);
            if (name != "Handle_Front" && name != "Handle_Back" || !doors.TryGetValue(Suffix(handle.name), out MeshRenderer door))
                continue;
            Bounds leaf = door.bounds;
            int thin = MinAxis(leaf.size);
            Vector3 outward = Vector3.zero;
            outward[thin] = Mathf.Sign(handle.bounds.center[thin] - leaf.center[thin]);
            if (outward == Vector3.zero)
                outward[thin] = name == "Handle_Front" ? 1f : -1f;
            handle.transform.position += outward * 0.003f;
            moved.Add(index);
        }

        // Everything else: the smaller object of a pair steps out along its own face normal.
        for (int pass = 0; pass < 3; pass++)
        {
            Physics.SyncTransforms();
            FindPairs(renderers, out Dictionary<(int, int), (int axis, int signA, int signB)> pairs);
            if (pairs.Count == 0)
                break;

            Dictionary<int, Vector3> push = new();
            foreach (KeyValuePair<(int a, int b), (int axis, int signA, int signB)> pair in pairs)
            {
                (int a, int b) = pair.Key;
                bool moveA = Area(renderers[a]) <= Area(renderers[b]);
                int who = moveA ? a : b;
                int sign = moveA ? pair.Value.signA : pair.Value.signB;
                if (sign == 0)
                    sign = 1;
                Vector3 step = Vector3.zero;
                step[pair.Value.axis] = sign * 0.002f * (pass + 1);
                push.TryAdd(who, step);
            }

            foreach (KeyValuePair<int, Vector3> entry in push)
            {
                renderers[entry.Key].transform.position += entry.Value;
                moved.Add(entry.Key);
            }

            fixedPairs += pairs.Count;
        }

        Physics.SyncTransforms();
        FindPairs(renderers, out Dictionary<(int, int), (int axis, int signA, int signB)> left);
        log.AppendLine($"Objects nudged: {moved.Count}. Pairs left: {left.Count}");
        foreach (var pair in left.Take(40))
            log.AppendLine($"  left: {renderers[pair.Key.Item1].name} | {renderers[pair.Key.Item2].name}  axis {pair.Value.axis} " +
                           $"at {renderers[pair.Key.Item1].bounds.center}");
        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/zfight.txt"), log.ToString());
        return $"Z-fighting: {pairInfo.Count} overlapping pairs before, {left.Count} after ({moved.Count} floor objects nudged).";
    }

    private static void FindPairs(List<MeshRenderer> renderers, out Dictionary<(int, int), (int axis, int signA, int signB)> pairs)
    {
        Dictionary<(int axis, long plane), List<Face>> buckets = new();
        for (int owner = 0; owner < renderers.Count; owner++)
        {
            MeshRenderer renderer = renderers[owner];
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = toWorld.MultiplyPoint3x4(verts[i]);
            int[] tris = mesh.triangles;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area < 0.0004f)
                    continue;
                Vector3 n = cross / (area * 2f);
                int axis = MaxAxis(n);
                if (Mathf.Abs(n[axis]) < 0.999f)
                    continue;
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                float plane = (a[axis] + b[axis] + c[axis]) / 3f;
                Face face = new()
                {
                    Owner = owner,
                    Sign = n[axis] > 0f ? 1 : -1,
                    MinA = Mathf.Min(a[u], b[u], c[u]), MaxA = Mathf.Max(a[u], b[u], c[u]),
                    MinB = Mathf.Min(a[v], b[v], c[v]), MaxB = Mathf.Max(a[v], b[v], c[v]),
                };
                (int, long) key = (axis, (long)Mathf.Round(plane / 0.001f));
                if (!buckets.TryGetValue(key, out List<Face> list))
                    buckets[key] = list = new List<Face>();
                list.Add(face);
            }
        }

        pairs = new Dictionary<(int, int), (int, int, int)>();
        foreach (KeyValuePair<(int axis, long plane), List<Face>> bucket in buckets)
        {
            // Neighbouring 1 mm planes count as the same plane too.
            List<Face> faces = new(bucket.Value);
            if (buckets.TryGetValue((bucket.Key.axis, bucket.Key.plane + 1), out List<Face> next))
                faces.AddRange(next);
            if (faces.Count > 6000)
                faces = faces.Take(6000).ToList();
            faces.Sort((x, y) => x.MinA.CompareTo(y.MinA));
            for (int i = 0; i < faces.Count; i++)
            for (int j = i + 1; j < faces.Count && faces[j].MinA < faces[i].MaxA - 0.01f; j++)
            {
                Face f = faces[i], g = faces[j];
                // Only faces looking the same way fight; back-to-back faces hide each other.
                if (f.Owner == g.Owner || f.Sign != g.Sign)
                    continue;
                if (Mathf.Min(f.MaxA, g.MaxA) - Mathf.Max(f.MinA, g.MinA) < 0.02f ||
                    Mathf.Min(f.MaxB, g.MaxB) - Mathf.Max(f.MinB, g.MinB) < 0.02f)
                    continue;
                (int, int) key = f.Owner < g.Owner ? (f.Owner, g.Owner) : (g.Owner, f.Owner);
                int signLow = f.Owner < g.Owner ? f.Sign : g.Sign;
                int signHigh = f.Owner < g.Owner ? g.Sign : f.Sign;
                pairs.TryAdd(key, (bucket.Key.axis, signLow, signHigh));
            }
        }
    }

    private static float Area(MeshRenderer renderer)
    {
        Vector3 s = renderer.bounds.size;
        return s.x * s.y + s.y * s.z + s.x * s.z;
    }

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static int MaxAxis(Vector3 v)
    {
        Vector3 a = Abs(v);
        return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
    }

    private static int MinAxis(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : v.y <= v.z ? 1 : 2;

    private static string Base(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && name.Substring(dot + 1).All(char.IsDigit) ? name.Substring(0, dot) : name;
    }

    private static string Suffix(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && name.Substring(dot + 1).All(char.IsDigit) ? name.Substring(dot + 1) : "";
    }

    // ------------------------------------------------------------------ roof

    [System.Serializable]
    private class RoofData
    {
        public float[] verts;
        public int[] tris;
    }

    /// <summary>
    /// The roof over the whole floor (corridors had none), visual only: no collider. Unity imports
    /// MAIN-ROOF.fbx as a stub, so the mesh comes from Tools/Blender/export_roof.py (Blender world
    /// space, converted like the floor model: Unity = floor origin + (-x, z, -y)), all faces turned down.
    /// </summary>
    public static string PlaceRoof()
    {
        GameObject floor = GameObject.Find(FloorRootName);
        foreach (GameObject old in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                     .Where(t => t.parent == null && t.name == RoofObjectName).Select(t => t.gameObject).ToArray())
            Object.DestroyImmediate(old);

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(RoofDataPath);
        if (json == null || floor == null)
            return "Roof data (run Tools/Blender/export_roof.py) or floor not found; roof skipped.";

        RoofData data = JsonUtility.FromJson<RoofData>(json.text);
        Vector3[] vertices = new Vector3[data.verts.Length / 3];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new Vector3(-data.verts[i * 3], data.verts[i * 3 + 2], -data.verts[i * 3 + 1]);
        int[] triangles = (int[])data.tris.Clone();
        int filled = FillRoofHoles(ref vertices, ref triangles);
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            // Unity is clockwise-front: the face looks down when this cross product points down.
            if (Vector3.Cross(b - a, c - a).y > 0f)
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RoofMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "RoofMesh" };
            AssetDatabase.CreateAsset(mesh, RoofMeshPath);
        }

        mesh.Clear();
        mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        GameObject roof = new(RoofObjectName);
        roof.transform.position = floor.transform.position;
        roof.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = roof.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CeilingMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
        GameObjectUtility.SetStaticEditorFlags(roof, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);

        Bounds bounds = renderer.bounds;
        Bounds floorBounds = floor.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds)
            .Aggregate((a, b) => { a.Encapsulate(b); return a; });
        return $"Roof placed: x {bounds.min.x:0.0}..{bounds.max.x:0.0}, z {bounds.min.z:0.0}..{bounds.max.z:0.0}, y {bounds.center.y:0.00} " +
               $"(floor x {floorBounds.min.x:0.0}..{floorBounds.max.x:0.0}, z {floorBounds.min.z:0.0}..{floorBounds.max.z:0.0}), {triangles.Length / 3} triangles, " +
               $"{filled} m² of enclosed hole filled.";
    }

    /// <summary>
    /// The exported roof has an enclosed hole over the middle of the floor (the atrium). Rasterises
    /// the roof on a 0.25 m grid, floods the uncovered cells from the border, and closes every
    /// uncovered cell the flood cannot reach with flat quads, 1 cm above the roof (grown by one cell
    /// so no sliver shows at the seam). Returns the filled area in m².
    /// </summary>
    private static int FillRoofHoles(ref Vector3[] vertices, ref int[] triangles)
    {
        const float cell = 0.25f;
        if (vertices.Length == 0)
            return 0;
        float minX = vertices.Min(v => v.x) - cell, maxX = vertices.Max(v => v.x) + cell;
        float minZ = vertices.Min(v => v.z) - cell, maxZ = vertices.Max(v => v.z) + cell;
        float roofY = vertices.Average(v => v.y);
        int nx = Mathf.CeilToInt((maxX - minX) / cell), nz = Mathf.CeilToInt((maxZ - minZ) / cell);
        bool[] covered = new bool[nx * nz];

        static float Side(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector2 a = new(vertices[triangles[t]].x, vertices[triangles[t]].z);
            Vector2 b = new(vertices[triangles[t + 1]].x, vertices[triangles[t + 1]].z);
            Vector2 c = new(vertices[triangles[t + 2]].x, vertices[triangles[t + 2]].z);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - minX) / cell));
            int x1 = Mathf.Min(nx - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - minX) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y, c.y) - minZ) / cell));
            int z1 = Mathf.Min(nz - 1, Mathf.FloorToInt((Mathf.Max(a.y, b.y, c.y) - minZ) / cell));
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                Vector2 p = new(minX + (x + 0.5f) * cell, minZ + (z + 0.5f) * cell);
                float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
                bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(negative && positive))
                    covered[x * nz + z] = true;
            }
        }

        bool[] outside = new bool[nx * nz];
        Queue<int> queue = new();
        for (int x = 0; x < nx; x++)
        for (int z = 0; z < nz; z++)
        {
            if ((x == 0 || z == 0 || x == nx - 1 || z == nz - 1) && !covered[x * nz + z])
            {
                outside[x * nz + z] = true;
                queue.Enqueue(x * nz + z);
            }
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int cx = current / nz, cz = current % nz;
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= nx || z >= nz)
                    continue;
                int next = x * nz + z;
                if (covered[next] || outside[next])
                    continue;
                outside[next] = true;
                queue.Enqueue(next);
            }
        }

        bool[] fill = new bool[nx * nz];
        int holeCells = 0;
        for (int x = 0; x < nx; x++)
        for (int z = 0; z < nz; z++)
        {
            if (covered[x * nz + z] || outside[x * nz + z])
                continue;
            holeCells++;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int gx = x + dx, gz = z + dz;
                if (gx >= 0 && gz >= 0 && gx < nx && gz < nz && !outside[gx * nz + gz])
                    fill[gx * nz + gz] = true;
            }
        }

        // Remember what the roof covers (with the holes closed) for PatchRoofOver.
        coverCell = cell;
        coverMinX = minX;
        coverMinZ = minZ;
        coverNx = nx;
        coverNz = nz;
        coverY = roofY;
        covered.CopyTo(coverage = new bool[covered.Length], 0);
        for (int i = 0; i < fill.Length; i++)
            coverage[i] |= fill[i];

        if (holeCells == 0)
            return 0;

        // One quad per run of filled cells along x in each row.
        List<Vector3> verts = new(vertices);
        List<int> tris = new(triangles);
        float y = roofY + 0.01f;
        for (int z = 0; z < nz; z++)
        {
            int x = 0;
            while (x < nx)
            {
                if (!fill[x * nz + z])
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < nx && fill[x * nz + z])
                    x++;
                float ax = minX + start * cell, bx = minX + x * cell;
                float az = minZ + z * cell, bz = az + cell;
                int i = verts.Count;
                verts.Add(new Vector3(ax, y, az));
                verts.Add(new Vector3(bx, y, az));
                verts.Add(new Vector3(bx, y, bz));
                verts.Add(new Vector3(ax, y, bz));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
        }

        vertices = verts.ToArray();
        triangles = tris.ToArray();
        return Mathf.RoundToInt(holeCells * cell * cell);
    }

    private static bool[] coverage;
    private static float coverCell, coverMinX, coverMinZ, coverY;
    private static int coverNx, coverNz;

    /// <summary>
    /// Ceiling patches (flat quads at roof height) over every given floor point the roof does not
    /// cover, e.g. corridors and stair passages outside the exported roof's outline. Run after
    /// PlaceRoof. Returns the patched area in m².
    /// </summary>
    public static int PatchRoofOver(IEnumerable<Vector3> floorPoints, float radius)
    {
        GameObject roof = GameObject.Find(RoofObjectName);
        GameObject floor = GameObject.Find(FloorRootName);
        if (coverage == null || roof == null || floor == null)
            return 0;

        Vector3 origin = floor.transform.position;
        bool[] patch = new bool[coverage.Length];
        int reach = Mathf.CeilToInt(radius / coverCell);
        int count = 0;
        foreach (Vector3 point in floorPoints)
        {
            int cx = Mathf.FloorToInt((point.x - origin.x - coverMinX) / coverCell);
            int cz = Mathf.FloorToInt((point.z - origin.z - coverMinZ) / coverCell);
            for (int x = cx - reach; x <= cx + reach; x++)
            for (int z = cz - reach; z <= cz + reach; z++)
            {
                bool inside = x >= 0 && z >= 0 && x < coverNx && z < coverNz;
                if (inside && (coverage[x * coverNz + z] || patch[x * coverNz + z]))
                    continue;
                if (!inside)
                    continue;   // outside the roof's box: nothing there to walk under
                patch[x * coverNz + z] = true;
                count++;
            }
        }

        foreach (Transform old in roof.transform.Cast<Transform>().Where(t => t.name == "Roof Patches").ToArray())
            Object.DestroyImmediate(old.gameObject);
        if (count == 0)
            return 0;

        List<Vector3> verts = new();
        List<int> tris = new();
        float y = coverY - 0.005f;
        for (int z = 0; z < coverNz; z++)
        {
            int x = 0;
            while (x < coverNx)
            {
                if (!patch[x * coverNz + z])
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < coverNx && patch[x * coverNz + z])
                    x++;
                float ax = coverMinX + start * coverCell, bx = coverMinX + x * coverCell;
                float az = coverMinZ + z * coverCell, bz = az + coverCell;
                int i = verts.Count;
                verts.Add(new Vector3(ax, y, az));
                verts.Add(new Vector3(ax, y, bz));
                verts.Add(new Vector3(bx, y, bz));
                verts.Add(new Vector3(bx, y, az));
                tris.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });   // facing down
            }
        }

        Mesh mesh = new() { name = "RoofPatches", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        const string path = "Assets/Routes/Shared/Models/RoofPatches.asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null)
            AssetDatabase.CreateAsset(saved = mesh, path);
        else
        {
            saved.Clear();
            EditorUtility.CopySerialized(mesh, saved);
        }

        EditorUtility.SetDirty(saved);
        GameObject patches = new("Roof Patches");
        patches.transform.SetParent(roof.transform, false);
        patches.AddComponent<MeshFilter>().sharedMesh = saved;
        MeshRenderer renderer = patches.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = roof.GetComponent<MeshRenderer>().sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
        return Mathf.RoundToInt(count * coverCell * coverCell);
    }

    private static Material CeilingMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(RoofMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, RoofMaterialPath);
        }

        Color color = new(0.62f, 0.62f, 0.6f);
        material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.doubleSidedGI = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------ fog below

    private const string FogObjectName = "Fog Below";

    /// <summary>
    /// Nothing is modelled under or around the floor, so the atrium and the windows looked onto a
    /// void. A stack of large drifting mist layers (GroundBreaking/FogLayer) under the floor, over a
    /// fog-coloured base, makes it read as an endless foggy drop. Visual only, no colliders.
    /// </summary>
    public static string PlaceFogBelow()
    {
        foreach (GameObject old in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                     .Where(t => t.parent == null && t.name == FogObjectName).Select(t => t.gameObject).ToArray())
            Object.DestroyImmediate(old);

        Shader shader = Shader.Find("GroundBreaking/FogLayer");
        GameObject floor = GameObject.Find(FloorRootName);
        if (shader == null || floor == null)
            return "Fog below skipped (FogLayer shader or floor missing).";

        Bounds floorBounds = floor.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds)
            .Aggregate((a, b) => { a.Encapsulate(b); return a; });
        Vector3 centre = new(floorBounds.center.x, floor.transform.position.y, floorBounds.center.z);
        GameObject root = new(FogObjectName);
        root.transform.position = centre;

        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        (string name, float depth, float density, float scale, Vector2 speed, bool opaque)[] layers =
        {
            ("Mist High", -1.2f, 0.5f, 9f, new Vector2(0.30f, 0.10f), false),
            ("Mist Mid", -2.8f, 0.7f, 15f, new Vector2(-0.18f, 0.22f), false),
            ("Mist Low", -5f, 0.9f, 24f, new Vector2(0.12f, -0.15f), false),
            ("Fog Base", -9f, 1f, 1f, Vector2.zero, true),
        };
        Color fog = RenderSettings.fogColor;
        foreach ((string name, float depth, float density, float scale, Vector2 speed, bool opaque) in layers)
        {
            string path = $"Assets/Routes/Shared/Materials/M_Fog_{name.Replace(" ", "")}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            // Moonlit mist, a little lighter than the night fog, over a base just under it.
            material.SetColor("_Color", opaque ? fog * 1.4f : new Color(0.17f, 0.19f, 0.24f));
            material.SetFloat("_Density", density);
            material.SetFloat("_Scale", scale);
            material.SetVector("_Speed", speed);
            material.SetFloat("_NearFade", opaque ? 0.01f : 4f);
            material.SetFloat("_Opaque", opaque ? 1f : 0f);
            EditorUtility.SetDirty(material);

            GameObject layer = new(name);
            layer.transform.SetParent(root.transform, false);
            layer.transform.localPosition = Vector3.up * depth;
            layer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            layer.transform.localScale = new Vector3(600f, 600f, 1f);
            layer.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return $"Fog below: {layers.Length} layers under the floor (down to {layers[^1].depth} m).";
    }

    // ------------------------------------------------------------------ night lighting

    public static string ApplyNight()
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
        Shader skyShader = Shader.Find("GroundBreaking/NightSky");
        if (sky == null && skyShader != null)
        {
            sky = new Material(skyShader);
            AssetDatabase.CreateAsset(sky, SkyMaterialPath);
        }

        if (sky != null)
        {
            RenderSettings.skybox = sky;
            // Below the horizon the sky matches the fog, so the mist below fades into it seamlessly.
            sky.SetColor("_GroundColor", new Color(0.035f, 0.04f, 0.055f));
            EditorUtility.SetDirty(sky);
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.10f, 0.11f, 0.16f);
        RenderSettings.ambientEquatorColor = new Color(0.06f, 0.06f, 0.08f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.03f;
        RenderSettings.fogColor = new Color(0.035f, 0.04f, 0.055f);
        RenderSettings.reflectionIntensity = 0.35f;

        Light moon = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).FirstOrDefault(l => l.type == LightType.Directional);
        if (moon != null)
        {
            moon.name = "Moonlight";
            moon.color = new Color(0.55f, 0.63f, 0.85f);
            moon.intensity = 0.3f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = moon;
        }

        return $"Night lighting: skybox {(sky != null ? "NightSky" : "missing shader")}, moonlight {(moon != null ? "set" : "missing")}, fog on.";
    }
}
