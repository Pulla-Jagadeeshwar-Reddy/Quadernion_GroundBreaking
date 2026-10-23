using Unity.Netcode;

public class NetworkInventory : NetworkBehaviour
{
    public NetworkVariable<bool> HasTestKey = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool TryGiveTestKey()
    {
        if (!IsServer || HasTestKey.Value)
            return false;

        HasTestKey.Value = true;
        return true;
    }
}
