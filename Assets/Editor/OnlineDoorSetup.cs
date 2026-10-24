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
            EditorUtility.DisplayDialog("Select a door", "Select the moving door leaf in OnlineFirstFloor first.", "OK");
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
        if (door != null)
        {
            EditorUtility.DisplayDialog("Already configured", "The selected object already has a Network Locked Door component.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Configure networked door",
                "This adds a NetworkObject and NetworkLockedDoor to the selected moving door leaf. " +
                "It sets this as a free-use door for both roles and links its local colliders plus a matching " +
                "'Collision - <door name>' proxy when found. You can change access rules and the open angle in the Inspector.",
                "Configure selected door", "Cancel"))
            return;

        if (selected.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(selected);

        door = Undo.AddComponent<NetworkLockedDoor>(selected);
        Collider[] blockers = FindDoorBlockers(selected);
        SerializedObject serializedDoor = new SerializedObject(door);
        serializedDoor.FindProperty("doorLeaf").objectReferenceValue = selected.transform;
        serializedDoor.FindProperty("doorBlockers").arraySize = blockers.Length;
        for (int index = 0; index < blockers.Length; index++)
            serializedDoor.FindProperty("doorBlockers").GetArrayElementAtIndex(index).objectReferenceValue = blockers[index];
        serializedDoor.FindProperty("requiresTestKey").boolValue = false;
        serializedDoor.FindProperty("allowGranny").boolValue = true;
        serializedDoor.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorUtility.SetDirty(selected);
        Selection.activeGameObject = selected;
        EditorUtility.DisplayDialog("Door configured",
            "The selected leaf now has networked open/close control. Check the Inspector: assign a hinge pivot if its imported pivot is not at the hinge, then save OnlineFirstFloor.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Make Selected Door Networked", true)]
    private static bool ValidateMakeSelectedDoorNetworked()
    {
        return Selection.activeGameObject != null &&
               SceneManager.GetActiveScene().name.Equals(OnlineSceneName, StringComparison.OrdinalIgnoreCase);
    }

    private static Collider[] FindDoorBlockers(GameObject selected)
    {
        var blockers = selected.GetComponentsInChildren<Collider>(true).ToList();
        GameObject proxyRoot = GameObject.Find(CollisionProxyRootName);
        if (proxyRoot == null)
            return blockers.Distinct().ToArray();

        string matchingProxyName = "Collision - " + selected.name;
        Transform matchingProxy = proxyRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name.Equals(matchingProxyName, StringComparison.Ordinal));
        if (matchingProxy != null)
            blockers.AddRange(matchingProxy.GetComponents<Collider>());

        return blockers.Where(collider => collider != null).Distinct().ToArray();
    }
}
