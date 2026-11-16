using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Hand-picked spots to board up, listed in Assets/Routes/extra_boards.json by floor position
/// (the x, z shown in the in-game room label). Only a position is needed:
/// - a door within 1.5 m: boards sized to that door, on the side of the spot (the door is also
///   locked as "Boarded up");
/// - otherwise the narrowest gap within 1.2 m of the spot.
/// { "spots": [ { "x": 12.5, "z": 7.0, "kind": "boards", "note": "behind the staff room cabinet" } ] }
/// kind: "boards" (default), "gold" (moves the Locksmith gold door here), "shutter" (the power shutter),
/// "stairs" (a sealed stairwell's entrance: boards the passage at the spot, never a door), "open" (a door that must never be boarded).
/// </summary>
public static class ExtraBoards
{
    public const string FilePath = "Assets/Routes/extra_boards.json";

    [Serializable]
    public class Spot
    {
        public float x;
        public float z;
        /// <summary>"boards" (default), "gold" (moves the Locksmith gold door here) or "shutter" (moves the power shutter here).</summary>
        public string kind = "boards";
        public string note = "";
    }

    [Serializable]
    private class Data
    {
        public List<Spot> spots = new();
    }

    public static List<Spot> Load()
    {
        if (!File.Exists(FilePath))
            return new List<Spot>();
        return JsonUtility.FromJson<Data>(File.ReadAllText(FilePath))?.spots ?? new List<Spot>();
    }

    public static (Vector3 center, Vector3 facing, float width) Fit(Spot spot) => Fit(spot, out _);

    /// <summary>
    /// Stair entrances ("stairs" spots): board the passage right at the spot, never a nearby door
    /// (the doors next to stair passages usually belong to real rooms).
    /// </summary>
    public static (Vector3 center, Vector3 facing, float width) FitPassage(Spot spot) => FitGap(new Vector3(spot.x, 0f, spot.z), 0.45f, true);

    /// <summary>
    /// Where the blocker goes for a spot: floor centre, the direction its back faces (away from the
    /// spot), and its width. <paramref name="door"/> is the door it covers, if any.
    /// </summary>
    public static (Vector3 center, Vector3 facing, float width) Fit(Spot spot, out NetworkLockedDoor door)
    {
        Vector3 point = new(spot.x, 0f, spot.z);
        door = null;

        // 1) A door near the spot: cover exactly that door, on the spot's side.
        float best = 1.5f;
        Bounds leaf = default;
        foreach (NetworkLockedDoor candidate in UnityEngine.Object.FindObjectsByType<NetworkLockedDoor>(FindObjectsInactive.Exclude))
        {
            if (!candidate.TryGetEditorLeafBounds(out Bounds bounds, out _))
                continue;
            Vector3 closest = bounds.ClosestPoint(new Vector3(point.x, bounds.center.y, point.z));
            float distance = Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(point.x, point.z));
            if (distance < best)
            {
                best = distance;
                leaf = bounds;
                door = candidate;
            }
        }

        if (door != null)
        {
            bool thinX = leaf.size.x < leaf.size.z;
            Vector3 normal = thinX ? Vector3.right : Vector3.forward;
            Vector3 toSpot = Vector3.Dot(point - leaf.center, normal) >= 0f ? normal : -normal;
            float width = (thinX ? leaf.size.z : leaf.size.x) + 0.12f;
            Vector3 center = new Vector3(leaf.center.x, FloorY(leaf.center), leaf.center.z) + toSpot * 0.12f;
            return (center, -toSpot, width);
        }

        // 2) The narrowest gap close to the spot (a doorway or a narrow passage).
        return FitGap(point, 1.2f, false);
    }

    /// <summary>
    /// The gap through <paramref name="point"/> (searching up to <paramref name="radius"/> around it):
    /// narrowest first, or nearest first when <paramref name="nearestFirst"/>.
    /// </summary>
    private static (Vector3 center, Vector3 facing, float width) FitGap(Vector3 point, float radius, bool nearestFirst)
    {
        Vector3 bestCenter = point;
        Vector3 bestFacing = Vector3.forward;
        float bestWidth = float.MaxValue;
        float bestOffset = float.MaxValue;
        float step = radius > 0.6f ? 0.2f : 0.15f;
        for (float ox = -radius; ox <= radius + 0.01f; ox += step)
        for (float oz = -radius; oz <= radius + 0.01f; oz += step)
        {
            float offset = Mathf.Sqrt(ox * ox + oz * oz);
            if (offset > radius)
                continue;

            Vector3 origin = new(point.x + ox, 1f, point.z + oz);
            if (Physics.CheckSphere(origin, 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;                                  // inside a wall

            float left = Ray(origin, Vector3.left), right = Ray(origin, Vector3.right);
            float back = Ray(origin, Vector3.back), front = Ray(origin, Vector3.forward);
            float widthX = left + right, widthZ = back + front;
            bool alongX = widthX <= widthZ;
            float width = alongX ? widthX : widthZ;
            if (width < 0.7f)
                continue;
            if (nearestFirst)
            {
                if (offset > bestOffset + 0.01f || (Mathf.Abs(offset - bestOffset) <= 0.01f && width >= bestWidth))
                    continue;
            }
            else if (width > bestWidth + 0.05f || (Mathf.Abs(width - bestWidth) <= 0.05f && offset >= bestOffset))
                continue;

            bestWidth = width;
            bestOffset = offset;
            bestCenter = alongX ? origin + Vector3.right * ((right - left) * 0.5f) : origin + Vector3.forward * ((front - back) * 0.5f);
            Vector3 normal = alongX ? Vector3.forward : Vector3.right;
            bestFacing = Vector3.Dot(point - bestCenter, normal) >= 0f ? -normal : normal;
        }

        if (bestWidth >= 7.9f || bestWidth == float.MaxValue)
            return (new Vector3(point.x, FloorY(point), point.z), Vector3.forward, 1.5f);

        return (new Vector3(bestCenter.x, FloorY(bestCenter), bestCenter.z), bestFacing, Mathf.Clamp(bestWidth, 0.6f, 6f));
    }

    private static float Ray(Vector3 from, Vector3 direction) =>
        Physics.Raycast(from, direction, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.distance : 4f;

    private static float FloorY(Vector3 at) =>
        Physics.Raycast(new Vector3(at.x, 1f, at.z), Vector3.down, out RaycastHit floor, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? floor.point.y : 0f;
}
