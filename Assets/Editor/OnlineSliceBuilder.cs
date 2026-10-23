using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OnlineSliceBuilder
{
    private const string ScenePath = "Assets/Scenes/OnlineVerticalSlice.unity";
    private const string PrefabPath = "Assets/Prefabs/OnlinePlayer.prefab";

    [MenuItem("Tools/First Floor/Build Online Vertical Slice")]
    private static void BuildOnlineVerticalSlice()
    {
        if (!EditorUtility.DisplayDialog(
                "Build online vertical slice",
                "The current scene will be saved before the new test scene opens. Continue?",
                "Save and continue",
                "Cancel"))
            return;

        EditorSceneManager.SaveOpenScenes();

        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Editor");

        GameObject playerPrefab = BuildPlayerPrefab();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildRoom();
        BuildKeyPickup();
        NetworkLockedDoor door = BuildDoor();
        BuildExit(door);
        BuildLighting();
        BuildNetworkManager(playerPrefab);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Online vertical slice created",
            "Open Assets/Scenes/OnlineVerticalSlice and use the on-screen Host/Join panel. " +
            "The host plays Granny (WASD); the joining player uses the arrow keys.",
            "OK");
    }

    private static GameObject BuildPlayerPrefab()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "OnlinePlayer";
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());

        CharacterController characterController = player.AddComponent<CharacterController>();
        characterController.height = 2f;
        characterController.radius = 0.45f;
        characterController.center = Vector3.zero;

        player.AddComponent<NetworkObject>();
        player.AddComponent<OwnerNetworkTransform>();
        player.AddComponent<NetworkInventory>();
        NetworkInteractor interactor = player.AddComponent<NetworkInteractor>();
        NetworkPlayerController controller = player.AddComponent<NetworkPlayerController>();

        GameObject cameraObject = new GameObject("Player Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.68f, 0f);
        UnityEngine.Camera playerCamera = cameraObject.AddComponent<UnityEngine.Camera>();
        playerCamera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();

        SetObjectReference(controller, "playerCamera", playerCamera);
        SetObjectReference(interactor, "viewCamera", playerCamera);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
        Object.DestroyImmediate(player);
        return prefab;
    }

    private static void BuildRoom()
    {
        CreateCube("Room Floor", new Vector3(0f, -0.1f, 0f), new Vector3(10f, 0.2f, 10f), new Color(0.28f, 0.3f, 0.33f));
        CreateCube("Back Wall", new Vector3(0f, 1.5f, 5f), new Vector3(10f, 3f, 0.2f), new Color(0.67f, 0.68f, 0.7f));
        CreateCube("Left Wall", new Vector3(-5f, 1.5f, 0f), new Vector3(0.2f, 3f, 10f), new Color(0.67f, 0.68f, 0.7f));
        CreateCube("Right Wall", new Vector3(5f, 1.5f, 0f), new Vector3(0.2f, 3f, 10f), new Color(0.67f, 0.68f, 0.7f));
        CreateCube("Front Wall Left", new Vector3(-3f, 1.5f, -5f), new Vector3(4f, 3f, 0.2f), new Color(0.67f, 0.68f, 0.7f));
        CreateCube("Front Wall Right", new Vector3(3f, 1.5f, -5f), new Vector3(4f, 3f, 0.2f), new Color(0.67f, 0.68f, 0.7f));

        CreateCube("Granny Spawn Marker", new Vector3(-2f, 0.05f, 0f), new Vector3(0.7f, 0.1f, 0.7f), new Color(0.55f, 0.12f, 0.12f));
        CreateCube("Player Spawn Marker", new Vector3(2f, 0.05f, -2f), new Vector3(0.7f, 0.1f, 0.7f), new Color(0.12f, 0.4f, 0.75f));
    }

    private static void BuildKeyPickup()
    {
        GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cube);
        key.name = "Test Key - Pick Up With E";
        key.transform.position = new Vector3(0f, 0.55f, 2f);
        key.transform.localScale = new Vector3(0.35f, 0.15f, 0.6f);
        SetColor(key, new Color(1f, 0.72f, 0.08f));
        key.AddComponent<NetworkObject>();
        key.AddComponent<NetworkPickup>();
    }

    private static NetworkLockedDoor BuildDoor()
    {
        GameObject door = new GameObject("Locked Exit Door");
        door.transform.position = new Vector3(-1f, 0f, -5f);
        door.AddComponent<NetworkObject>();
        NetworkLockedDoor lockedDoor = door.AddComponent<NetworkLockedDoor>();

        GameObject hinge = new GameObject("Door Hinge");
        hinge.transform.SetParent(door.transform, false);

        GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.name = "Door Leaf";
        leaf.transform.SetParent(hinge.transform, false);
        leaf.transform.localPosition = new Vector3(0.9f, 1f, 0f);
        leaf.transform.localScale = new Vector3(1.8f, 2f, 0.18f);
        SetColor(leaf, new Color(0.35f, 0.2f, 0.1f));

        SetObjectReference(lockedDoor, "doorLeaf", hinge.transform);
        return lockedDoor;
    }

    private static void BuildExit(NetworkLockedDoor door)
    {
        GameObject exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
        exit.name = "Escape Point - Press E";
        exit.transform.position = new Vector3(0f, 0.8f, -6.7f);
        exit.transform.localScale = new Vector3(1.5f, 1.6f, 0.2f);
        SetColor(exit, new Color(0.12f, 0.65f, 0.25f));

        exit.AddComponent<NetworkObject>();
        NetworkEscapePoint escapePoint = exit.AddComponent<NetworkEscapePoint>();
        SetObjectReference(escapePoint, "requiredDoor", door);
    }

    private static void BuildLighting()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void BuildNetworkManager(GameObject playerPrefab)
    {
        GameObject managerObject = new GameObject("NetworkManager");
        UnityTransport transport = managerObject.AddComponent<UnityTransport>();
        NetworkManager manager = managerObject.AddComponent<NetworkManager>();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.TickRate = 30;
        manager.NetworkConfig.EnableSceneManagement = true;

        RelayConnectionUI connectionUI = managerObject.AddComponent<RelayConnectionUI>();
        SetObjectReference(connectionUI, "networkManager", manager);
    }

    private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        SetColor(cube, color);
        return cube;
    }

    private static void SetColor(GameObject target, Color color)
    {
        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer != null)
            targetRenderer.material.color = color;
    }

    private static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedTarget = new SerializedObject(target);
        SerializedProperty property = serializedTarget.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError($"Could not find serialized field '{propertyName}' on {target.name}.");
            return;
        }

        property.objectReferenceValue = value;
        serializedTarget.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
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
