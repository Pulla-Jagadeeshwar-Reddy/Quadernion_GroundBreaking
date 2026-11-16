/// <summary>
/// Optional second action on an interactable, bound to F (e.g. jamming a door shut).
/// </summary>
public interface ISecondaryInteractable
{
    /// <summary>Prompt line for the F action, or null/empty when not available to this player.</summary>
    string GetSecondaryPrompt(NetworkPlayerController player, NetworkInventory inventory);

    float SecondaryHoldSeconds { get; }
    float GetSecondaryProgress();
    void BeginSecondary();
    void CancelSecondary();
}
