using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A Runner-only item lying in the level. Pressing E gives the item and despawns it.
/// Defaults to the original test key so OnlineVerticalSlice keeps working.
/// </summary>
public class NetworkPickup : NetworkBehaviour, INetworkInteractable
{
    [SerializeField] private string itemId = ItemIds.TestKey;
    [SerializeField, Min(1)] private int count = 1;
    [SerializeField] private float serverInteractionRange = 3.5f;
    [Tooltip("Optional idle animation so pickups read clearly at a distance.")]
    [SerializeField] private bool bobAndSpin = true;

    private Vector3 visualBasePosition;
    private Transform visual;
    private Light glow;

    /// <summary>Spawned pickups on this machine (client-side item finder).</summary>
    public static readonly System.Collections.Generic.List<NetworkPickup> Active = new();

    public override void OnNetworkSpawn() => Active.Add(this);

    public override void OnNetworkDespawn() => Active.Remove(this);

    public string ItemId => itemId;
    public int Count => count;
    public float HoldSeconds => 0f;

    /// <summary>Editor/builder setup before spawning.</summary>
    public void Configure(string newItemId, int newCount)
    {
        itemId = newItemId;
        count = Mathf.Max(1, newCount);
    }

    private void Start()
    {
        if (transform.childCount > 0)
        {
            visual = transform.GetChild(0);
            visualBasePosition = visual.localPosition;
        }

        // A soft local glow so items read from across a room (not networked, no shadows).
        if (itemId != ItemIds.TestKey)
        {
            GameObject lightObject = new("Pickup Glow");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * 0.5f;
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.85f, 0.45f);
            glow.range = 2.2f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
        }
    }

    private void Update()
    {
        if (!bobAndSpin || visual == null)
            return;

        float time = Time.time + transform.position.x * 0.37f;
        if (glow != null)
            glow.intensity = 1.2f + Mathf.Sin(time * 3f) * 0.5f;
        visual.localPosition = visualBasePosition + Vector3.up * (Mathf.Sin(time * 2f) * 0.04f);
        visual.Rotate(0f, 45f * Time.deltaTime, 0f, Space.World);
    }

    public string GetPrompt(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (!IsSpawned || player == null || player.IsGranny)
            return null;

        string name = ItemIds.DisplayName(itemId);
        return count > 1 ? $"E  Pick up {name} x{count}" : $"E  Pick up {name}";
    }

    public float GetHoldProgress() => 0f;
    public void BeginInteract() => InteractRpc();
    public void CancelInteract() { }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null)
            return;

        float distance = Vector3.Distance(playerObject.transform.position, transform.position);
        if (distance > serverInteractionRange)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
        if (role == null || role.IsGranny || inventory == null || role.MovementLocked.Value)
            return;

        if (itemId == ItemIds.TestKey)
        {
            if (!inventory.TryGiveTestKey())
                return;
        }
        else
        {
            inventory.ServerGive(itemId, count);
        }

        RouteManager.Instance?.ServerOnItemPickedUp(this, role);
        RouteManager.Instance?.ServerSfx("pickup", transform.position, 0.8f, 12f);
        NetworkObject.Despawn();
    }
}
