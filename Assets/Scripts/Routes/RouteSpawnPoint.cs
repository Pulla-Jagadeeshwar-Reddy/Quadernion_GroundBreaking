using System;
using UnityEngine;

[Flags]
public enum SpawnCategory
{
    None = 0,
    SmallItem = 1 << 0,
    Tool = 1 << 1,
    Fuse = 1 << 2,
    Plank = 1 << 3,
    Janitor = 1 << 4,
}

/// <summary>
/// A candidate spot for a randomly placed item. Lives in RouteContent, never inside the floor model.
/// At runtime the server drops the spot onto whatever floor/desk is below it, so a re-modelled
/// floor still gives valid spots.
/// </summary>
public class RouteSpawnPoint : MonoBehaviour
{
    [SerializeField] private SpawnCategory categories = SpawnCategory.SmallItem;
    [SerializeField] private string roomName = "";
    [SerializeField] private bool behindCrouchGap;

    public SpawnCategory Categories => categories;
    public string RoomName => string.IsNullOrEmpty(roomName) ? RoomVolume.NameAt(transform.position) : roomName;
    public bool BehindCrouchGap => behindCrouchGap;

    public void Configure(SpawnCategory newCategories, string newRoomName, bool crouchGap)
    {
        categories = newCategories;
        roomName = newRoomName;
        behindCrouchGap = crouchGap;
    }

    /// <summary>
    /// Finds the surface below this point (floor, desk, shelf). Returns false if there is nothing
    /// within reach or the spot is buried inside geometry.
    /// </summary>
    public bool TryResolve(out Vector3 position)
    {
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        position = transform.position;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.collider.GetComponentInParent<NetworkPlayerController>() != null)
            return false;

        position = hit.point + Vector3.up * 0.12f;
        return !Physics.CheckSphere(position + Vector3.up * 0.1f, 0.08f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = behindCrouchGap ? new Color(1f, 0.5f, 0f) : new Color(0.2f, 0.9f, 0.4f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.25f);
    }
}
