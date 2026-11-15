using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkInteractor : NetworkBehaviour
{
    [SerializeField] private UnityEngine.Camera viewCamera;
    [SerializeField] private float interactionRange = 3.5f;

    private NetworkPickup focusedPickup;
    private NetworkLockedDoor focusedDoor;
    private NetworkEscapePoint focusedExit;

    private void Awake()
    {
        if (viewCamera == null)
            viewCamera = GetComponentInChildren<UnityEngine.Camera>(true);
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || viewCamera == null || !viewCamera.enabled)
            return;

        NetworkPlayerController player = GetComponent<NetworkPlayerController>();
        if (player == null || !player.MatchStarted.Value)
        {
            focusedPickup = null;
            focusedDoor = null;
            focusedExit = null;
            return;
        }

        focusedPickup = null;
        focusedDoor = null;
        focusedExit = null;

        Ray viewRay = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        if (Physics.Raycast(viewRay, out RaycastHit hit, interactionRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            focusedPickup = hit.collider.GetComponentInParent<NetworkPickup>();
            if (focusedPickup == null)
                focusedDoor = hit.collider.GetComponentInParent<NetworkLockedDoor>();
            if (focusedPickup == null && focusedDoor == null)
                NetworkLockedDoor.TryFindForCollider(hit.collider, out focusedDoor);
            if (focusedPickup == null && focusedDoor == null)
                focusedExit = hit.collider.GetComponentInParent<NetworkEscapePoint>();
        }

        // The first-floor door's blocker is a separate proxy, and approximate wall proxies
        // can be the first ray hit. Resolve a visible door by its rendered bounds as a fallback.
        if (focusedPickup == null && focusedDoor == null && focusedExit == null &&
            NetworkLockedDoor.TryFindAimedDoor(viewRay, transform.position, interactionRange,
                out NetworkLockedDoor aimedDoor))
            focusedDoor = aimedDoor;

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (focusedPickup != null)
                focusedPickup.InteractRpc();
            else if (focusedDoor != null)
                focusedDoor.InteractRpc();
            else if (focusedExit != null)
                focusedExit.InteractRpc();
        }
    }

    private void OnGUI()
    {
        if (!IsSpawned || !IsOwner)
            return;

        NetworkPlayerController player = GetComponent<NetworkPlayerController>();
        NetworkInventory inventory = GetComponent<NetworkInventory>();
        if (player != null && player.MatchStarted.Value)
            DrawMatchHud(player, inventory);

        string message = null;
        if (focusedPickup != null)
            message = "E  Pick up key";
        else if (focusedDoor != null)
            message = focusedDoor.GetInteractionPrompt(
                player, inventory);
        else if (focusedExit != null)
            message = "E  Escape";

        if (!string.IsNullOrEmpty(message))
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.72f, 280f, 36f), message);
        }

    }

    private static void DrawMatchHud(NetworkPlayerController player, NetworkInventory inventory)
    {
        const float width = 330f;
        const float height = 105f;
        float x = Screen.width - width - 16f;
        GUI.Box(new Rect(x, 12f, width, height), player.IsGranny ? "GRANNY" : "RUNNER");

        if (player.IsGranny)
        {
            GUI.Label(new Rect(x + 14f, 40f, width - 28f, 24f), "Stop the Runner from reaching the exit.");
            GUI.Label(new Rect(x + 14f, 66f, width - 28f, 24f), "Move: WASD    Look: Mouse");
            return;
        }

        bool hasKey = inventory != null && inventory.HasTestKey.Value;
        GUI.Label(new Rect(x + 14f, 38f, width - 28f, 24f),
            hasKey ? "Test key: Collected" : "Test key: Find it in the room");
        GUI.Label(new Rect(x + 14f, 62f, width - 28f, 24f),
            hasKey ? "Next: Open the exit, then escape" : "Goal: Find key → open exit → escape");
        GUI.Label(new Rect(x + 14f, 83f, width - 28f, 18f), "Move: Arrow keys    Look: Mouse    C: Crouch");
    }
}
