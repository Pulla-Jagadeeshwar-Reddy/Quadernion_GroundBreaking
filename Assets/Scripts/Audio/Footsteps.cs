using UnityEngine;

/// <summary>
/// Footsteps for every player (local and remote), from how far the body moved, so it needs no
/// networking. Crouching is near silent, sprinting is loud, Granny's steps are heavier and lower.
/// </summary>
public class Footsteps : MonoBehaviour
{
    [SerializeField] private float walkStride = 0.75f;
    [SerializeField] private float sprintStride = 1.05f;

    private NetworkPlayerController player;
    private Vector3 lastPosition;
    private float travelled;

    private void Awake()
    {
        player = GetComponent<NetworkPlayerController>();
        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;
        float vertical = Mathf.Abs(delta.y);
        delta.y = 0f;
        float distance = delta.magnitude;

        // Teleports and falls are not steps.
        if (distance > 1.5f || vertical > 0.5f || player == null || !player.IsSpawned)
        {
            travelled = 0f;
            return;
        }

        float speed = distance / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed < 0.4f)
            return;

        bool crouched = player.IsCrouched.Value;
        bool sprinting = speed > 5.2f;
        travelled += distance;
        float stride = sprinting ? sprintStride : walkStride;
        if (travelled < stride)
            return;

        travelled = 0f;
        bool granny = player.IsGranny;
        float volume = crouched ? 0.12f : sprinting ? 0.85f : 0.45f;
        float range = crouched ? 5f : sprinting ? 22f : 14f;
        if (granny)
        {
            volume = Mathf.Min(1f, volume * 1.4f);
            range *= 1.3f;
        }

        string group = granny && Sfx.Has("step_heavy") ? "step_heavy" : "step_floor";
        Sfx.Play(group, position, volume, range, granny ? 0.8f : 1f);
    }
}
