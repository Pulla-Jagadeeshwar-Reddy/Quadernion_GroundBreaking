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

        Transform[] groups = activeScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name.StartsWith(DoorGroupPrefix, StringComparison.Ordinal))
            .ToArray();
        Transform[] doors = groups.Where(group => FindMovingLeaf(group.gameObject) != group).ToArray();
        if (doors.Length == 0)
        {
            EditorUtility.DisplayDialog("No doors found",
                "No Door_Group.* objects with a child named Door.* were found in OnlineFirstFloor.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Configure all first-floor doors",
                $"Configure {doors.Length} Door_Group.* objects with a NetworkObject, NetworkLockedDoor, hinge, blockers, and opening angle (0, 0, -90) for every door?",
                "Configure all", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Configure all first-floor doors");
        int configured = 0;
        int missingHinge = 0;
        foreach (Transform group in doors)
        {
            Transform leaf = FindMovingLeaf(group.gameObject);
            if (FindHingePivot(leaf) == null)
            {
                missingHinge++;
                Debug.LogWarning($"[OnlineDoorSetup] No DoorHinge* found for {group.name}; configured its interaction but it will rotate around the leaf origin.", group.gameObject);
            }

            ConfigureDoor(group.gameObject);
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.objects = doors.Select(group => group.gameObject).ToArray();
        EditorUtility.DisplayDialog("Doors configured",
            $"Configured {configured} doors. {missingHinge} have no matching DoorHinge* pivot; inspect those hinges and save OnlineFirstFloor.",
            "OK");
    }

    private static void ConfigureDoor(GameObject selected)
    {
        bool isNewDoor = selected.GetComponent<NetworkLockedDoor>() == null;
        Transform doorLeaf = FindMovingLeaf(selected);
        if (selected.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(selected);

        NetworkLockedDoor door = selected.GetComponent<NetworkLockedDoor>();
        if (door == null)
            door = Undo.AddComponent<NetworkLockedDoor>(selected);
        else
            Undo.RecordObject(door, "Configure networked door");

        Collider[] blockers = FindDoorBlockers(doorLeaf);
        SerializedObject serializedDoor = new SerializedObject(door);
        serializedDoor.FindProperty("doorLeaf").objectReferenceValue = doorLeaf;
        serializedDoor.FindProperty("hingePivot").objectReferenceValue = FindHingePivot(doorLeaf);
        serializedDoor.FindProperty("doorBlockers").arraySize = blockers.Length;
        for (int index = 0; index < blockers.Length; index++)
            serializedDoor.FindProperty("doorBlockers").GetArrayElementAtIndex(index).objectReferenceValue = blockers[index];
        serializedDoor.FindProperty("openEulerAngles").vector3Value = new Vector3(0f, 0f, -90f);
        if (isNewDoor)
        {
            serializedDoor.FindProperty("requiresTestKey").boolValue = false;
            serializedDoor.FindProperty("allowGranny").boolValue = true;
        }
        serializedDoor.ApplyModifiedProperties();
        EditorUtility.SetDirty(selected);
        EditorUtility.SetDirty(door);
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
        if (doorLeaf.parent == null)
            return null;

        string suffix = doorLeaf.name.StartsWith("Door.", StringComparison.Ordinal)
            ? doorLeaf.name.Substring("Door".Length).Replace(".", "")
            : string.Empty;
        if (string.IsNullOrEmpty(suffix))
            return null;

        string expectedName = "DoorHinge" + suffix;
        return doorLeaf.parent.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name.Equals(expectedName, StringComparison.Ordinal));
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
