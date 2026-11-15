using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Where the Runner leaves the floor. In the original vertical slice it needs the test key
/// and an open door; on the first floor it needs a route's final prop to be completed.
/// </summary>
public class NetworkEscapePoint : NetworkBehaviour, INetworkInteractable
{
    [SerializeField] private NetworkLockedDoor requiredDoor;
    [SerializeField] private bool requiresTestKey = true;
    [SerializeField] private RouteInteractable[] requiredCompleted;
    [SerializeField] private string routeName = "";
    [SerializeField] private string blockedPrompt = "";
    [SerializeField] private float serverInteractionRange = 3.5f;
    [Tooltip("If > 0, an unlocked exit triggers automatically when the Runner comes this close (e.g. walking down the stairs).")]
    [SerializeField] private float autoEscapeRadius;

    public NetworkVariable<bool> HasEscaped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public string RouteName => routeName;
    public RouteInteractable[] RequiredCompleted => requiredCompleted;
    public float HoldSeconds => 0f;
    public float GetHoldProgress() => 0f;
    public void BeginInteract() => InteractRpc();
    public void CancelInteract() { }

    /// <summary>Builder setup for first-floor route exits.</summary>
    public void Configure(string newRouteName, RouteInteractable[] required, string newBlockedPrompt, float autoRadius = 0f)
    {
        autoEscapeRadius = autoRadius;
        routeName = newRouteName;
        requiredCompleted = required;
        blockedPrompt = newBlockedPrompt;
        requiresTestKey = false;
        requiredDoor = null;
    }

    public bool IsUnlocked(NetworkInventory inventory)
    {
        if (requiresTestKey && (inventory == null || !inventory.HasTestKey.Value))
            return false;

        if (requiredDoor != null && !requiredDoor.IsOpen.Value)
            return false;

        if (requiredCompleted != null)
        {
            foreach (RouteInteractable step in requiredCompleted)
            {
                if (step != null && !step.IsCompleted)
                    return false;
            }
        }

        return true;
    }

    public string GetPrompt(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (!IsSpawned || player == null || player.IsGranny || HasEscaped.Value)
            return null;

        if (!IsUnlocked(inventory))
            return string.IsNullOrEmpty(blockedPrompt) ? null : blockedPrompt;

        return string.IsNullOrEmpty(routeName) ? "E  Escape" : $"E  Escape ({routeName})";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        if (HasEscaped.Value)
            return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || Vector3.Distance(playerObject.transform.position, transform.position) > serverInteractionRange)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
        if (role == null || role.IsGranny || role.MovementLocked.Value || !IsUnlocked(inventory))
            return;

        HasEscaped.Value = true;
        RouteManager.Instance?.ServerRunnerEscaped(this, role);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || HasEscaped.Value || autoEscapeRadius <= 0f)
            return;

        foreach (NetworkClient client in Unity.Netcode.NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            NetworkPlayerController role = client.PlayerObject.GetComponent<NetworkPlayerController>();
            NetworkInventory inventory = client.PlayerObject.GetComponent<NetworkInventory>();
            if (role == null || role.IsGranny || role.MovementLocked.Value || !role.MatchStarted.Value)
                continue;

            Vector3 offset = client.PlayerObject.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > autoEscapeRadius || !IsUnlocked(inventory))
                continue;

            HasEscaped.Value = true;
            RouteManager.Instance?.ServerRunnerEscaped(this, role);
            return;
        }
    }

    /// <summary>Server only: reset for a new round.</summary>
    public void ServerReset()
    {
        if (IsServer)
            HasEscaped.Value = false;
    }

    private void OnGUI()
    {
        // The route manager draws the result screen on the first floor; this banner is for the slice.
        if (RouteManager.Instance != null)
            return;

        if (IsSpawned && HasEscaped.Value)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.38f, 340f, 70f),
                "PLAYER ESCAPED!  Granny loses this round.");
        }
    }
}
