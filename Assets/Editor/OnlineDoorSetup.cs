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

        bool isNewDoor = door == null;
        Transform doorLeaf = FindMovingLeaf(selected);
        if (selected.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(selected);

        if (isNewDoor)
            door = Undo.AddComponent<NetworkLockedDoor>(selected);

        Collider[] blockers = FindDoorBlockers(doorLeaf);
        SerializedObject serializedDoor = new SerializedObject(door);
        serializedDoor.FindProperty("doorLeaf").objectReferenceValue = doorLeaf;
        serializedDoor.FindProperty("hingePivot").objectReferenceValue = FindHingePivot(doorLeaf);
        serializedDoor.FindProperty("doorBlockers").arraySize = blockers.Length;
        for (int index = 0; index < blockers.Length; index++)
            serializedDoor.FindProperty("doorBlockers").GetArrayElementAtIndex(index).objectReferenceValue = blockers[index];
        if (isNewDoor)
        {
            serializedDoor.FindProperty("requiresTestKey").boolValue = false;
            serializedDoor.FindProperty("allowGranny").boolValue = true;
        }
        serializedDoor.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorUtility.SetDirty(selected);
        Selection.activeGameObject = selected;
        EditorUtility.DisplayDialog("Door configured",
            "The selected door now has networked open/close control. Check the Inspector: assign or position a hinge pivot if needed, then save OnlineFirstFloor.",
            "OK");
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
