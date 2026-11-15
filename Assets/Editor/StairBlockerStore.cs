using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Hand-placed stair blockers. The route builder puts boards / the power shutter / the gold door in
/// front of each stairwell automatically; to put one somewhere else, open OnlineFirstFloor, unpack
/// nothing, just move/rotate the blocker objects (children of "Barrier - Stairs N", "Gold Stair Door",
/// "R2_Shutter (blocker)") and run Tools/Routes/Save Stair Blocker Positions. The positions are kept
/// in Assets/Routes/stair_blockers.json and used by every rebuild.
/// Width: scale the blocker on X (1 = keep the width it had).
/// </summary>
public static class StairBlockerStore
{
    private const string FilePath = "Assets/Routes/stair_blockers.json";

    [Serializable]
    private class Entry
    {
        public string barrier;
        public Vector3 center;
        public Vector3 facing;
        public float width;
    }

    [Serializable]
    private class Data
    {
        public List<Entry> entries = new();
    }

    public static bool TryGet(string barrierName, out List<(Vector3 center, Vector3 facing, float width)> placements)
    {
        placements = new List<(Vector3, Vector3, float)>();
        if (!File.Exists(FilePath))
            return false;

        Data data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
        foreach (Entry entry in data.entries)
        {
            if (entry.barrier == barrierName)
                placements.Add((entry.center, entry.facing, entry.width));
        }

        return placements.Count > 0;
    }

    [MenuItem("Tools/Routes/Save Stair Blocker Positions")]
    public static void Save()
    {
        Data data = new();
        foreach (Transform barrier in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            string name = barrier.name;
            if (!(name.StartsWith("Barrier - ") || name == "Gold Stair Door" || name == "R2_Shutter (blocker)"))
                continue;

            foreach (Transform blocker in barrier)
            {
                BoxCollider box = blocker.GetComponent<BoxCollider>();
                if (box == null)
                    continue;
                data.entries.Add(new Entry
                {
                    barrier = name,
                    center = blocker.position,
                    facing = Vector3.ProjectOnPlane(blocker.forward, Vector3.up).normalized,
                    width = box.size.x * Mathf.Abs(blocker.lossyScale.x) - (blocker.name == "Boards" ? 0.2f : 0f),
                });
            }
        }

        File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        AssetDatabase.ImportAsset(FilePath);
        EditorUtility.DisplayDialog("Stair blockers",
            $"Saved {data.entries.Count} blocker positions to {FilePath}.\nThey are used by Tools/Routes/Refresh After Floor Change.", "OK");
    }

    [MenuItem("Tools/Routes/Reset Stair Blocker Positions")]
    public static void Reset()
    {
        if (File.Exists(FilePath))
            AssetDatabase.DeleteAsset(FilePath);
        EditorUtility.DisplayDialog("Stair blockers", "Hand-placed positions removed; the next refresh places blockers automatically.", "OK");
    }
}
