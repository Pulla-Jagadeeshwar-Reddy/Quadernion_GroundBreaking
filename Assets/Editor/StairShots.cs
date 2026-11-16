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
