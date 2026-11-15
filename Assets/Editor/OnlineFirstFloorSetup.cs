using System.Collections.Generic;
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

    [MenuItem("Tools/First Floor/Rebuild Approximate Collision Boxes (legacy)")]
    private static void RebuildSimpleCollisionProxies()
    {
        if (!EditorUtility.DisplayDialog(
                "Rebuild first-floor collision",
                "Legacy option: this replaces detailed collision with broad mesh-bounds boxes and can block doorways or extend into rooms. " +
                "Use Tools > First Floor > Use Exact Mesh Collision for the current first-floor scene instead.",
                "Rebuild collision",
                "Cancel"))
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        if (firstFloor == null)
        {
            EditorUtility.DisplayDialog("FirstFloor model not found",
                "The copied scene does not contain a root named FirstFloor. No collision was changed.", "OK");
            return;
        }

        GameObject oldProxyRoot = GameObject.Find("Online Collision Proxies");
        if (oldProxyRoot != null)
            Undo.DestroyObjectImmediate(oldProxyRoot);

        GameObject proxyRoot = new GameObject("Online Collision Proxies");
        Undo.RegisterCreatedObjectUndo(proxyRoot, "Create simple first-floor collision proxies");

        GameObject[] modelRoots = mainModel == null
            ? new[] { firstFloor }
            : new[] { firstFloor, mainModel };
        MeshFilter[] meshFilters = modelRoots
            .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
            .Where(filter => filter.sharedMesh != null && filter.GetComponent<Renderer>() != null)
            .ToArray();

        int proxyCount = 0;
        foreach (MeshFilter meshFilter in meshFilters)
        {
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 worldSize = Vector3.Scale(meshBounds.size, meshFilter.transform.lossyScale);
            if (worldSize.magnitude < 0.25f)
                continue;

            GameObject proxy = new GameObject("Collision - " + meshFilter.name);
            Undo.RegisterCreatedObjectUndo(proxy, "Create map collision proxy");
            proxy.transform.SetParent(proxyRoot.transform, false);
            proxy.transform.SetPositionAndRotation(meshFilter.transform.position, meshFilter.transform.rotation);
            proxy.transform.localScale = meshFilter.transform.lossyScale;

            BoxCollider box = proxy.AddComponent<BoxCollider>();
            box.center = meshBounds.center;
            box.size = new Vector3(
                Mathf.Max(meshBounds.size.x, 0.08f),
                Mathf.Max(meshBounds.size.y, 0.08f),
                Mathf.Max(meshBounds.size.z, 0.08f));
            proxyCount++;
        }

        if (proxyCount == 0)
        {
            Undo.DestroyObjectImmediate(proxyRoot);
            EditorUtility.DisplayDialog("No collision proxies created",
                "No renderable map meshes were found. The existing collision was left in place.", "OK");
            return;
        }

        int removedMeshColliders = 0;
        foreach (MeshFilter meshFilter in meshFilters)
        {
            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
            if (meshCollider == null)
                continue;

            Undo.DestroyObjectImmediate(meshCollider);
            removedMeshColliders++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorUtility.DisplayDialog("Simple collision rebuilt",
            $"Created {proxyCount} box collision proxies and removed {removedMeshColliders} mesh colliders in OnlineFirstFloor. " +
            "The original scenes were not touched. Playtest wall movement and crouch release; if a doorway is blocked, disable its corresponding Collision object in the Online Collision Proxies group.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Use Exact Mesh Collision (legacy)")]
    private static void UseExactMeshCollision()
    {
        if (!EditorUtility.DisplayDialog(
                "Use exact mesh collision",
                "Legacy option: per-renderer MeshColliders can trap the CharacterController at mesh seams. " +
                "Use Restore Stable Box Collision instead. This affects OnlineFirstFloor only.",
                "Repair collision",
                "Cancel"))
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        if (firstFloor == null)
        {
            EditorUtility.DisplayDialog("FirstFloor model not found",
                "OnlineFirstFloor has no root named FirstFloor. No collision was changed.", "OK");
            return;
        }

        GameObject proxyRoot = GameObject.Find("Online Collision Proxies");
        int disabledProxyCount = 0;
        if (proxyRoot != null)
        {
            foreach (BoxCollider proxy in proxyRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                // Door blocker boxes are deliberately retained and are toggled by NetworkLockedDoor.
                if (proxy.name.StartsWith("Collision - Door", System.StringComparison.Ordinal))
                    continue;

                // This also disables legacy carved wall segments, which otherwise overlap the exact mesh collider.
                Undo.RecordObject(proxy, "Disable approximate collision proxy");
                if (proxy.enabled)
                {
                    proxy.enabled = false;
                    disabledProxyCount++;
                }
            }
        }

        int meshCollidersAdded = 0;
        int meshCollidersRefreshed = 0;
        GameObject[] roots = mainModel == null ? new[] { firstFloor } : new[] { firstFloor, mainModel };
        foreach (MeshFilter filter in roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null || IsMovingDoorPart(filter.transform))
                continue;

            MeshCollider meshCollider = filter.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                // Keep pre-existing fitted primitive colliders where supplied by the model.
                if (filter.GetComponent<Collider>() != null)
                    continue;
                meshCollider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                meshCollidersAdded++;
            }
            else
            {
                Undo.RecordObject(meshCollider, "Refresh exact mesh collision");
                meshCollidersRefreshed++;
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = filter.sharedMesh;
            meshCollider.convex = false;
            meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                          MeshColliderCookingOptions.EnableMeshCleaning |
                                          MeshColliderCookingOptions.WeldColocatedVertices;
        }

        // Door blockers can be outside the FirstFloor/Main model roots, so collect them from the configured doors
        // and ensure they remain enabled in the closed pose. NetworkLockedDoor disables them after opening.
        foreach (NetworkLockedDoor door in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<NetworkLockedDoor>(true)))
        {
            SerializedObject serializedDoor = new SerializedObject(door);
            SerializedProperty blockers = serializedDoor.FindProperty("doorBlockers");
            if (blockers == null)
                continue;

            for (int i = 0; i < blockers.arraySize; i++)
            {
                Collider blocker = blockers.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
                if (blocker != null && blocker.name.StartsWith("Collision - Door", System.StringComparison.Ordinal))
                    blocker.enabled = true;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Collision repaired",
            $"Added {meshCollidersAdded} exact mesh colliders and refreshed {meshCollidersRefreshed}. Disabled {disabledProxyCount} approximate collision boxes and legacy doorway segments; door blocker boxes were kept. OnlineFirstFloor was saved. Playtest entering rooms and approaching furniture.",
            "OK");
    }

    private static bool IsMovingDoorPart(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            if (current.name.StartsWith("Door_Group", System.StringComparison.Ordinal) ||
                current.name.StartsWith("DoorFrame", System.StringComparison.Ordinal) ||
                current.name.StartsWith("Door.", System.StringComparison.Ordinal) ||
                current.name.StartsWith("DoorHinge", System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsWallProxyName(string objectName)
    {
        return objectName.StartsWith("Collision - Plane", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Floor", System.StringComparison.Ordinal);
    }

    [MenuItem("Tools/First Floor/Restore Stable Box Collision")]
    private static void RestoreStableBoxCollision()
    {
        if (!EditorUtility.DisplayDialog(
                "Restore stable collision",
                "This disables the per-mesh colliders that are trapping the characters and restores the existing box collision proxies. " +
                "Previously carved doorway segments stay enabled so the doors remain passable. OnlineFirstFloor only will be changed.",
                "Restore collision",
                "Cancel"))
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        GameObject proxyRoot = GameObject.Find("Online Collision Proxies");
        if (firstFloor == null || proxyRoot == null)
        {
            EditorUtility.DisplayDialog("Collision objects missing",
                "OnlineFirstFloor needs FirstFloor and Online Collision Proxies. No collision was changed.", "OK");
            return;
        }

        int disabledMeshColliders = 0;
        GameObject[] roots = mainModel == null ? new[] { firstFloor } : new[] { firstFloor, mainModel };
        foreach (MeshCollider meshCollider in roots
                     .SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true)))
        {
            Undo.RecordObject(meshCollider, "Disable snagging mesh collision");
            if (meshCollider.enabled)
            {
                meshCollider.enabled = false;
                disabledMeshColliders++;
            }
        }

        BoxCollider[] proxies = proxyRoot.GetComponentsInChildren<BoxCollider>(true);
        int enabledBoxes = 0;
        int keptCarvedWallsDisabled = 0;
        foreach (BoxCollider box in proxies)
        {
            bool wallWithCarvedOpenings = IsWallProxyName(box.name) &&
                box.GetComponentsInChildren<BoxCollider>(true)
                    .Any(child => child != box && child.name.StartsWith("Doorway Collision Segment", System.StringComparison.Ordinal));

            Undo.RecordObject(box, "Restore stable box collision proxy");
            bool shouldBeEnabled = !wallWithCarvedOpenings;
            if (box.enabled != shouldBeEnabled)
            {
                box.enabled = shouldBeEnabled;
                if (shouldBeEnabled)
                    enabledBoxes++;
                else if (IsWallProxyName(box.name))
                    keptCarvedWallsDisabled++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            EditorUtility.DisplayDialog("Save failed",
                "The stable collision setup was applied in memory but could not be saved. Save OnlineFirstFloor before building.", "OK");
            return;
        }

        EditorUtility.DisplayDialog("Stable collision restored",
            $"Disabled {disabledMeshColliders} mesh colliders, enabled {enabledBoxes} simple collision boxes, and kept {keptCarvedWallsDisabled} carved wall boxes disabled. Doorway segments and door blockers are enabled. OnlineFirstFloor was saved.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Create Room Traversal Test Copy")]
    private static void CreateRoomTraversalTestCopy()
    {
        Scene sourceScene = SceneManager.GetActiveScene();
        if (!sourceScene.name.Equals("OnlineFirstFloor", System.StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Open OnlineFirstFloor",
                "Open OnlineFirstFloor first. This creates a separate test-scene copy and leaves the source scene unchanged.", "OK");
            return;
        }

        if (sourceScene.isDirty)
        {
            EditorUtility.DisplayDialog("Save OnlineFirstFloor first",
                "Save OnlineFirstFloor, then run this command again. The test copy is made from the saved scene so no pending edits are lost.", "OK");
            return;
        }

        const string desiredTestPath = "Assets/Scenes/OnlineFirstFloor_RoomTraversalTest.unity";
        string testPath = AssetDatabase.GenerateUniqueAssetPath(desiredTestPath);
        if (!AssetDatabase.CopyAsset(sourceScene.path, testPath))
        {
            EditorUtility.DisplayDialog("Could not create test copy", "Unity could not copy OnlineFirstFloor. The original scene was not changed.", "OK");
            return;
        }

        AssetDatabase.Refresh();
        Scene testScene = EditorSceneManager.OpenScene(testPath, OpenSceneMode.Single);
        GameObject proxyRoot = GameObject.Find("Online Collision Proxies");
        if (proxyRoot == null)
        {
            EditorUtility.DisplayDialog("Collision proxies missing",
                $"Created {testPath}, but it has no Online Collision Proxies root. The original scene was not changed.", "OK");
            return;
        }

        int disabledPropBoxes = 0;
        int preservedStructureBoxes = 0;
        foreach (BoxCollider proxy in proxyRoot.GetComponentsInChildren<BoxCollider>(true))
        {
            if (IsTraversalStructureProxy(proxy.name))
            {
                preservedStructureBoxes++;
                continue;
            }

            if (proxy.enabled)
            {
                Undo.RecordObject(proxy, "Disable approximate prop collider in test copy");
                proxy.enabled = false;
                disabledPropBoxes++;
            }
        }

        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        GameObject[] roots = mainModel == null ? new[] { firstFloor } : new[] { firstFloor, mainModel };
        int disabledMeshColliders = 0;
        foreach (MeshCollider meshCollider in roots
                     .Where(root => root != null)
                     .SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true)))
        {
            if (!meshCollider.enabled)
                continue;

            Undo.RecordObject(meshCollider, "Disable mesh collider in room traversal test copy");
            meshCollider.enabled = false;
            disabledMeshColliders++;
        }

        int restoredFloorBoxes = RestoreTestFloorSupport(proxyRoot);
        EditorSceneManager.MarkSceneDirty(testScene);
        bool safeSpawnsSet = TryPlaceTestSpawnsOnFloor(proxyRoot, out string spawnMessage);
        EnsureSceneInBuildSettings(testPath);
        if (!EditorSceneManager.SaveScene(testScene, testPath))
        {
            EditorUtility.DisplayDialog("Test copy save failed",
                $"The test copy was created at {testPath}, but Unity could not save its collision changes. OnlineFirstFloor remains unchanged.", "OK");
            return;
        }

        EditorUtility.DisplayDialog(safeSpawnsSet ? "Room traversal test ready" : "Test copy needs safe spawn points",
            $"Opened a separate test scene: {testPath}. Disabled {disabledPropBoxes} approximate prop/rail boxes and {disabledMeshColliders} mesh colliders; kept {preservedStructureBoxes} structural boxes and restored {restoredFloorBoxes} horizontal floor colliders. {spawnMessage} OnlineFirstFloor is unchanged.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Repair Room Traversal Test Spawns")]
    private static void RepairRoomTraversalTestSpawns()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path.IndexOf("RoomTraversalTest", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            EditorUtility.DisplayDialog("Open the traversal test copy",
                "This repair only changes a scene whose path contains RoomTraversalTest. Open that test copy first; OnlineFirstFloor will not be changed.", "OK");
            return;
        }

        GameObject proxyRoot = GameObject.Find("Online Collision Proxies");
        if (proxyRoot == null)
        {
            EditorUtility.DisplayDialog("Could not find safe spawn points",
                "Online Collision Proxies is missing from this test copy. The original OnlineFirstFloor remains unchanged.", "OK");
            return;
        }

        int restoredFloorBoxes = RestoreTestFloorSupport(proxyRoot);
        if (!TryPlaceTestSpawnsOnFloor(proxyRoot, out string message))
        {
            EditorUtility.DisplayDialog("Could not find safe spawn points",
                $"No clear, supported floor positions were found after restoring {restoredFloorBoxes} horizontal floor boxes. The original OnlineFirstFloor remains unchanged. Do not press Play until the test copy has walkable floors.", "OK");
            return;
        }

        EnsureSceneInBuildSettings(scene.path);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, scene.path))
        {
            EditorUtility.DisplayDialog("Save failed", "Spawn positions were adjusted in memory but could not be saved. Save the test copy before playing.", "OK");
            return;
        }

        EditorUtility.DisplayDialog("Test spawns repaired",
            $"Restored {restoredFloorBoxes} horizontal floor colliders (box proxies and model floor meshes), moved both spawn markers onto clear supported floor, and added the test scene to Build Settings for Netcode. {message} OnlineFirstFloor was not changed.",
            "OK");
    }

    private static int RestoreTestFloorSupport(GameObject proxyRoot)
    {
        int restored = 0;
        foreach (BoxCollider box in proxyRoot.GetComponentsInChildren<BoxCollider>(true))
        {
            if (box == null || !IsTraversalFloorProxy(box.name))
                continue;

            Bounds bounds = GetColliderWorldBounds(box);
            // Walkable slabs are broad in X/Z and thin in world Y; vertical walls remain untouched.
            if (bounds.size.y > 0.45f || bounds.size.x < 1.1f || bounds.size.z < 1.1f)
                continue;

            BoxCollider[] generatedSegments = box.GetComponentsInChildren<BoxCollider>(true)
                .Where(child => child != box &&
                                child.name.StartsWith("Doorway Collision Segment", System.StringComparison.Ordinal))
                .ToArray();
            foreach (BoxCollider segment in generatedSegments)
            {
                if (!segment.enabled)
                    continue;

                Undo.RecordObject(segment, "Restore continuous floor in traversal test");
                segment.enabled = false;
            }

            if (!box.enabled)
            {
                Undo.RecordObject(box, "Restore floor support in traversal test");
                box.enabled = true;
                restored++;
            }
        }

        // The room-test profile disables model MeshColliders to avoid snagging on wall seams.
        // Keep broad, thin horizontal model colliders enabled as a fallback where the box-proxy
        // set does not cover the floor continuously.
        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        GameObject[] modelRoots = mainModel == null ? new[] { firstFloor } : new[] { firstFloor, mainModel };
        foreach (MeshCollider meshCollider in modelRoots
                     .Where(root => root != null)
                     .SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true)))
        {
            if (!IsHorizontalFloorSurface(GetColliderWorldBounds(meshCollider)))
                continue;

            if (!meshCollider.enabled)
            {
                Undo.RecordObject(meshCollider, "Restore horizontal floor mesh in traversal test");
                meshCollider.enabled = true;
                restored++;
            }
        }

        Physics.SyncTransforms();
        return restored;
    }

    private static bool TryPlaceTestSpawnsOnFloor(GameObject proxyRoot, out string message)
    {
        message = "";
        Transform grannyMarker = GameObject.Find("Granny Spawn Marker")?.transform;
        Transform playerMarker = GameObject.Find("Player Spawn Marker")?.transform;
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        CharacterController characterController = playerPrefab != null
            ? playerPrefab.GetComponent<CharacterController>()
            : null;
        if (grannyMarker == null || playerMarker == null || characterController == null)
            return false;

        Physics.SyncTransforms();
        Vector3 grannyTarget = grannyMarker.position;
        Vector3 playerTarget = playerMarker.position;
        Vector3 prefabScale = playerPrefab.transform.lossyScale;
        float worldHeight = characterController.height * Mathf.Abs(prefabScale.y);
        float worldRadius = characterController.radius * Mathf.Max(Mathf.Abs(prefabScale.x), Mathf.Abs(prefabScale.z));
        float skinWidth = characterController.skinWidth * Mathf.Abs(prefabScale.y);
        float spawnLift = worldHeight * 0.5f + skinWidth + 0.025f;
        var safePoints = new List<Vector3>();

        GameObject firstFloor = GameObject.Find("FirstFloor");
        GameObject mainModel = GameObject.Find("Main");
        GameObject[] modelRoots = mainModel == null ? new[] { firstFloor } : new[] { firstFloor, mainModel };
        Collider[] allFloorLikeColliders = proxyRoot.GetComponentsInChildren<BoxCollider>(true)
            .Where(box => box != null && box.enabled && box.gameObject.activeInHierarchy && IsTraversalFloorProxy(box.name))
            .Cast<Collider>()
            .Concat(modelRoots
                .Where(root => root != null)
                .SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true))
                .Where(mesh => mesh != null && mesh.enabled && mesh.gameObject.activeInHierarchy &&
                               IsHorizontalFloorSurface(GetColliderWorldBounds(mesh))))
            .Where(collider => IsHorizontalFloorSurface(GetColliderWorldBounds(collider)))
            .Distinct()
            .ToArray();

        Collider[] namedFloorBoxes = allFloorLikeColliders
            .Where(collider => collider.name.StartsWith("Collision - Floor", System.StringComparison.Ordinal))
            .ToArray();
        CollectSafeSpawnPoints(namedFloorBoxes, spawnLift, worldHeight, worldRadius, characterController.center,
            prefabScale, safePoints);
        if (safePoints.Count < 2)
            CollectSafeSpawnPoints(allFloorLikeColliders, spawnLift, worldHeight, worldRadius, characterController.center,
                prefabScale, safePoints);

        if (safePoints.Count < 2)
        {
            message = "No safe point was found on active floor box or mesh colliders. Do not press Play yet; inspect the test copy's floor collider coverage.";
            return false;
        }

        safePoints = safePoints
            .GroupBy(point => new Vector3Int(Mathf.RoundToInt(point.x * 2f), Mathf.RoundToInt(point.y * 2f), Mathf.RoundToInt(point.z * 2f)))
            .Select(group => group.First())
            .ToList();

        Vector3 grannySpawn = Vector3.zero;
        Vector3 playerSpawn = Vector3.zero;
        bool foundPair = false;
        foreach (Vector3 candidate in safePoints.OrderBy(point => HorizontalDistanceSquared(point, grannyTarget)))
        {
            Vector3? other = safePoints
                .Where(point => Mathf.Abs(point.y - candidate.y) < 0.25f &&
                                HorizontalDistanceSquared(point, candidate) >= 1.8f)
                .OrderBy(point => HorizontalDistanceSquared(point, playerTarget))
                .Select(point => (Vector3?)point)
                .FirstOrDefault();
            if (!other.HasValue)
                continue;

            grannySpawn = candidate;
            playerSpawn = other.Value;
            foundPair = true;
            break;
        }

        if (!foundPair)
        {
            message = "A clear floor was found, but not two safe spawn positions on the same level.";
            return false;
        }

        Undo.RecordObject(grannyMarker, "Move Granny test spawn onto floor");
        Undo.RecordObject(playerMarker, "Move Player test spawn onto floor");
        grannyMarker.position = grannySpawn;
        playerMarker.position = playerSpawn;
        EditorUtility.SetDirty(grannyMarker);
        EditorUtility.SetDirty(playerMarker);

        GameObject networkManagerObject = GameObject.Find("NetworkManager");
        NetworkManager manager = networkManagerObject != null ? networkManagerObject.GetComponent<NetworkManager>() : null;
        if (manager != null)
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty config = serializedManager.FindProperty("NetworkConfig");
            SerializedProperty sceneManagement = config?.FindPropertyRelative("EnableSceneManagement");
            if (sceneManagement != null)
            {
                sceneManagement.boolValue = true;
                serializedManager.ApplyModifiedProperties();
            }
        }

        message = $"Spawn points: Granny ({grannySpawn.x:F1}, {grannySpawn.y:F1}, {grannySpawn.z:F1}); Player ({playerSpawn.x:F1}, {playerSpawn.y:F1}, {playerSpawn.z:F1}).";
        return true;
    }

    private static void CollectSafeSpawnPoints(Collider[] floorBoxes, float spawnLift, float worldHeight,
        float worldRadius, Vector3 controllerCenter, Vector3 prefabScale, List<Vector3> safePoints)
    {
        foreach (Collider floor in floorBoxes)
        {
            Bounds bounds = GetColliderWorldBounds(floor);
            float inset = worldRadius + 0.12f;
            float availableX = bounds.size.x - inset * 2f;
            float availableZ = bounds.size.z - inset * 2f;
            if (availableX <= 0f || availableZ <= 0f)
                continue;

            int xSteps = Mathf.Max(1, Mathf.CeilToInt(availableX / 0.65f));
            int zSteps = Mathf.Max(1, Mathf.CeilToInt(availableZ / 0.65f));
            for (int xIndex = 0; xIndex <= xSteps; xIndex++)
            {
                float x = bounds.min.x + inset + availableX * xIndex / xSteps;
                for (int zIndex = 0; zIndex <= zSteps; zIndex++)
                {
                    float z = bounds.min.z + inset + availableZ * zIndex / zSteps;
                    Vector3 rayOrigin = new Vector3(x, bounds.max.y + 0.2f, z);
                    if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                            Mathf.Max(0.6f, bounds.size.y + 0.5f), Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore) || hit.collider != floor || hit.normal.y < 0.85f)
                        continue;

                    Vector3 spawn = new Vector3(x, hit.point.y + spawnLift, z);
                    Vector3 worldCenter = spawn + Vector3.Scale(controllerCenter, prefabScale);
                    Vector3 lowerSphere = worldCenter + Vector3.up * (-worldHeight * 0.5f + worldRadius);
                    Vector3 upperSphere = worldCenter + Vector3.up * (worldHeight * 0.5f - worldRadius);
                    Collider[] overlaps = Physics.OverlapCapsule(lowerSphere, upperSphere, worldRadius * 0.92f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    bool obstructed = overlaps.Any(overlap => overlap != floor &&
                        !(IsHorizontalFloorSurface(GetColliderWorldBounds(overlap)) &&
                          GetColliderWorldBounds(overlap).max.y <= hit.point.y + 0.04f));
                    if (!obstructed)
                        safePoints.Add(spawn);
                }
            }
        }
    }

    private static bool IsTraversalFloorProxy(string objectName)
    {
        return objectName.StartsWith("Collision - Floor", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Plane", System.StringComparison.Ordinal);
    }

    private static bool IsHorizontalFloorSurface(Bounds bounds)
    {
        return bounds.size.y <= 0.45f && bounds.size.x >= 1.1f && bounds.size.z >= 1.1f;
    }

    private static Bounds GetColliderWorldBounds(Collider collider)
    {
        if (collider == null)
            return new Bounds();

        if (collider.enabled && collider.gameObject.activeInHierarchy)
            return collider.bounds;

        Bounds localBounds;
        if (collider is BoxCollider box)
        {
            localBounds = new Bounds(box.center, box.size);
        }
        else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            localBounds = meshCollider.sharedMesh.bounds;
        }
        else
        {
            return collider.bounds;
        }

        Transform colliderTransform = collider.transform;
        Vector3 firstCorner = colliderTransform.TransformPoint(localBounds.min);
        Bounds worldBounds = new Bounds(firstCorner, Vector3.zero);
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 corner = new Vector3(
                x == 0 ? localBounds.min.x : localBounds.max.x,
                y == 0 ? localBounds.min.y : localBounds.max.y,
                z == 0 ? localBounds.min.z : localBounds.max.z);
            worldBounds.Encapsulate(colliderTransform.TransformPoint(corner));
        }

        return worldBounds;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return (first - second).sqrMagnitude;
    }

    private static void EnsureSceneInBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        if (existing.Any(entry => entry.path.Equals(scenePath, System.StringComparison.OrdinalIgnoreCase)))
            return;

        EditorBuildSettings.scenes = existing
            .Concat(new[] { new EditorBuildSettingsScene(scenePath, true) })
            .ToArray();
    }

    private static bool IsTraversalStructureProxy(string objectName)
    {
        return objectName.StartsWith("Collision - Plane", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Floor", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Stairs", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Door", System.StringComparison.Ordinal) ||
               objectName.StartsWith("Doorway Collision Segment", System.StringComparison.Ordinal);
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
