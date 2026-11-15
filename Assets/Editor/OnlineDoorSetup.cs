using System;
using System.Collections.Generic;
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

    private sealed class DoorwayCut
    {
        public int horizontalAxis;
        public int verticalAxis;
        public int depthAxis;
        public float horizontalMin;
        public float horizontalMax;
        public float verticalMin;
        public float verticalMax;
    }

    private readonly struct BoxSegment
    {
        public readonly Vector3 center;
        public readonly Vector3 size;

        public BoxSegment(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }

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
        if (!EditorSceneManager.SaveScene(activeScene))
        {
            EditorUtility.DisplayDialog("Door save failed",
                "The door was configured in memory, but OnlineFirstFloor could not be saved. Save the scene before building.",
                "OK");
            return;
        }

        Selection.activeGameObject = selected;
        EditorUtility.DisplayDialog("Door configured",
            "The selected door now has networked open/close control, and OnlineFirstFloor was saved. Check the Inspector and position the hinge pivot if needed.",
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
        if (!EditorSceneManager.SaveScene(activeScene))
        {
            EditorUtility.DisplayDialog("Door save failed",
                "The doors were configured in memory, but OnlineFirstFloor could not be saved. The build would use the previous saved scene.",
                "OK");
            return;
        }

        Selection.objects = doors.Select(group => group.gameObject).ToArray();
        EditorUtility.DisplayDialog("Doors configured",
            $"Configured {configured} doors: {rightHandleDoors} right-handle (-90), {leftHandleDoors} left-handle (+90). Removed {duplicateComponents} duplicate door components. {missingHinge} hinges could not be found or created. OnlineFirstFloor was saved.",
            "OK");
    }

    [MenuItem("Tools/First Floor/Carve Doorway Collision Openings")]
    private static void CarveDoorwayCollisionOpenings()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool isSupportedScene = scene.name.Equals(OnlineSceneName, StringComparison.OrdinalIgnoreCase) ||
                                scene.name.IndexOf("RoomTraversalTest", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isSupportedScene)
        {
            EditorUtility.DisplayDialog("Open a first-floor scene",
                "Open OnlineFirstFloor or an OnlineFirstFloor_RoomTraversalTest copy before carving doorway collisions.", "OK");
            return;
        }

        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Stop Play mode",
                "Stop Play mode, then run this command again. Doorway collider changes must be made to the saved scene.", "OK");
            return;
        }

        GameObject proxyRoot = GameObject.Find(CollisionProxyRootName);
        if (proxyRoot == null)
        {
            EditorUtility.DisplayDialog("Collision proxies not found",
                $"The Online Collision Proxies object is missing from {scene.name}.", "OK");
            return;
        }

        NetworkLockedDoor[] doors = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NetworkLockedDoor>(true))
            .ToArray();
        BoxCollider[] wallBoxes = proxyRoot.GetComponentsInChildren<BoxCollider>(true)
            .Where(box => box != null && IsWallProxyName(box.name))
            .ToArray();
        var cutsByWall = new Dictionary<BoxCollider, List<DoorwayCut>>();
        var seenDoorBlockers = new HashSet<int>();
        var doorsWithoutWallCut = new List<string>();

        foreach (NetworkLockedDoor door in doors)
        {
            bool foundWallCut = false;
            SerializedObject serializedDoor = new SerializedObject(door);
            SerializedProperty blockers = serializedDoor.FindProperty("doorBlockers");
            if (blockers == null)
            {
                doorsWithoutWallCut.Add(door.name);
                continue;
            }

            for (int blockerIndex = 0; blockerIndex < blockers.arraySize; blockerIndex++)
            {
                Collider collider = blockers.GetArrayElementAtIndex(blockerIndex).objectReferenceValue as Collider;
                if (!(collider is BoxCollider doorBox) || !doorBox.name.StartsWith("Collision - Door", StringComparison.Ordinal) ||
                    !seenDoorBlockers.Add(doorBox.GetInstanceID()))
                    continue;

                foreach (BoxCollider wall in wallBoxes)
                {
                    if (!TryCreateDoorwayCut(wall, doorBox, out DoorwayCut cut))
                        continue;

                    if (!cutsByWall.TryGetValue(wall, out List<DoorwayCut> cuts))
                    {
                        cuts = new List<DoorwayCut>();
                        cutsByWall.Add(wall, cuts);
                    }

                    cuts.Add(cut);
                    foundWallCut = true;
                }
            }

            if (!foundWallCut)
                doorsWithoutWallCut.Add(door.name);
        }

        if (cutsByWall.Count == 0)
        {
            EditorUtility.DisplayDialog("No doorway blockers found",
                "No Plane/Floor collision proxy boxes overlapped any configured door blocker. Run Configure All First-Floor Doors first and check that the matching Collision - Door object is assigned in Door Blockers.", "OK");
            return;
        }

        int openings = cutsByWall.Values.Sum(cuts => cuts.Count);
        if (!EditorUtility.DisplayDialog("Carve doorway collision openings",
                $"This will rebuild doorway cuts on {cutsByWall.Count} wall boxes for {openings} door-to-wall overlap(s), including previously carved walls. " +
                "Old generated doorway segments on those walls will be replaced. The operation supports Undo.",
                "Carve openings", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Carve doorway collision openings");
        int segmentCount = 0;
        foreach (KeyValuePair<BoxCollider, List<DoorwayCut>> entry in cutsByWall)
        {
            BoxCollider wall = entry.Key;
            List<BoxSegment> segments = BuildSegments(wall, entry.Value);
            BoxCollider[] oldSegments = wall.GetComponentsInChildren<BoxCollider>(true)
                .Where(collider => collider != wall &&
                                   collider.name.StartsWith("Doorway Collision Segment", StringComparison.Ordinal))
                .ToArray();
            foreach (BoxCollider oldSegment in oldSegments)
                Undo.DestroyObjectImmediate(oldSegment.gameObject);

            Undo.RecordObject(wall, "Replace doorway wall collision");
            wall.enabled = false;

            for (int index = 0; index < segments.Count; index++)
            {
                BoxSegment segment = segments[index];
                var segmentObject = new GameObject($"Doorway Collision Segment {index + 1:00}");
                Undo.RegisterCreatedObjectUndo(segmentObject, "Create doorway wall segment collider");
                segmentObject.layer = wall.gameObject.layer;
                segmentObject.isStatic = wall.gameObject.isStatic;
                segmentObject.transform.SetParent(wall.transform, false);
                segmentObject.transform.localPosition = segment.center;
                segmentObject.transform.localRotation = Quaternion.identity;
                segmentObject.transform.localScale = Vector3.one;
                BoxCollider segmentCollider = segmentObject.AddComponent<BoxCollider>();
                segmentCollider.size = segment.size;
                segmentCount++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            EditorUtility.DisplayDialog("Save failed",
                "Doorway collision openings were created in memory but OnlineFirstFloor could not be saved. Use Undo if needed, then save the scene before building.",
                "OK");
            return;
        }

        Debug.Log($"[OnlineDoorSetup] Carved {openings} doorway openings through {cutsByWall.Count} wall proxies using {segmentCount} replacement colliders.");
        EditorUtility.DisplayDialog("Doorway collisions updated",
            $"Carved {openings} doorway openings through {cutsByWall.Count} wall collision proxies. Created {segmentCount} wall segments; OnlineFirstFloor was saved." +
            (doorsWithoutWallCut.Count == 0
                ? " Every configured door overlapped at least one wall proxy."
                : " No wall proxy overlapped these doors: " + string.Join(", ", doorsWithoutWallCut) +
                  ". Check their Door Blockers references or nearby collision meshes."),
            "OK");
    }

    private static bool IsWallProxyName(string objectName)
    {
        return objectName.StartsWith("Collision - Plane", StringComparison.Ordinal) ||
               objectName.StartsWith("Collision - Floor", StringComparison.Ordinal);
    }

    private static bool TryCreateDoorwayCut(BoxCollider wall, BoxCollider door, out DoorwayCut cut)
    {
        cut = null;
        Transform wallTransform = wall.transform;
        Transform doorTransform = door.transform;
        Vector3 horizontal = wallTransform.InverseTransformDirection(doorTransform.right).normalized;
        Vector3 vertical = wallTransform.InverseTransformDirection(doorTransform.forward).normalized;
        Vector3 depth = wallTransform.InverseTransformDirection(doorTransform.up).normalized;
        int horizontalAxis = DominantAxis(horizontal);
        int verticalAxis = DominantAxis(vertical);
        int depthAxis = DominantAxis(depth);
        if (horizontalAxis == verticalAxis || horizontalAxis == depthAxis || verticalAxis == depthAxis ||
            Mathf.Abs(Component(horizontal, horizontalAxis)) < 0.95f ||
            Mathf.Abs(Component(vertical, verticalAxis)) < 0.95f ||
            Mathf.Abs(Component(depth, depthAxis)) < 0.95f)
            return false;

        Vector3 center = wallTransform.InverseTransformPoint(doorTransform.TransformPoint(door.center));
        Vector3 halfRight = wallTransform.InverseTransformVector(doorTransform.TransformVector(Vector3.right * door.size.x * 0.5f));
        Vector3 halfUp = wallTransform.InverseTransformVector(doorTransform.TransformVector(Vector3.up * door.size.y * 0.5f));
        Vector3 halfForward = wallTransform.InverseTransformVector(doorTransform.TransformVector(Vector3.forward * door.size.z * 0.5f));
        Vector3 halfExtents = new Vector3(
            Mathf.Abs(halfRight.x) + Mathf.Abs(halfUp.x) + Mathf.Abs(halfForward.x),
            Mathf.Abs(halfRight.y) + Mathf.Abs(halfUp.y) + Mathf.Abs(halfForward.y),
            Mathf.Abs(halfRight.z) + Mathf.Abs(halfUp.z) + Mathf.Abs(halfForward.z));

        Vector3 wallMin = wall.center - wall.size * 0.5f;
        Vector3 wallMax = wall.center + wall.size * 0.5f;
        float clearance = 0.02f;
        float wallHorizontalMin = Component(wallMin, horizontalAxis);
        float wallHorizontalMax = Component(wallMax, horizontalAxis);
        float wallVerticalMin = Component(wallMin, verticalAxis);
        float wallVerticalMax = Component(wallMax, verticalAxis);
        float wallDepthMin = Component(wallMin, depthAxis);
        float wallDepthMax = Component(wallMax, depthAxis);

        float doorHorizontalMin = Component(center, horizontalAxis) - Component(halfExtents, horizontalAxis) - clearance;
        float doorHorizontalMax = Component(center, horizontalAxis) + Component(halfExtents, horizontalAxis) + clearance;
        float doorVerticalMin = Component(center, verticalAxis) - Component(halfExtents, verticalAxis) - clearance;
        float doorVerticalMax = Component(center, verticalAxis) + Component(halfExtents, verticalAxis) + clearance;
        float doorDepthMin = Component(center, depthAxis) - Component(halfExtents, depthAxis);
        float doorDepthMax = Component(center, depthAxis) + Component(halfExtents, depthAxis);

        // A wall piece may end exactly at the doorway edge, so clamp the cut to its bounds
        // instead of rejecting it. Only carve when the wall and opening overlap in all axes.
        if (doorHorizontalMax <= wallHorizontalMin || doorHorizontalMin >= wallHorizontalMax ||
            doorVerticalMax <= wallVerticalMin || doorVerticalMin >= wallVerticalMax ||
            doorDepthMax < wallDepthMin || doorDepthMin > wallDepthMax)
            return false;

        float horizontalMin = Mathf.Max(doorHorizontalMin, wallHorizontalMin);
        float horizontalMax = Mathf.Min(doorHorizontalMax, wallHorizontalMax);
        float verticalMin = Mathf.Max(doorVerticalMin, wallVerticalMin);
        float verticalMax = Mathf.Min(doorVerticalMax, wallVerticalMax);
        if (horizontalMax - horizontalMin < 0.05f || verticalMax - verticalMin < 0.05f)
            return false;

        cut = new DoorwayCut
        {
            horizontalAxis = horizontalAxis,
            verticalAxis = verticalAxis,
            depthAxis = depthAxis,
            horizontalMin = horizontalMin,
            horizontalMax = horizontalMax,
            verticalMin = verticalMin,
            verticalMax = verticalMax
        };
        return true;
    }

    private static List<BoxSegment> BuildSegments(BoxCollider wall, List<DoorwayCut> cuts)
    {
        Vector3 minimum = wall.center - wall.size * 0.5f;
        Vector3 maximum = wall.center + wall.size * 0.5f;
        int horizontalAxis = cuts[0].horizontalAxis;
        int verticalAxis = cuts[0].verticalAxis;
        int depthAxis = cuts[0].depthAxis;
        var horizontalBounds = new List<float> { Component(minimum, horizontalAxis), Component(maximum, horizontalAxis) };
        foreach (DoorwayCut cut in cuts)
        {
            if (cut.horizontalAxis != horizontalAxis || cut.verticalAxis != verticalAxis || cut.depthAxis != depthAxis)
                continue;
            horizontalBounds.Add(cut.horizontalMin);
            horizontalBounds.Add(cut.horizontalMax);
        }
        horizontalBounds = horizontalBounds.Distinct().OrderBy(value => value).ToList();

        var segments = new List<BoxSegment>();
        for (int index = 0; index < horizontalBounds.Count - 1; index++)
        {
            float left = horizontalBounds[index];
            float right = horizontalBounds[index + 1];
            if (right - left < 0.02f)
                continue;

            float midpoint = (left + right) * 0.5f;
            List<(float min, float max)> openings = cuts
                .Where(cut => cut.horizontalAxis == horizontalAxis && cut.verticalAxis == verticalAxis &&
                              cut.depthAxis == depthAxis && midpoint > cut.horizontalMin && midpoint < cut.horizontalMax)
                .Select(cut => (cut.verticalMin, cut.verticalMax))
                .OrderBy(interval => interval.verticalMin)
                .ToList();
            var mergedOpenings = new List<(float min, float max)>();
            foreach ((float min, float max) opening in openings)
            {
                if (mergedOpenings.Count == 0 || opening.min > mergedOpenings[^1].max)
                    mergedOpenings.Add(opening);
                else
                    mergedOpenings[^1] = (mergedOpenings[^1].min, Mathf.Max(mergedOpenings[^1].max, opening.max));
            }

            float verticalCursor = Component(minimum, verticalAxis);
            foreach ((float min, float max) opening in mergedOpenings)
            {
                AddSegment(segments, horizontalAxis, verticalAxis, depthAxis,
                    left, right, verticalCursor, Mathf.Min(opening.min, Component(maximum, verticalAxis)),
                    Component(minimum, depthAxis), Component(maximum, depthAxis));
                verticalCursor = Mathf.Max(verticalCursor, opening.max);
            }

            AddSegment(segments, horizontalAxis, verticalAxis, depthAxis,
                left, right, verticalCursor, Component(maximum, verticalAxis),
                Component(minimum, depthAxis), Component(maximum, depthAxis));
        }

        return segments;
    }

    private static void AddSegment(List<BoxSegment> segments, int horizontalAxis, int verticalAxis, int depthAxis,
        float horizontalMin, float horizontalMax, float verticalMin, float verticalMax, float depthMin, float depthMax)
    {
        if (horizontalMax - horizontalMin < 0.02f || verticalMax - verticalMin < 0.02f || depthMax - depthMin < 0.02f)
            return;

        Vector3 center = Vector3.zero;
        Vector3 size = Vector3.zero;
        SetComponent(ref center, horizontalAxis, (horizontalMin + horizontalMax) * 0.5f);
        SetComponent(ref center, verticalAxis, (verticalMin + verticalMax) * 0.5f);
        SetComponent(ref center, depthAxis, (depthMin + depthMax) * 0.5f);
        SetComponent(ref size, horizontalAxis, horizontalMax - horizontalMin);
        SetComponent(ref size, verticalAxis, verticalMax - verticalMin);
        SetComponent(ref size, depthAxis, depthMax - depthMin);
        segments.Add(new BoxSegment(center, size));
    }

    private static int DominantAxis(Vector3 value)
    {
        Vector3 absolute = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return 0;
        return absolute.y >= absolute.z ? 1 : 2;
    }

    private static float Component(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetComponent(ref Vector3 value, int axis, float component)
    {
        if (axis == 0)
            value.x = component;
        else if (axis == 1)
            value.y = component;
        else
            value.z = component;
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
