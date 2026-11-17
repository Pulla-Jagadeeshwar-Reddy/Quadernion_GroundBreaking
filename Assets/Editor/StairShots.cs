using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Debug: renders a top-down and an eye-level picture of every stair blocker into Logs/stair_shots,
/// so the boarding can be checked without walking there.
/// Batch (needs graphics, so no -nographics): -executeMethod StairShots.RenderFromCommandLine
/// </summary>
public static class StairShots
{
    [MenuItem("Tools/Routes/Debug/Render Stair Shots")]
    public static void RenderFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/OnlineFirstFloor.unity", OpenSceneMode.Single);
        Physics.SyncTransforms();
        string folder = Path.Combine(Application.dataPath, "../Logs/stair_shots");
        Directory.CreateDirectory(folder);

        GameObject camObject = new("Stair Shot Camera");
        UnityEngine.Camera cam = camObject.AddComponent<UnityEngine.Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        RenderTexture target = new(900, 700, 24);
        cam.targetTexture = target;

        int count = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            string n = t.name;
            if (!(n.StartsWith("Barrier - ") || n == "Gold Stair Door" || n == "R2_Shutter (blocker)" ||
                  n == "Boarded Spots" || n == "R5_ChainLock" || n == "R3_LadderSpot"))
                continue;

            Vector3 center = t.position;
            if (n == "R3_LadderSpot")
            {
                // Show the finished ladder.
                foreach (Transform part in t.GetComponentsInChildren<Transform>(true))
                    if (part.name.StartsWith("Stage"))
                        part.gameObject.SetActive(true);
            }
            string safe = n.Replace(" ", "_").Replace("(", "").Replace(")", "");

            // Top-down: walls/floor around the stairwell, blockers visible from above.
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            camObject.transform.SetPositionAndRotation(new Vector3(center.x, 2.9f, center.z), Quaternion.Euler(90f, 0f, 0f));
            Save(cam, target, Path.Combine(folder, $"{safe}_top.png"));

            // Eye level, looking at each blocker panel from 3.5 m in front of it.
            int panel = 0;
            foreach (Transform child in t)
            {
                if (child.GetComponentInChildren<Renderer>() == null)
                    continue;
                // Blockers face the stairs, so the player stands on the other side.
                Vector3 back = n == "R5_ChainLock" || n == "R3_LadderSpot" ? child.forward : -child.forward;
                Vector3 eye = child.position + back * 3.5f + Vector3.up * 1.45f;
                // Step closer until the camera is not inside a wall (no closer than 0.8 m).
                for (float distance = 3.5f; distance >= 0.8f; distance -= 0.3f)
                {
                    eye = child.position + back * distance + Vector3.up * 1.45f;
                    if (!Physics.CheckSphere(eye, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        !Physics.Linecast(eye, child.position + Vector3.up * 1.0f + back * 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        break;
                }
                cam.orthographic = false;
                cam.fieldOfView = 65f;
                cam.farClipPlane = 60f;
                camObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(child.position + Vector3.up * 1.0f - eye));
                Save(cam, target, Path.Combine(folder, $"{safe}_eye{panel}.png"));
                panel++;
            }

            count++;
        }

        Object.DestroyImmediate(camObject);
        Debug.Log($"[StairShots] rendered {count} stair blockers into Logs/stair_shots");
    }

    /// <summary>Debug: night lighting views (spawn, corridor lamps, a room, looking up) into Logs/atmosphere_shots.</summary>
    [MenuItem("Tools/Routes/Debug/Render Atmosphere Shots")]
    public static void RenderAtmosphere()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/OnlineFirstFloor.unity", OpenSceneMode.Single);
        Physics.SyncTransforms();
        string folder = Path.Combine(Application.dataPath, "../Logs/atmosphere_shots");
        Directory.CreateDirectory(folder);
        GameObject camObject = new("Atmosphere Camera");
        UnityEngine.Camera cam = camObject.AddComponent<UnityEngine.Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 70f;
        cam.farClipPlane = 120f;
        RenderTexture target = new(1100, 700, 24);
        cam.targetTexture = target;

        void Shot(string name, Vector3 eye, Vector3 look)
        {
            camObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            Save(cam, target, Path.Combine(folder, name + ".png"));
        }

        GameObject spawn = GameObject.Find("Player Spawn Marker");
        if (spawn != null)
        {
            Vector3 eye = spawn.transform.position + Vector3.up * 1.6f;
            for (int i = 0; i < 4; i++)
                Shot($"spawn_{i * 90}", eye, eye + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward);
            Shot("spawn_up", eye, eye + new Vector3(0.3f, 1f, 0.2f));
        }

        int lamps = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            bool corridor = t.name.StartsWith("Corridor Lamp ") && !t.name.EndsWith("Light");
            bool room = t.name == "Room Lamp - Classroom 01" || t.name == "Room Lamp - Staff Room";
            if (!corridor && !room || corridor && lamps >= 4)
                continue;
            Vector3 floor = new(t.position.x, 0f, t.position.z);
            Vector3 best = floor + Vector3.up * 1.6f + Vector3.right * 0.5f;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                Vector3 eye = floor + Vector3.up * 1.6f + dir * 5f;
                if (!Physics.Linecast(floor + Vector3.up * 1.6f, eye, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    best = eye;
                    break;
                }
            }

            Shot(t.name.Replace(" ", "_"), best, floor + Vector3.up * 1.2f);
            if (corridor)
                lamps++;
        }

        Shot("outside_sky", new Vector3(-20f, 8f, 40f), new Vector3(-20f, 14f, 80f));

        // This round's floor fixes.
        Shot("fix_lift_hall_glass", new Vector3(-23.5f, 1.6f, 18.5f), new Vector3(-23.5f, 1.4f, 26f));
        Shot("fix_washroom_from_corridor", new Vector3(-27f, 1.6f, -7.6f), new Vector3(-29f, 1.2f, -13f));
        Shot("fix_washroom_inside", new Vector3(-25.2f, 1.7f, -10.4f), new Vector3(-31f, 1.0f, -15f));
        Shot("fix_corridor_end", new Vector3(-26f, 1.6f, -8.8f), new Vector3(-34f, 1.2f, -8.8f));
        Shot("fix_hole_east_from_corridor", new Vector3(-3f, 1.2f, 6.8f), new Vector3(-3f, 0.6f, 3.6f));
        Shot("fix_hole_west_from_lab", new Vector3(-9f, 1.2f, 0.5f), new Vector3(-9f, 0.6f, 3.6f));
        Shot("fix_stairs_c01", new Vector3(6.8f, 1.6f, 14.8f), new Vector3(6.8f, 1.0f, 17f));
        Shot("fix_stairs_west", new Vector3(-54f, 1.6f, 1.5f), new Vector3(-53.5f, 0.8f, -2.6f));
        Shot("fix_spawn_stairs", new Vector3(-11f, 1.6f, 14.6f), new Vector3(-11f, 1.0f, 17f));
        Shot("fix_ladder", new Vector3(-25.5f, 1.6f, 15.6f), new Vector3(-28f, 0.6f, 13.6f));
        Shot("fix_atrium_fog", new Vector3(-25f, 1.7f, 15f), new Vector3(-25f, -4f, 8f));
        Shot("fix_atrium_down", new Vector3(-25f, 2.5f, 9f), new Vector3(-24f, -6f, 9.5f));
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!t.name.StartsWith("R1_KeyCabinet_") || t.parent == null || t.parent.name != "R1 Locksmith")
                continue;
            Vector3 cabinetCentre = t.GetComponentsInChildren<Renderer>().Select(r => r.bounds.center).Aggregate(Vector3.zero, (a, b) => a + b) /
                             Mathf.Max(1, t.GetComponentsInChildren<Renderer>().Length);
            // The wall is the nearest thing behind it: look at it from the other side.
            Vector3 wallward = Vector3.forward;
            float nearest = float.MaxValue;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                if (Physics.Raycast(cabinetCentre, dir, out RaycastHit hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    hit.distance < nearest && !hit.collider.transform.IsChildOf(t))
                {
                    nearest = hit.distance;
                    wallward = dir;
                }
            }

            Shot("cab_" + t.name, cabinetCentre - wallward * 1.8f + Vector3.up * 0.15f, cabinetCentre);
        }

        Object.DestroyImmediate(camObject);
        Debug.Log($"[StairShots] atmosphere shots written to Logs/atmosphere_shots");
    }

    private static void Save(UnityEngine.Camera cam, RenderTexture target, string path)
    {
        cam.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }
}
