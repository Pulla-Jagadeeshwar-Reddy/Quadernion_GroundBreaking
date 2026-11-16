using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>One stack of items in a player's inventory.</summary>
public struct InventoryEntry : INetworkSerializable, IEquatable<InventoryEntry>
{
    public FixedString32Bytes Id;
    public int Count;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref Count);
    }

    public bool Equals(InventoryEntry other) => Id.Equals(other.Id) && Count == other.Count;
    public override bool Equals(object obj) => obj is InventoryEntry other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Count);
}

/// <summary>
/// Server-authoritative item storage. Clients only read.
/// HasTestKey is kept for the original OnlineVerticalSlice scene.
/// </summary>
public class NetworkInventory : NetworkBehaviour
{
    public NetworkVariable<bool> HasTestKey = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkList<InventoryEntry> Items;

    private void Awake()
    {
        Items = new NetworkList<InventoryEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    public bool TryGiveTestKey()
    {
        if (!IsServer || HasTestKey.Value)
            return false;

        HasTestKey.Value = true;
        return true;
    }

    public int CountOf(string itemId)
    {
        if (Items == null || string.IsNullOrEmpty(itemId))
            return 0;

        if (itemId == ItemIds.TestKey && HasTestKey.Value)
            return 1;

        FixedString32Bytes key = itemId;
        foreach (InventoryEntry entry in Items)
        {
            if (entry.Id.Equals(key))
                return entry.Count;
        }

        return 0;
    }

    public bool Has(string itemId, int count = 1) => CountOf(itemId) >= count;

    public bool HasAll(ItemAmount[] items)
    {
        if (items == null)
            return true;

        foreach (ItemAmount item in items)
        {
            if (!string.IsNullOrEmpty(item.itemId) && !Has(item.itemId, item.SafeCount))
                return false;
        }

        return true;
    }

    /// <summary>Server only. Adds items to the player's inventory.</summary>
    public void ServerGive(string itemId, int count = 1)
    {
        if (!IsServer || string.IsNullOrEmpty(itemId) || count <= 0)
            return;

        if (itemId == ItemIds.TestKey)
        {
            HasTestKey.Value = true;
            return;
        }

        FixedString32Bytes key = itemId;
        for (int index = 0; index < Items.Count; index++)
        {
            InventoryEntry entry = Items[index];
            if (!entry.Id.Equals(key))
                continue;

            entry.Count += count;
            Items[index] = entry;
            return;
        }

        Items.Add(new InventoryEntry { Id = key, Count = count });
    }

    /// <summary>Server only. Removes items; returns false (and removes nothing) if there are not enough.</summary>
    public bool ServerTake(string itemId, int count = 1)
    {
        if (!IsServer || string.IsNullOrEmpty(itemId) || count <= 0)
            return false;

        FixedString32Bytes key = itemId;
        for (int index = 0; index < Items.Count; index++)
        {
            InventoryEntry entry = Items[index];
            if (!entry.Id.Equals(key))
                continue;

            if (entry.Count < count)
                return false;

            entry.Count -= count;
            if (entry.Count == 0)
                Items.RemoveAt(index);
            else
                Items[index] = entry;
            return true;
        }

        return false;
    }

    /// <summary>Server only. Empties the inventory for a new round.</summary>
    public void ServerClear()
    {
        if (!IsServer)
            return;

        Items.Clear();
        HasTestKey.Value = false;
    }
}
