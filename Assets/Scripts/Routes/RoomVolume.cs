using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Names an area of the floor (Staff Room, Lounge...). Our own boxes, independent of how the
/// floor model is split into objects, so they survive Ritu re-exporting the model.
/// </summary>
public class RoomVolume : MonoBehaviour
{
    [SerializeField] private string roomName = "Room";
    [SerializeField] private Vector3 size = new(5f, 3f, 5f);
    [Tooltip("Item needed to get in (e.g. the janitor key). Used by the route solver.")]
    [SerializeField] private string lockedByItem = "";

    private static readonly List<RoomVolume> Rooms = new();

    public string RoomName => roomName;
    public string LockedByItem => lockedByItem;
    public Vector3 Size => size;

    public void Configure(string newName, Vector3 newSize, string lockItem)
    {
        roomName = newName;
        size = newSize;
        lockedByItem = lockItem;
    }

    private void OnEnable() => Rooms.Add(this);
    private void OnDisable() => Rooms.Remove(this);

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector3 half = size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public static RoomVolume At(Vector3 worldPosition)
    {
        if (Rooms.Count == 0)
        {
            foreach (RoomVolume room in FindObjectsByType<RoomVolume>(FindObjectsInactive.Exclude))
            {
                if (!Rooms.Contains(room))
                    Rooms.Add(room);
            }
        }

        foreach (RoomVolume room in Rooms)
        {
            if (room != null && room.Contains(worldPosition))
                return room;
        }

        return null;
    }

    public static string NameAt(Vector3 worldPosition)
    {
        RoomVolume room = At(worldPosition);
        return room != null ? room.roomName : "Corridor";
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = string.IsNullOrEmpty(lockedByItem) ? new Color(0.3f, 0.6f, 1f, 0.25f) : new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
