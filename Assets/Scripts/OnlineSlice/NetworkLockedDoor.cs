using Unity.Netcode;
using UnityEngine;

public class NetworkLockedDoor : NetworkBehaviour
{
    [SerializeField] private Transform doorLeaf;
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float animationSpeed = 5f;

    public NetworkVariable<bool> IsOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Quaternion closedRotation;
    private Quaternion openRotation;

    private void Awake()
    {
        if (doorLeaf == null)
            doorLeaf = transform;

        closedRotation = doorLeaf.localRotation;
        openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
    }

    private void Update()
    {
        Quaternion target = IsOpen.Value ? openRotation : closedRotation;
        doorLeaf.localRotation = Quaternion.Slerp(doorLeaf.localRotation, target, Time.deltaTime * animationSpeed);
    }

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
        if (role == null || role.IsGranny)
            return;

        if (!IsOpen.Value)
        {
            NetworkInventory inventory = playerObject.GetComponent<NetworkInventory>();
            if (inventory == null || !inventory.HasTestKey.Value)
                return;
        }

        IsOpen.Value = !IsOpen.Value;
    }
}
