using Unity.Netcode;
using UnityEngine;

public class NetworkEscapePoint : NetworkBehaviour
{
    [SerializeField] private NetworkLockedDoor requiredDoor;
    public NetworkVariable<bool> HasEscaped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        if (HasEscaped.Value)
            return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || Vector3.Distance(playerObject.transform.position, transform.position) > 3.5f)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
        if (role == null || role.IsGranny || inventory == null || !inventory.HasTestKey.Value)
            return;

        if (requiredDoor == null || !requiredDoor.IsOpen.Value)
            return;

        HasEscaped.Value = true;
    }

    private void OnGUI()
    {
        if (IsSpawned && HasEscaped.Value)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.38f, 340f, 70f),
                "PLAYER ESCAPED!  Granny loses this round.");
        }
    }
}
