using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Configures only the OnlineFirstFloor copy for the online movement prototype.</summary>
public static class OnlineFirstFloorSetup
{
    private const string ScenePath = "Assets/Scenes/OnlineFirstFloor.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/OnlinePlayer.prefab";
    private const string LegacyPlayerName = "Player";
    private const string LegacyCameraName = "Main Camera";

    [MenuItem("Tools/First Floor/Configure Online First Floor")]
    private static void ConfigureOnlineFirstFloor()
    {
        if (!EditorUtility.DisplayDialog(
                "Configure Online First Floor",
                "This configures the OnlineFirstFloor copy only. It adds collision to the imported map meshes, " +
                "adds online spawn markers and the Relay host/join manager, and disables the old offline player/camera. " +
                "FirstFloor_Assembly and OnlineVerticalSlice are not changed.",
                "Configure copy",
                "Cancel"))
            return;

        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
        {
            EditorUtility.DisplayDialog("Scene not found", $"Create or save {ScenePath} first.", "OK");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        if (firstFloor == null)
        {
            EditorUtility.DisplayDialog("FirstFloor model not found",
                "The copied scene does not contain a root named FirstFloor. No setup was applied.", "OK");
            return;
        }

        DisableLegacyOfflineObjects();
        int collidersConfigured = ConfigureStaticMeshColliders(firstFloor);
        if (mainModel != null)
            collidersConfigured += ConfigureStaticMeshColliders(mainModel);

        Bounds mapBounds = GetMapBounds(firstFloor, mainModel);
        CreateOrUpdateMarker("Granny Spawn Marker",
            new Vector3(mapBounds.center.x - 1.5f, mapBounds.min.y + 1.05f, mapBounds.center.z),
            Quaternion.LookRotation(Vector3.forward), new Color(0.8f, 0.15f, 0.15f));
        CreateOrUpdateMarker("Player Spawn Marker",
            new Vector3(mapBounds.center.x + 1.5f, mapBounds.min.y + 1.05f, mapBounds.center.z),
            Quaternion.LookRotation(Vector3.back), new Color(0.15f, 0.45f, 0.9f));

        EnsureNetworkManager();
        AddSceneToBuildSettings(ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Online first-floor setup complete",
            $"Configured {ScenePath}. Added or refreshed {collidersConfigured} MeshCollider component(s). " +
            "The spawn markers are at the map bounds center; inspect and move them in Scene view to safe, open floor locations before playtesting.",
            "OK");
    }

    private static void DisableLegacyOfflineObjects()
    {
        GameObject oldPlayer = GameObject.Find(LegacyPlayerName);
        if (oldPlayer != null && oldPlayer.GetComponent<NetworkObject>() == null)
        {
            Undo.RecordObject(oldPlayer, "Disable old offline player in online scene");
            oldPlayer.SetActive(false);
        }

        GameObject oldCamera = GameObject.Find(LegacyCameraName);
        if (oldCamera != null)
        {
            Camera camera = oldCamera.GetComponent<Camera>();
            if (camera != null)
            {
                Undo.RecordObject(camera, "Disable old camera in online scene");
                camera.enabled = false;
            }

            AudioListener listener = oldCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                Undo.RecordObject(listener, "Disable old audio listener in online scene");
                listener.enabled = false;
            }
        }
    }

    private static int ConfigureStaticMeshColliders(GameObject root)
    {
        int configured = 0;
        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null)
                continue;

            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                if (meshFilter.GetComponent<Collider>() != null)
                    continue;
                meshCollider = Undo.AddComponent<MeshCollider>(meshFilter.gameObject);
            }

            Undo.RecordObject(meshCollider, "Configure first-floor mesh collider");
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                         MeshColliderCookingOptions.EnableMeshCleaning |
                                         MeshColliderCookingOptions.WeldColocatedVertices;
            configured++;
        }

        return configured;
    }

    private static Bounds GetMapBounds(params GameObject[] roots)
    {
        Renderer[] renderers = roots
            .Where(root => root != null)
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .ToArray();

        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f));

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void CreateOrUpdateMarker(string markerName, Vector3 position, Quaternion rotation, Color color)
    {
        GameObject marker = GameObject.Find(markerName);
        bool created = marker == null;
        if (marker == null)
        {
            marker = new GameObject(markerName);
            Undo.RegisterCreatedObjectUndo(marker, $"Create {markerName}");
        }

        if (created)
        {
            Undo.RecordObject(marker.transform, $"Place {markerName}");
            marker.transform.SetPositionAndRotation(position, rotation);
        }
        OnlineSpawnMarker markerVisualization = marker.GetComponent<OnlineSpawnMarker>();
        if (markerVisualization == null)
            markerVisualization = Undo.AddComponent<OnlineSpawnMarker>(marker);
        SerializedObject serializedMarker = new SerializedObject(markerVisualization);
        serializedMarker.FindProperty("gizmoColor").colorValue = color;
        serializedMarker.ApplyModifiedProperties();
    }

    private static void EnsureNetworkManager()
    {
        GameObject managerObject = GameObject.Find("NetworkManager");
        if (managerObject == null)
        {
            managerObject = new GameObject("NetworkManager");
            Undo.RegisterCreatedObjectUndo(managerObject, "Create first-floor NetworkManager");
        }

        UnityTransport transport = managerObject.GetComponent<UnityTransport>();
        if (transport == null)
            transport = Undo.AddComponent<UnityTransport>(managerObject);

        NetworkManager manager = managerObject.GetComponent<NetworkManager>();
        if (manager == null)
            manager = Undo.AddComponent<NetworkManager>(managerObject);

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogError($"Online player prefab not found at {PlayerPrefabPath}; NetworkManager was added without a player prefab.");
            return;
        }

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.TickRate = 30;
        manager.NetworkConfig.EnableSceneManagement = true;

        RelayConnectionUI connectionUI = managerObject.GetComponent<RelayConnectionUI>();
        if (connectionUI == null)
            connectionUI = Undo.AddComponent<RelayConnectionUI>(managerObject);

        SerializedObject serializedUI = new SerializedObject(connectionUI);
        serializedUI.FindProperty("networkManager").objectReferenceValue = manager;
        serializedUI.ApplyModifiedProperties();
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        EditorBuildSettingsScene target = scenes.FirstOrDefault(scene => scene.path == scenePath);
        if (target == null)
            target = new EditorBuildSettingsScene(scenePath, true);
        else
            target.enabled = true;

        EditorBuildSettings.scenes = new[] { target }
            .Concat(scenes.Where(scene => scene.path != scenePath))
            .ToArray();
    }
}
