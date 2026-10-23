using Unity.Netcode;
using UnityEngine;

public class NetworkPickup : NetworkBehaviour
{
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractRpc(RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!Unity.Netcode.NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
            return;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || Vector3.Distance(playerObject.transform.position, transform.position) > 3.5f)
            return;

        NetworkPlayerController role = playerObject.GetComponent<NetworkPlayerController>();
        NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
        if (role == null || role.IsGranny || inventory == null || !inventory.TryGiveTestKey())
            return;

        NetworkObject.Despawn();
    }
}
