using UnityEngine;

/// <summary>
/// "Lock the door nearest to this spot with item X." Lets route content lock a floor door
/// (e.g. the janitor room) without referencing anything inside the floor model.
/// The RouteManager applies it on the server at the start of every round.
/// </summary>
public class DoorLockRule : MonoBehaviour
{
    [SerializeField] private string requiredItemId = ItemIds.R3JanitorKey;
    [SerializeField] private float searchRadius = 3f;

    public string RequiredItemId => requiredItemId;
    public float SearchRadius => searchRadius;

    public void Configure(string itemId, float radius)
    {
        requiredItemId = itemId;
        searchRadius = radius;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}
