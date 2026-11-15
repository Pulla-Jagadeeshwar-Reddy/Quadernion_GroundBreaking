using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkLockedDoor : NetworkBehaviour, INetworkInteractable, ISecondaryInteractable
{
    // ---------------------------------------------------------------- jamming (Runner F / Granny breaks)
    private const float JamSeconds = 2.5f;
    private const float UnjamSeconds = 1f;
    private const float BreakSeconds = 4f;
    private const byte HoldNone = 0, HoldJam = 1, HoldUnjam = 2, HoldBreak = 3;

    /// <summary>Jammed shut by the Runner: nobody can open it normally; Granny must break it down.</summary>
    public NetworkVariable<bool> Jammed = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Broken down by Granny this round: cannot be jammed again.</summary>
    public NetworkVariable<bool> Broken = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ulong holdClient = ulong.MaxValue;
    private byte holdKind;
    private double holdStart;
    private float localPrimaryStart = -1f, localPrimaryDuration;
    private float localSecondaryStart = -1f, localSecondaryDuration;
    private GameObject jamVisual;

    private static NetworkPlayerController LocalPlayer
    {
        get
        {
            Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
            return manager != null && manager.LocalClient?.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<NetworkPlayerController>()
                : null;
        }
    }

    private static double ServerNow => Unity.Netcode.NetworkManager.Singleton != null
        ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time
        : Time.timeAsDouble;

    public string GetSecondaryPrompt(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (isDuplicateComponent || isDuplicateLeaf || !IsSpawned || player == null || player.IsGranny)
            return null;
        if (Jammed.Value)
            return "Hold F  Unjam door";
        if (IsOpen.Value || Broken.Value || RequiredItemId != null || requiresTestKey)
            return null;
        return "Hold F  Jam door shut (Granny has to break it)";
    }

    public float SecondaryHoldSeconds => Jammed.Value ? UnjamSeconds : JamSeconds;

    public float GetSecondaryProgress() => localSecondaryStart < 0f || localSecondaryDuration <= 0f
        ? 0f
        : Mathf.Clamp01((Time.time - localSecondaryStart) / localSecondaryDuration);

    public void BeginSecondary()
    {
        localSecondaryDuration = SecondaryHoldSeconds;
        localSecondaryStart = Time.time;
        BeginHoldRpc(Jammed.Value ? HoldUnjam : HoldJam);
    }

    public void CancelSecondary()
    {
        localSecondaryStart = -1f;
        CancelHoldRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginHoldRpc(byte kind, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!TryGetInRangePlayer(sender, out NetworkPlayerController role))
            return;

        bool allowed = kind switch
        {
            HoldJam => !role.IsGranny && !Jammed.Value && !IsOpen.Value && !Broken.Value && RequiredItemId == null && !requiresTestKey,
            HoldUnjam => !role.IsGranny && Jammed.Value,
            HoldBreak => role.IsGranny && Jammed.Value,
            _ => false,
        };
        if (!allowed)
            return;

        holdClient = sender;
        holdKind = kind;
        holdStart = ServerNow;
        if (kind == HoldJam)
            RouteManager.Instance?.ServerMakeNoise(LeafCenter, NoiseLevel.Small, "Furniture scraping");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelHoldRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == holdClient)
            holdClient = ulong.MaxValue;
    }

    private void ServerTickHold()
    {
        if (holdClient == ulong.MaxValue)
            return;

        if (!TryGetInRangePlayer(holdClient, out NetworkPlayerController role) || role.MovementLocked.Value)
        {
            holdClient = ulong.MaxValue;
            return;
        }

        float duration = holdKind switch
        {
            HoldJam => JamSeconds,
            HoldUnjam => UnjamSeconds,
            _ => BreakSeconds,
        };
        if (ServerNow - holdStart < duration)
            return;

        byte kind = holdKind;
        holdClient = ulong.MaxValue;
        switch (kind)
        {
            case HoldJam:
                if (!IsOpen.Value)
                    Jammed.Value = true;
                break;
            case HoldUnjam:
                Jammed.Value = false;
                break;
            case HoldBreak:
                Jammed.Value = false;
                Broken.Value = true;
                IsOpen.Value = true;
                DoorBrokenRpc(RoomVolume.NameAt(LeafCenter));
                break;
        }
    }

    [Rpc(SendTo.Everyone)]
    private void DoorBrokenRpc(Unity.Collections.FixedString64Bytes room)
    {
        NetworkPlayerController local = LocalPlayer;
        if (local != null && !local.IsGranny)
            RunnerHud.AddToast($"Granny smashed through a jammed door ({room})!", new Color(1f, 0.4f, 0.35f), true);
    }

    private bool TryGetInRangePlayer(ulong clientId, out NetworkPlayerController role)
    {
        role = null;
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            return false;

        role = client.PlayerObject.GetComponent<NetworkPlayerController>();
        if (role == null || !role.MatchStarted.Value)
            return false;

        Vector3 position = client.PlayerObject.transform.position;
        float distance = TryGetLeafBounds(out Bounds leaf)
            ? Vector3.Distance(position, leaf.ClosestPoint(position))
            : Vector3.Distance(position, doorLeaf != null ? doorLeaf.position : transform.position);
        return distance <= serverInteractionRange;
    }

    private void OnJammedChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            if (jamVisual == null && TryGetLeafBounds(out Bounds leaf))
            {
                // A wooden bar wedged across the doorway, through both sides of the leaf.
                jamVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                jamVisual.name = "Jam Bar";
                Destroy(jamVisual.GetComponent<Collider>());
                bool thinX = leaf.size.x < leaf.size.z;
                jamVisual.transform.position = new Vector3(leaf.center.x, leaf.min.y + 1.0f, leaf.center.z);
                jamVisual.transform.localScale = thinX
                    ? new Vector3(leaf.size.x + 0.25f, 0.12f, leaf.size.z * 0.95f)
                    : new Vector3(leaf.size.x * 0.95f, 0.12f, leaf.size.z + 0.25f);
                Renderer bar = jamVisual.GetComponent<Renderer>();
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    bar.material = new Material(lit);
                    bar.material.SetColor("_BaseColor", new Color(0.45f, 0.28f, 0.14f));
                }
            }

            if (jamVisual != null)
                jamVisual.SetActive(true);
        }
        else if (jamVisual != null)
        {
            jamVisual.SetActive(false);
        }
    }

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
    [SerializeField] private bool allowGranny = true;
    [SerializeField] private float serverInteractionRange = 3.5f;

    public NetworkVariable<bool> IsOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Item id needed to open this door while closed (empty = no item lock). Set at runtime by
    /// route rules (DoorLockRule), so the door setup in the floor scene never needs editing.
    /// </summary>
    public NetworkVariable<Unity.Collections.FixedString32Bytes> RequiredItem = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Granny breaking a jammed door is a hold; everything else is a press.</summary>
    public float HoldSeconds => Jammed.Value && LocalPlayer != null && LocalPlayer.IsGranny ? BreakSeconds : 0f;

    public float GetHoldProgress() => localPrimaryStart < 0f || localPrimaryDuration <= 0f
        ? 0f
        : Mathf.Clamp01((Time.time - localPrimaryStart) / localPrimaryDuration);

    public void BeginInteract()
    {
        if (Jammed.Value)
        {
            if (LocalPlayer != null && LocalPlayer.IsGranny)
            {
                localPrimaryDuration = BreakSeconds;
                localPrimaryStart = Time.time;
                BeginHoldRpc(HoldBreak);
            }

            return;
        }

        InteractRpc();
    }

    public void CancelInteract()
    {
        if (localPrimaryStart >= 0f)
        {
            localPrimaryStart = -1f;
            CancelHoldRpc();
        }
    }
    string INetworkInteractable.GetPrompt(NetworkPlayerController player, NetworkInventory inventory) =>
        GetInteractionPrompt(player, inventory);

    /// <summary>Approximate centre of the door leaf, for nearest-door lookups.</summary>
    public Vector3 LeafCenter => TryGetLeafBounds(out Bounds bounds)
        ? bounds.center
        : (doorLeaf != null ? doorLeaf.position : transform.position);

    /// <summary>Finds the spawned door whose leaf is closest to a world position.</summary>
    public static bool TryFindNearest(Vector3 position, float maxDistance, out NetworkLockedDoor door)
    {
        door = null;
        float best = maxDistance;
        for (int index = SpawnedDoors.Count - 1; index >= 0; index--)
        {
            NetworkLockedDoor candidate = SpawnedDoors[index];
            if (candidate == null)
            {
                SpawnedDoors.RemoveAt(index);
                continue;
            }

            if (candidate.isDuplicateComponent || candidate.isDuplicateLeaf)
                continue;

            float distance = Vector3.Distance(position, candidate.LeafCenter);
            if (distance > best)
                continue;

            best = distance;
            door = candidate;
        }

        return door != null;
    }

    /// <summary>All currently spawned doors (server uses this to reset a round).</summary>
    public static IReadOnlyList<NetworkLockedDoor> AllSpawned => SpawnedDoors;

    /// <summary>Server only: close the door and set (or clear) its item lock.</summary>
    public void ServerSetState(bool open, string requiredItemId)
    {
        if (!IsServer)
            return;

        IsOpen.Value = open;
        Jammed.Value = false;
        Broken.Value = false;
        holdClient = ulong.MaxValue;
        RequiredItem.Value = string.IsNullOrEmpty(requiredItemId)
            ? default
            : new Unity.Collections.FixedString32Bytes(requiredItemId);
    }

    private string RequiredItemId => RequiredItem.Value.IsEmpty ? null : RequiredItem.Value.ToString();

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Vector3 closedWorldPosition;
    private Quaternion closedWorldRotation;
    private Vector3 openWorldPosition;
    private Quaternion openWorldRotation;
    private bool isDuplicateComponent;
    private bool isDuplicateLeaf;

    private void Awake()
    {
        NetworkLockedDoor[] components = GetComponents<NetworkLockedDoor>();
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != this)
                continue;

            isDuplicateComponent = index > 0;
            break;
        }

        // A duplicate component can otherwise fight the active component over the same leaf.
        if (isDuplicateComponent)
            return;

        if (doorLeaf == null)
            doorLeaf = transform;

        doorRenderers = doorLeaf.GetComponentsInChildren<Renderer>(true);

        // Existing scene instances may still serialize the old common angle. Infer the
        // correct sign from each leaf's handle side before calculating its open pose.
        if (TryGetHandleSide(out bool handleOnRight))
            openEulerAngles = new Vector3(0f, 0f, handleOnRight ? -90f : 90f);

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

    private bool TryGetHandleSide(out bool handleOnRight)
    {
        handleOnRight = true;
        Transform[] handles = doorLeaf.GetComponentsInChildren<Transform>(true);
        float handleXTotal = 0f;
        int handleCount = 0;
        foreach (Transform child in handles)
        {
            if (!child.name.StartsWith("Handle", System.StringComparison.OrdinalIgnoreCase))
                continue;

            handleXTotal += doorLeaf.InverseTransformPoint(child.position).x;
            handleCount++;
        }

        if (handleCount == 0 || !TryGetDoorBoundsInLocalSpace(out Bounds doorBounds))
            return false;

        handleOnRight = handleXTotal / handleCount >= doorBounds.center.x;
        return true;
    }

    private bool TryGetDoorBoundsInLocalSpace(out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;
        foreach (Renderer targetRenderer in doorRenderers)
        {
            if (targetRenderer == null || IsHandlePart(targetRenderer.transform))
                continue;

            Bounds worldBounds = targetRenderer.bounds;
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

    private bool IsHandlePart(Transform candidate)
    {
        for (Transform current = candidate; current != null && current != doorLeaf; current = current.parent)
        {
            if (current.name.StartsWith("Handle", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public override void OnNetworkSpawn()
    {
        if (isDuplicateComponent || doorLeaf == null)
            return;

        // A duplicated scene object can accidentally point at another door's leaf.
        // Let only one network behaviour animate and toggle a given leaf.
        foreach (NetworkLockedDoor existing in SpawnedDoors)
        {
            if (existing == null || existing == this || existing.doorLeaf != doorLeaf)
                continue;

            isDuplicateLeaf = true;
            Debug.LogWarning($"[{nameof(NetworkLockedDoor)}] {name} shares the door leaf '{doorLeaf.name}' with {existing.name}; this duplicate will be ignored.", this);
            return;
        }

        if (!SpawnedDoors.Contains(this))
            SpawnedDoors.Add(this);

        if (IsServer)
            IsOpen.Value = false;

        IsOpen.OnValueChanged += OnOpenChanged;
        Jammed.OnValueChanged += OnJammedChanged;
        if (Jammed.Value)
            OnJammedChanged(false, true);
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
        if (isDuplicateComponent || isDuplicateLeaf)
            return;

        IsOpen.OnValueChanged -= OnOpenChanged;
        Jammed.OnValueChanged -= OnJammedChanged;
        SpawnedDoors.Remove(this);
        if (jamVisual != null)
            Destroy(jamVisual);
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

    /// <summary>Leaf bounds from its renderers; works in the editor before Awake (route builder).</summary>
    public bool TryGetEditorLeafBounds(out Bounds bounds, out Transform leaf)
    {
        leaf = doorLeaf != null ? doorLeaf : transform;
        bounds = default;
        bool has = false;
        foreach (Renderer target in leaf.GetComponentsInChildren<Renderer>(true))
        {
            if (has)
                bounds.Encapsulate(target.bounds);
            else
            {
                bounds = target.bounds;
                has = true;
            }
        }

        return has;
    }

    /// <summary>True when the Runner can open this door without the old test key (used by the route builder).</summary>
    public bool OpensWithoutTestKey => !requiresTestKey;

    public bool OwnsCollider(Collider collider)
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
        if (isDuplicateComponent || isDuplicateLeaf)
            return;

        if (IsServer && IsSpawned)
            ServerTickHold();

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
        if (isDuplicateComponent || isDuplicateLeaf || !IsSpawned || player == null)
            return string.Empty;

        if (player.IsGranny && !allowGranny)
            return "Granny cannot use this door";

        if (Jammed.Value)
            return player.IsGranny ? "Hold E  Break down the jammed door" : "Jammed shut";

        string lockItem = RequiredItemId;
        if (!IsOpen.Value && lockItem != null && (inventory == null || !inventory.Has(lockItem)))
            return player.IsGranny ? "E  Open door" : $"Locked — needs {ItemIds.DisplayName(lockItem)}";

        if (!IsOpen.Value && requiresTestKey && (inventory == null || !inventory.HasTestKey.Value))
            return "Locked — find the key";

        if (!IsOpen.Value && lockItem != null)
            return $"E  Unlock with {ItemIds.DisplayName(lockItem)}";

        return IsOpen.Value ? "E  Close door" : "E  Open door";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        if (isDuplicateComponent || isDuplicateLeaf)
            return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || !manager.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null)
            return;

        Vector3 playerPosition = playerObject.transform.position;
        float interactionDistance = Vector3.Distance(playerPosition,
            doorLeaf != null ? doorLeaf.position : transform.position);
        if (TryGetLeafBounds(out Bounds leafBounds))
            interactionDistance = Vector3.Distance(playerPosition, leafBounds.ClosestPoint(playerPosition));
        if (interactionDistance > serverInteractionRange)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        if (role == null || !role.MatchStarted.Value || (role.IsGranny && !allowGranny) || Jammed.Value)
            return;

        if (!IsOpen.Value && requiresTestKey)
        {
            NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
            if (inventory == null || !inventory.HasTestKey.Value)
                return;
        }

        // Granny holds a master key: she can always open route-locked doors to chase the Runner.
        string lockItem = RequiredItemId;
        if (!IsOpen.Value && lockItem != null && !role.IsGranny)
        {
            NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
            if (inventory == null || !inventory.Has(lockItem))
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
        if (isDuplicateComponent || hingePivot == null)
            return;

        Vector3 point = hingePivot.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(point, 0.09f);
        Gizmos.DrawLine(point - Vector3.up * 0.35f, point + Vector3.up * 0.35f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(point, hingePivot.up * 0.55f);
    }
}
