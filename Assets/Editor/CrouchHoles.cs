using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Crouch holes: low openings cut through existing walls of the floor model, which only the
/// crouching Runner fits through (Granny cannot crouch, so she has to go round by the doors).
/// The wall meshes are cut in place (the visible faces and the collision), overlapping collision
/// boxes are split around the hole, and a thin frame lines the opening. Everything is recorded on
/// a CrouchHoleCut component and undone by Restore at the start of the next build.
/// </summary>
public static class CrouchHoles
{
    public const float Width = 1.2f;
    public const float Height = 1.4f;
    private const string MeshFolder = "Assets/Routes/Shared/Meshes";

    /// <summary>Puts every wall back the way the floor model has it.</summary>
    public static int Restore()
    {
        int restored = 0;
        foreach (CrouchHoleCut cut in Object.FindObjectsByType<CrouchHoleCut>(FindObjectsInactive.Include))
        {
            if (cut.originalMesh != null)
            {
                MeshFilter filter = cut.GetComponent<MeshFilter>();
                if (filter != null)
                    filter.sharedMesh = cut.originalMesh;
                foreach (MeshCollider collider in cut.GetComponents<MeshCollider>())
                {
                    if (collider.sharedMesh != null && collider.sharedMesh.name.StartsWith("CrouchCut_"))
                        collider.sharedMesh = cut.originalMesh;
                }
            }

            foreach (Collider collider in cut.disabledColliders)
            {
                if (collider != null)
                    collider.enabled = true;
            }

            Object.DestroyImmediate(cut);
            restored++;
        }

        Physics.SyncTransforms();
        return restored;
    }

    /// <summary>
    /// Cuts a hole through the wall found from <paramref name="near"/> looking along
    /// <paramref name="into"/> (within 1.5 m, at waist height). Replacement collision and the frame
    /// go under <paramref name="parent"/>.
    /// </summary>
    public static string Cut(string name, Vector3 near, Vector3 into, Transform parent, Material frameMaterial, string contentRootName)
    {
        Vector3 from = new(near.x, 1.0f, near.z);
        if (!Physics.Raycast(from, into, out RaycastHit wall, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return $"{name}: no wall within 1.5 m of ({near.x:0.0}, {near.z:0.0}) towards ({into.x:0}, {into.z:0}); skipped.";
        Vector3 face = wall.point;

        // Its far face, seen from the other side.
        Vector3 beyond = face + into * 1.2f;
        float thickness = Physics.Raycast(beyond, -into, out RaycastHit back, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? 1.2f - back.distance
            : 0.3f;
        thickness = Mathf.Clamp(thickness, 0.05f, 1.0f);

        Vector3 across = Vector3.Cross(Vector3.up, into).normalized;
        Vector3 centre = new Vector3(face.x, 0f, face.z) + into * (thickness * 0.5f);
        // The hole box (world, axis-aligned): through the whole wall plus a margin.
        Vector3 size = Abs(across) * Width + Vector3.up * Height + Abs(into) * (thickness + 0.12f);
        Bounds hole = new(centre + Vector3.up * (Height * 0.5f), size);

        int meshes = 0, boxes = 0;
        HashSet<GameObject> changed = new();
        foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            Renderer renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh == null || renderer == null || filter.transform.root.name == contentRootName ||
                !renderer.bounds.Intersects(hole))
                continue;
            Mesh cut = CutMesh(filter, hole, into, across);
            if (cut == null)
                continue;

            CrouchHoleCut record = Record(filter.gameObject);
            if (record.originalMesh == null)
                record.originalMesh = filter.sharedMesh;
            filter.sharedMesh = cut;
            foreach (MeshCollider collider in filter.GetComponents<MeshCollider>())
            {
                if (collider.sharedMesh == record.originalMesh || collider.sharedMesh == null)
                    collider.sharedMesh = cut;
            }

            changed.Add(filter.gameObject);
            meshes++;
        }

        // Collision boxes through the hole (the floor model's proxies): switched off and replaced by
        // pieces around it.
        GameObject collision = new($"{name} Collision");
        collision.transform.SetParent(parent, false);
        foreach (BoxCollider box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (!box.enabled || box.isTrigger || box.transform.root.name == contentRootName || !box.bounds.Intersects(Shrink(hole, 0.02f)))
                continue;
            Bounds world = box.bounds;
            Record(box.gameObject).disabledColliders.Add(box);
            box.enabled = false;
            foreach (Bounds piece in Subtract(world, hole))
            {
                GameObject part = new("Piece");
                part.transform.SetParent(collision.transform, false);
                part.transform.position = piece.center;
                part.AddComponent<BoxCollider>().size = piece.size;
                boxes++;
            }
        }

        // A thin frame lining the opening (also hides the cut edges of the wall).
        float depth = thickness + 0.06f;
        GameObject frame = new($"{name} Frame");
        frame.transform.SetParent(parent, false);
        frame.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(into, Vector3.up));
        void Bar(Vector3 local, Vector3 barSize)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Frame Bar";
            bar.transform.SetParent(frame.transform, false);
            bar.transform.localPosition = local;
            bar.transform.localScale = barSize;
            bar.GetComponent<Renderer>().sharedMaterial = frameMaterial;
        }

        Bar(new Vector3(-Width * 0.5f + 0.03f, Height * 0.5f, 0f), new Vector3(0.06f, Height, depth));
        Bar(new Vector3(Width * 0.5f - 0.03f, Height * 0.5f, 0f), new Vector3(0.06f, Height, depth));
        Bar(new Vector3(0f, Height - 0.03f, 0f), new Vector3(Width, 0.06f, depth));

        Physics.SyncTransforms();
        return $"{name}: {Width} x {Height} m hole at ({centre.x:0.0}, {centre.z:0.0}) through a {thickness:0.00} m wall facing " +
               $"({into.x:0}, {into.z:0}); {meshes} wall meshes cut, {boxes} collision pieces around it.";
    }

    private static CrouchHoleCut Record(GameObject target) =>
        target.GetComponent<CrouchHoleCut>() ?? target.AddComponent<CrouchHoleCut>();

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static Bounds Shrink(Bounds b, float by)
    {
        b.Expand(-by * 2f);
        return b;
    }

    /// <summary>The parts of <paramref name="box"/> outside <paramref name="hole"/> (up to six boxes).</summary>
    private static IEnumerable<Bounds> Subtract(Bounds box, Bounds hole)
    {
        Vector3 min = box.min, max = box.max;
        for (int axis = 0; axis < 3; axis++)
        {
            if (hole.min[axis] > min[axis])
            {
                Vector3 hiCorner = max;
                hiCorner[axis] = hole.min[axis];
                yield return Box(min, hiCorner);
                min[axis] = hole.min[axis];
            }

            if (hole.max[axis] < max[axis])
            {
                Vector3 loCorner = min;
                loCorner[axis] = hole.max[axis];
                yield return Box(loCorner, max);
                max[axis] = hole.max[axis];
            }
        }
    }

    private static Bounds Box(Vector3 min, Vector3 max)
    {
        Bounds b = new();
        b.SetMinMax(min, max);
        return b;
    }

    // ---------------------------------------------------------------- mesh cutting

    private struct V
    {
        public Vector3 P, N;
        public Vector2 UV;

        public static V Lerp(V a, V b, float t) => new()
        {
            P = Vector3.Lerp(a.P, b.P, t),
            N = Vector3.Lerp(a.N, b.N, t).normalized,
            UV = Vector2.Lerp(a.UV, b.UV, t),
        };
    }

    /// <summary>
    /// The mesh with the hole taken out of every face that looks along the wall's normal and lies
    /// inside the hole's depth; other faces are kept as they are. Null when nothing changed.
    /// </summary>
    private static Mesh CutMesh(MeshFilter filter, Bounds hole, Vector3 into, Vector3 across)
    {
        Mesh source = filter.sharedMesh;
        Matrix4x4 toWorld = filter.transform.localToWorldMatrix, toLocal = filter.transform.worldToLocalMatrix;
        Vector3[] positions = source.vertices;
        Vector3[] normals = source.normals.Length == positions.Length ? source.normals : new Vector3[positions.Length];
        Vector2[] uvs = source.uv.Length == positions.Length ? source.uv : new Vector2[positions.Length];

        int uAxis = Mathf.Abs(across.x) > 0.5f ? 0 : 2;
        int dAxis = Mathf.Abs(into.x) > 0.5f ? 0 : 2;
        float u0 = hole.min[uAxis], u1 = hole.max[uAxis], v0 = hole.min.y, v1 = hole.max.y;
        float d0 = hole.min[dAxis], d1 = hole.max[dAxis];

        List<Vector3> outP = new(), outN = new();
        List<Vector2> outUV = new();
        List<int>[] outTris = new List<int>[source.subMeshCount];
        bool any = false;

        V World(int i) => new()
        {
            P = toWorld.MultiplyPoint3x4(positions[i]),
            N = toWorld.MultiplyVector(normals[i]).normalized,
            UV = uvs[i],
        };

        void Emit(int sub, List<V> poly)
        {
            if (poly.Count < 3)
                return;
            int start = outP.Count;
            foreach (V v in poly)
            {
                outP.Add(toLocal.MultiplyPoint3x4(v.P));
                outN.Add(toLocal.MultiplyVector(v.N).normalized);
                outUV.Add(v.UV);
            }

            for (int k = 1; k + 1 < poly.Count; k++)
                outTris[sub].AddRange(new[] { start, start + k, start + k + 1 });
        }

        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            outTris[sub] = new List<int>();
            int[] tris = source.GetTriangles(sub);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                List<V> tri = new() { World(tris[t]), World(tris[t + 1]), World(tris[t + 2]) };
                Vector3 normal = Vector3.Cross(tri[1].P - tri[0].P, tri[2].P - tri[0].P);
                bool wallFace = normal.sqrMagnitude > 1e-10f && Mathf.Abs(Vector3.Dot(normal.normalized, into)) > 0.95f;
                float depth = (tri[0].P[dAxis] + tri[1].P[dAxis] + tri[2].P[dAxis]) / 3f;
                float minU = tri.Min(v => v.P[uAxis]), maxU = tri.Max(v => v.P[uAxis]);
                float minV = tri.Min(v => v.P.y), maxV = tri.Max(v => v.P.y);
                bool overlaps = wallFace && depth >= d0 && depth <= d1 && maxU > u0 && minU < u1 && maxV > v0 && minV < v1;
                if (!overlaps)
                {
                    Emit(sub, tri);
                    continue;
                }

                any = true;
                // The triangle minus the hole rectangle: left, right, and above/below between them.
                Emit(sub, Clip(tri, uAxis, u0, false));
                Emit(sub, Clip(tri, uAxis, u1, true));
                List<V> middle = Clip(Clip(tri, uAxis, u0, true), uAxis, u1, false);
                Emit(sub, Clip(middle, 1, v0, false));
                Emit(sub, Clip(middle, 1, v1, true));
            }
        }

        if (!any)
            return null;

        Mesh mesh = new() { name = "CrouchCut_" + source.name };
        mesh.indexFormat = outP.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(outP);
        mesh.SetNormals(outN);
        mesh.SetUVs(0, outUV);
        mesh.subMeshCount = outTris.Length;
        for (int sub = 0; sub < outTris.Length; sub++)
            mesh.SetTriangles(outTris[sub], sub);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder("Assets/Routes/Shared", "Meshes");
        string path = $"{MeshFolder}/CrouchCut_{filter.gameObject.name}_{Mathf.RoundToInt(hole.center.x * 10)}_{Mathf.RoundToInt(hole.center.z * 10)}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Sutherland-Hodgman: keeps the part with coordinate >= limit (keepAbove) or <= limit.</summary>
    private static List<V> Clip(List<V> poly, int axis, float limit, bool keepAbove)
    {
        List<V> result = new();
        if (poly.Count == 0)
            return result;
        bool Inside(V v) => keepAbove ? v.P[axis] >= limit : v.P[axis] <= limit;
        for (int i = 0; i < poly.Count; i++)
        {
            V a = poly[i], b = poly[(i + 1) % poly.Count];
            bool ia = Inside(a), ib = Inside(b);
            if (ia)
                result.Add(a);
            if (ia != ib)
            {
                float t = (limit - a.P[axis]) / (b.P[axis] - a.P[axis]);
                result.Add(V.Lerp(a, b, t));
            }
        }

        return result;
    }
}
