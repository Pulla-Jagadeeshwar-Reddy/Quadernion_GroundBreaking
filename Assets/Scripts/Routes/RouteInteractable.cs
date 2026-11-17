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

    [Header("Deposits")]
    [Tooltip("Required items that can be placed here one at a time (fuses, planks, the ice block) instead of carrying them all at once.")]
    [SerializeField] private string[] depositItemIds = System.Array.Empty<string>();
    [Tooltip("Shown while anything is placed here and the step is not complete (e.g. the ice block on the bench).")]
    [SerializeField] private GameObject[] depositVisuals;

    /// <summary>How many of each deposit item (same order as depositItemIds) are already placed here.</summary>
    private NetworkList<int> deposited;

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

    /// <summary>Builder setup: items that can be placed here one at a time.</summary>
    public void ConfigureDeposits(string[] itemIds, GameObject[] visuals = null)
    {
        depositItemIds = itemIds ?? System.Array.Empty<string>();
        depositVisuals = visuals;
    }

    private int DepositIndex(string itemId)
    {
        if (depositItemIds == null)
            return -1;
        for (int index = 0; index < depositItemIds.Length; index++)
        {
            if (depositItemIds[index] == itemId)
                return index;
        }

        return -1;
    }

    /// <summary>How many of this item are already placed here.</summary>
    public int DepositedOf(string itemId)
    {
        int index = DepositIndex(itemId);
        return index >= 0 && deposited != null && index < deposited.Count ? deposited[index] : 0;
    }

    /// <summary>Items toward this step: carried plus already placed here.</summary>
    public int HaveCount(string itemId, NetworkInventory inventory) =>
        (inventory != null ? inventory.CountOf(itemId) : 0) + DepositedOf(itemId);

    private bool AnyDeposited()
    {
        if (deposited == null)
            return false;
        foreach (int count in deposited)
        {
            if (count > 0)
                return true;
        }

        return false;
    }

    /// <summary>A deposit item the player carries and this step still needs, or null.</summary>
    private string PlaceableItem(NetworkInventory inventory)
    {
        if (inventory == null || requiredItems == null || depositItemIds == null)
            return null;

        foreach (ItemAmount item in requiredItems)
        {
            if (DepositIndex(item.itemId) < 0)
                continue;
            if (DepositedOf(item.itemId) < item.SafeCount && inventory.CountOf(item.itemId) > 0)
                return item.itemId;
        }

        return null;
    }

    /// <summary>Server: move whatever deposit items the player carries (up to what is needed) onto this step.</summary>
    private bool ServerDeposit(NetworkInventory inventory)
    {
        if (inventory == null || requiredItems == null || deposited == null)
            return false;

        bool placed = false;
        foreach (ItemAmount item in requiredItems)
        {
            int index = DepositIndex(item.itemId);
            if (index < 0 || index >= deposited.Count)
                continue;

            int give = Mathf.Min(item.SafeCount - deposited[index], inventory.CountOf(item.itemId));
            if (give <= 0 || !inventory.ServerTake(item.itemId, give))
                continue;

            deposited[index] += give;
            placed = true;
        }

        return placed;
    }

    private void ServerClearDeposits()
    {
        if (deposited == null || depositItemIds == null)
            return;
        while (deposited.Count < depositItemIds.Length)
            deposited.Add(0);
        for (int index = 0; index < deposited.Count; index++)
            deposited[index] = 0;
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
        deposited = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this))
            All.Add(this);

        if (IsServer)
            ServerClearDeposits();

        Stage.OnValueChanged += OnStageChanged;
        Active.OnValueChanged += OnActiveChanged;
        deposited.OnListChanged += OnDepositsChanged;
        HolderClientId.OnValueChanged += OnHolderChanged;
        ApplyVisuals();
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        Stage.OnValueChanged -= OnStageChanged;
        Active.OnValueChanged -= OnActiveChanged;
        deposited.OnListChanged -= OnDepositsChanged;
        HolderClientId.OnValueChanged -= OnHolderChanged;
        holdSound?.End();
    }

    private void OnDepositsChanged(NetworkListEvent<int> change)
    {
        ApplyVisuals();
        if (RouteManager.RoundLive && change.Type == NetworkListEvent<int>.EventType.Value && change.Value > change.PreviousValue)
            Sfx.Play("place", transform.position, 0.8f, 14f);
    }

    private void OnStageChanged(int previous, int current)
    {
        ApplyVisuals();
        if (RouteManager.RoundLive && current > previous)
            Sfx.Play(Sounds.done, transform.position, 1f, 26f);
    }

    // ------------------------------------------------------------------ sound

    private SfxRepeater holdSound;

    /// <summary>What this step sounds like while held (group, seconds between plays, volume), and when a stage finishes.</summary>
    private (string loop, float interval, float volume, string done) Sounds => objectId switch
    {
        _ when objectId.StartsWith("R1_KeyCabinet") => ("hit", 0.55f, 0.8f, "shatter"),
        "R1_TV" => ("beep", 0.7f, 0.6f, "confirm"),
        "R1_StairDoor" => ("pry", 0.8f, 0.8f, "unlock"),
        "R2_PanelCover" => ("screw", 0.9f, 0.45f, "metal_drop"),
        "R2_Panel" => ("electric", 0.9f, 0.7f, "power_on"),
        "R3_LadderSpot" => ("hammer", 0.45f, 0.8f, "wood_place"),
        "R5_Freezer" => ("pry", 0.8f, 0.8f, "door_open"),
        "R5_ChainLock" => ("chain", 0.6f, 0.8f, "unlock"),
        _ when objectId.StartsWith("R3_PC") => ("hack", 1.6f, 0.22f, "confirm"),
        _ when objectId.StartsWith("R5_SmashBench") => ("ice_hit", 0.6f, 0.8f, "ice_shatter"),
        _ => ("rummage", 0.6f, 0.8f, "confirm"),
    };

    private void OnHolderChanged(ulong previous, ulong current)
    {
        holdSound ??= new SfxRepeater(Sounds.loop, Sounds.interval, Sounds.volume, Sounds.loop == "hack");
        if (current != ulong.MaxValue && RouteManager.RoundLive)
            holdSound.Begin(holdSeconds * (1f - GetHoldProgress()));
        else
            holdSound.End();
    }
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
        SetAll(depositVisuals, !complete && AnyDeposited());

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
        {
            // Still missing something, but carrying a piece that can be placed here now.
            string placeable = PrerequisitesDone() && DescribeMissing(inventory) != null ? PlaceableItem(inventory) : null;
            if (placeable != null)
                return $"E  Place {ItemIds.DisplayName(placeable)} on the {displayName}  (still needs {DescribeMissing(inventory)})";
            return blocked;
        }

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

    private bool PrerequisitesDone()
    {
        if (prerequisites == null)
            return true;
        foreach (RouteInteractable prerequisite in prerequisites)
        {
            if (prerequisite != null && !prerequisite.IsCompleted)
                return false;
        }

        return true;
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

            int have = HaveCount(item.itemId, inventory);
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

        if (!TryGetPlayer(sender, out NetworkPlayerController player, out NetworkInventory inventory))
            return;

        // Place whatever pieces the player carries (fuses, planks, the ice block) before checking
        // whether everything is here; if this completes the set, the hold starts right away.
        if (player.MatchStarted.Value && !player.ActionsLocked && !(runnerOnly && player.IsGranny) &&
            PrerequisitesDone() && DistanceTo(player.transform.position) <= serverInteractionRange &&
            ServerDeposit(inventory))
        {
            PlacedRpc(ItemSummary(), RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        if (!CanServerUse(player, inventory))
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

    private string ItemSummary()
    {
        List<string> parts = new();
        if (requiredItems != null)
        {
            foreach (ItemAmount item in requiredItems)
            {
                if (DepositIndex(item.itemId) >= 0)
                    parts.Add($"{ItemIds.DisplayName(item.itemId)} {DepositedOf(item.itemId)}/{item.SafeCount}");
            }
        }

        string text = $"Placed on the {displayName}: {string.Join(", ", parts)}";
        return text.Length > 120 ? text.Substring(0, 120) : text;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void PlacedRpc(Unity.Collections.FixedString128Bytes text, RpcParams rpcParams)
    {
        RunnerHud.AddToast(text.ToString(), new Color(0.75f, 0.9f, 1f));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelHoldRpc(RpcParams rpcParams = default)
    {
        if (HolderClientId.Value == rpcParams.Receive.SenderClientId)
            ServerStopHold(true);
    }

    private void Update()
    {
        holdSound?.Tick(transform.position);
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
            bool finalStep = Stage.Value + 1 >= steps;
            ulong holder = HolderClientId.Value;
            ServerStopHold(false);
            ServerCompleteStep(player, inventory);

            // Multi-stage steps (the ladder): a player still holding E carries on with the next
            // stage instead of having to let go and press again. Letting go cancels as usual.
            if (!finalStep && Active.Value && !IsCompleted && CanServerUse(player, inventory))
            {
                HolderClientId.Value = holder;
                HoldStartServerTime.Value = ServerNow();
            }
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
        if (player == null || !player.MatchStarted.Value || player.ActionsLocked)
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
                if (string.IsNullOrEmpty(item.itemId))
                    continue;
                // Pieces already placed here count toward what is used up.
                int fromInventory = item.SafeCount - DepositedOf(item.itemId);
                if (fromInventory > 0)
                    inventory.ServerTake(item.itemId, fromInventory);
            }
        }

        if (finalStep)
            ServerClearDeposits();

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
        ServerClearDeposits();
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
