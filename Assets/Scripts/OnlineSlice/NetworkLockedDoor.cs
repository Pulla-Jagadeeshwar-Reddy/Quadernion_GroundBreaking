using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkLockedDoor : NetworkBehaviour
{
    private static readonly List<NetworkLockedDoor> SpawnedDoors = new();
    private Renderer[] doorRenderers;

    [Header("Door motion")]
    [Tooltip("Assign the moving door leaf. Use Hinge Pivot for a separate hinge point; do not assign the stationary frame.")]
    [SerializeField] private Transform doorLeaf;
    [Tooltip("Optional external hinge pivot. Use this when the door leaf is inside an imported prefab and cannot be reparented.")]
    [SerializeField] private Transform hingePivot;
    [Tooltip("Optional colliders that physically block the doorway while closed. Include the matching Online Collision Proxies collider if one exists.")]
    [SerializeField] private Collider[] doorBlockers;
    [SerializeField] private Vector3 openEulerAngles = new(0f, 0f, -90f);
    [SerializeField] private float animationSpeed = 5f;

    [Header("Access")]
    [SerializeField] private bool requiresTestKey = true;
    [SerializeField] private bool allowGranny = false;
    [SerializeField] private float serverInteractionRange = 3.5f;

    public NetworkVariable<bool> IsOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Vector3 closedWorldPosition;
    private Quaternion closedWorldRotation;
    private Vector3 openWorldPosition;
    private Quaternion openWorldRotation;

    private void Awake()
    {
        if (doorLeaf == null)
            doorLeaf = transform;

        doorRenderers = doorLeaf.GetComponentsInChildren<Renderer>(true);

        closedRotation = doorLeaf.localRotation;
        openRotation = closedRotation * Quaternion.Euler(openEulerAngles);

        if (hingePivot != null)
        {
            closedWorldPosition = doorLeaf.position;
            closedWorldRotation = doorLeaf.rotation;
            Quaternion worldRotationDelta = hingePivot.rotation * Quaternion.Euler(openEulerAngles) *
                                            Quaternion.Inverse(hingePivot.rotation);
            openWorldPosition = hingePivot.position + worldRotationDelta * (closedWorldPosition - hingePivot.position);
            openWorldRotation = worldRotationDelta * closedWorldRotation;
        }

        if (doorBlockers == null || doorBlockers.Length == 0)
            doorBlockers = doorLeaf.GetComponents<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        if (!SpawnedDoors.Contains(this))
            SpawnedDoors.Add(this);

        if (IsServer)
            IsOpen.Value = false;

        IsOpen.OnValueChanged += OnOpenChanged;
        if (IsOpen.Value)
            SetBlockersEnabled(false);
        else
        {
            if (hingePivot != null)
                doorLeaf.SetPositionAndRotation(closedWorldPosition, closedWorldRotation);
            else
                doorLeaf.localRotation = closedRotation;
            SetBlockersEnabled(true);
        }
    }

    public override void OnNetworkDespawn()
    {
        IsOpen.OnValueChanged -= OnOpenChanged;
        SpawnedDoors.Remove(this);
    }

    public static bool TryFindForCollider(Collider collider, out NetworkLockedDoor door)
    {
        for (int index = SpawnedDoors.Count - 1; index >= 0; index--)
        {
            NetworkLockedDoor candidate = SpawnedDoors[index];
            if (candidate == null || !candidate.IsSpawned)
            {
                SpawnedDoors.RemoveAt(index);
                continue;
            }

            if (!candidate.OwnsCollider(collider))
                continue;

            door = candidate;
            return true;
        }

        door = null;
        return false;
    }

    public static bool TryFindAimedDoor(Ray viewRay, Vector3 playerPosition, float maxDistance,
        out NetworkLockedDoor door)
    {
        float nearestDoorDistance = float.PositiveInfinity;
        door = null;

        for (int index = SpawnedDoors.Count - 1; index >= 0; index--)
        {
            NetworkLockedDoor candidate = SpawnedDoors[index];
            if (candidate == null || !candidate.IsSpawned)
            {
                SpawnedDoors.RemoveAt(index);
                continue;
            }

            if (!candidate.TryGetLeafBounds(out Bounds leafBounds) ||
                Vector3.Distance(playerPosition, leafBounds.ClosestPoint(playerPosition)) > maxDistance)
                continue;

            bool rayStartsInsideDoor = leafBounds.Contains(viewRay.origin);
            float boundsDistance = 0f;
            if (!rayStartsInsideDoor && !leafBounds.IntersectRay(viewRay, out boundsDistance))
                continue;
            if (boundsDistance > maxDistance || boundsDistance >= nearestDoorDistance)
                continue;

            // Imported floor collision boxes can overlap a visible door's bounds. Ignore an
            // obstruction only when its bounds overlap the door bounds; unrelated walls still block.
            if (!rayStartsInsideDoor &&
                Physics.Raycast(viewRay, out RaycastHit obstruction, boundsDistance + 0.05f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !candidate.OwnsCollider(obstruction.collider) &&
                obstruction.distance < boundsDistance - 0.2f &&
                !leafBounds.Intersects(obstruction.collider.bounds))
                continue;

            nearestDoorDistance = boundsDistance;
            door = candidate;
        }

        return door != null;
    }

    private bool TryGetLeafBounds(out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;
        if (doorRenderers != null)
        {
            foreach (Renderer targetRenderer in doorRenderers)
            {
                if (targetRenderer == null)
                    continue;

                if (hasBounds)
                    bounds.Encapsulate(targetRenderer.bounds);
                else
                {
                    bounds = targetRenderer.bounds;
                    hasBounds = true;
                }
            }
        }

        if (!hasBounds && doorBlockers != null)
        {
            foreach (Collider blocker in doorBlockers)
            {
                if (blocker == null)
                    continue;

                if (hasBounds)
                    bounds.Encapsulate(blocker.bounds);
                else
                {
                    bounds = blocker.bounds;
                    hasBounds = true;
                }
            }
        }

        return hasBounds;
    }

    private bool OwnsCollider(Collider collider)
    {
        if (collider == null)
            return false;

        if (doorLeaf != null && (collider.transform == doorLeaf || collider.transform.IsChildOf(doorLeaf)))
            return true;

        if (doorBlockers == null)
            return false;

        foreach (Collider blocker in doorBlockers)
        {
            if (blocker == collider)
                return true;
        }

        return false;
    }

    private void Update()
    {
        if (hingePivot != null)
        {
            Vector3 targetPosition = IsOpen.Value ? openWorldPosition : closedWorldPosition;
            Quaternion targetRotation = IsOpen.Value ? openWorldRotation : closedWorldRotation;
            doorLeaf.SetPositionAndRotation(
                Vector3.Lerp(doorLeaf.position, targetPosition, Time.deltaTime * animationSpeed),
                Quaternion.Slerp(doorLeaf.rotation, targetRotation, Time.deltaTime * animationSpeed));

            if (Vector3.Distance(doorLeaf.position, targetPosition) <= 0.001f &&
                Quaternion.Angle(doorLeaf.rotation, targetRotation) <= 0.1f)
            {
                doorLeaf.SetPositionAndRotation(targetPosition, targetRotation);
                SetBlockersEnabled(!IsOpen.Value);
            }
        }
        else
        {
            Quaternion target = IsOpen.Value ? openRotation : closedRotation;
            doorLeaf.localRotation = Quaternion.Slerp(doorLeaf.localRotation, target, Time.deltaTime * animationSpeed);

            if (Quaternion.Angle(doorLeaf.localRotation, target) <= 0.1f)
            {
                doorLeaf.localRotation = target;
                SetBlockersEnabled(!IsOpen.Value);
            }
        }
    }

    public string GetInteractionPrompt(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (!IsSpawned || player == null)
            return string.Empty;

        if (player.IsGranny && !allowGranny)
            return "Granny cannot use this door";

        if (!IsOpen.Value && requiresTestKey && (inventory == null || !inventory.HasTestKey.Value))
            return "Locked — find the key";

        return IsOpen.Value ? "E  Close door" : "E  Open door";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || !manager.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        Vector3 interactionPoint = doorLeaf != null ? doorLeaf.position : transform.position;
        if (playerObject == null || Vector3.Distance(playerObject.transform.position, interactionPoint) > serverInteractionRange)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        if (role == null || !role.MatchStarted.Value || (role.IsGranny && !allowGranny))
            return;

        if (!IsOpen.Value && requiresTestKey)
        {
            NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
            if (inventory == null || !inventory.HasTestKey.Value)
                return;
        }

        IsOpen.Value = !IsOpen.Value;
    }

    private void OnOpenChanged(bool previousValue, bool newValue)
    {
        if (newValue)
            SetBlockersEnabled(false);
    }

    private void SetBlockersEnabled(bool enabled)
    {
        if (doorBlockers == null)
            return;

        foreach (Collider blocker in doorBlockers)
        {
            if (blocker != null)
                blocker.enabled = enabled;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (hingePivot == null)
            return;

        Vector3 point = hingePivot.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(point, 0.09f);
        Gizmos.DrawLine(point - Vector3.up * 0.35f, point + Vector3.up * 0.35f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(point, hingePivot.up * 0.55f);
    }
}
