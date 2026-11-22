using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The room shells in Main.blend ("Plane.*", "Floor.*") are open boxes whose faces point one way only,
/// often away from the corridor. Blender draws back faces, so the model looks solid there; Unity culls
/// them, which shows up as see-through slivers at wall ends and door jambs. On import, every shell
/// triangle gets a reversed twin with flipped normals, so the walls render from both sides.
/// The .blend is untouched. Bump the version to force a reimport after changing this.
/// </summary>
public class DoubleSidedRoomShells : AssetPostprocessor
{
    // Which objects of which model get both sides. The washroom (exported from the team's full floor
    // file) has wall boxes and partitions whose faces point into the wall, not the room.
    private static readonly Dictionary<string, Regex> Models = new()
    {
        { "Assets/Scenes/Main.blend", new Regex(@"^(Plane|Floor)(\.\d+)?$") },
        { "Assets/Routes/Shared/Models/Washroom.fbx", new Regex(@"^(Cube\.\d+|URINAL AND DIVIDER.*|MIRROR LAYER.*)$") },
    };

    // Lift alcoves: inside-out boxes meant to be looked into from the lobby. Doubling them turns
    // the three open lift shafts on each side into a flat wall, so they stay single-sided.
    private static readonly HashSet<string> Keep = new() { "Plane.010", "Plane.018", "Plane.019", "Plane.021", "Plane.022", "Plane.023" };

    public override uint GetVersion() => 4;

    // The meshes must be readable while this importer rewrites them (Read/Write in the import settings).
    private void OnPreprocessModel()
    {
        if (Models.ContainsKey(assetPath) && assetImporter is ModelImporter importer && !importer.isReadable)
            importer.isReadable = true;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!Models.TryGetValue(assetPath, out Regex shellName))
            return;

        HashSet<Mesh> done = new();
        int count = 0;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !shellName.IsMatch(filter.gameObject.name) || Keep.Contains(filter.gameObject.name) || !done.Add(mesh))
                continue;
            if (!mesh.isReadable)
            {
                Debug.LogError($"[DoubleSidedRoomShells] {mesh.name} is not readable; left single-sided.");
                continue;
            }
            MakeDoubleSided(mesh);
            count++;
        }

        Debug.Log($"[DoubleSidedRoomShells] {count} room shell meshes made double-sided in {assetPath}");
    }

    private static void MakeDoubleSided(Mesh mesh)
    {
        int n = mesh.vertexCount;
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Color[] colors = mesh.colors;
        List<Vector2> uv0 = new(), uv1 = new();
        mesh.GetUVs(0, uv0);
        mesh.GetUVs(1, uv1);

        Vector3[] p2 = new Vector3[n * 2];
        positions.CopyTo(p2, 0);
        positions.CopyTo(p2, n);

        int[][] tris = new int[mesh.subMeshCount][];
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] src = mesh.GetTriangles(sub);
            int[] dst = new int[src.Length * 2];
            src.CopyTo(dst, 0);
            for (int t = 0; t + 2 < src.Length; t += 3)
            {
                // Reversed winding on the duplicated vertices.
                dst[src.Length + t] = src[t] + n;
                dst[src.Length + t + 1] = src[t + 2] + n;
                dst[src.Length + t + 2] = src[t + 1] + n;
            }
            tris[sub] = dst;
        }

        IndexFormat format = n * 2 > 65000 ? IndexFormat.UInt32 : mesh.indexFormat;
        mesh.Clear();
        mesh.indexFormat = format;
        mesh.vertices = p2;
        if (normals.Length == n)
        {
            Vector3[] n2 = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                n2[i] = normals[i];
                n2[i + n] = -normals[i];
            }
            mesh.normals = n2;
        }
        if (tangents.Length == n)
        {
            Vector4[] t2 = new Vector4[n * 2];
            for (int i = 0; i < n; i++)
            {
                t2[i] = tangents[i];
                Vector4 t = tangents[i];
                t2[i + n] = new Vector4(t.x, t.y, t.z, -t.w); // keeps the bitangent with the flipped normal
            }
            mesh.tangents = t2;
        }
        if (colors.Length == n)
        {
            Color[] c2 = new Color[n * 2];
            colors.CopyTo(c2, 0);
            colors.CopyTo(c2, n);
            mesh.colors = c2;
        }
        if (uv0.Count == n)
        {
            uv0.AddRange(uv0.ToArray());
            mesh.SetUVs(0, uv0);
        }
        if (uv1.Count == n)
        {
            uv1.AddRange(uv1.ToArray());
            mesh.SetUVs(1, uv1);
        }

        mesh.subMeshCount = tris.Length;
        for (int sub = 0; sub < tris.Length; sub++)
            mesh.SetTriangles(tris[sub], sub, false);
        mesh.RecalculateBounds();
    }
}
