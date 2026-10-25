using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkInteractor : NetworkBehaviour
{
    [SerializeField] private UnityEngine.Camera viewCamera;
    [SerializeField] private float interactionRange = 3f;

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

        if (Physics.Raycast(viewCamera.transform.position, viewCamera.transform.forward,
                out RaycastHit hit, interactionRange))
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
            NetworkLockedDoor.TryFindAimedDoor(
                new Ray(viewCamera.transform.position, viewCamera.transform.forward),
                transform.position, interactionRange, out NetworkLockedDoor aimedDoor))
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

        string message = null;
        if (focusedPickup != null)
            message = "E  Pick up key";
        else if (focusedDoor != null)
            message = focusedDoor.GetInteractionPrompt(
                GetComponent<NetworkPlayerController>(), GetComponent<NetworkInventory>());
        else if (focusedExit != null)
            message = "E  Escape";

        if (!string.IsNullOrEmpty(message))
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.72f, 280f, 36f), message);
        }

        if (IsGranny())
            GUI.Label(new Rect(15f, 15f, 200f, 30f), "Role: Granny  |  WASD to move");
        else
            GUI.Label(new Rect(15f, 15f, 220f, 30f), "Role: Player  |  Arrow keys to move");
    }

    private bool IsGranny()
    {
        NetworkPlayerController controller = GetComponent<NetworkPlayerController>();
        return controller != null && controller.IsGranny;
    }
}
