using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds everything the escape routes need into one prefab, Assets/Routes/RouteContent.prefab,
/// and drops a single instance into the first-floor scene. Nothing in here is parented to or
/// references the floor model, so Ritu can replace the floor and simply re-run
/// "Tools/Routes/Refresh After Floor Change".
/// </summary>
public static class RouteSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/OnlineFirstFloor.unity";
    private const string RoutesRoot = "Assets/Routes";
    private const string ContentPrefabPath = RoutesRoot + "/RouteContent.prefab";
    private const string PickupFolder = RoutesRoot + "/Shared/Prefabs";
    private const string MaterialFolder = RoutesRoot + "/Shared/Materials";
    private const string NetworkPrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
    private const string ContentRootName = "RouteContent";
    private const string JanitorRoom = "Janitor Room";

    // ------------------------------------------------------------------ layout data
    // World-space (x, z) footprints taken from the first-floor model. Heights are found by
    // dropping onto the floor, so small changes to the model are absorbed automatically.

    private struct RoomSpec
    {
        public string Name;
        public float MinX, MaxX, MinZ, MaxZ;
        public string LockedBy;

        public RoomSpec(string name, float minX, float maxX, float minZ, float maxZ, string lockedBy = "")
        {
            Name = name;
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
            LockedBy = lockedBy;
        }

        public Vector3 Center => new((MinX + MaxX) * 0.5f, 1.5f, (MinZ + MaxZ) * 0.5f);
        public Vector3 Size => new(MaxX - MinX, 3f, MaxZ - MinZ);
    }

    private static readonly RoomSpec[] Rooms =
    {
        new("Classroom 1", 7.6f, 16.2f, -19.8f, -8.5f),
        new("Lounge", 9.9f, 16.2f, -6.1f, -3.6f),
        new("Staff Room", 7.5f, 16.2f, -0.2f, 11.7f),
        new("Room above HOD", 9.8f, 16.2f, 17.2f, 20.0f),
        new("Classroom 01", -3.8f, 2.9f, 16.6f, 26.0f),
        new("Classroom 02", -39.6f, -33.0f, 16.6f, 26.0f),
        new("Classroom 03", -50.0f, -43.3f, 16.6f, 26.0f),
        new("Laboratory 2", -63.5f, -51.6f, 14.0f, 26.3f),
        new("Laboratory 3", -63.5f, -51.6f, 7.8f, 11.6f),
        new("Classroom 04", -63.5f, -54.7f, -4.3f, 4.5f),
        new("Classroom 05", -49.9f, -43.3f, -7.8f, 1.6f),
        new("Classroom 06", -39.5f, -33.0f, -7.8f, 1.6f),
        new("Laboratory", -12.5f, 0.7f, -13.8f, 3.6f),
        new("Electrical", -18.4f, -17.1f, -15.7f, -12.5f),
        new(JanitorRoom, -13.4f, -10.3f, 5.9f, 8.7f, ItemIds.R3JanitorKey),
    };

    private struct StairSpec
    {
        public string Name;
        public float MinX, MaxX, MinZ, MaxZ;

        public StairSpec(string name, float minX, float maxX, float minZ, float maxZ)
        {
            Name = name;
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public Vector3 Center => new((MinX + MaxX) * 0.5f, 1.5f, (MinZ + MaxZ) * 0.5f);
        public Vector3 Size => new(MaxX - MinX + 0.4f, 3.2f, MaxZ - MinZ + 0.4f);
    }

    // Approximate stairwell positions used only to decide which detected stairwell is which exit.
    private static readonly Vector3 LocksmithStairsHint = new(5.15f, 0f, -9.7f);
    private static readonly Vector3 BlackoutStairsHint = new(-22.05f, 0f, -14.0f);

    // Filled by DetectStairwells() from the floor's own stair collision on every build.
    private static StairSpec StairsLocksmith;
    private static StairSpec StairsBlackout;
    private static StairSpec[] SealedStairs = Array.Empty<StairSpec>();

    // Anchors are approximate; SnapToFloor searches up to 4 m around each for walkable floor.
    private static readonly Vector3 KeyCabinetSpot = new(11.8f, 0f, 5.7f);
    private static readonly Vector3 TvSpot = new(8.0f, 0f, -9.5f);
    private static readonly Vector3 PanelSpot = new(-17.75f, 0f, -14.1f);
    private static readonly Vector3 FreezerSpot = new(9.0f, 0f, -5.0f);
    private static readonly Vector3 BenchSpot = new(-36.2f, 0f, -3.0f);
    private static readonly Vector3 LiftSpot = new(-17.5f, 0f, 20.9f);
    private static readonly Vector3 JanitorDoorSpot = new(-10.9f, 1f, 5.92f);
    private static readonly Vector3 LadderSpot = new(-28.0f, 0f, 13.3f);

    /// <summary>How many existing desks get a hackable PC (3 of them are switched on each round).</summary>
    private const int PcCandidateCount = 6;

    // ------------------------------------------------------------------ menu entry points

    [MenuItem("Tools/Routes/Build Route Content")]
    public static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string report = Build();
        EditorUtility.DisplayDialog("Route content built", report, "OK");
    }

    [MenuItem("Tools/Routes/Refresh After Floor Change")]
    public static void RefreshMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Routes", "Stop Play mode first, then run Refresh After Floor Change again.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // Tighten oversized collision boxes, then re-drop every prop, spot and barrier onto the current floor.
        string report = CollisionProxyRefiner.Refine() + "\n\n" + Build() + "\n\n" + Validate(100);
        Debug.Log("[RouteSceneBuilder] " + report);
        EditorUtility.DisplayDialog("Routes refreshed",
            report + "\n\nIf the floor's doors changed, also run Tools/First Floor/Configure All First-Floor Doors.", "OK");
    }

    [MenuItem("Tools/Routes/Validate Routes")]
    public static void ValidateMenu()
    {
        EditorUtility.DisplayDialog("Route validation", Validate(100), "OK");
    }

    /// <summary>Batch-mode entry: Unity.exe -batchmode -quit -executeMethod RouteSceneBuilder.BuildFromCommandLine</summary>
    public static void BuildFromCommandLine()
    {
        PlayerModelBuilder.Build();
        Debug.Log("[RouteSceneBuilder] " + CollisionProxyRefiner.Refine());
        string report = Build();
        Debug.Log("[RouteSceneBuilder] " + report);
        Debug.Log("[RouteSceneBuilder] " + Validate(100));
    }

    // ------------------------------------------------------------------ build

    public static string Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        grid = null;
        EnsureFolder(PickupFolder);
        EnsureFolder(MaterialFolder);

        Materials materials = new();
        Dictionary<string, GameObject> pickups = BuildPickupPrefabs(materials);
        RegisterNetworkPrefabs(pickups.Values);

        foreach (GameObject old in scene.GetRootGameObjects().Where(go => go.name == ContentRootName).ToArray())
            Object.DestroyImmediate(old);

        GameObject root = new(ContentRootName);
        List<string> warnings = new();

        // Rooms first: other steps use them for names and locks.
        Transform roomsParent = Child(root.transform, "Rooms");
        foreach (RoomSpec room in Rooms)
        {
            GameObject roomObject = Child(roomsParent, "Room - " + room.Name).gameObject;
            roomObject.transform.position = room.Center;
            roomObject.AddComponent<RoomVolume>().Configure(room.Name, room.Size, room.LockedBy);
        }

        // Permanent barriers on stairs that are not an exit.
        if (!DetectStairwells(warnings))
            return "No stair collision found (Collision - Stairs*); route content was not built.";
        // A first walk of the floor (stairs open) tells which stairwell sides players can reach,
        // so the boards go across every real entrance.
        Physics.SyncTransforms();
        grid = WalkGrid.Build(root.transform);
        stairLog.Clear();
        Transform barriers = Child(root.transform, "Stair Barriers");
        foreach (StairSpec stair in SealedStairs)
            BuildStairBarrier(barriers, stair, materials, "Barrier - " + stair.Name);

        Physics.SyncTransforms();

        // Everything below is placed only where the player can actually walk to from spawn.
        grid = WalkGrid.Build(root.transform);
        warnings.Add($"Walkable floor reachable from spawn: {grid.ReachableArea:0} m² ({grid.ReachableCells} cells).");
        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/reach_map.txt"), grid.Dump());

        // Doors that lead nowhere the player can go: boarded and locked for good.
        int deadDoors = BoardUpDeadDoors(Child(root.transform, "Boarded Doors"), materials);

        // The floor model's own desks, fans and windows (found by name, so a new model just works).
        List<EnvObject> envObjects = FindEnvObjects(root.transform);
        benchCandidates.Clear();

        Transform routes = Child(root.transform, "Routes");
        BuildLocksmith(Child(routes, "R1 Locksmith"), materials, warnings);
        BuildBlackout(Child(routes, "R2 Blackout"), materials, warnings);
        RouteInteractable[] pcs = BuildLadder(Child(routes, "R3 Ladder"), materials, warnings, envObjects);
        BuildFrozenKey(Child(routes, "R5 Frozen Key"), materials, warnings);

        Physics.SyncTransforms();

        // Walk the floor again with the route doors/shutters in place, so no item lands in a
        // passage that is only open once a route is done.
        grid = WalkGrid.Build(root.transform);

        janitorWarning = null;
        int spotCount = BuildSpawnPoints(Child(root.transform, "Spawn Points"), root.transform);
        if (janitorWarning != null)
            warnings.Add(janitorWarning);
        warnings.Add($"Boarded {SealedStairs.Length} sealed stair passages at their entrance and {deadDoors} doors that lead nowhere.");

        GameObject managerObject = Child(root.transform, "Route Manager").gameObject;
        managerObject.AddComponent<NetworkObject>();
        RouteManager manager = managerObject.AddComponent<RouteManager>();
        manager.Configure(BuildItemRules(), pickups.Select(pair => new PickupPrefabEntry { itemId = pair.Key, prefab = pair.Value }).ToArray(),
            pcs, 3, ItemIds.R3JanitorKey, benchCandidates.ToArray());

        EnvSpot[] envSpots = envObjects
            .Where(env => !env.UsedByPc)
            .Select(env => new EnvSpot { kind = env.Kind, center = env.Bounds.center, size = env.Bounds.size, label = env.Label })
            .ToArray();
        managerObject.AddComponent<EnvInteractionManager>().Configure(envSpots);
        warnings.Add($"Floor interactions: {envSpots.Count(s => s.kind == EnvKind.Desk)} searchable desks, " +
                     $"{envSpots.Count(s => s.kind == EnvKind.Fan)} fans, {envSpots.Count(s => s.kind == EnvKind.Window)} windows.");

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, ContentPrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        string summary = $"RouteContent rebuilt: {Rooms.Length} rooms, {spotCount} item spots, {pcs.Length} PC desks, " +
                         $"{pickups.Count} pickup prefabs.";
        if (warnings.Count > 0)
            summary += "\nWarnings:\n- " + string.Join("\n- ", warnings);
        return prefab != null ? summary : "Failed to save RouteContent prefab.";
    }

    private static ItemSpawnRule[] BuildItemRules()
    {
        return new[]
        {
            new ItemSpawnRule { itemId = ItemIds.Hammer, count = 1, categories = SpawnCategory.Tool },
            new ItemSpawnRule { itemId = ItemIds.Screwdriver, count = 1, categories = SpawnCategory.Tool },
            new ItemSpawnRule { itemId = ItemIds.Crowbar, count = 1, categories = SpawnCategory.Tool, forbiddenRooms = new[] { "Lounge" } },
            new ItemSpawnRule { itemId = ItemIds.R2Fuse, count = 3, categories = SpawnCategory.Fuse, forbiddenRooms = new[] { "Electrical" } },
            new ItemSpawnRule { itemId = ItemIds.R3Plank, count = 2, categories = SpawnCategory.Janitor, requiredRoom = JanitorRoom },
            new ItemSpawnRule { itemId = ItemIds.R3Nails, count = 1, categories = SpawnCategory.Janitor, requiredRoom = JanitorRoom },
        };
    }

    // ------------------------------------------------------------------ routes

    private static void BuildLocksmith(Transform parent, Materials materials, List<string> warnings)
    {
        const string route = "Locksmith";

        RouteInteractable cabinet = NewStep(parent, "R1_KeyCabinet", KeyCabinetSpot, warnings);
        GameObject cabinetVisual = AddModel(cabinet.transform, "Route01_Locksmith", "R1_KeyCabinet",
            () => Box("Cabinet", new Vector3(0.6f, 0.75f, 0.2f), new Vector3(0f, 1.2f, 0f), materials.Metal));
        cabinet.Configure("R1_KeyCabinet", route, "Key Cabinet", "Smash", 3f, 1, false, NoiseLevel.Loud, NoiseLevel.None,
            null, Items((ItemIds.Hammer, 1)), null, Items((ItemIds.R1DoorKey, 1)));
        // The model carries both the intact glass and the shards; show the shards only once smashed.
        GameObject[] shards = FindChildren(cabinetVisual.transform, "GlassShards");
        GameObject[] glass = FindChildren(cabinetVisual.transform, "Glass").Except(shards).ToArray();
        foreach (GameObject shard in shards)
            shard.SetActive(false);
        cabinet.ConfigureVisuals(shards, glass, null, new[] { Glow(cabinet.transform, new Vector3(0f, 1.2f, -0.15f), materials) }, null);
        EnsureCollider(cabinetVisual);

        RouteInteractable tv = NewStep(parent, "R1_TV", TvSpot, warnings);
        GameObject tvVisual = AddModel(tv.transform, "Route01_Locksmith", "R1_TV", () =>
        {
            GameObject group = new("TV");
            Box("Stand", new Vector3(0.6f, 1.1f, 0.4f), new Vector3(0f, 0.55f, 0f), materials.Wood).transform.SetParent(group.transform, false);
            Box("Screen", new Vector3(1.0f, 0.6f, 0.08f), new Vector3(0f, 1.45f, 0f), materials.Screen).transform.SetParent(group.transform, false);
            return group;
        });
        EnsureCollider(tvVisual);
        FaceReachableFloor(tv.transform, tvVisual, "Screen");
        tv.Configure("R1_TV", route, "Staff TV", "Read the code on", 2f, 1, false, NoiseLevel.None, NoiseLevel.None,
            null, null, null, Items((ItemIds.R1Passcode, 1)));

        // Boarded, padlocked barricade across the Stairs-06 stairwell. The barricade is part of the
        // step itself, so aiming at it anywhere gives the prompt. Step 1 unscrews the boards,
        // step 2 opens the padlock with the key and the passcode.
        RouteInteractable stairDoor = NewStep(parent, "R1_StairDoor", Flat(StairsLocksmith.Center), warnings, snap: false);
        GameObject barrier = BuildStairBarrier(stairDoor.transform, StairsLocksmith, materials, "Gold Stair Door", StairBlock.GoldDoor);
        GameObject padlock = Box("Padlock", new Vector3(0.3f, 0.35f, 0.3f), new Vector3(0f, 1.0f, 0f), materials.Brass);
        padlock.transform.SetParent(barrier.transform, true);
        Object.DestroyImmediate(padlock.GetComponent<Collider>());
        stairDoor.Configure("R1_StairDoor", route, "Boarded Stair Door", "Open", 6.5f, 2, false, NoiseLevel.Small, NoiseLevel.Small,
            null, Items((ItemIds.Screwdriver, 1), (ItemIds.R1DoorKey, 1), (ItemIds.R1Passcode, 1)), null, null);
        stairDoor.ConfigureVisuals(null, new[] { barrier }, null, null, null);

        BuildExit(parent, "Exit - Locksmith", Flat(StairsLocksmith.Center), route, new[] { stairDoor },
            "Stairs door: boarded and locked", materials);
    }

    private static void BuildBlackout(Transform parent, Materials materials, List<string> warnings)
    {
        const string route = "Blackout";

        // One fuse-board model: its Cover part comes off with the screwdriver, its Fuses part
        // appears when the fuses are fitted, its LEDs blink while the power is out.
        RouteInteractable panel = NewStep(parent, "R2_Panel", PanelSpot, warnings);
        GameObject panelModel = AddModel(panel.transform, "Route02_Blackout", "R2_FusePanel", () =>
        {
            GameObject group = new("Fuse Panel");
            Box("Body", new Vector3(0.75f, 1.65f, 0.3f), new Vector3(0f, 0.92f, 0f), materials.DarkMetal).transform.SetParent(group.transform, false);
            Box("Cover", new Vector3(0.72f, 1.6f, 0.03f), new Vector3(0f, 0.92f, -0.17f), materials.Metal).transform.SetParent(group.transform, false);
            Box("Fuses", new Vector3(0.45f, 0.2f, 0.05f), new Vector3(0f, 1.12f, -0.1f), materials.Brass).transform.SetParent(group.transform, false);
            Box("LEDs", new Vector3(0.3f, 0.03f, 0.02f), new Vector3(0f, 1.7f, -0.17f), materials.AlertGlow).transform.SetParent(group.transform, false);
            foreach (Collider part in group.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(part);
            return group;
        });
        EnsureCollider(panelModel);
        FaceReachableFloor(panel.transform, panelModel, "Cover");
        GameObject[] coverParts = FindChildren(panelModel.transform, "Cover");
        GameObject[] fuseParts = FindChildren(panelModel.transform, "Fuses");
        GameObject[] ledParts = FindChildren(panelModel.transform, "LEDs");

        RouteInteractable cover = NewStep(parent, "R2_PanelCover", panel.transform.position, warnings, snap: false);
        cover.transform.rotation = panel.transform.rotation;
        cover.Configure("R2_PanelCover", route, "Fuse Panel Cover", "Unscrew", 4f, 1, false, NoiseLevel.None, NoiseLevel.None,
            null, Items((ItemIds.Screwdriver, 1)), null, null);
        // The cover's hitbox wraps the whole board, so aiming at it from any side targets the
        // cover until it is off (otherwise the board is hit first and says "cover first").
        Bounds boardBounds = RendererBounds(panelModel, panel.transform.position + Vector3.up);
        GameObject coverHitbox = new("Cover Hitbox");
        coverHitbox.transform.SetParent(cover.transform, false);
        coverHitbox.transform.SetPositionAndRotation(boardBounds.center, Quaternion.identity);
        BoxCollider hitbox = coverHitbox.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(boardBounds.size.x + 0.15f, Mathf.Min(boardBounds.size.y, 1.9f) + 0.1f, boardBounds.size.z + 0.15f);
        cover.ConfigureVisuals(null, coverParts.Append(coverHitbox).ToArray(), null, null, null);

        panel.Configure("R2_Panel", route, "Fuse Panel", "Fit fuses into", 8f, 1, false, NoiseLevel.Loud, NoiseLevel.None,
            new[] { cover }, Items((ItemIds.R2Fuse, 3)), Items((ItemIds.R2Fuse, 3)), null);

        GameObject shutter = BuildStairBarrier(parent, StairsBlackout, materials, "R2_Shutter (blocker)", StairBlock.Shutter);
        panel.ConfigureVisuals(fuseParts, new[] { shutter }, null, ledParts, null);

        BuildExit(parent, "Exit - Blackout", Flat(StairsBlackout.Center), route, new[] { panel },
            "Power shutter is down", materials);
    }

    private static RouteInteractable[] BuildLadder(Transform parent, Materials materials, List<string> warnings, List<EnvObject> envObjects)
    {
        const string route = "Ladder";
        List<RouteInteractable> pcs = new();

        // PCs sit on the floor model's own desks: the computer part of the hack-station model
        // (monitor, tower, keyboard) without its desk, spread out across the floor.
        PcSetMesh pcSet = BuildPcSetMesh();
        List<(EnvObject desk, DeskTop top)> pcDesks = PickPcDesks(envObjects, PcCandidateCount);
        if (pcDesks.Count < 3)
            warnings.Add($"Only {pcDesks.Count} desks could take a PC (need 3+). Check the floor model has desks (Shorter/Longer/Cube).");

        for (int index = 0; index < pcDesks.Count; index++)
        {
            (EnvObject deskObject, DeskTop deskTop) = pcDesks[index];
            deskObject.UsedByPc = true;
            RouteInteractable pc = NewStep(parent, $"R3_PC_{index + 1}", deskTop.Center, warnings, snap: false);

            GameObject desk;
            Vector3 screenNormal = Vector3.back;
            if (pcSet != null)
            {
                desk = new GameObject("PC Set");
                desk.transform.SetParent(pc.transform, false);
                desk.AddComponent<MeshFilter>().sharedMesh = pcSet.Mesh;
                desk.AddComponent<MeshRenderer>().sharedMaterials = pcSet.Materials;
                screenNormal = pcSet.ScreenNormal;
            }
            else
            {
                desk = new GameObject("PC Set");
                desk.transform.SetParent(pc.transform, false);
                Box("Monitor", new Vector3(0.5f, 0.38f, 0.3f), new Vector3(-0.25f, 0.27f, 0f), materials.DarkMetal).transform.SetParent(desk.transform, false);
                Box("Tower", new Vector3(0.2f, 0.45f, 0.45f), new Vector3(0.3f, 0.225f, 0f), materials.DarkMetal).transform.SetParent(desk.transform, false);
            }

            EnsureCollider(desk);

            // Square to the desk: screen faces the desk's room side (the long edge next to reachable
            // floor), computer pushed to the back edge and centred along the desk.
            Vector3 across = Vector3.Cross(Vector3.up, deskTop.Along).normalized;
            float frontDistance = ReachDistance(deskTop.Center + across * (deskTop.HalfShort + 0.5f));
            float backDistance = ReachDistance(deskTop.Center - across * (deskTop.HalfShort + 0.5f));
            Vector3 facing = frontDistance <= backDistance ? across : -across;
            Quaternion rotation = Quaternion.Euler(0f, Vector3.SignedAngle(Flat(screenNormal), facing, Vector3.up), 0f);

            MeshFilter setFilter = desk.GetComponent<MeshFilter>();
            Bounds local = setFilter != null && setFilter.sharedMesh != null ? setFilter.sharedMesh.bounds
                : new Bounds(new Vector3(0f, 0.25f, 0f), new Vector3(1.0f, 0.5f, 0.45f));
            Vector3 normalAxis = Mathf.Abs(screenNormal.x) > Mathf.Abs(screenNormal.z) ? Vector3.right : Vector3.forward;
            float depth = Vector3.Scale(local.size, normalAxis).magnitude;
            float back = Mathf.Max(0f, deskTop.HalfShort - depth * 0.5f - 0.04f);
            Vector3 target = deskTop.Center - facing * back;
            Vector3 pivotOffset = rotation * new Vector3(local.center.x, 0f, local.center.z);
            pc.transform.SetPositionAndRotation(target - pivotOffset, rotation);

            // The "hackable" glow is a copy of the model's own screen surface, so it always lines up.
            GameObject screen = OverlaySubmesh(desk, "Screen", materials.Screen, $"R3_PC_ScreenGlow");
            if (screen == null)
            {
                screen = Box("Screen Glow", new Vector3(0.42f, 0.3f, 0.02f), new Vector3(-0.25f, 0.29f, -0.16f), materials.Screen);
                screen.transform.SetParent(pc.transform, false);
                Object.DestroyImmediate(screen.GetComponent<Collider>());
            }

            // Crouching behind the desk hides the Runner while hacking (optional).
            pc.Configure($"R3_PC_{index + 1}", route, "Office PC", "Hack", 6f, 1, false, NoiseLevel.None, NoiseLevel.Small,
                null, null, null, Items((ItemIds.R3Plank, 2)));
            pc.ConfigureVisuals(null, null, null, new[] { screen }, null);
            pcs.Add(pc);
        }

        // Janitor room: locked door (by rule, not by reference) and a sign.
        GameObject lockRule = Child(parent, "Janitor Door Lock").gameObject;
        lockRule.transform.position = JanitorDoorSpot;
        lockRule.AddComponent<DoorLockRule>().Configure(ItemIds.R3JanitorKey, 1.2f);
        GameObject sign = AddModel(parent, "Shared", "S_JanitorSign",
            () => Box("Janitor Sign", new Vector3(0.5f, 0.2f, 0.03f), new Vector3(0f, 0.09f, 0f), materials.Brass));
        sign.transform.position = JanitorDoorSpot + new Vector3(0f, 1.06f, -0.1f);
        foreach (Collider part in sign.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(part);

        // Ladder spot at the atrium railing (the collision floor covers the atrium, so this is a fixed anchor).
        Vector3 edge = SnapToFloor(LadderSpot, "R3_LadderSpot", warnings);
        Vector3 outward = Vector3.back;
        RouteInteractable ladder = NewStep(parent, "R3_LadderSpot", edge, warnings, snap: false);
        ladder.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
        GameObject[] stages = new GameObject[3];
        string ladderPath = $"{RoutesRoot}/Route03_Hacker/Models/R3_Ladder.fbx";
        if (File.Exists(ladderPath))
        {
            // Plank pile at the build spot (Base) and three ladder sections that appear as it is built.
            GameObject ladderModel = AddModel(ladder.transform, "Route03_Hacker", "R3_Ladder", () => new GameObject("R3_Ladder"));
            ladderModel.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            for (int stage = 0; stage < 3; stage++)
                stages[stage] = FindChildren(ladderModel.transform, $"Stage{stage + 1}").FirstOrDefault();
            GameObject[] baseParts = FindChildren(ladderModel.transform, "Base");
            if (baseParts.Length > 0)
                EnsureCollider(baseParts[0]);
        }
        else
        {
            GameObject bracket = Box("Bracket", new Vector3(1.2f, 0.25f, 0.4f), new Vector3(0f, 0.125f, 0f), materials.Hazard);
            bracket.transform.SetParent(ladder.transform, false);
            for (int stage = 0; stage < 3; stage++)
            {
                float length = 1.4f * (stage + 1);
                GameObject piece = Box($"Ladder Stage {stage + 1}", new Vector3(0.6f, length, 0.08f),
                    new Vector3(0f, 0.6f - length * 0.5f, 0.45f), materials.Wood);
                piece.transform.SetParent(ladder.transform, false);
                Object.DestroyImmediate(piece.GetComponent<Collider>());
                stages[stage] = piece;
            }
        }

        ladder.Configure("R3_LadderSpot", route, "Atrium Ladder", "Build", 4f, 3, false, NoiseLevel.Small, NoiseLevel.Loud,
            null, Items((ItemIds.R3Plank, 8), (ItemIds.R3Nails, 1), (ItemIds.Hammer, 1)),
            Items((ItemIds.R3Plank, 8), (ItemIds.R3Nails, 1)), null);
        ladder.ConfigureVisuals(null, null, stages, null, null);

        // The escape is the ladder itself: aim at it (or walk onto it) once it is built.
        BuildExit(parent, "Exit - Ladder", ladder.transform.position + ladder.transform.forward * 0.45f, route, new[] { ladder },
            "Build the ladder first", materials, ladderExit: true);
        return pcs.ToArray();
    }

    private static void BuildFrozenKey(Transform parent, Materials materials, List<string> warnings)
    {
        const string route = "Frozen Key";

        RouteInteractable freezer = NewStep(parent, "R5_Freezer", FreezerSpot, warnings);
        GameObject freezerVisual = AddModel(freezer.transform, "Route05_FrozenKey", "R5_Freezer",
            () => Box("Freezer", new Vector3(1.2f, 0.9f, 0.7f), new Vector3(0f, 0.45f, 0f), materials.White));
        EnsureCollider(freezerVisual);
        freezer.Configure("R5_Freezer", route, "Freezer", "Pry open", 5f, 1, false, NoiseLevel.Small, NoiseLevel.None,
            null, Items((ItemIds.Crowbar, 1)), null, Items((ItemIds.R5IceBlock, 1)));

        // The workbench stands sideways on a classroom dais (teacher's platform), off the walkways.
        // One bench per dais is built; the round picks one of them.
        List<(Vector3 position, Quaternion rotation)> benchSpots = FindDaisBenchSpots();
        if (benchSpots.Count == 0)
        {
            warnings.Add("No classroom dais found for the ice bench; using the fallback spot.");
            benchSpots.Add((SnapToFloor(BenchSpot, "R5_SmashBench", warnings), Quaternion.identity));
        }

        for (int index = 0; index < benchSpots.Count; index++)
        {
            (Vector3 position, Quaternion rotation) = benchSpots[index];
            RouteInteractable bench = NewStep(parent, $"R5_SmashBench_{index + 1}", position, warnings, snap: false);
            bench.transform.rotation = rotation;
            GameObject benchVisual = AddModel(bench.transform, "Route05_FrozenKey", "R5_SmashBench",
                () => Box("Bench", new Vector3(1.6f, 0.9f, 0.7f), new Vector3(0f, 0.45f, 0f), materials.Wood));
            EnsureCollider(benchVisual);
            bench.Configure($"R5_SmashBench_{index + 1}", route, "Workbench", "Smash the ice on", 6f, 1, false, NoiseLevel.Loud, NoiseLevel.None,
                null, Items((ItemIds.R5IceBlock, 1), (ItemIds.Hammer, 1)), Items((ItemIds.R5IceBlock, 1)), Items((ItemIds.R5BellKey, 1)));
            benchCandidates.Add(bench);
        }

        RouteInteractable chain = NewStep(parent, "R5_ChainLock", LiftSpot, warnings);
        GameObject chainVisual = AddModel(chain.transform, "Route05_FrozenKey", "R5_ChainLock",
            () => Box("Chain", new Vector3(1.2f, 1.6f, 0.15f), new Vector3(0f, 1.0f, 0f), materials.Metal));
        EnsureCollider(chainVisual);
        chain.Configure("R5_ChainLock", route, "Lift Chain", "Unlock", 6f, 1, false, NoiseLevel.Small, NoiseLevel.None,
            null, Items((ItemIds.R5BellKey, 1)), Items((ItemIds.R5BellKey, 1)), null);
        chain.ConfigureVisuals(null, new[] { chainVisual }, null, null, null);

        BuildExit(parent, "Exit - Frozen Key", LiftSpot + new Vector3(-0.9f, 0f, 1.2f), route, new[] { chain },
            "Service lift: chained shut", materials);
    }

    // ------------------------------------------------------------------ floor-model furniture

    private static readonly List<RouteInteractable> benchCandidates = new();

    /// <summary>
    /// Classroom daises (long low platforms: "Teacher Supremacy" and similar 0.15-0.4 m slabs about
    /// 1-2 m wide and 4-8 m long). Returns a spot near one end of each, with the bench's long side
    /// along the dais and its front toward the room.
    /// </summary>
    private static List<(Vector3 position, Quaternion rotation)> FindDaisBenchSpots()
    {
        List<(Vector3, Quaternion)> spots = new();
        Transform contentRoot = ContentRootTransform;
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (renderer.GetComponentInParent<NetworkObject>() != null ||
                (contentRoot != null && renderer.transform.IsChildOf(contentRoot)))
                continue;

            Bounds bounds = renderer.bounds;
            Vector3 size = bounds.size;
            float shortSide = Mathf.Min(size.x, size.z);
            float longSide = Mathf.Max(size.x, size.z);
            bool named = BaseName(renderer.gameObject.name) == "Teacher Supremacy";
            bool shaped = size.y is > 0.12f and < 0.4f && bounds.min.y is > -0.15f and < 0.15f &&
                          shortSide is > 0.9f and < 2.2f && longSide is > 3.5f and < 9f;
            if (!named && !shaped)
                continue;
            Debug.Log($"[RouteSceneBuilder] Dais candidate {renderer.name} size {size}.");

            Vector3 along = size.x >= size.z ? Vector3.right : Vector3.forward;
            Vector3 across = size.x >= size.z ? Vector3.forward : Vector3.right;

            // Top of the dais from its collision (fall back to the mesh top).
            float topY = bounds.max.y;
            if (Physics.Raycast(new Vector3(bounds.center.x, bounds.max.y + 1f, bounds.center.z), Vector3.down, out RaycastHit top, 2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Mathf.Abs(top.point.y - bounds.max.y) < 0.2f)
                topY = top.point.y;

            // The room side is the long side with reachable floor next to it.
            Vector3 center = new(bounds.center.x, topY, bounds.center.z);
            float offset = shortSide * 0.5f + 0.4f;
            float plusDistance = ReachDistance(center + across * offset);
            float minusDistance = ReachDistance(center - across * offset);
            if (float.IsPositiveInfinity(plusDistance) && float.IsPositiveInfinity(minusDistance))
            {
                Debug.Log($"[RouteSceneBuilder] Dais {renderer.name}: no reachable floor beside it.");
                continue;
            }
            Vector3 roomSide = plusDistance <= minusDistance ? across : -across;
            Quaternion rotation = Quaternion.LookRotation(-roomSide, Vector3.up);

            // Near one end, so the middle of the dais stays free; pick the end that has room.
            bool placed = false;
            foreach (float end in new[] { 1f, -1f })
            {
                Vector3 position = center + along * (end * (longSide * 0.5f - 1.1f));
                // Bench footprint (1.6 x 0.7), oriented along the dais, kept on top of it.
                if (Physics.CheckBox(position + Vector3.up * 0.65f, new Vector3(0.78f, 0.45f, 0.33f), rotation,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;

                // Bench model: long side along local X, front toward local -Z (faces the room).
                spots.Add((position, rotation));
                placed = true;
                break;
            }

            if (!placed)
                Debug.Log($"[RouteSceneBuilder] Dais {renderer.name}: both ends blocked.");
        }

        return spots;
    }

    private static float ReachDistance(Vector3 position)
    {
        if (grid == null || !grid.TryNearestReachable(position, 2f, out Vector3 floor))
            return float.PositiveInfinity;
        return Vector3.Distance(Flat(floor), Flat(position));
    }

    private static Transform ContentRootTransform
    {
        get
        {
            GameObject root = GameObject.Find(ContentRootName);
            return root != null ? root.transform : null;
        }
    }

    private sealed class EnvObject
    {
        public EnvKind Kind;
        public Bounds Bounds;
        public string Label;
        public bool UsedByPc;
        public MeshRenderer Source;
    }

    /// <summary>
    /// Desks, ceiling fans and windows in the floor model, by object name (Blender names survive the
    /// import): classroom benches "Shorter"/"Longer", cabin desks "Cube" of desk size, fan "Blades",
    /// and "window". Only ones next to floor the Runner can reach are kept.
    /// </summary>
    private static List<EnvObject> FindEnvObjects(Transform contentRoot)
    {
        List<EnvObject> found = new();
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (renderer.transform.IsChildOf(contentRoot) || renderer.GetComponentInParent<NetworkObject>() != null)
                continue;

            string name = BaseName(renderer.gameObject.name);
            Bounds bounds = renderer.bounds;
            Vector3 size = bounds.size;
            EnvObject env = null;

            if (name == "Shorter" || name == "Longer")
            {
                env = new EnvObject { Kind = EnvKind.Desk, Label = "Desk" };
            }
            else if (name == "Cube" && size.y is > 0.3f and < 0.95f && bounds.min.y is > -0.3f and < 0.25f &&
                     Mathf.Max(size.x, size.z) < 2.2f && Mathf.Min(size.x, size.z) > 0.35f)
            {
                env = new EnvObject { Kind = EnvKind.Desk, Label = "Office desk" };
            }
            else if (name == "Blades")
            {
                env = new EnvObject { Kind = EnvKind.Fan, Label = "Fan" };
            }
            else if (name == "window")
            {
                env = new EnvObject { Kind = EnvKind.Window, Label = "Window" };
            }

            if (env == null)
                continue;

            float reach = env.Kind switch
            {
                EnvKind.Desk => Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.9f,
                EnvKind.Fan => 2.5f,
                _ => 1.6f,
            };
            if (grid == null || !grid.TryNearestReachable(new Vector3(bounds.center.x, 0f, bounds.center.z), reach, out _))
                continue;

            // Slightly bigger boxes so aiming is forgiving; windows get a thicker box than the glass.
            Vector3 grow = env.Kind == EnvKind.Window
                ? new Vector3(size.x < size.z ? 0.3f : 0.05f, 0.05f, size.z < size.x ? 0.3f : 0.05f)
                : Vector3.one * 0.1f;
            bounds.size = new Vector3(Mathf.Max(size.x, grow.x), size.y, Mathf.Max(size.z, grow.z)) + Vector3.one * 0.05f;

            // Desks: the whole desk (bounding box of its mesh) is the hitbox; simple and easy to aim at.
            env.Bounds = bounds;
            env.Source = renderer;
            found.Add(env);
        }

        return found;
    }

    private static string BaseName(string name)
    {
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)(\.\d+)*(\s\(\d+\))?$");
        return match.Success ? match.Groups[1].Value : name;
    }

    /// <summary>Spread-out desks with a flat top for the PCs (farthest-point sampling).</summary>
    private static List<(EnvObject desk, DeskTop top)> PickPcDesks(List<EnvObject> envObjects, int count)
    {
        List<(EnvObject desk, DeskTop top)> candidates = new();
        foreach (EnvObject env in envObjects)
        {
            if (env.Kind == EnvKind.Desk && TryDeskTopRegion(env.Source, out DeskTop top, true))
                candidates.Add((env, top));
        }

        List<(EnvObject desk, DeskTop top)> chosen = new();
        if (candidates.Count == 0)
            return chosen;

        // Start near the old first PC spot (office area), then keep picking the farthest desk.
        Vector3 start = new(-8f, 0f, -6f);
        chosen.Add(candidates.OrderBy(c => Vector3.Distance(Flat(c.top.Center), start)).First());
        while (chosen.Count < count && chosen.Count < candidates.Count)
        {
            (EnvObject desk, DeskTop top) best = default;
            float bestDistance = -1f;
            foreach ((EnvObject desk, DeskTop top) candidate in candidates)
            {
                if (chosen.Any(c => c.desk == candidate.desk))
                    continue;
                float nearest = chosen.Min(c => Vector3.Distance(Flat(c.top.Center), Flat(candidate.top.Center)));
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = candidate;
                }
            }

            if (best.desk == null)
                break;
            chosen.Add(best);
        }

        return chosen;
    }

    private struct DeskTop
    {
        public Vector3 Center;   // on the top surface
        public Vector3 Along;    // world X or Z, the desk's long direction
        public float HalfLong;
        public float HalfShort;
    }

    /// <summary>
    /// The desk's writing surface from its visible mesh: upward-facing triangles at the top height,
    /// rasterised on a 5 cm grid; the largest connected patch is the desk top. Benches that include
    /// a seat give the desk part, not the seat.
    /// </summary>
    private static bool TryDeskTopRegion(MeshRenderer renderer, out DeskTop top, bool forPc)
    {
        top = default;
        MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null)
            return false;

        Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
        Vector3[] verts = mesh.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
        int[] tris = mesh.triangles;
        Bounds bounds = renderer.bounds;
        float maxY = bounds.max.y;
        const float cell = 0.05f;
        int nx = Mathf.CeilToInt(bounds.size.x / cell) + 1;
        int nz = Mathf.CeilToInt(bounds.size.z / cell) + 1;
        if (nx * nz > 400000)
            return false;
        bool[] filled = new bool[nx * nz];

        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
            if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < maxY - 0.03f)
                continue;
            if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) < 0.9f)
                continue;

            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - bounds.min.x) / cell);
            int x1 = Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - bounds.min.x) / cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - bounds.min.z) / cell);
            int z1 = Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - bounds.min.z) / cell);
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(nx - 1, x1); x++)
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(nz - 1, z1); z++)
            {
                Vector2 p = new(bounds.min.x + (x + 0.5f) * cell, bounds.min.z + (z + 0.5f) * cell);
                if (InTriangle(p, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z)))
                    filled[x * nz + z] = true;
            }
        }

        // Largest connected patch.
        int[] label = new int[filled.Length];
        int bestLabel = 0, bestSize = 0, next = 0;
        Queue<int> queue = new();
        (int dx, int dz)[] neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int i = 0; i < filled.Length; i++)
        {
            if (!filled[i] || label[i] != 0)
                continue;

            next++;
            int size = 0;
            label[i] = next;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                size++;
                int cx = current / nz, cz = current % nz;
                foreach ((int dx, int dz) in neighbours)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= nx || z >= nz)
                        continue;
                    int n = x * nz + z;
                    if (!filled[n] || label[n] != 0)
                        continue;
                    label[n] = next;
                    queue.Enqueue(n);
                }
            }

            if (size > bestSize)
            {
                bestSize = size;
                bestLabel = next;
            }
        }

        if (bestSize == 0)
            return false;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] != bestLabel)
                continue;
            float x = bounds.min.x + (i / nz + 0.5f) * cell;
            float z = bounds.min.z + (i % nz + 0.5f) * cell;
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        float sizeX = maxX - minX + cell, sizeZ = maxZ - minZ + cell;
        // A PC needs room for a monitor and tower; a searchable desk just needs a real top.
        if (forPc ? Mathf.Max(sizeX, sizeZ) < 0.9f || Mathf.Min(sizeX, sizeZ) < 0.3f
                  : Mathf.Max(sizeX, sizeZ) < 0.4f || Mathf.Min(sizeX, sizeZ) < 0.2f)
            return false;

        Vector3 center = new((minX + maxX) * 0.5f, maxY, (minZ + maxZ) * 0.5f);
        if (forPc && Physics.CheckBox(center + Vector3.up * 0.45f, new Vector3(sizeX * 0.4f, 0.35f, sizeZ * 0.4f), Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        top = new DeskTop
        {
            Center = center,
            Along = sizeX >= sizeZ ? Vector3.right : Vector3.forward,
            HalfLong = Mathf.Max(sizeX, sizeZ) * 0.5f,
            HalfShort = Mathf.Min(sizeX, sizeZ) * 0.5f,
        };
        return true;
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0;
        bool positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    }

    private sealed class PcSetMesh
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Vector3 ScreenNormal;
    }

    /// <summary>
    /// Cuts the computer (everything standing on the desk top) out of R3_HackStation.fbx into its
    /// own mesh asset, pivot at the bottom, so it can stand on any desk. Null when the model is missing.
    /// </summary>
    private static PcSetMesh BuildPcSetMesh()
    {
        string path = $"{RoutesRoot}/Route03_Hacker/Models/R3_HackStation.fbx";
        GameObject asset = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        if (asset == null)
            return null;

        GameObject temp = (GameObject)Object.Instantiate(asset);
        try
        {
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            List<(Vector3[] v, Vector3[] n, Vector2[] uv, Material mat)> triangles = new();
            float deskTop = float.NegativeInfinity;

            foreach (MeshFilter filter in temp.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh source = filter.sharedMesh;
                if (renderer == null || source == null)
                    continue;

                Matrix4x4 toRoot = filter.transform.localToWorldMatrix;
                Vector3[] verts = source.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
                Vector3[] normals = source.normals.Length == verts.Length
                    ? source.normals.Select(n => toRoot.MultiplyVector(n).normalized).ToArray()
                    : new Vector3[verts.Length];
                Vector2[] uvs = source.uv.Length == verts.Length ? source.uv : new Vector2[verts.Length];
                Material[] mats = renderer.sharedMaterials;

                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    Material mat = sub < mats.Length ? mats[sub] : null;
                    int[] tris = source.GetTriangles(sub);
                    if (mat != null && mat.name.IndexOf("DeskTop", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (int index in tris)
                            deskTop = Mathf.Max(deskTop, verts[index].y);
                    }

                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        triangles.Add((new[] { verts[a], verts[b], verts[c] }, new[] { normals[a], normals[b], normals[c] },
                            new[] { uvs[a], uvs[b], uvs[c] }, mat));
                    }
                }
            }

            if (float.IsNegativeInfinity(deskTop))
                deskTop = 0.75f;

            List<Material> materialsOut = new();
            List<List<int>> trisOut = new();
            List<Vector3> vOut = new();
            List<Vector3> nOut = new();
            List<Vector2> uvOut = new();
            Vector3 screenNormal = Vector3.zero;

            foreach ((Vector3[] v, Vector3[] n, Vector2[] uv, Material mat) tri in triangles)
            {
                if (Mathf.Min(tri.v[0].y, Mathf.Min(tri.v[1].y, tri.v[2].y)) < deskTop - 0.003f)
                    continue;

                int slot = materialsOut.IndexOf(tri.mat);
                if (slot < 0)
                {
                    slot = materialsOut.Count;
                    materialsOut.Add(tri.mat);
                    trisOut.Add(new List<int>());
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    trisOut[slot].Add(vOut.Count);
                    vOut.Add(tri.v[corner] - Vector3.up * deskTop);
                    nOut.Add(tri.n[corner]);
                    uvOut.Add(tri.uv[corner]);
                }

                if (tri.mat != null && tri.mat.name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                    screenNormal += tri.n[0] + tri.n[1] + tri.n[2];
            }

            if (vOut.Count == 0)
                return null;

            string meshPath = $"{RoutesRoot}/Shared/Meshes/R3_PC_Set.asset";
            EnsureFolder($"{RoutesRoot}/Shared/Meshes");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            mesh.Clear();
            mesh.indexFormat = vOut.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vOut);
            mesh.SetNormals(nOut);
            mesh.SetUVs(0, uvOut);
            mesh.subMeshCount = trisOut.Count;
            for (int slot = 0; slot < trisOut.Count; slot++)
                mesh.SetTriangles(trisOut[slot], slot);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            return new PcSetMesh
            {
                Mesh = mesh,
                Materials = materialsOut.ToArray(),
                ScreenNormal = screenNormal.sqrMagnitude > 0.0001f ? screenNormal.normalized : Vector3.back,
            };
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    // ------------------------------------------------------------------ pieces

    private static RouteInteractable NewStep(Transform parent, string name, Vector3 position, List<string> warnings, bool snap = true)
    {
        GameObject step = new(name);
        step.transform.SetParent(parent, false);
        step.transform.position = snap ? SnapToFloor(position, name, warnings) : position;
        step.AddComponent<NetworkObject>();
        return step.AddComponent<RouteInteractable>();
    }

    private static void BuildExit(Transform parent, string name, Vector3 position, string route, RouteInteractable[] required,
        string blockedPrompt, Materials materials, bool ladderExit = false)
    {
        GameObject exit = new(name);
        exit.transform.SetParent(parent, false);
        exit.transform.position = new Vector3(position.x, Mathf.Max(position.y, 0.2f), position.z);
        exit.AddComponent<NetworkObject>();
        NetworkEscapePoint escape = exit.AddComponent<NetworkEscapePoint>();
        // Walking into an open exit (down the stairs, into the lift, onto the ladder) escapes too.
        escape.Configure(route, required, blockedPrompt, 2.0f);

        if (ladderExit)
        {
            // Hitbox over the top of the ladder (railing height and a bit below).
            BoxCollider grab = exit.AddComponent<BoxCollider>();
            grab.center = new Vector3(0f, 0.3f, 0f);
            grab.size = new Vector3(0.9f, 1.6f, 0.6f);
            return;
        }

        GameObject sign = AddModel(exit.transform, "Shared", "S_ExitSign",
            () => Box("Exit Sign", new Vector3(0.6f, 0.25f, 0.6f), new Vector3(0f, 0.9f, 0f), materials.ExitGlow));
        EnsureCollider(sign);
    }

    /// <summary>
    /// Finds every stairwell from the floor's stair collision ("Collision - Stairs*" boxes and any
    /// collider going below the floor), groups nearby pieces, and assigns two of them as route exits.
    /// </summary>
    private static bool DetectStairwells(List<string> warnings)
    {
        List<Bounds> pieces = new();
        foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!collider.enabled || collider.isTrigger || collider.transform.root.name == ContentRootName)
                continue;
            if (!collider.name.StartsWith("Collision - Stairs") && !collider.transform.name.StartsWith("Stairs"))
                continue;
            pieces.Add(collider.bounds);
        }

        // Merge pieces that touch or are within 1.5 m into stairwells.
        List<Bounds> wells = new();
        foreach (Bounds piece in pieces)
        {
            Bounds grown = piece;
            grown.Expand(new Vector3(3f, 0f, 3f));
            int match = wells.FindIndex(well => well.Intersects(grown));
            if (match >= 0)
            {
                Bounds merged = wells[match];
                merged.Encapsulate(piece);
                wells[match] = merged;
            }
            else
            {
                wells.Add(piece);
            }
        }

        wells = wells.Where(well => well.min.y < -0.4f).ToList();
        if (wells.Count < 2)
            return false;

        StairSpec ToSpec(Bounds well, string name) => new(name, well.min.x, well.max.x, well.min.z, well.max.z);
        int locksmith = Nearest(wells, LocksmithStairsHint);
        StairsLocksmith = ToSpec(wells[locksmith], "Stairs (Locksmith exit)");
        List<Bounds> rest = wells.Where((_, index) => index != locksmith).ToList();
        int blackout = Nearest(rest, BlackoutStairsHint);
        StairsBlackout = ToSpec(rest[blackout], "Stairs (Blackout exit)");
        SealedStairs = rest.Where((_, index) => index != blackout)
            .Select((well, index) => ToSpec(well, $"Stairs {index + 1}"))
            .ToArray();
        warnings.Add($"Stairwells found: {wells.Count} (2 route exits, {SealedStairs.Length} sealed).");
        return true;
    }

    private static int Nearest(List<Bounds> wells, Vector3 hint)
    {
        int best = 0;
        for (int index = 1; index < wells.Count; index++)
        {
            if (Vector3.Distance(Flat(wells[index].center), hint) < Vector3.Distance(Flat(wells[best].center), hint))
                best = index;
        }

        return best;
    }

    private enum StairBlock
    {
        Boards,
        Shutter,
        GoldDoor,
    }

    /// <summary>
    /// Blocks a stairwell: a collider over the stairwell itself, plus a visible blocker (boards, the
    /// power shutter or the Locksmith route's gold door) across the entrance of the passage leading
    /// to it, or across the stairwell's open side when it opens straight onto a wide area.
    /// </summary>
    private static GameObject BuildStairBarrier(Transform parent, StairSpec stair, Materials materials, string name, StairBlock kind = StairBlock.Boards)
    {
        GameObject barrier = new(name);
        barrier.transform.SetParent(parent, false);
        barrier.transform.position = stair.Center;
        BoxCollider blocker = barrier.AddComponent<BoxCollider>();
        blocker.size = stair.Size;

        List<(Vector3 center, Vector3 facing, float width)> placements = StairBlockerStore.TryGet(name, out var saved)
            ? saved
            : FindStairEntrances(stair);
        foreach ((Vector3 center, Vector3 facing, float width) in placements)
        {
            GameObject visual = kind switch
            {
                StairBlock.Shutter => ShutterPanel(center, facing, width, materials),
                StairBlock.GoldDoor => GoldDoorPanel(center, facing, width, materials),
                _ => BoardPanel("Boards", center, facing, width, materials, true),
            };
            visual.transform.SetParent(barrier.transform, true);
            stairLog.AppendLine($"{name}: {kind} at ({center.x:0.0}, {center.z:0.0}) width {width:0.0} m facing ({facing.x:0}, {facing.z:0})");
        }

        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/stair_blocks.txt"), stairLog.ToString());
        return barrier;
    }

    private static readonly System.Text.StringBuilder stairLog = new();

    /// <summary>
    /// Every side of a stairwell that players can walk up to (from the walk grid; the most open side
    /// when there is no grid), each followed out along its passage until it widens: those mouths are
    /// where blockers go. Returns floor centre, direction facing the stairs, and width per entrance.
    /// </summary>
    private static List<(Vector3 center, Vector3 facing, float width)> FindStairEntrances(StairSpec stair)
    {
        List<(Vector3 center, Vector3 facing, float width)> result = new();
        Physics.SyncTransforms();
        var sides = StairSides(stair);
        foreach (int side in StairEntranceSides(stair))
        {
            Vector3 across = Vector3.Cross(Vector3.up, sides[side].outward) * (sides[side].length * 0.5f);
            Vector3 a = sides[side].point - across, b = sides[side].point + across;
            // Right in front of the stairs on this side (reliable on every layout); move a blocker by
            // hand and use Tools/Routes/Save Stair Blocker Positions to put it somewhere else.
            float floorY = Physics.Raycast(new Vector3(sides[side].point.x, 1f, sides[side].point.z), Vector3.down, out RaycastHit hit, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
            (Vector3 center, Vector3 facing, float width) mouth =
                (new Vector3(sides[side].point.x, floorY, sides[side].point.z), -sides[side].outward, sides[side].length);
            if (result.All(other => Vector3.Distance(Flat(other.center), Flat(mouth.center)) > 1f))
                result.Add(mouth);
        }

        return result;
    }

    private static (Vector3 point, Vector3 outward, float length)[] StairSides(StairSpec stair)
    {
        const float pad = 0.25f;
        return new[]
        {
            (new Vector3(stair.MaxX + pad, 1f, (stair.MinZ + stair.MaxZ) * 0.5f), Vector3.right, stair.MaxZ - stair.MinZ + 0.4f),
            (new Vector3(stair.MinX - pad, 1f, (stair.MinZ + stair.MaxZ) * 0.5f), Vector3.left, stair.MaxZ - stair.MinZ + 0.4f),
            (new Vector3((stair.MinX + stair.MaxX) * 0.5f, 1f, stair.MaxZ + pad), Vector3.forward, stair.MaxX - stair.MinX + 0.4f),
            (new Vector3((stair.MinX + stair.MaxX) * 0.5f, 1f, stair.MinZ - pad), Vector3.back, stair.MaxX - stair.MinX + 0.4f),
        };
    }

    private static List<int> StairEntranceSides(StairSpec stair)
    {
        var sides = StairSides(stair);
        List<int> reachable = new();
        for (int index = 0; index < sides.Length; index++)
        {
            Vector3 probe = sides[index].point + sides[index].outward * 0.4f;
            bool walled = Physics.Raycast(sides[index].point - sides[index].outward * 0.35f, sides[index].outward, 0.9f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (!walled && ReachDistance(new Vector3(probe.x, 0f, probe.z)) <= 0.7f)
                reachable.Add(index);
        }

        if (reachable.Count > 0)
            return reachable;

        int open = 0;
        float bestRun = -1f;
        for (int index = 0; index < sides.Length; index++)
        {
            float run = Physics.Raycast(sides[index].point, sides[index].outward, out RaycastHit hit, 12f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 12f;
            if (run > bestRun)
            {
                bestRun = run;
                open = index;
            }
        }

        return new List<int> { open };
    }

    /// <summary>
    /// From one stairwell side, follow the stair passage outward:
    /// - an opening in one side wall (the usual case: a short landing with a doorway off it) gets the
    ///   blocker across that opening;
    /// - the passage opening onto a wide area gets the blocker across the passage end;
    /// - a stairwell that opens straight onto a wide area gets it across the stair side.
    /// </summary>
    private static (Vector3 center, Vector3 facing, float width) FindStairEntrance(StairSpec stair, int open)
    {
        var sides = StairSides(stair);
        Vector3 start = sides[open].point;                  // y = 1 m, middle of the side
        Vector3 outward = sides[open].outward;
        Vector3 across = Vector3.Cross(Vector3.up, outward);
        float sideLength = sides[open].length;

        static float Ray(Vector3 from, Vector3 direction, float max = 8f) =>
            Physics.Raycast(from, direction, out RaycastHit hit, max, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.distance : max;

        (Vector3, Vector3, float) Result(Vector3 center, Vector3 facing, float width)
        {
            float floorY = Physics.Raycast(new Vector3(center.x, 1f, center.z), Vector3.down, out RaycastHit floor, 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? floor.point.y : 0f;
            return (new Vector3(center.x, floorY, center.z), facing, Mathf.Clamp(width, 0.8f, 6f));
        }

        float l0 = Ray(start, -across), r0 = Ray(start, across);
        if (l0 + r0 > sideLength + 1.5f)
            return Result(start, -outward, sideLength);      // opens straight onto a wide area

        const float step = 0.2f;
        Vector3 lastCenter = start + across * ((r0 - l0) * 0.5f);
        float lastWidth = l0 + r0;
        for (float d = step; d <= 12f; d += step)
        {
            Vector3 probe = start + outward * d;
            float l = Ray(probe, -across), r = Ray(probe, across);
            bool leftOpen = l > l0 + 1.0f, rightOpen = r > r0 + 1.0f;
            if (leftOpen && rightOpen)
                return Result(lastCenter, -outward, lastWidth);

            if (leftOpen || rightOpen)
            {
                Vector3 sideDir = rightOpen ? across : -across;
                float wall = rightOpen ? r0 : l0;
                float endD = d;
                for (float e = d + step; e <= d + 6f; e += step)
                {
                    if (Ray(start + outward * e, sideDir) <= wall + 1.0f)
                        break;
                    endD = e;
                }

                float mid = (d + endD) * 0.5f;
                return Result(start + outward * mid + sideDir * wall, -sideDir, endD - d + step + 0.3f);
            }

            if (Ray(probe, outward, 0.25f) < 0.25f)
                break;                                         // dead end with no opening

            lastCenter = probe + across * ((r - l) * 0.5f);
            lastWidth = l + r;
        }

        return Result(start, -outward, sideLength);
    }

    private static GameObject ShutterPanel(Vector3 center, Vector3 facing, float width, Materials materials)
    {
        GameObject root = new("Power Shutter");
        root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));
        GameObject model = AddModel(root.transform, "Route02_Blackout", "R2_Shutter",
            () => Box("Shutter", new Vector3(1f, 2.2f, 0.06f), new Vector3(0f, 1.1f, 0f), materials.Shutter));
        foreach (Collider part in model.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(part);
        model.transform.localScale = new Vector3(width, 1f, 1f);
        BoxCollider blocker = root.AddComponent<BoxCollider>();
        blocker.center = new Vector3(0f, 1.1f, 0f);
        blocker.size = new Vector3(width, 2.2f, 0.3f);
        return root;
    }

    /// <summary>The Locksmith route's door: gold (matches the Gold Door Key), boarded, padlocked.</summary>
    private static GameObject GoldDoorPanel(Vector3 center, Vector3 facing, float width, Materials materials)
    {
        GameObject root = new("Gold Door");
        root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));
        void Part(string name, Vector3 size, Vector3 position, Material material)
        {
            GameObject part = Box(name, size, position, material);
            part.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        Part("Frame L", new Vector3(0.1f, 2.2f, 0.16f), new Vector3(-width * 0.5f + 0.05f, 1.1f, 0f), materials.WoodDark);
        Part("Frame R", new Vector3(0.1f, 2.2f, 0.16f), new Vector3(width * 0.5f - 0.05f, 1.1f, 0f), materials.WoodDark);
        Part("Frame Top", new Vector3(width, 0.12f, 0.16f), new Vector3(0f, 2.16f, 0f), materials.WoodDark);
        Part("Door", new Vector3(width - 0.2f, 2.08f, 0.06f), new Vector3(0f, 1.04f, 0f), materials.GoldDoor);
        Part("Key Sign", new Vector3(0.34f, 0.18f, 0.02f), new Vector3(0f, 1.7f, -0.045f), materials.Brass);
        for (int index = 0; index < 3; index++)
        {
            GameObject plank = Box($"Plank {index + 1}", new Vector3(width + 0.1f, 0.16f, 0.04f),
                new Vector3(0f, 0.6f + index * 0.45f, -0.06f), materials.Wood);
            plank.transform.localRotation = Quaternion.Euler(0f, 0f, index % 2 == 0 ? 6f : -5f);
            plank.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(plank.GetComponent<Collider>());
        }

        Part("Padlock", new Vector3(0.12f, 0.14f, 0.06f), new Vector3(width * 0.25f, 1.05f, -0.1f), materials.Brass);
        Part("Shackle", new Vector3(0.08f, 0.08f, 0.02f), new Vector3(width * 0.25f, 1.15f, -0.1f), materials.Metal);

        BoxCollider blocker = root.AddComponent<BoxCollider>();
        blocker.center = new Vector3(0f, 1.1f, -0.03f);
        blocker.size = new Vector3(width, 2.2f, 0.25f);
        return root;
    }

    /// <summary>
    /// Working doors with no floor the player can reach on one side lead nowhere: lock them for good
    /// (a DoorLockRule with the Boarded lock) and nail boards across them on the reachable side.
    /// Door-shaped collision with no working door gets the same boards.
    /// </summary>
    private static int BoardUpDeadDoors(Transform parent, Materials materials)
    {
        if (grid == null)
            return 0;

        System.Text.StringBuilder log = new();
        HashSet<Transform> seen = new();
        int count = 0;

        void Board(Bounds leaf, string what, bool lockDoor)
        {
            bool thinX = leaf.size.x < leaf.size.z;
            Vector3 normal = thinX ? Vector3.right : Vector3.forward;
            float front = ReachDistance(leaf.center + normal * 0.8f);
            float back = ReachDistance(leaf.center - normal * 0.8f);
            bool frontOk = front <= 0.6f, backOk = back <= 0.6f;
            if (frontOk == backOk)
                return;                                   // both sides usable, or neither: leave it

            Vector3 side = frontOk ? normal : -normal;
            float width = (thinX ? leaf.size.z : leaf.size.x) + 0.3f;
            float floorY = grid.TryNearestReachable(leaf.center + side * 0.8f, 1.5f, out Vector3 floor) ? floor.y : 0f;
            GameObject panel = BoardPanel($"Boarded Door {count + 1}", new Vector3(leaf.center.x, floorY, leaf.center.z) + side * 0.1f,
                side, width, materials, false);
            panel.transform.SetParent(parent, true);
            if (lockDoor)
            {
                GameObject rule = new($"Boarded Door Lock {count + 1}");
                rule.transform.SetParent(parent, false);
                rule.transform.position = leaf.center;
                rule.AddComponent<DoorLockRule>().Configure(ItemIds.Boarded, 1.0f);
            }

            log.AppendLine($"{panel.name}: {what} at ({leaf.center.x:0.0}, {leaf.center.z:0.0})");
            count++;
        }

        foreach (NetworkLockedDoor door in Object.FindObjectsByType<NetworkLockedDoor>(FindObjectsInactive.Exclude))
        {
            if (door.TryGetEditorLeafBounds(out Bounds leaf, out Transform leafTransform) && seen.Add(leafTransform))
                Board(leaf, "door leading nowhere", true);
        }

        foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!collider.enabled || !collider.name.StartsWith("Collision - Door", StringComparison.Ordinal))
                continue;
            if (Object.FindObjectsByType<NetworkLockedDoor>(FindObjectsInactive.Exclude).Any(d => d.OwnsCollider(collider)))
                continue;
            Board(collider.bounds, "door with no working door", false);
        }

        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/boarded.txt"), log.ToString());
        return count;
    }

    /// <summary>
    /// A boarded-up opening: 2 posts and 4 slightly crooked planks, base on the floor, local X along
    /// the opening, facing <paramref name="facing"/>. Optionally solid.
    /// </summary>
    private static GameObject BoardPanel(string name, Vector3 basePoint, Vector3 facing, float length, Materials materials, bool solid)
    {
        GameObject panel = new(name);
        panel.transform.SetPositionAndRotation(basePoint, Quaternion.LookRotation(Flat(facing).normalized, Vector3.up));
        System.Random random = new(Mathf.RoundToInt(basePoint.x * 37f + basePoint.z * 91f));

        foreach (float x in new[] { -length * 0.5f + 0.12f, length * 0.5f - 0.12f })
        {
            GameObject post = Box("Post", new Vector3(0.1f, 2.05f, 0.08f), new Vector3(x, 1.025f, 0.05f), materials.WoodDark);
            post.transform.SetParent(panel.transform, false);
            Object.DestroyImmediate(post.GetComponent<Collider>());
        }

        float[] heights = { 0.4f, 0.85f, 1.3f, 1.75f };
        for (int index = 0; index < heights.Length; index++)
        {
            float tilt = (float)(random.NextDouble() * 10.0 - 5.0);
            float shift = (float)(random.NextDouble() * 0.16 - 0.08);
            GameObject plank = Box($"Plank {index + 1}", new Vector3(length + 0.2f, 0.18f, 0.04f),
                new Vector3(shift, heights[index], -0.01f * (index % 2)), materials.Wood);
            plank.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            plank.transform.SetParent(panel.transform, false);
            Object.DestroyImmediate(plank.GetComponent<Collider>());
        }

        if (solid)
        {
            BoxCollider blocker = panel.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, 1.05f, 0.03f);
            blocker.size = new Vector3(length + 0.2f, 2.1f, 0.15f);
        }

        return panel;
    }

    private static GameObject Glow(Transform parent, Vector3 localPosition, Materials materials)
    {
        GameObject glow = Box("Hint Glow", new Vector3(0.15f, 0.15f, 0.02f), localPosition, materials.Screen);
        glow.transform.SetParent(parent, false);
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        return glow;
    }

    /// <summary>
    /// Item spots: desk/shelf tops anywhere on the floor, plus open floor inside named rooms, plus
    /// a few fixed spots in the janitor room. Everything comes from the current collision, so a
    /// new floor model gives new spots when the content is rebuilt.
    /// </summary>
    private static int BuildSpawnPoints(Transform parent, Transform contentRoot)
    {
        Bounds world = FloorBounds(contentRoot);
        List<(Vector3 point, string room, bool desk)> found = new();
        const float step = 1.7f;

        for (float x = world.min.x + 0.5f; x <= world.max.x - 0.5f; x += step)
        {
            for (float z = world.min.z + 0.5f; z <= world.max.z - 0.5f; z += step)
            {
                if (!TrySample(new Vector3(x, 0f, z), contentRoot, out Vector3 point, out bool desk))
                    continue;

                string room = RoomAt(point);
                if (room == JanitorRoom)
                    continue;
                if (room == null && !desk)
                    continue;

                found.Add((point, room ?? "Corridor", desk));
            }
        }

        // Spread the spots: at most 6 per room, desks first.
        int total = 0;
        foreach (IGrouping<string, (Vector3 point, string room, bool desk)> group in found.GroupBy(spot => spot.room))
        {
            List<(Vector3 point, string room, bool desk)> ordered = group.OrderByDescending(spot => spot.desk).ToList();
            int keep = Mathf.Min(6, ordered.Count);
            int stride = Mathf.Max(1, ordered.Count / keep);
            for (int index = 0, kept = 0; index < ordered.Count && kept < keep; index += stride, kept++)
            {
                AddSpot(parent, $"Spot - {group.Key} {kept + 1}", ordered[index].point,
                    SpawnCategory.SmallItem | SpawnCategory.Tool | SpawnCategory.Fuse, group.Key);
                total++;
            }
        }

        // Janitor room: fixed spots around the room centre, dropped to the floor.
        RoomSpec janitor = Rooms.First(room => room.Name == JanitorRoom);
        Vector3[] offsets = { new(-0.8f, 0f, 0.6f), new(0.8f, 0f, 0.6f), new(-0.8f, 0f, -0.3f), new(0.8f, 0f, -0.3f) };
        int reachableJanitorSpots = 0;
        for (int index = 0; index < offsets.Length; index++)
        {
            Vector3 spot = Flat(janitor.Center) + offsets[index];
            Vector3 point = spot + Vector3.up * 0.12f;
            if (TryFloorNear(spot, 1.0f, out Vector3 floor) && floor.x >= janitor.MinX && floor.x <= janitor.MaxX &&
                floor.z >= janitor.MinZ && floor.z <= janitor.MaxZ)
            {
                point = floor + Vector3.up * 0.12f;
                reachableJanitorSpots++;
            }

            AddSpot(parent, $"Spot - {JanitorRoom} {index + 1}", point, SpawnCategory.Janitor, JanitorRoom);
            total++;
        }

        if (reachableJanitorSpots < 3)
            janitorWarning = $"Only {reachableJanitorSpots} janitor-room spots are on reachable floor; check the Janitor Room bounds.";

        return total;
    }

    private static void AddSpot(Transform parent, string name, Vector3 position, SpawnCategory categories, string room)
    {
        GameObject spot = new(name);
        spot.transform.SetParent(parent, false);
        spot.transform.position = position;
        spot.AddComponent<RouteSpawnPoint>().Configure(categories, room, false);
    }

    private static string RoomAt(Vector3 point)
    {
        foreach (RoomSpec room in Rooms)
        {
            if (point.x >= room.MinX && point.x <= room.MaxX && point.z >= room.MinZ && point.z <= room.MaxZ)
                return room.Name;
        }

        return null;
    }

    private static Bounds FloorBounds(Transform contentRoot)
    {
        Bounds bounds = new(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (collider.isTrigger || collider.transform.IsChildOf(contentRoot))
                continue;

            if (!any)
            {
                bounds = collider.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private static bool TrySample(Vector3 xz, Transform contentRoot, out Vector3 point, out bool desk)
    {
        point = default;
        desk = false;
        Vector3 origin = new(xz.x, 2.4f, xz.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        // Floors, desks and low shelves only; skip our own barriers/props.
        if (hit.point.y < -0.3f || hit.point.y > 1.3f || hit.collider.transform.IsChildOf(contentRoot))
            return false;

        desk = hit.point.y > 0.3f;
        point = hit.point + Vector3.up * 0.12f;

        // Must be within arm's reach of floor the player can actually walk to from spawn.
        if (grid == null || !grid.TryNearestReachable(point, desk ? 1.3f : 0.45f, out _))
            return false;

        return !Physics.CheckSphere(point + Vector3.up * 0.25f, 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private static bool TryFloorNear(Vector3 position, float radius, out Vector3 floor)
    {
        floor = default;
        return grid != null && grid.TryNearestReachable(position, radius, out floor);
    }

    private static Vector3 SnapToFloor(Vector3 position, string label, List<string> warnings)
    {
        if (TryFloorNear(new Vector3(position.x, 0f, position.z), 6f, out Vector3 floor))
        {
            if (Vector3.Distance(Flat(floor), Flat(position)) > 0.6f)
                warnings.Add($"{label}: moved {Vector3.Distance(Flat(floor), Flat(position)):0.0} m to reachable floor.");
            return floor;
        }

        warnings.Add($"{label}: no reachable floor within 6 m of ({position.x:0.0}, {position.z:0.0}); placed at y=0. Move it by hand.");
        return new Vector3(position.x, 0f, position.z);
    }

    private static WalkGrid grid;
    private static string janitorWarning;

    /// <summary>
    /// A 0.4 m grid of floor cells where the 1.6 m player capsule fits, flood-filled from the
    /// spawn markers with all doors treated as open. "Reachable" means a player can walk there.
    /// </summary>
    private sealed class WalkGrid
    {
        private const float Cell = 0.4f;
        private const float Radius = 0.28f;
        private Vector3 origin;
        private int countX;
        private int countZ;
        private float[] floorY;
        private bool[] walkable;
        private bool[] reachable;

        public int ReachableCells { get; private set; }
        public float ReachableArea => ReachableCells * Cell * Cell;

        public static WalkGrid Build(Transform contentRoot)
        {
            WalkGrid result = new();
            List<Collider> disabled = DisableDoorColliders();
            try
            {
                Bounds bounds = FloorBounds(contentRoot);
                result.origin = new Vector3(bounds.min.x, 0f, bounds.min.z);
                result.countX = Mathf.CeilToInt(bounds.size.x / Cell);
                result.countZ = Mathf.CeilToInt(bounds.size.z / Cell);
                int total = result.countX * result.countZ;
                result.floorY = new float[total];
                result.walkable = new bool[total];
                result.reachable = new bool[total];

                for (int x = 0; x < result.countX; x++)
                for (int z = 0; z < result.countZ; z++)
                {
                    Vector3 center = result.CellCenter(x, z);
                    int index = x * result.countZ + z;
                    if (!Physics.Raycast(new Vector3(center.x, 2.4f, center.z), Vector3.down, out RaycastHit hit, 3.2f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                        hit.point.y < -0.3f || hit.point.y > 0.3f)
                        continue;

                    if (Physics.CheckCapsule(hit.point + Vector3.up * (Radius + 0.12f), hit.point + Vector3.up * (1.55f - Radius), Radius,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;

                    result.walkable[index] = true;
                    result.floorY[index] = hit.point.y;
                }

                foreach (string marker in new[] { "Player Spawn Marker", "Granny Spawn Marker" })
                {
                    GameObject spawn = GameObject.Find(marker);
                    if (spawn != null)
                        result.Flood(spawn.transform.position);
                }
            }
            finally
            {
                foreach (Collider collider in disabled)
                    collider.enabled = true;
                Physics.SyncTransforms();
            }

            return result;
        }

        private static List<Collider> DisableDoorColliders()
        {
            // Only doors the Runner can really open count as open. Door-shaped collision with no
            // working NetworkLockedDoor (or one needing the old test key) stays solid, so rooms
            // behind it are not treated as reachable and get no items.
            NetworkLockedDoor[] doors = Object.FindObjectsByType<NetworkLockedDoor>(FindObjectsInactive.Exclude);
            List<Collider> disabled = new();
            int solidDoorParts = 0;
            foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (!collider.enabled)
                    continue;

                bool opens = doors.Any(door => door.OpensWithoutTestKey && door.OwnsCollider(collider));
                if (!opens)
                {
                    if (IsDoorPart(collider.transform))
                        solidDoorParts++;
                    continue;
                }

                collider.enabled = false;
                disabled.Add(collider);
            }

            Debug.Log($"[RouteSceneBuilder] Walk grid: {disabled.Count} colliders of {doors.Length} working doors treated as open; " +
                      $"{solidDoorParts} door-shaped colliders with no working door stay solid.");
            Physics.SyncTransforms();
            return disabled;
        }

        private static bool IsDoorPart(Transform target)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                string name = current.name;
                if (name.StartsWith("Collision - Door") || name.StartsWith("Door.") || name.StartsWith("Door_Group") ||
                    name.StartsWith("DoorHinge") || name == "Door")
                    return true;
            }

            return false;
        }

        private Vector3 CellCenter(int x, int z) => origin + new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);

        private bool TryCell(Vector3 position, out int x, out int z)
        {
            x = Mathf.FloorToInt((position.x - origin.x) / Cell);
            z = Mathf.FloorToInt((position.z - origin.z) / Cell);
            return x >= 0 && z >= 0 && x < countX && z < countZ;
        }

        private void Flood(Vector3 seedPosition)
        {
            // Start from the nearest walkable cell to the marker.
            int seed = -1;
            float best = 3f;
            for (int x = 0; x < countX; x++)
            for (int z = 0; z < countZ; z++)
            {
                int index = x * countZ + z;
                if (!walkable[index])
                    continue;
                float distance = Vector2.Distance(new Vector2(seedPosition.x, seedPosition.z),
                    new Vector2(CellCenter(x, z).x, CellCenter(x, z).z));
                if (distance >= best)
                    continue;
                best = distance;
                seed = index;
            }

            if (seed < 0 || reachable[seed])
                return;

            Queue<int> queue = new();
            queue.Enqueue(seed);
            reachable[seed] = true;
            ReachableCells++;
            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cx = current / countZ;
                int cz = current % countZ;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = cx + dx[direction];
                    int nz = cz + dz[direction];
                    if (nx < 0 || nz < 0 || nx >= countX || nz >= countZ)
                        continue;
                    int next = nx * countZ + nz;
                    if (reachable[next] || !walkable[next] || Mathf.Abs(floorY[next] - floorY[current]) > 0.3f)
                        continue;
                    reachable[next] = true;
                    ReachableCells++;
                    queue.Enqueue(next);
                }
            }
        }

        /// <summary>
        /// The entrance of a stair passage. Spreads over reachable floor from the cells along the
        /// stairwell side (a-b) in 0.4 m distance bands; each band's size is the passage width there.
        /// The mouth is the last narrow band before the floor opens up (or a doorway, which is the
        /// narrowest band). Returns its floor centre, the direction toward the stairs and its width.
        /// </summary>
        public bool FindMouth(Vector3 a, Vector3 b, out Vector3 center, out Vector3 towardStairs, out float width)
        {
            center = default;
            towardStairs = default;
            width = 0f;
            int total = countX * countZ;
            int[] dist = new int[total];
            for (int i = 0; i < total; i++)
                dist[i] = -1;

            Queue<int> queue = new();
            Vector2 a2 = new(a.x, a.z), b2 = new(b.x, b.z);
            for (int x = 0; x < countX; x++)
            for (int z = 0; z < countZ; z++)
            {
                int index = x * countZ + z;
                if (!reachable[index])
                    continue;
                Vector3 c = CellCenter(x, z);
                Vector2 p = new(c.x, c.z);
                Vector2 ab = b2 - a2;
                float t = Mathf.Clamp01(Vector2.Dot(p - a2, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                if (Vector2.Distance(p, a2 + ab * t) > 0.6f)
                    continue;
                dist[index] = 0;
                queue.Enqueue(index);
            }

            if (queue.Count == 0)
                return false;

            List<List<int>> bands = new();
            int visited = 0;
            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int d = dist[current];
                if (d > 30 || visited > 900)
                    break;
                while (bands.Count <= d)
                    bands.Add(new List<int>());
                bands[d].Add(current);
                visited++;
                int cx = current / countZ, cz = current % countZ;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + dx[k], nz = cz + dz[k];
                    if (nx < 0 || nz < 0 || nx >= countX || nz >= countZ)
                        continue;
                    int next = nx * countZ + nz;
                    if (!reachable[next] || dist[next] >= 0 || Mathf.Abs(floorY[next] - floorY[current]) > 0.3f)
                        continue;
                    dist[next] = d + 1;
                    queue.Enqueue(next);
                }
            }

            if (bands.Count < 3)
                return false;

            // Width of a band = its extent across the passage (perpendicular to the way out).
            float BandWidth(int k)
            {
                List<int> cells = bands[k];
                Vector3 mean = Mean(cells);
                Vector3 outward = k > 0 ? Flat(mean - Mean(bands[k - 1])) : Vector3.zero;
                Vector3 across = outward.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, outward.normalized) : Vector3.right;
                float min = float.MaxValue, max = float.MinValue;
                foreach (int cell in cells)
                {
                    float t2 = Vector3.Dot(CellCenter(cell / countZ, cell % countZ) - mean, across);
                    min = Mathf.Min(min, t2);
                    max = Mathf.Max(max, t2);
                }

                return max - min + Cell;
            }

            int last = bands.Count - 1;
            float[] widths = new float[bands.Count];
            for (int k = 1; k <= last; k++)
                widths[k] = BandWidth(k);

            float narrowest = float.MaxValue;
            int mouth = 1;
            for (int k = 1; k <= last; k++)
            {
                if (widths[k] > narrowest + 1.6f && k > 2)
                    break;                                    // the floor opens up: stop here
                if (widths[k] <= narrowest + 0.3f)
                {
                    if (widths[k] < narrowest)
                        narrowest = widths[k];
                    mouth = k;                               // keep the furthest narrow band
                }
            }

            Vector3 mouthMean = Mean(bands[mouth]);
            Vector3 back = Flat(Mean(bands[Mathf.Max(0, mouth - 1)]) - mouthMean);
            if (back.sqrMagnitude < 0.0001f)
                return false;

            float y = 0f;
            foreach (int cell in bands[mouth])
                y += floorY[cell];
            center = new Vector3(mouthMean.x, y / bands[mouth].Count, mouthMean.z);
            towardStairs = back.normalized;
            width = widths[mouth] + 0.3f;
            return true;
        }

        private Vector3 Mean(List<int> cells)
        {
            Vector3 sum = Vector3.zero;
            foreach (int cell in cells)
                sum += CellCenter(cell / countZ, cell % countZ);
            return sum / Mathf.Max(1, cells.Count);
        }

        private static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

        /// <summary>Debug: top-down map. 'o' reachable, '.' walkable but cut off, ' ' blocked/no floor.</summary>
        public string Dump()
        {
            System.Text.StringBuilder map = new();
            map.AppendLine($"origin x={origin.x:0.0} z={origin.z:0.0}, 2 cells (0.8 m) per char, top row = max z");
            for (int z = countZ - 1; z >= 0; z -= 2)
            {
                map.Append($"{origin.z + z * Cell,7:0.0} ");
                for (int x = 0; x < countX; x += 2)
                {
                    int index = x * countZ + z;
                    map.Append(reachable[index] ? 'o' : walkable[index] ? '.' : ' ');
                }

                map.AppendLine();
            }

            return map.ToString();
        }

        public bool TryNearestReachable(Vector3 position, float maxDistance, out Vector3 floor)
        {
            floor = default;
            if (!TryCell(position, out int cx, out int cz))
                return false;

            int range = Mathf.CeilToInt(maxDistance / Cell);
            float best = maxDistance;
            bool found = false;
            for (int x = Mathf.Max(0, cx - range); x <= Mathf.Min(countX - 1, cx + range); x++)
            for (int z = Mathf.Max(0, cz - range); z <= Mathf.Min(countZ - 1, cz + range); z++)
            {
                int index = x * countZ + z;
                if (!reachable[index])
                    continue;
                Vector3 center = CellCenter(x, z);
                float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(center.x, center.z));
                if (distance > best)
                    continue;
                best = distance;
                floor = new Vector3(center.x, floorY[index], center.z);
                found = true;
            }

            return found;
        }
    }

    // ------------------------------------------------------------------ pickups

    private static Dictionary<string, GameObject> BuildPickupPrefabs(Materials materials)
    {
        // Last value: the item's longest side after scaling. Deliberately larger than real life so
        // items read clearly from across a room.
        (string id, string folder, string model, Vector3 fallbackSize, Material fallback, float displaySize)[] items =
        {
            (ItemIds.Hammer, "Shared", "T_Hammer", new Vector3(0.12f, 0.35f, 0.08f), materials.Metal, 0.6f),
            (ItemIds.Screwdriver, "Shared", "T_Screwdriver", new Vector3(0.05f, 0.25f, 0.05f), materials.Hazard, 0.5f),
            (ItemIds.Crowbar, "Shared", "T_Crowbar", new Vector3(0.06f, 0.7f, 0.06f), materials.AlertGlow, 0.85f),
            (ItemIds.R2Fuse, "Route02_Blackout", "R2_Fuse", new Vector3(0.1f, 0.18f, 0.1f), materials.Brass, 0.4f),
            (ItemIds.R3Plank, "Route03_Hacker", "R3_Plank", new Vector3(1.0f, 0.03f, 0.15f), materials.Wood, 1.0f),
            (ItemIds.R3Nails, "Route03_Hacker", "R3_Nails", new Vector3(0.15f, 0.08f, 0.1f), materials.Metal, 0.35f),
        };

        Dictionary<string, GameObject> result = new();
        foreach (var item in items)
        {
            GameObject root = new("Pickup_" + item.id);
            GameObject visual = AddModel(root.transform, item.folder, item.model,
                () => Box(item.model, item.fallbackSize, Vector3.zero, item.fallback));
            visual.name = "Visual";
            visual.transform.SetAsFirstSibling();
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);

            // Normalise size, then sit the item's bottom on the spot.
            Bounds raw = RendererBounds(visual, root.transform.position);
            float longest = Mathf.Max(raw.size.x, Mathf.Max(raw.size.y, raw.size.z));
            if (longest > 0.0001f)
                visual.transform.localScale *= item.displaySize / longest;
            Bounds scaled = RendererBounds(visual, root.transform.position);
            visual.transform.position += new Vector3(root.transform.position.x - scaled.center.x,
                root.transform.position.y - scaled.min.y + 0.05f, root.transform.position.z - scaled.center.z);

            // Generous collider so items are easy to aim at.
            Bounds bounds = RendererBounds(visual, root.transform.position);
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center - root.transform.position;
            box.size = Vector3.Max(bounds.size + Vector3.one * 0.1f, Vector3.one * 0.45f);

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkPickup>().Configure(item.id, 1);

            string path = $"{PickupFolder}/Pickup_{item.id}.prefab";
            result[item.id] = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        return result;
    }

    private static void RegisterNetworkPrefabs(IEnumerable<GameObject> prefabs)
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabListPath);
        if (list == null)
        {
            Debug.LogWarning("RouteSceneBuilder: DefaultNetworkPrefabs.asset not found; register pickup prefabs manually.");
            return;
        }

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && !list.PrefabList.Any(entry => entry.Prefab == prefab))
                list.Add(new NetworkPrefab { Prefab = prefab });
        }

        EditorUtility.SetDirty(list);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Descendants whose name is the given name or a Blender duplicate of it ("Glass.001").</summary>
    private static GameObject[] FindChildren(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true)
            .Where(child => child != root && (child.name == name || child.name.StartsWith(name + ".")))
            .Select(child => child.gameObject)
            .ToArray();

    private static Vector3 Flat(Vector3 position) => new(position.x, 0f, position.z);

    private static ItemAmount[] Items(params (string id, int count)[] items) =>
        items.Select(item => new ItemAmount(item.id, item.count)).ToArray();

    private static Transform Child(Transform parent, string name)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    /// <summary>Instantiates Assets/Routes/{folder}/Models/{model}.fbx, or the fallback primitive.</summary>
    private static GameObject AddModel(Transform parent, string folder, string model, Func<GameObject> fallback)
    {
        string path = $"{RoutesRoot}/{folder}/Models/{model}.fbx";
        GameObject asset = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        GameObject instance = asset != null ? (GameObject)PrefabUtility.InstantiatePrefab(asset) : fallback();
        instance.name = model;
        instance.transform.SetParent(parent, false);
        return instance;
    }

    /// <summary>
    /// Copies the submesh of <paramref name="model"/> that uses a material whose name contains
    /// <paramref name="materialKeyword"/>, pushed 3 mm out along its normals, as a child with
    /// <paramref name="material"/>. Returns null when the model has no such submesh (fallback geometry).
    /// The mesh is saved as an asset (shared by every instance) so the prefab can reference it.
    /// </summary>
    private static GameObject OverlaySubmesh(GameObject model, string materialKeyword, Material material, string meshName)
    {
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            Mesh source = filter.sharedMesh;
            if (renderer == null || source == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            for (int sub = 0; sub < Mathf.Min(mats.Length, source.subMeshCount); sub++)
            {
                if (mats[sub] == null || mats[sub].name.IndexOf(materialKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string path = $"{RoutesRoot}/Shared/Meshes/{meshName}.asset";
                Mesh overlay = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (overlay == null)
                {
                    EnsureFolder($"{RoutesRoot}/Shared/Meshes");
                    overlay = new Mesh();
                    AssetDatabase.CreateAsset(overlay, path);
                }

                Vector3[] verts = source.vertices;
                Vector3[] normals = source.normals;
                Vector2[] uvs = source.uv;
                int[] tris = source.GetTriangles(sub);
                Dictionary<int, int> remap = new();
                List<Vector3> outVerts = new();
                List<Vector3> outNormals = new();
                List<Vector2> outUvs = new();
                List<int> outTris = new();
                foreach (int index in tris)
                {
                    if (!remap.TryGetValue(index, out int mapped))
                    {
                        mapped = outVerts.Count;
                        remap[index] = mapped;
                        Vector3 n = normals.Length > index ? normals[index] : Vector3.zero;
                        outVerts.Add(verts[index] + n * 0.003f);
                        outNormals.Add(n);
                        outUvs.Add(uvs.Length > index ? uvs[index] : Vector2.zero);
                    }
                    outTris.Add(mapped);
                }

                overlay.Clear();
                overlay.SetVertices(outVerts);
                overlay.SetNormals(outNormals);
                overlay.SetUVs(0, outUvs);
                overlay.SetTriangles(outTris, 0);
                overlay.RecalculateBounds();
                EditorUtility.SetDirty(overlay);

                GameObject glow = new("Screen Glow");
                glow.transform.SetParent(filter.transform, false);
                glow.AddComponent<MeshFilter>().sharedMesh = overlay;
                glow.AddComponent<MeshRenderer>().sharedMaterial = material;
                return glow;
            }
        }

        return null;
    }

    /// <summary>
    /// Turns <paramref name="step"/> about Y so the model's <paramref name="frontPart"/> side faces the
    /// open floor around it (the direction with reachable floor and no wall in the way).
    /// </summary>
    private static void FaceReachableFloor(Transform step, GameObject model, string frontPart)
    {
        step.rotation = Quaternion.identity;
        Bounds all = RendererBounds(model, step.position);
        GameObject[] front = FindChildren(model.transform, frontPart);
        Vector3 localFront = front.Length > 0 ? Flat(RendererBounds(front[0], all.center).center - all.center) : Vector3.back;
        if (localFront.sqrMagnitude < 0.0001f)
            localFront = Vector3.back;

        Vector3 best = Vector3.back;
        float bestScore = float.PositiveInfinity;
        foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            Vector3 origin = step.position + Vector3.up * 1.0f;
            float score = ReachDistance(step.position + direction * 1.0f);
            if (Physics.Raycast(origin + direction * (Mathf.Max(all.extents.x, all.extents.z) + 0.05f), direction, 0.8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                score += 10f;
            if (score < bestScore)
            {
                bestScore = score;
                best = direction;
            }
        }

        step.rotation = Quaternion.Euler(0f, Vector3.SignedAngle(localFront.normalized, best, Vector3.up), 0f);
    }

    private static void EnsureCollider(GameObject visual)
    {
        if (visual.GetComponentInChildren<Collider>() != null)
            return;

        Bounds bounds = RendererBounds(visual, visual.transform.position);
        BoxCollider box = visual.AddComponent<BoxCollider>();
        box.center = visual.transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = visual.transform.lossyScale;
        box.size = new Vector3(bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private static Bounds RendererBounds(GameObject target, Vector3 fallbackCenter)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(fallbackCenter, Vector3.one * 0.3f);

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static GameObject Box(string name, Vector3 size, Vector3 localPosition, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private sealed class Materials
    {
        public readonly Material Wood = Make("M_Route_Wood", new Color(0.55f, 0.36f, 0.2f), 0.75f);
        public readonly Material WoodDark = Make("M_Route_WoodDark", new Color(0.3f, 0.19f, 0.1f), 0.8f);
        public readonly Material GoldDoor = Make("M_Route_GoldDoor", new Color(0.9f, 0.68f, 0.18f), 0.45f, 0.3f);
        public readonly Material Metal = Make("M_Route_Metal", new Color(0.62f, 0.64f, 0.66f), 0.35f, 0.8f);
        public readonly Material DarkMetal = Make("M_Route_DarkMetal", new Color(0.16f, 0.17f, 0.19f), 0.4f, 0.6f);
        public readonly Material Brass = Make("M_Route_Brass", new Color(0.85f, 0.65f, 0.25f), 0.3f, 0.9f);
        public readonly Material White = Make("M_Route_White", new Color(0.9f, 0.92f, 0.95f), 0.5f);
        public readonly Material Barricade = Make("M_Route_Barricade", new Color(0.35f, 0.28f, 0.2f), 0.85f);
        public readonly Material Shutter = Make("M_Route_Shutter", new Color(0.45f, 0.48f, 0.5f), 0.45f, 0.7f);
        public readonly Material Hazard = Make("M_Route_Hazard", new Color(0.95f, 0.75f, 0.1f), 0.6f, 0f, new Color(0.25f, 0.18f, 0f));
        public readonly Material Screen = Make("M_Route_Screen", new Color(0.15f, 0.5f, 0.9f), 0.2f, 0f, new Color(0.3f, 0.9f, 1.6f));
        public readonly Material AlertGlow = Make("M_Route_AlertGlow", new Color(0.9f, 0.2f, 0.15f), 0.3f, 0f, new Color(1.8f, 0.25f, 0.15f));
        public readonly Material ExitGlow = Make("M_Route_ExitGlow", new Color(0.15f, 0.8f, 0.3f), 0.3f, 0f, new Color(0.2f, 1.6f, 0.4f));

        private static Material Make(string name, Color color, float roughness, float metallic = 0f, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Smoothness", 1f - roughness);
            material.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }
    }

    // ------------------------------------------------------------------ validation

    public static string Validate(int seeds)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        RouteManager manager = Object.FindFirstObjectByType<RouteManager>(FindObjectsInactive.Include);
        if (manager == null)
            return "No RouteManager in the scene. Run Tools/Routes/Build Route Content first.";

        RouteSpawnPoint[] points = manager.transform.parent.GetComponentsInChildren<RouteSpawnPoint>(true);
        RouteInteractable[] steps = manager.transform.parent.GetComponentsInChildren<RouteInteractable>(true);
        DoorLockRule[] locks = manager.transform.parent.GetComponentsInChildren<DoorLockRule>(true);

        int failures = 0;
        string firstFailure = null;
        for (int seed = 0; seed < seeds; seed++)
        {
            RouteManager.RoutePlan plan = manager.BuildPlan(seed * 31 + 7, points, false);
            if (plan == null)
            {
                failures++;
                firstFailure ??= $"seed {seed}: not enough spawn points";
                continue;
            }

            if (!RouteSolver.AllRoutesCompletable(plan, out string report))
            {
                failures++;
                firstFailure ??= $"seed {seed}: {report}";
            }
        }

        IEnumerable<string> produced = manager.ItemRules.Select(rule => rule.itemId)
            .Concat(steps.SelectMany(step => step.GrantedItems ?? Array.Empty<ItemAmount>()).Select(item => item.itemId))
            .Append(manager.PcBonusItem);
        List<string> unused = RouteSolver.FindUnusedItems(produced, steps)
            .Where(id => locks.All(rule => rule.RequiredItemId != id))
            .ToList();

        Dictionary<SpawnCategory, int> perCategory = new();
        foreach (RouteSpawnPoint point in points)
        {
            foreach (SpawnCategory category in Enum.GetValues(typeof(SpawnCategory)))
            {
                if (category != SpawnCategory.None && (point.Categories & category) != 0)
                    perCategory[category] = perCategory.TryGetValue(category, out int count) ? count + 1 : 1;
            }
        }

        string result = $"{seeds - failures}/{seeds} random layouts keep all 4 routes completable.";
        if (firstFailure != null)
            result += "\nFirst failure: " + firstFailure;
        result += unused.Count == 0 ? "\nEvery item is used by some step." : "\nUnused items: " + string.Join(", ", unused);
        result += "\nSpots per category: " + string.Join(", ", perCategory.Select(pair => $"{pair.Key} {pair.Value}"));
        return result;
    }
}
