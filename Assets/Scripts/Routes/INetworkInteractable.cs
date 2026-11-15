/// <summary>
/// Anything the local player can aim at and press/hold E on.
/// Implemented by pickups, doors, escape points and route props so the interactor
/// does not need to know every concrete type.
/// </summary>
public interface INetworkInteractable
{
    /// <summary>Prompt for this player, or null/empty when there is nothing to do.</summary>
    string GetPrompt(NetworkPlayerController player, NetworkInventory inventory);

    /// <summary>Seconds E must be held. 0 means a single press.</summary>
    float HoldSeconds { get; }

    /// <summary>0..1 progress for the local player's HUD bar.</summary>
    float GetHoldProgress();

    /// <summary>Called on the owning client when E is pressed while aiming at this object.</summary>
    void BeginInteract();

    /// <summary>Called on the owning client when E is released or the aim moves away.</summary>
    void CancelInteract();
}
