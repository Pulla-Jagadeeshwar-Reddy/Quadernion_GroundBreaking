using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerController : NetworkBehaviour
{
    [Header("Local view")]
    [SerializeField] private UnityEngine.Camera playerCamera;
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float gravity = -20f;
    [SerializeField, Range(0.5f, 0.9f)] private float crouchScale = 0.55f;
    [SerializeField, Range(0.2f, 0.8f)] private float crouchSpeedMultiplier = 0.45f;
    [SerializeField] private Vector3 grannySpawn = new(-2f, 1.5f, 0f);
    [SerializeField] private Vector3 runnerSpawn = new(2f, 1.5f, -2f);

    private CharacterController characterController;
    private Vector3 standingScale;
    private float verticalSpeed;
    private float pitch;
    private bool cursorLocked;
    private bool isCrouched;

    public bool IsGranny => Unity.Netcode.NetworkManager.Singleton != null &&
                            OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        standingScale = transform.localScale;
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<UnityEngine.Camera>(true);
    }

    public override void OnNetworkSpawn()
    {
        bool isLocalPlayer = IsOwner;

        if (playerCamera != null)
        {
            playerCamera.enabled = isLocalPlayer;
            AudioListener listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = isLocalPlayer;
        }

        if (isLocalPlayer)
        {
            string markerName = IsGranny ? "Granny Spawn Marker" : "Player Spawn Marker";
            Transform spawnMarker = GameObject.Find(markerName)?.transform;
            Vector3 spawnPosition = spawnMarker != null
                ? spawnMarker.position
                : (IsGranny ? grannySpawn : runnerSpawn);
            Quaternion spawnRotation = spawnMarker != null ? spawnMarker.rotation : Quaternion.identity;
            characterController.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            Physics.SyncTransforms();
            characterController.enabled = true;
            verticalSpeed = 0f;
            LockCursor();
        }

        ApplyRoleColor();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
            return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            UnlockCursor();
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            LockCursor();

        bool wantsToCrouch = keyboard.cKey.isPressed;
        if (wantsToCrouch != isCrouched && (wantsToCrouch || CanStandUp()))
            SetCrouched(wantsToCrouch);

        if (cursorLocked && mouse != null && playerCamera != null)
        {
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        float forward;
        float sideways;
        if (IsGranny)
        {
            forward = Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed);
            sideways = Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
        }
        else
        {
            forward = Axis(keyboard.upArrowKey.isPressed, keyboard.downArrowKey.isPressed);
            sideways = Axis(keyboard.rightArrowKey.isPressed, keyboard.leftArrowKey.isPressed);
        }

        Vector3 direction = (transform.forward * forward + transform.right * sideways).normalized;
        if (characterController.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        verticalSpeed += gravity * Time.deltaTime;
        float currentMoveSpeed = isCrouched ? moveSpeed * crouchSpeedMultiplier : moveSpeed;
        Vector3 velocity = direction * currentMoveSpeed;
        velocity.y = verticalSpeed;
        characterController.Move(velocity * Time.deltaTime);
    }

    private bool CanStandUp()
    {
        float standingHeight = characterController.height * standingScale.y;
        float crouchedHeight = standingHeight * crouchScale;
        float radius = characterController.radius * Mathf.Max(standingScale.x, standingScale.z);
        Vector3 standingRootPosition = transform.position + transform.up * ((standingHeight - crouchedHeight) * 0.5f);
        Vector3 centerOffset = transform.rotation * Vector3.Scale(characterController.center, standingScale);
        Vector3 capsuleCenter = standingRootPosition + centerOffset;
        float halfSegment = Mathf.Max(0f, standingHeight * 0.5f - radius);
        Vector3 lower = capsuleCenter - transform.up * halfSegment;
        Vector3 upper = capsuleCenter + transform.up * halfSegment;
        float footHeight = capsuleCenter.y - standingHeight * 0.5f;
        float floorContactTolerance = characterController.skinWidth * Mathf.Max(standingScale.y, 1f);

        Collider[] overlaps = Physics.OverlapCapsule(lower, upper, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == characterController || overlap.transform == transform || overlap.transform.IsChildOf(transform))
                continue;
            if (overlap.bounds.max.y <= footHeight + floorContactTolerance)
                continue;

            return false;
        }

        return true;
    }

    private void SetCrouched(bool crouched)
    {
        float standingHeight = characterController.height * standingScale.y;
        float currentHeight = standingHeight * (isCrouched ? crouchScale : 1f);
        float targetHeight = standingHeight * (crouched ? crouchScale : 1f);
        Vector3 positionAdjustment = transform.up * ((targetHeight - currentHeight) * 0.5f);

        characterController.enabled = false;
        transform.localScale = standingScale;
        if (crouched)
            transform.localScale = new Vector3(standingScale.x, standingScale.y * crouchScale, standingScale.z);
        transform.position += positionAdjustment;
        Physics.SyncTransforms();
        characterController.enabled = true;
        isCrouched = crouched;
    }

    private static float Axis(bool positive, bool negative)
    {
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

    private void ApplyRoleColor()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Color roleColor = IsGranny ? new Color(0.65f, 0.12f, 0.12f) : new Color(0.12f, 0.45f, 0.8f);
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
                continue;

            Material material = targetRenderer.material;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", roleColor);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", roleColor);
        }
    }

    private void LockCursor()
    {
        cursorLocked = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        cursorLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
