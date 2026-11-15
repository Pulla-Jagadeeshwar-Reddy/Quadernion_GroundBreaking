using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Debug helpers for checking the first-floor collision against the visible model.
/// Batch: -executeMethod RouteFloorProbe.DumpFromCommandLine (writes Logs/floor_map.txt and Logs/proxy_report.txt)
/// </summary>
public static class RouteFloorProbe
{
    [MenuItem("Tools/Routes/Debug/Dump Floor Map")]
    public static void DumpFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/OnlineFirstFloor.unity", OpenSceneMode.Single);
        Physics.SyncTransforms();
        WriteProxyReport();
        WriteFloorMeshReport();
        WriteRoomProbe();
        Debug.Log("[RouteFloorProbe] wrote Logs/proxy_report.txt and Logs/floor_meshes.txt");
    }

    /// <summary>What sits under / around the centre of a few rooms that the walk grid says are blocked.</summary>
    private static void WriteRoomProbe()
    {
        (string name, Vector3 point)[] probes =
        {
            ("Classroom 02", new Vector3(-36f, 0f, 21f)),
            ("Classroom 03", new Vector3(-46.5f, 0f, 21f)),
            ("Classroom 06", new Vector3(-36f, 0f, -3f)),
            ("Laboratory", new Vector3(-6f, 0f, -6f)),
            ("Staff Room", new Vector3(12f, 0f, 5f)),
            ("Janitor Room", new Vector3(-11.8f, 0f, 7.3f)),
            ("Classroom 1", new Vector3(12f, 0f, -14f)),
        };

        StringBuilder report = new();
        foreach ((string name, Vector3 point) in probes)
        {
            report.AppendLine($"== {name} at ({point.x}, {point.z})");
            foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 2.4f, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                         .OrderBy(h => h.distance))
                report.AppendLine($"   ray hit {Path(hit.collider.transform)} at y={hit.point.y:0.00}");

            foreach (Collider overlap in Physics.OverlapCapsule(point + Vector3.up * 0.4f, point + Vector3.up * 1.3f, 0.3f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Bounds b = overlap.bounds;
                report.AppendLine($"   body overlaps {Path(overlap.transform)} ({overlap.GetType().Name}) bounds size ({b.size.x:0.0},{b.size.y:0.0},{b.size.z:0.0}) center ({b.center.x:0.0},{b.center.y:0.0},{b.center.z:0.0}) enabled={overlap.enabled}");
            }
        }

        File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/room_probe.txt"), report.ToString());
    }

    private static string Path(Transform target)
    {
        string path = target.name;
        for (Transform current = target.parent; current != null && path.Length < 120; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    /// <summary>Flat meshes near y=0 (floors) and whether any collider sits on top of them.</summary>
    private static void WriteFloorMeshReport()
    {
        StringBuilder report = new();
        int missing = 0;
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer.transform.root.name == "RouteContent" || renderer.transform.root.name == "Online Collision Proxies")
                continue;

            Bounds bounds = renderer.bounds;
            if (bounds.size.y > 0.35f || bounds.center.y < -0.4f || bounds.center.y > 0.3f)
                continue;
            if (bounds.size.x * bounds.size.z < 3f)
                continue;

            // Sample the mesh footprint: how much of it has a collider surface near the mesh top?
            int samples = 0, covered = 0;
            for (float x = bounds.min.x + 0.3f; x < bounds.max.x; x += 0.8f)
            for (float z = bounds.min.z + 0.3f; z < bounds.max.z; z += 0.8f)
            {
                samples++;
                if (Physics.Raycast(new Vector3(x, bounds.max.y + 0.6f, z), Vector3.down, out RaycastHit hit, 1.2f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    covered++;
            }

            float ratio = samples > 0 ? covered / (float)samples : 1f;
            if (ratio < 0.8f)
                missing++;
            report.AppendLine($"{renderer.name} [{renderer.transform.parent?.name}] center ({bounds.center.x:0.0},{bounds.center.y:0.00},{bounds.center.z:0.0}) size ({bounds.size.x:0.0},{bounds.size.y:0.00},{bounds.size.z:0.0}) covered {ratio:P0}");
        }

        report.Insert(0, $"{missing} flat floor meshes are mostly without collision\n");
        File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/floor_meshes.txt"), report.ToString());
    }

    /// <summary>
    /// Lists collision proxies that block body height (0.3-1.6 m) but have no visible mesh behind
    /// them: missing source, disabled renderer, inactive object, or a box much bigger than the mesh.
    /// </summary>
    private static void WriteProxyReport()
    {
        GameObject proxyRoot = GameObject.Find("Online Collision Proxies");
        StringBuilder report = new();
        if (proxyRoot == null)
        {
            File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/proxy_report.txt"), "no proxy root");
            return;
        }

        var renderers = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(filter => filter.transform.root.name != "Online Collision Proxies")
            .GroupBy(filter => filter.name)
            .ToDictionary(group => group.Key, group => group.ToList());

        int suspicious = 0;
        foreach (BoxCollider box in proxyRoot.GetComponentsInChildren<BoxCollider>(false))
        {
            if (!box.enabled)
                continue;

            Bounds bounds = box.bounds;
            if (bounds.max.y < 0.3f || bounds.min.y > 1.6f)
                continue;

            string name = box.name.StartsWith("Collision - ") ? box.name.Substring(12) : box.name;
            string reason = null;
            if (box.name.StartsWith("Doorway Collision Segment") || box.name.StartsWith("Refined Box"))
                continue;

            if (!renderers.TryGetValue(name, out var candidates))
            {
                reason = "no mesh with this name";
            }
            else
            {
                MeshFilter source = candidates.OrderBy(c => Vector3.Distance(c.transform.position, box.transform.position)).First();
                Renderer renderer = source.GetComponent<Renderer>();
                if (Vector3.Distance(source.transform.position, box.transform.position) > 0.5f)
                    reason = "nearest same-name mesh is far away";
                else if (!source.gameObject.activeInHierarchy)
                    reason = "source object inactive";
                else if (renderer == null || !renderer.enabled)
                    reason = "source renderer disabled";
                else
                {
                    Bounds visible = renderer.bounds;
                    float boxVolume = bounds.size.x * bounds.size.y * bounds.size.z;
                    float meshVolume = Mathf.Max(0.001f, visible.size.x * visible.size.y * visible.size.z);
                    if (boxVolume > meshVolume * 3f && boxVolume > 0.5f)
                        reason = $"box {boxVolume:0.0} m³ vs visible {meshVolume:0.0} m³";
                }
            }

            if (reason == null)
                continue;

            suspicious++;
            report.AppendLine($"{box.name} at ({bounds.center.x:0.0}, {bounds.center.y:0.0}, {bounds.center.z:0.0}) size ({bounds.size.x:0.0}, {bounds.size.y:0.0}, {bounds.size.z:0.0}): {reason}");
        }

        report.Insert(0, $"{suspicious} suspicious body-height proxies\n");
        File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/proxy_report.txt"), report.ToString());
    }
}
