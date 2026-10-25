using System;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OnlineDoorSetup
{
    private const string OnlineSceneName = "OnlineFirstFloor";
    private const string CollisionProxyRootName = "Online Collision Proxies";
    private const string DoorGroupPrefix = "Door_Group.";

    [MenuItem("Tools/First Floor/Make Selected Door Networked")]
    private static void MakeSelectedDoorNetworked()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Select a door", "Select the door leaf or its Door_Group object in OnlineFirstFloor first.", "OK");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Equals(OnlineSceneName, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Wrong scene",
                "This tool only changes the OnlineFirstFloor scene. Open that copy and select a moving door leaf.", "OK");
            return;
        }

        NetworkLockedDoor door = selected.GetComponent<NetworkLockedDoor>();
        if (!EditorUtility.DisplayDialog("Configure networked door",
                "This adds or refreshes the NetworkObject and NetworkLockedDoor on the selected door object. " +
                "It finds a child named Door.* as the moving leaf, links a matching DoorHinge* pivot and " +
                "'Collision - <door name>' proxy when present. New doors are free-use for both roles; existing access settings are preserved.",
                "Configure selected door", "Cancel"))
            return;

        ConfigureDoor(selected);

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorUtility.SetDirty(selected);
        Selection.activeGameObject = selected;
        EditorUtility.DisplayDialog("Door configured",
            "The selected door now has networked open/close control. Check the Inspector: assign or position a hinge pivot if needed, then save OnlineFirstFloor.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Configure All First-Floor Doors")]
    private static void ConfigureAllDoors()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Equals(OnlineSceneName, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Wrong scene",
                "Open OnlineFirstFloor before configuring its doors.", "OK");
            return;
        }

        Transform[] sceneTransforms = activeScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .ToArray();
        var doorObjects = new System.Collections.Generic.HashSet<GameObject>();
        foreach (Transform group in sceneTransforms.Where(candidate =>
                     candidate.name.StartsWith(DoorGroupPrefix, StringComparison.Ordinal)))
        {
            if (FindMovingLeaf(group.gameObject) != group)
                doorObjects.Add(group.gameObject);
        }

        foreach (Transform candidate in sceneTransforms)
        {
            if (candidate.GetComponent<NetworkLockedDoor>() != null)
                doorObjects.Add(candidate.gameObject);
        }

        Transform[] doors = doorObjects.Select(gameObject => gameObject.transform).ToArray();
        if (doors.Length == 0)
        {
            EditorUtility.DisplayDialog("No doors found",
                "No Door_Group.* objects with a child named Door.* were found in OnlineFirstFloor.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Configure all first-floor doors",
                $"Configure {doors.Length} first-floor doors while keeping existing hinge objects? Right-handle doors use (0, 0, -90), left-handle doors use (0, 0, 90). Duplicate NetworkLockedDoor components on the same object will be removed.",
                "Configure all", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Configure all first-floor doors");
        int configured = 0;
        int missingHinge = 0;
        int rightHandleDoors = 0;
        int leftHandleDoors = 0;
        int duplicateComponents = 0;
        foreach (Transform group in doors)
        {
            Transform leaf = FindMovingLeaf(group.gameObject);
            if (FindOrCreateHingePivot(leaf) == null)
            {
                missingHinge++;
                Debug.LogWarning($"[OnlineDoorSetup] Could not create a hinge pivot for {group.name}; check that its door mesh has a Renderer.", group.gameObject);
            }

            duplicateComponents += Mathf.Max(0, group.GetComponents<NetworkLockedDoor>().Length - 1);
            if (ConfigureDoor(group.gameObject))
                rightHandleDoors++;
            else
                leftHandleDoors++;
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.objects = doors.Select(group => group.gameObject).ToArray();
        EditorUtility.DisplayDialog("Doors configured",
            $"Configured {configured} doors: {rightHandleDoors} right-handle (-90), {leftHandleDoors} left-handle (+90). Removed {duplicateComponents} duplicate door components. {missingHinge} hinges could not be found or created. Save OnlineFirstFloor.",
            "OK");
    }

    private static bool ConfigureDoor(GameObject selected)
    {
        NetworkLockedDoor[] existingDoors = selected.GetComponents<NetworkLockedDoor>();
        bool isNewDoor = existingDoors.Length == 0;
        Transform doorLeaf = FindMovingLeaf(selected);
        if (selected.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(selected);

        NetworkLockedDoor door = existingDoors.Length > 0 ? existingDoors[0] : null;
        if (door == null)
            door = Undo.AddComponent<NetworkLockedDoor>(selected);
        else
            Undo.RecordObject(door, "Configure networked door");

        for (int index = 1; index < existingDoors.Length; index++)
            Undo.DestroyObjectImmediate(existingDoors[index]);

        Collider[] blockers = FindDoorBlockers(doorLeaf);
        SerializedObject serializedDoor = new SerializedObject(door);
        serializedDoor.FindProperty("doorLeaf").objectReferenceValue = doorLeaf;
        serializedDoor.FindProperty("hingePivot").objectReferenceValue = FindOrCreateHingePivot(doorLeaf);
        serializedDoor.FindProperty("doorBlockers").arraySize = blockers.Length;
        for (int index = 0; index < blockers.Length; index++)
            serializedDoor.FindProperty("doorBlockers").GetArrayElementAtIndex(index).objectReferenceValue = blockers[index];
        bool handleOnRight = HandleIsOnLocalRight(doorLeaf);
        serializedDoor.FindProperty("openEulerAngles").vector3Value =
            new Vector3(0f, 0f, handleOnRight ? -90f : 90f);
        if (isNewDoor)
        {
            serializedDoor.FindProperty("requiresTestKey").boolValue = false;
            serializedDoor.FindProperty("allowGranny").boolValue = true;
        }
        serializedDoor.ApplyModifiedProperties();
        EditorUtility.SetDirty(selected);
        EditorUtility.SetDirty(door);
        Debug.Log($"[OnlineDoorSetup] {selected.name}: {(handleOnRight ? "right handle, -90" : "left handle, +90")}; hinge={serializedDoor.FindProperty("hingePivot").objectReferenceValue?.name ?? "MISSING"}.", selected);
        return handleOnRight;
    }

    [MenuItem("Tools/First Floor/Make Selected Door Networked", true)]
    private static bool ValidateMakeSelectedDoorNetworked()
    {
        return Selection.activeGameObject != null &&
               SceneManager.GetActiveScene().name.Equals(OnlineSceneName, StringComparison.OrdinalIgnoreCase);
    }

    private static Transform FindMovingLeaf(GameObject selected)
    {
        if (selected.name.StartsWith("Door.", StringComparison.Ordinal))
            return selected.transform;

        Transform leaf = selected.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate != selected.transform && candidate.name.StartsWith("Door.", StringComparison.Ordinal));
        return leaf != null ? leaf : selected.transform;
    }

    private static Transform FindHingePivot(Transform doorLeaf)
    {
        string suffix = doorLeaf.name.StartsWith("Door.", StringComparison.Ordinal)
            ? doorLeaf.name.Substring("Door".Length).Replace(".", "")
            : string.Empty;
        if (string.IsNullOrEmpty(suffix))
            return null;

        string expectedName = "DoorHinge" + suffix;
        Transform match = doorLeaf.parent != null
            ? doorLeaf.parent.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name.Equals(expectedName, StringComparison.Ordinal))
            : null;
        if (match != null)
            return match;

        Scene scene = doorLeaf.gameObject.scene;
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(candidate => candidate.name.Equals(expectedName, StringComparison.Ordinal));
    }

    private static Transform FindOrCreateHingePivot(Transform doorLeaf)
    {
        Transform hinge = FindHingePivot(doorLeaf);
        if (hinge != null)
            return hinge;

        string generatedName = "DoorHinge_Auto_" + doorLeaf.name;
        Scene scene = doorLeaf.gameObject.scene;
        hinge = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(candidate => candidate.name.Equals(generatedName, StringComparison.Ordinal));
        if (hinge != null)
            return hinge;

        if (!TryGetDoorBoundsInLocalSpace(doorLeaf, out Bounds doorBounds))
            return null;

        Transform[] handles = doorLeaf.GetComponentsInChildren<Transform>(true)
            .Where(candidate => candidate.name.StartsWith("Handle", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        float handleX = handles.Length > 0
            ? handles.Average(handle => doorLeaf.InverseTransformPoint(handle.position).x)
            : doorBounds.center.x + Mathf.Epsilon;
        float hingeX = handleX >= doorBounds.center.x ? doorBounds.min.x : doorBounds.max.x;

        var pivotObject = new GameObject(generatedName);
        Undo.RegisterCreatedObjectUndo(pivotObject, "Create door hinge pivot");
        if (pivotObject.scene != scene)
            SceneManager.MoveGameObjectToScene(pivotObject, scene);
        pivotObject.transform.SetPositionAndRotation(
            doorLeaf.TransformPoint(new Vector3(hingeX, doorBounds.center.y, doorBounds.center.z)),
            doorLeaf.rotation);
        return pivotObject.transform;
    }

    private static bool TryGetDoorBoundsInLocalSpace(Transform doorLeaf, out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;
        foreach (Renderer renderer in doorLeaf.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || IsHandlePart(renderer.transform, doorLeaf))
                continue;

            Bounds worldBounds = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldPoint = new Vector3(
                    (corner & 1) == 0 ? worldBounds.min.x : worldBounds.max.x,
                    (corner & 2) == 0 ? worldBounds.min.y : worldBounds.max.y,
                    (corner & 4) == 0 ? worldBounds.min.z : worldBounds.max.z);
                Vector3 localPoint = doorLeaf.InverseTransformPoint(worldPoint);
                if (hasBounds)
                    bounds.Encapsulate(localPoint);
                else
                {
                    bounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
            }
        }

        return hasBounds;
    }

    private static bool HandleIsOnLocalRight(Transform doorLeaf)
    {
        Transform[] handles = doorLeaf.GetComponentsInChildren<Transform>(true)
            .Where(candidate => candidate.name.StartsWith("Handle", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (handles.Length == 0)
        {
            Debug.LogWarning($"[OnlineDoorSetup] No Handle* object found under {doorLeaf.name}; defaulting to right-handle swing (-90).", doorLeaf.gameObject);
            return true;
        }

        if (!TryGetDoorBoundsInLocalSpace(doorLeaf, out Bounds doorBounds))
        {
            Debug.LogWarning($"[OnlineDoorSetup] No door Renderer found under {doorLeaf.name}; defaulting to right-handle swing (-90).", doorLeaf.gameObject);
            return true;
        }

        float handleLocalX = handles.Average(handle => doorLeaf.InverseTransformPoint(handle.position).x);
        return handleLocalX >= doorBounds.center.x;
    }

    private static bool IsHandlePart(Transform candidate, Transform doorLeaf)
    {
        for (Transform current = candidate; current != null && current != doorLeaf; current = current.parent)
        {
            if (current.name.StartsWith("Handle", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Collider[] FindDoorBlockers(Transform doorLeaf)
    {
        var blockers = doorLeaf.GetComponentsInChildren<Collider>(true).ToList();
        GameObject proxyRoot = GameObject.Find(CollisionProxyRootName);
        if (proxyRoot == null)
            return blockers.Distinct().ToArray();

        string matchingProxyName = "Collision - " + doorLeaf.name;
        Transform matchingProxy = proxyRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name.Equals(matchingProxyName, StringComparison.Ordinal));
        if (matchingProxy != null)
            blockers.AddRange(matchingProxy.GetComponents<Collider>());

        return blockers.Where(collider => collider != null).Distinct().ToArray();
    }
}
