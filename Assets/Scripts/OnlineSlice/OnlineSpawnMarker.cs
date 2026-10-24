using UnityEngine;

/// <summary>Editor-only visualization for named spawn points used by NetworkPlayerController.</summary>
public sealed class OnlineSpawnMarker : MonoBehaviour
{
    [SerializeField] private Color gizmoColor = Color.cyan;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, 0.45f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.2f);
    }
}
