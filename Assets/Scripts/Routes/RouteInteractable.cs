using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public enum NoiseLevel
{
    None = 0,
    Small = 1,
    Loud = 2,
}

/// <summary>
/// A data-driven route step: smash the cabinet, hack a PC, fit the fuses, build the ladder...
/// The Runner holds E; the server checks range, items, crouch and prerequisites every frame and
/// completes the step when the hold finishes. Progress is kept if the Runner lets go.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class RouteInteractable : NetworkBehaviour, INetworkInteractable
{
    [Header("Identity")]
    [SerializeField] private string objectId = "R0_Step";
    [SerializeField] private string routeName = "";
    [SerializeField] private string displayName = "Object";
    [SerializeField] private string actionVerb = "Use";

    [Header("Rules")]
    [SerializeField, Min(0f)] private float holdSeconds = 3f;
    [SerializeField, Min(1)] private int steps = 1;
    [SerializeField] private bool runnerOnly = true;
    [SerializeField] private bool requiresCrouch;
    [Tooltip("Optional trigger volume the Runner must be standing in (e.g. behind the crouch cover).")]
    [SerializeField] private Collider requiredZone;
    [SerializeField] private RouteInteractable[] prerequisites;
    [SerializeField] private ItemAmount[] requiredItems;
    [SerializeField] private ItemAmount[] consumedItems;
    [SerializeField] private ItemAmount[] grantedItems;
    [SerializeField] private NoiseLevel noiseOnComplete = NoiseLevel.None;
    [SerializeField] private NoiseLevel noiseWhileHolding = NoiseLevel.None;
    [SerializeField] private float serverInteractionRange = 3.5f;

    [Header("Visuals (toggled on every peer)")]
    [SerializeField] private GameObject[] showWhenComplete;
    [SerializeField] private GameObject[] hideWhenComplete;
    [Tooltip("Shown cumulatively as steps complete (stage 1 shows element 0, and so on).")]
    [SerializeField] private GameObject[] stageVisuals;
    [Tooltip("Shown while this step is available (glow, LEDs, screen).")]
    [SerializeField] private GameObject[] availableHints;

    public NetworkVariable<bool> Active = new(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Stage = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> HolderClientId = new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> HoldStartServerTime = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> SavedSeconds = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly List<ItemAmount> bonusGrants = new();
    private float nextHoldNoiseTime;
    private Renderer[] ownRenderers;
    private Collider[] ownColliders;

    public static readonly List<RouteInteractable> All = new();

    public string ObjectId => objectId;
    public string RouteName => routeName;
    public string DisplayName => displayName;
    public string ActionVerb => actionVerb;
    public bool IsCompleted => Stage.Value >= steps;
    public bool IsActive => Active.Value;
    public float HoldSeconds => holdSeconds;
    public int Steps => steps;
    public bool RequiresCrouch => requiresCrouch;
    public RouteInteractable[] Prerequisites => prerequisites;
    public ItemAmount[] RequiredItems => requiredItems;
    public ItemAmount[] ConsumedItems => consumedItems;
    public ItemAmount[] GrantedItems => grantedItems;
    public IReadOnlyList<ItemAmount> BonusGrants => bonusGrants;
    private bool IsHeld => HolderClientId.Value != ulong.MaxValue;

    /// <summary>Builder setup. Called in the editor before the prefab is saved.</summary>
    public void Configure(string id, string route, string name, string verb, float hold, int stepCount,
        bool crouch, NoiseLevel completeNoise, NoiseLevel holdNoise,
        RouteInteractable[] prereqs, ItemAmount[] required, ItemAmount[] consumed, ItemAmount[] granted)
    {
        objectId = id;
        routeName = route;
        displayName = name;
        actionVerb = verb;
        holdSeconds = hold;
        steps = Mathf.Max(1, stepCount);
        requiresCrouch = crouch;
        noiseOnComplete = completeNoise;
        noiseWhileHolding = holdNoise;
        prerequisites = prereqs;
        requiredItems = required;
        consumedItems = consumed;
        grantedItems = granted;
    }

    public void ConfigureVisuals(GameObject[] show, GameObject[] hide, GameObject[] stages, GameObject[] hints, Collider zone)
    {
        showWhenComplete = show;
        hideWhenComplete = hide;
        stageVisuals = stages;
        availableHints = hints;
        requiredZone = zone;
    }

    private void Awake()
    {
        ownRenderers = GetComponentsInChildren<Renderer>(true);
        ownColliders = GetComponentsInChildren<Collider>(true);
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this))
            All.Add(this);

        Stage.OnValueChanged += OnStageChanged;
        Active.OnValueChanged += OnActiveChanged;
        ApplyVisuals();
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Stage.OnValueChanged -= OnStageChanged;
        Active.OnValueChanged -= OnActiveChanged;
    }

    private void OnStageChanged(int previous, int current) => ApplyVisuals();
    private void OnActiveChanged(bool previous, bool current) => ApplyVisuals();

    private void ApplyVisuals()
    {
        bool active = Active.Value;
        foreach (Renderer target in ownRenderers)
        {
            if (target != null)
                target.enabled = active;
        }

        foreach (Collider target in ownColliders)
        {
            if (target != null && target != requiredZone)
                target.enabled = active;
        }

        if (!active)
            return;

        bool complete = IsCompleted;
        SetAll(showWhenComplete, complete);
        SetAll(hideWhenComplete, !complete);
        SetAll(availableHints, !complete);

        if (stageVisuals != null)
        {
            for (int index = 0; index < stageVisuals.Length; index++)
            {
                if (stageVisuals[index] != null)
                    stageVisuals[index].SetActive(index < Stage.Value);
            }
        }
    }

    private static void SetAll(GameObject[] targets, bool value)
    {
        if (targets == null)
            return;

        foreach (GameObject target in targets)
        {
            if (target != null && target.activeSelf != value)
                target.SetActive(value);
        }
    }

    // ---------------------------------------------------------------- client side

    public string GetPrompt(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (!IsSpawned || !Active.Value || player == null || IsCompleted)
            return null;

        if (runnerOnly && player.IsGranny)
            return null;

        string blocked = GetBlockedReason(player, inventory, player.IsCrouched.Value, true);
        if (blocked != null)
            return blocked;

        if (IsHeld && HolderClientId.Value != Unity.Netcode.NetworkManager.Singleton.LocalClientId)
            return "Someone is using this";

        string stepText = steps > 1 ? $" ({Stage.Value + 1}/{steps})" : string.Empty;
        return holdSeconds > 0f
            ? $"Hold E  {actionVerb} {displayName}{stepText}"
            : $"E  {actionVerb} {displayName}{stepText}";
    }

    public float GetHoldProgress()
    {
        if (holdSeconds <= 0f || !IsSpawned)
            return 0f;

        float seconds = SavedSeconds.Value;
        if (IsHeld)
            seconds = ServerNow() - HoldStartServerTime.Value;

        return Mathf.Clamp01(seconds / holdSeconds);
    }

    public void BeginInteract() => BeginHoldRpc();
    public void CancelInteract() => CancelHoldRpc();

    // ---------------------------------------------------------------- rules (both sides)

    /// <summary>Why the player cannot use this right now, or null when they can.</summary>
    private string GetBlockedReason(NetworkPlayerController player, NetworkInventory inventory, bool crouched, bool forPrompt)
    {
        if (prerequisites != null)
        {
            foreach (RouteInteractable prerequisite in prerequisites)
            {
                if (prerequisite != null && !prerequisite.IsCompleted)
                    return forPrompt ? $"{displayName}: {prerequisite.actionVerb} the {prerequisite.displayName} first" : "prereq";
            }
        }

        string missing = DescribeMissing(inventory);
        if (missing != null)
            return forPrompt ? $"{displayName}: needs {missing}" : "items";

        if (requiresCrouch && !crouched)
            return forPrompt ? $"Crouch (C) to {actionVerb.ToLowerInvariant()} the {displayName}" : "crouch";

        if (requiredZone != null && player != null && !requiredZone.bounds.Contains(player.transform.position))
            return forPrompt ? $"Get behind cover to {actionVerb.ToLowerInvariant()}" : "zone";

        return null;
    }

    private string DescribeMissing(NetworkInventory inventory)
    {
        if (requiredItems == null || requiredItems.Length == 0)
            return null;

        StringBuilder builder = null;
        foreach (ItemAmount item in requiredItems)
        {
            if (string.IsNullOrEmpty(item.itemId))
                continue;

            int have = inventory != null ? inventory.CountOf(item.itemId) : 0;
            if (have >= item.SafeCount)
                continue;

            builder ??= new StringBuilder();
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append(ItemIds.DisplayName(item.itemId));
            if (item.SafeCount > 1)
                builder.Append($" {have}/{item.SafeCount}");
        }

        return builder?.ToString();
    }

    private float ServerNow()
    {
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        return manager != null ? manager.ServerTime.TimeAsFloat : Time.time;
    }

    // ---------------------------------------------------------------- server side

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginHoldRpc(RpcParams rpcParams = default)
    {
        if (!Active.Value || IsCompleted)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (IsHeld && HolderClientId.Value != sender)
            return;

        if (!TryGetPlayer(sender, out NetworkPlayerController player, out NetworkInventory inventory) ||
            !CanServerUse(player, inventory))
            return;

        if (holdSeconds <= 0f)
        {
            ServerCompleteStep(player, inventory);
            return;
        }

        HolderClientId.Value = sender;
        HoldStartServerTime.Value = ServerNow() - SavedSeconds.Value;
        nextHoldNoiseTime = Time.time;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelHoldRpc(RpcParams rpcParams = default)
    {
        if (HolderClientId.Value == rpcParams.Receive.SenderClientId)
            ServerStopHold(true);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || !IsHeld)
            return;

        if (!TryGetPlayer(HolderClientId.Value, out NetworkPlayerController player, out NetworkInventory inventory) ||
            !CanServerUse(player, inventory))
        {
            ServerStopHold(true);
            return;
        }

        if (noiseWhileHolding != NoiseLevel.None && Time.time >= nextHoldNoiseTime)
        {
            nextHoldNoiseTime = Time.time + 3f;
            RouteManager.Instance?.ServerMakeNoise(transform.position, noiseWhileHolding, $"{actionVerb} {displayName}");
        }

        if (ServerNow() - HoldStartServerTime.Value >= holdSeconds)
        {
            ServerStopHold(false);
            ServerCompleteStep(player, inventory);
        }
    }

    private void ServerStopHold(bool keepProgress)
    {
        SavedSeconds.Value = keepProgress
            ? Mathf.Clamp(ServerNow() - HoldStartServerTime.Value, 0f, holdSeconds)
            : 0f;
        HolderClientId.Value = ulong.MaxValue;
    }

    private bool CanServerUse(NetworkPlayerController player, NetworkInventory inventory)
    {
        if (player == null || !player.MatchStarted.Value || player.MovementLocked.Value)
            return false;

        if (runnerOnly && player.IsGranny)
            return false;

        if (DistanceTo(player.transform.position) > serverInteractionRange)
            return false;

        return GetBlockedReason(player, inventory, player.IsCrouched.Value, false) == null;
    }

    public float DistanceTo(Vector3 position)
    {
        float best = Vector3.Distance(position, transform.position);
        foreach (Collider target in ownColliders)
        {
            if (target == null || !target.enabled || target.isTrigger)
                continue;

            best = Mathf.Min(best, Vector3.Distance(position, target.ClosestPoint(position)));
        }

        return best;
    }

    private void ServerCompleteStep(NetworkPlayerController player, NetworkInventory inventory)
    {
        bool finalStep = Stage.Value + 1 >= steps;
        if (finalStep && consumedItems != null)
        {
            foreach (ItemAmount item in consumedItems)
            {
                if (!string.IsNullOrEmpty(item.itemId))
                    inventory.ServerTake(item.itemId, item.SafeCount);
            }
        }

        Stage.Value += 1;
        SavedSeconds.Value = 0f;

        if (!finalStep)
            return;

        if (grantedItems != null)
        {
            foreach (ItemAmount item in grantedItems)
                inventory.ServerGive(item.itemId, item.SafeCount);
        }

        foreach (ItemAmount item in bonusGrants)
            inventory.ServerGive(item.itemId, item.SafeCount);

        if (noiseOnComplete != NoiseLevel.None)
            RouteManager.Instance?.ServerMakeNoise(transform.position, noiseOnComplete, displayName);

        RouteManager.Instance?.ServerOnStepCompleted(this, player);
    }

    private static bool TryGetPlayer(ulong clientId, out NetworkPlayerController player, out NetworkInventory inventory)
    {
        player = null;
        inventory = null;
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            return false;

        player = client.PlayerObject.GetComponent<NetworkPlayerController>();
        inventory = client.PlayerObject.GetComponent<NetworkInventory>();
        return player != null && inventory != null;
    }

    /// <summary>Server only: reset for a new round.</summary>
    public void ServerReset(bool active)
    {
        if (!IsServer)
            return;

        bonusGrants.Clear();
        HolderClientId.Value = ulong.MaxValue;
        SavedSeconds.Value = 0f;
        Stage.Value = 0;
        Active.Value = active;
    }

    /// <summary>Server only: an extra item handed out on completion (e.g. the janitor key on one PC).</summary>
    public void ServerAddBonusGrant(string itemId, int count)
    {
        if (IsServer && !string.IsNullOrEmpty(itemId))
            bonusGrants.Add(new ItemAmount(itemId, count));
    }

    /// <summary>Server only: Granny hit the Runner, so any hold in progress is lost.</summary>
    public void ServerInterrupt(ulong clientId)
    {
        if (IsServer && HolderClientId.Value == clientId)
            ServerStopHold(false);
    }
}
