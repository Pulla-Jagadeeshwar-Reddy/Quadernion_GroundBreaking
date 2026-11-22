using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = System.Random;

/// <summary>One kind of item the server scatters each round.</summary>
[Serializable]
public class ItemSpawnRule
{
    public string itemId;
    [Min(1)] public int count = 1;
    public SpawnCategory categories = SpawnCategory.SmallItem;
    [Tooltip("If set, every copy must be placed in this room (e.g. Janitor Room).")]
    public string requiredRoom = "";
    [Tooltip("Rooms this item may never be placed in (usually the room where it is used).")]
    public string[] forbiddenRooms = Array.Empty<string>();
}

[Serializable]
public class PickupPrefabEntry
{
    public string itemId;
    public GameObject prefab;
}

public enum RoundPhase
{
    Waiting = 0,
    Playing = 1,
    Ended = 2,
}

/// <summary>
/// Server brain for the first-floor match: resets the round, activates a random set of PCs,
/// scatters every route item so that all four routes stay completable, routes noise pings to
/// Granny, and ends the round on escape or catch.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class RouteManager : NetworkBehaviour
{
    public static RouteManager Instance { get; private set; }

    /// <summary>True while a round is being played (state-change sounds stay quiet during resets).</summary>
    public static bool RoundLive => Instance != null && Instance.IsSpawned && Instance.Phase.Value == RoundPhase.Playing;

    [Header("Items")]
    [SerializeField] private ItemSpawnRule[] itemRules = Array.Empty<ItemSpawnRule>();
    [SerializeField] private PickupPrefabEntry[] pickupPrefabs = Array.Empty<PickupPrefabEntry>();

    [Header("Ladder route")]
    [SerializeField] private RouteInteractable[] pcCandidates = Array.Empty<RouteInteractable>();
    [SerializeField, Min(1)] private int activePcCount = 3;
    [Tooltip("Alternative spots for the ice-smashing bench; one is used each round.")]
    [SerializeField] private RouteInteractable[] benchCandidates = Array.Empty<RouteInteractable>();
    [Tooltip("Alternative wall spots for the key cabinet; one is used each round.")]
    [SerializeField] private RouteInteractable[] cabinetCandidates = Array.Empty<RouteInteractable>();
    [SerializeField] private string pcBonusItem = ItemIds.R3JanitorKey;

    [Header("Pressure")]
    [SerializeField] private float smallNoiseRadius = 18f;
    [SerializeField] private float bellKeyPingInterval = 8f;
    [SerializeField] private float catchRange = 1.8f;
    [Tooltip("Round length; when it runs out Granny wins. 0 = no limit.")]
    [SerializeField, Min(0f)] private float roundMinutes = 10f;
    [Tooltip("Seconds Granny is held at her spawn when a round starts, so the Runner gets a head start. " +
             "The round timer starts when she is released. 0 = no delay.")]
    [SerializeField, Min(0f)] private float grannyReleaseSeconds = 15f;

    [Header("Placement")]
    [SerializeField, Min(1)] private int maxPlacementAttempts = 25;

    public NetworkVariable<RoundPhase> Phase = new(RoundPhase.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString128Bytes> ResultText = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> RunnerWon = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> RoundSeed = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Server time the round runs out (0 = no limit).</summary>
    public NetworkVariable<double> RoundEndTime = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Server time Granny is released at round start (0 = not held).</summary>
    public NetworkVariable<double> GrannyReleaseTime = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Players who pressed Play again on the result screen.</summary>
    public NetworkVariable<int> RematchVotes = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly HashSet<ulong> rematchVoters = new();
    private bool votedLocally;
    private int lastTickSecond = -1;
    private int lastReleaseSecond = -1;
    private bool wasHeld;

    /// <summary>True while Granny is still held at her spawn at the start of a round.</summary>
    public static bool GrannyHeld => GrannyReleaseLeft > 0;

    /// <summary>Seconds until Granny is released on this peer (0 once she is out).</summary>
    public static double GrannyReleaseLeft
    {
        get
        {
            if (!RoundLive || Instance.GrannyReleaseTime.Value <= 0 || Instance.NetworkManager == null)
                return 0;
            return Math.Max(0, Instance.GrannyReleaseTime.Value - Instance.NetworkManager.ServerTime.Time);
        }
    }

    private readonly List<NetworkObject> spawnedPickups = new();
    private readonly List<NoisePing> grannyPings = new();
    private float nextBellPingTime;

    public ItemSpawnRule[] ItemRules => itemRules;
    public PickupPrefabEntry[] PickupPrefabs => pickupPrefabs;
    public RouteInteractable[] PcCandidates => pcCandidates;
    public int ActivePcCount => activePcCount;
    public string PcBonusItem => pcBonusItem;
    public float CatchRange => catchRange;

    private struct NoisePing
    {
        public string Text;
        public float Time;
    }

    /// <summary>Builder setup.</summary>
    public void Configure(ItemSpawnRule[] rules, PickupPrefabEntry[] prefabs, RouteInteractable[] pcs, int pcCount, string bonusItem,
        RouteInteractable[] benches = null, RouteInteractable[] cabinets = null)
    {
        benchCandidates = benches ?? Array.Empty<RouteInteractable>();
        cabinetCandidates = cabinets ?? Array.Empty<RouteInteractable>();
        itemRules = rules;
        pickupPrefabs = prefabs;
        pcCandidates = pcs;
        activePcCount = pcCount;
        pcBonusItem = bonusItem;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;

        // In-scene objects keep their network values from the last session (after a disconnect and
        // a new host), so a new session must start from Waiting or the round never resets and the
        // previous round's open doors/exits carry over.
        if (IsServer)
        {
            ServerBackToWaiting();
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        Phase.OnValueChanged += OnPhaseChanged;
        Sfx.StartAmbience();
    }

    /// <summary>Seconds left in the round on this peer (negative when there is no limit).</summary>
    /// The clock stands at the full round length until Granny is released.
    public double SecondsLeft =>
        RoundEndTime.Value <= 0 || NetworkManager == null ? -1
        : Math.Min(RoundEndTime.Value - NetworkManager.ServerTime.Time,
            GrannyReleaseTime.Value > 0 ? RoundEndTime.Value - GrannyReleaseTime.Value : double.MaxValue);

    private void OnPhaseChanged(RoundPhase previous, RoundPhase current)
    {
        votedLocally = false;
        if (current == RoundPhase.Playing)
        {
            Sfx.Play2D("round_start", 0.7f);
            return;
        }

        if (current != RoundPhase.Ended)
            return;

        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        NetworkPlayerController local = manager != null && manager.LocalClient?.PlayerObject != null
            ? manager.LocalClient.PlayerObject.GetComponent<NetworkPlayerController>()
            : null;
        bool localWon = local != null && (local.IsGranny ? !RunnerWon.Value : RunnerWon.Value);
        Sfx.Play2D(localWon ? "sting_win" : "sting_lose", 0.9f);
    }

    /// <summary>Server: play a sound for everyone at a position.</summary>
    public void ServerSfx(string group, Vector3 position, float volume = 1f, float range = 22f)
    {
        if (IsServer && IsSpawned)
            PlaySfxRpc(group, position, volume, range);
    }

    [Rpc(SendTo.Everyone)]
    private void PlaySfxRpc(FixedString32Bytes group, Vector3 position, float volume, float range) =>
        Sfx.Play(group.ToString(), position, volume, range);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRematchRpc(RpcParams rpcParams = default)
    {
        if (Phase.Value != RoundPhase.Ended)
            return;

        rematchVoters.Add(rpcParams.Receive.SenderClientId);
        int players = 0;
        foreach (NetworkPlayerController _ in AllPlayers())
            players++;
        RematchVotes.Value = rematchVoters.Count;
        if (rematchVoters.Count >= players)
            ServerStartRound();
    }

    public override void OnNetworkDespawn()
    {
        Phase.OnValueChanged -= OnPhaseChanged;
        if (Instance == this)
            Instance = null;
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void ServerBackToWaiting()
    {
        Phase.Value = RoundPhase.Waiting;
        RoundEndTime.Value = 0;
        GrannyReleaseTime.Value = 0;
        rematchVoters.Clear();
        RematchVotes.Value = 0;
        ResultText.Value = default;
        RunnerWon.Value = false;
    }

    /// <summary>
    /// Server: someone left mid-match. Send whoever is still here back to the lobby; the next match
    /// starts a fresh round.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || NetworkManager == null || NetworkManager.ShutdownInProgress || clientId == NetworkManager.ServerClientId)
            return;
        if (LocalSinglePlayerMode.IsActive || Phase.Value == RoundPhase.Waiting)
            return;

        ServerBackToWaiting();
        foreach (NetworkPlayerController player in AllPlayers())
        {
            if (player.OwnerClientId == clientId)
                continue;
            player.MatchStarted.Value = false;
            player.IsReady.Value = false;
            player.MovementLocked.Value = false;
        }
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (IsServer)
            ServerTick();

        // Granny's release: a tick for each of the last five seconds, a sting when she is out.
        bool held = GrannyHeld;
        if (held)
        {
            int second = (int)Math.Ceiling(GrannyReleaseLeft);
            if (second != lastReleaseSecond && second <= 5)
                Sfx.Play2D("tick", 0.5f);
            lastReleaseSecond = second;
        }
        else if (wasHeld && RoundLive)
        {
            Sfx.Play2D("round_start", 0.8f);
        }

        wasHeld = held;

        // Last minute: a soft tick every second.
        double left = SecondsLeft;
        if (Phase.Value == RoundPhase.Playing && left >= 0 && left <= 60)
        {
            int second = (int)Math.Ceiling(left);
            if (second != lastTickSecond)
            {
                lastTickSecond = second;
                Sfx.Play2D("tick", second <= 10 ? 0.6f : 0.3f);
            }
        }

        // Host shortcut for a quick rematch.
        if (IsServer && Phase.Value == RoundPhase.Ended && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            ServerStartRound();
    }

    // ================================================================ round flow (server)

    private void ServerTick()
    {
        if (Phase.Value == RoundPhase.Waiting && AnyPlayerInMatch())
        {
            ServerStartRound();
            return;
        }

        if (Phase.Value != RoundPhase.Playing)
            return;

        if (RoundEndTime.Value > 0 && NetworkManager.ServerTime.Time >= RoundEndTime.Value)
        {
            EndRound(false, "Time's up! Granny wins.");
            return;
        }

        NetworkPlayerController runner = FindRunner(out NetworkInventory runnerInventory);
        if (runner != null && runnerInventory != null && runnerInventory.Has(ItemIds.R5BellKey) && Time.time >= nextBellPingTime)
        {
            nextBellPingTime = Time.time + bellKeyPingInterval;
            ServerMakeNoise(runner.transform.position, NoiseLevel.Loud, "Bell key jingling");
        }
    }

    private static bool AnyPlayerInMatch()
    {
        foreach (NetworkPlayerController player in AllPlayers())
        {
            if (player.MatchStarted.Value)
                return true;
        }

        return false;
    }

    private static IEnumerable<NetworkPlayerController> AllPlayers()
    {
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null)
            yield break;

        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent(out NetworkPlayerController player))
                yield return player;
        }
    }

    private static NetworkPlayerController FindRunner(out NetworkInventory inventory)
    {
        inventory = null;
        foreach (NetworkPlayerController player in AllPlayers())
        {
            if (player.IsGranny)
                continue;

            inventory = player.GetComponent<NetworkInventory>();
            return player;
        }

        return null;
    }

    /// <summary>Server only: reset everything and lay out a fresh random round.</summary>
    public void ServerStartRound()
    {
        if (!IsServer)
            return;

        foreach (NetworkObject pickup in spawnedPickups)
        {
            if (pickup != null && pickup.IsSpawned)
                pickup.Despawn();
        }

        spawnedPickups.Clear();
        grannyPings.Clear();

        foreach (NetworkEscapePoint exit in FindObjectsByType<NetworkEscapePoint>(FindObjectsInactive.Exclude))
            exit.ServerReset();

        ResetDoors();

        int baseSeed = Environment.TickCount;
        RoutePlan plan = null;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            RoutePlan candidate = BuildPlan(baseSeed + attempt * 7919);
            if (candidate == null)
                continue;

            if (RouteSolver.AllRoutesCompletable(candidate, out _))
            {
                plan = candidate;
                break;
            }
        }

        if (plan == null)
        {
            Debug.LogWarning("RouteManager: no fully completable layout found; using the last attempt anyway.");
            plan = BuildPlan(baseSeed);
        }

        EnvInteractionManager.Instance?.ServerResetRound();
        ApplyPlan(plan);
        RoundSeed.Value = plan?.Seed ?? baseSeed;

        foreach (NetworkPlayerController player in AllPlayers())
        {
            player.GetComponent<NetworkInventory>()?.ServerClear();
            player.MovementLocked.Value = false;
            Transform marker = GameObject.Find(player.IsGranny ? "Granny Spawn Marker" : "Player Spawn Marker")?.transform;
            if (marker != null)
                player.ServerTeleport(marker.position, marker.rotation);
        }

        ResultText.Value = default;
        RunnerWon.Value = false;
        rematchVoters.Clear();
        RematchVotes.Value = 0;
        double now = NetworkManager.ServerTime.Time;
        GrannyReleaseTime.Value = grannyReleaseSeconds > 0f ? now + grannyReleaseSeconds : 0;
        double clockStart = GrannyReleaseTime.Value > 0 ? GrannyReleaseTime.Value : now;
        RoundEndTime.Value = roundMinutes > 0f ? clockStart + roundMinutes * 60.0 : 0;
        Phase.Value = RoundPhase.Playing;
        nextBellPingTime = Time.time + bellKeyPingInterval;
    }

    private void ResetDoors()
    {
        foreach (NetworkLockedDoor door in NetworkLockedDoor.AllSpawned.ToArray())
        {
            if (door != null && door.IsSpawned)
                door.ServerSetState(false, null);
        }

        foreach (DoorLockRule rule in GetComponentsInParentRoot<DoorLockRule>())
        {
            if (NetworkLockedDoor.TryFindNearest(rule.transform.position, rule.SearchRadius, out NetworkLockedDoor door))
                door.ServerSetState(false, rule.RequiredItemId);
            else
                Debug.LogWarning($"RouteManager: no door found near lock rule '{rule.name}'. Re-run Tools/Routes/Refresh After Floor Change.");
        }
    }

    private T[] GetComponentsInParentRoot<T>() where T : Component
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        return root.GetComponentsInChildren<T>(true);
    }

    // ================================================================ placement

    /// <summary>A full round layout: which PCs are on, who gets the bonus, and where each item goes.</summary>
    public class RoutePlan
    {
        public int Seed;
        public readonly List<RouteInteractable> ActivePcs = new();
        public RouteInteractable BonusPc;
        public RouteInteractable ActiveBench;
        public RouteInteractable ActiveCabinet;
        public string BonusItem;
        public readonly List<(string itemId, Vector3 position, string room)> Items = new();
        public readonly List<RouteInteractable> Interactables = new();
        public readonly List<NetworkEscapePoint> Exits = new();
        public readonly Dictionary<string, string> LockedRooms = new();
    }

    public RoutePlan BuildPlan(int seed)
    {
        return BuildPlan(seed, GetComponentsInParentRoot<RouteSpawnPoint>(), true);
    }

    /// <summary>
    /// Builds a layout. In the editor the validator passes resolveToFloor=false so it can run
    /// without physics; at runtime spots are dropped onto the real floor.
    /// </summary>
    public RoutePlan BuildPlan(int seed, RouteSpawnPoint[] points, bool resolveToFloor)
    {
        Random random = new(seed);
        RoutePlan plan = new() { Seed = seed, BonusItem = pcBonusItem };

        foreach (RoomVolume room in GetComponentsInParentRoot<RoomVolume>())
        {
            if (!string.IsNullOrEmpty(room.LockedByItem))
                plan.LockedRooms[room.RoomName] = room.LockedByItem;
        }

        // Pick the active PCs and which one hands out the janitor key.
        List<RouteInteractable> pcs = pcCandidates.Where(pc => pc != null).OrderBy(_ => random.Next()).ToList();
        plan.ActivePcs.AddRange(pcs.Take(Mathf.Min(activePcCount, pcs.Count)));
        plan.BonusPc = plan.ActivePcs.Count > 0 ? plan.ActivePcs[random.Next(plan.ActivePcs.Count)] : null;

        // One bench spot per round.
        List<RouteInteractable> benches = benchCandidates.Where(b => b != null).ToList();
        if (benches.Count > 0)
            plan.ActiveBench = benches[random.Next(benches.Count)];

        // One key cabinet wall spot per round.
        List<RouteInteractable> cabinets = cabinetCandidates.Where(c => c != null).ToList();
        if (cabinets.Count > 0)
            plan.ActiveCabinet = cabinets[random.Next(cabinets.Count)];

        foreach (RouteInteractable step in GetComponentsInParentRoot<RouteInteractable>())
        {
            if (IsActiveIn(plan, step))
                plan.Interactables.Add(step);
        }

        plan.Exits.AddRange(GetComponentsInParentRoot<NetworkEscapePoint>());

        // Resolve spots and shuffle them.
        List<(RouteSpawnPoint point, Vector3 position, string room)> spots = new();
        foreach (RouteSpawnPoint point in points)
        {
            if (point == null)
                continue;

            Vector3 position = point.transform.position;
            if (resolveToFloor && !point.TryResolve(out position))
                continue;

            spots.Add((point, position, point.RoomName));
        }

        spots = spots.OrderBy(_ => random.Next()).ToList();
        HashSet<int> used = new();

        // Most constrained rules first so they are not starved of spots.
        IEnumerable<ItemSpawnRule> orderedRules = itemRules
            .Where(rule => rule != null && !string.IsNullOrEmpty(rule.itemId))
            .OrderByDescending(rule => string.IsNullOrEmpty(rule.requiredRoom) ? 0 : 1)
            .ThenBy(rule => CountSpots(spots, rule));

        foreach (ItemSpawnRule rule in orderedRules)
        {
            HashSet<string> roomsUsedByThisItem = new();
            for (int copy = 0; copy < rule.count; copy++)
            {
                int chosen = PickSpot(spots, used, rule, roomsUsedByThisItem, plan.LockedRooms, true);
                if (chosen < 0)
                    chosen = PickSpot(spots, used, rule, roomsUsedByThisItem, plan.LockedRooms, false);
                if (chosen < 0)
                {
                    Debug.LogWarning($"RouteManager: not enough spawn points for {rule.itemId} (copy {copy + 1}/{rule.count}).");
                    return null;
                }

                used.Add(chosen);
                roomsUsedByThisItem.Add(spots[chosen].room);
                plan.Items.Add((rule.itemId, spots[chosen].position, spots[chosen].room));
            }
        }

        return plan;
    }

    private static int CountSpots(List<(RouteSpawnPoint point, Vector3 position, string room)> spots, ItemSpawnRule rule)
    {
        return spots.Count(spot => (spot.point.Categories & rule.categories) != 0);
    }

    private static int PickSpot(List<(RouteSpawnPoint point, Vector3 position, string room)> spots, HashSet<int> used,
        ItemSpawnRule rule, HashSet<string> roomsUsed, Dictionary<string, string> lockedRooms, bool spreadRooms)
    {
        for (int index = 0; index < spots.Count; index++)
        {
            if (used.Contains(index))
                continue;

            var spot = spots[index];
            if ((spot.point.Categories & rule.categories) == 0)
                continue;

            if (!string.IsNullOrEmpty(rule.requiredRoom))
            {
                if (spot.room != rule.requiredRoom)
                    continue;
            }
            else
            {
                if (rule.forbiddenRooms != null && rule.forbiddenRooms.Contains(spot.room))
                    continue;

                // Items only go in a locked room when a rule asks for it explicitly.
                if (lockedRooms.ContainsKey(spot.room))
                    continue;
            }

            if (spreadRooms && roomsUsed.Contains(spot.room))
                continue;

            return index;
        }

        return -1;
    }

    private bool IsActiveIn(RoutePlan plan, RouteInteractable step)
    {
        if (pcCandidates.Contains(step))
            return plan.ActivePcs.Contains(step);
        if (benchCandidates.Contains(step))
            return step == plan.ActiveBench;
        if (cabinetCandidates.Contains(step))
            return step == plan.ActiveCabinet;
        return true;
    }

    private void ApplyPlan(RoutePlan plan)
    {
        if (plan == null)
            return;

        foreach (RouteInteractable step in GetComponentsInParentRoot<RouteInteractable>())
        {
            step.ServerReset(IsActiveIn(plan, step));
        }

        if (plan.BonusPc != null && !string.IsNullOrEmpty(plan.BonusItem))
            plan.BonusPc.ServerAddBonusGrant(plan.BonusItem, 1);

        foreach (var item in plan.Items)
        {
            // Some items are hidden in a desk in the same room instead of lying in the open.
            if (EnvInteractionManager.Instance != null && EnvInteractionManager.Instance.ServerTryHide(item.itemId, 1, item.position))
                continue;

            GameObject prefab = pickupPrefabs.FirstOrDefault(entry => entry.itemId == item.itemId)?.prefab;
            if (prefab == null)
            {
                Debug.LogWarning($"RouteManager: no pickup prefab for {item.itemId}.");
                continue;
            }

            GameObject instance = Instantiate(prefab, item.position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(true);
            spawnedPickups.Add(networkObject);
        }

        EnvInteractionManager.Instance?.ServerOpenDecoyDesks();
    }

    // ================================================================ events (server)

    public void ServerOnItemPickedUp(NetworkPickup pickup, NetworkPlayerController player)
    {
        if (pickup != null)
            spawnedPickups.Remove(pickup.NetworkObject);
    }

    public void ServerOnStepCompleted(RouteInteractable step, NetworkPlayerController player)
    {
    }

    public void ServerRunnerEscaped(NetworkEscapePoint exit, NetworkPlayerController runner)
    {
        if (Phase.Value != RoundPhase.Playing)
            return;

        string route = string.IsNullOrEmpty(exit.RouteName) ? "an exit" : exit.RouteName;
        EndRound(true, $"RUNNER ESCAPED via {route}!  Granny loses.");
    }

    public void ServerRunnerCaught(NetworkPlayerController granny, NetworkPlayerController runner)
    {
        if (Phase.Value != RoundPhase.Playing)
            return;

        foreach (RouteInteractable step in RouteInteractable.All)
            step.ServerInterrupt(runner.OwnerClientId);

        EndRound(false, "GRANNY CAUGHT THE RUNNER!  Granny wins.");
    }

    private void EndRound(bool runnerWon, string text)
    {
        RunnerWon.Value = runnerWon;
        ResultText.Value = text;
        Phase.Value = RoundPhase.Ended;
        foreach (NetworkPlayerController player in AllPlayers())
            player.MovementLocked.Value = true;
    }

    /// <summary>Server only: Granny hears something. Small noises only carry so far.</summary>
    public void ServerMakeNoise(Vector3 position, NoiseLevel level, string label)
    {
        if (!IsServer || level == NoiseLevel.None || Phase.Value != RoundPhase.Playing)
            return;

        // A running ceiling fan drowns out quiet noises in its room.
        if (level == NoiseLevel.Small && EnvInteractionManager.Instance != null && EnvInteractionManager.Instance.IsMaskedByFan(position))
            return;

        foreach (NetworkPlayerController player in AllPlayers())
        {
            if (!player.IsGranny)
                continue;

            float distance = Vector3.Distance(player.transform.position, position);
            if (level == NoiseLevel.Small && distance > smallNoiseRadius)
                continue;

            string room = RoomVolume.NameAt(position);
            string loudness = level == NoiseLevel.Loud ? "LOUD" : "Quiet";
            NoisePingRpc($"{loudness}: {label} — {room} ({Mathf.RoundToInt(distance)} m)",
                RpcTarget.Single(player.OwnerClientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void NoisePingRpc(FixedString128Bytes text, RpcParams rpcParams)
    {
        grannyPings.Add(new NoisePing { Text = text.ToString(), Time = Time.time });
        if (grannyPings.Count > 4)
            grannyPings.RemoveAt(0);
    }

    // ================================================================ HUD helpers (clients)

    /// <summary>One line per route describing the Runner's next step.</summary>
    public List<string> GetRouteStatusLines(NetworkInventory inventory)
    {
        List<string> lines = new();
        foreach (NetworkEscapePoint exit in FindObjectsByType<NetworkEscapePoint>(FindObjectsInactive.Exclude)
                     .OrderBy(e => e.RouteName))
        {
            if (string.IsNullOrEmpty(exit.RouteName))
                continue;

            lines.Add($"{exit.RouteName}: {DescribeNextStep(exit, inventory)}");
        }

        return lines;
    }

    /// <summary>One checklist row in the Runner HUD (a step, a multi-stage step, or a group of same-named steps).</summary>
    public sealed class RouteStepRow
    {
        public string Label;
        public int Done;
        public int Total;
        public bool IsNext;
        public readonly List<(string itemId, int have, int need)> Needs = new();
        public bool Complete => Done >= Total;
    }

    /// <summary>Per-route progress for the Runner HUD.</summary>
    public sealed class RouteProgress
    {
        public string Name;
        public bool Unlocked;
        public bool Escaped;
        public readonly List<RouteStepRow> Rows = new();
        public int DoneUnits => Rows.Sum(row => Mathf.Min(row.Done, row.Total));
        public int TotalUnits => Rows.Sum(row => row.Total);
    }

    /// <summary>Client-safe: builds an ordered checklist per route from the spawned route steps.</summary>
    public List<RouteProgress> GetRouteProgress(NetworkInventory inventory)
    {
        List<RouteProgress> result = new();
        foreach (NetworkEscapePoint exit in FindObjectsByType<NetworkEscapePoint>(FindObjectsInactive.Exclude)
                     .OrderBy(e => e.RouteName))
        {
            if (string.IsNullOrEmpty(exit.RouteName))
                continue;

            RouteProgress progress = new()
            {
                Name = exit.RouteName,
                Unlocked = exit.IsUnlocked(inventory),
                Escaped = exit.HasEscaped.Value,
            };

            List<RouteInteractable> steps = RouteInteractable.All
                .Where(step => step != null && step.IsActive && step.RouteName == exit.RouteName)
                .ToList();
            Dictionary<RouteInteractable, int> depth = ComputeDepths(steps);

            bool nextChosen = false;
            foreach (IGrouping<string, RouteInteractable> group in steps
                         .OrderBy(step => depth[step]).ThenBy(step => step.ObjectId, StringComparer.Ordinal)
                         .GroupBy(step => step.DisplayName))
            {
                List<RouteInteractable> members = group.ToList();
                RouteInteractable first = members[0];
                RouteStepRow row = new();
                if (members.Count > 1)
                {
                    row.Done = members.Count(step => step.IsCompleted);
                    row.Total = members.Count;
                    row.Label = $"{Capitalize(first.ActionVerb)} {first.DisplayName}s";
                }
                else
                {
                    row.Done = Mathf.Clamp(first.Stage.Value, 0, first.Steps);
                    row.Total = first.Steps;
                    row.Label = $"{Capitalize(first.ActionVerb)} {first.DisplayName}";
                }

                if (!row.Complete)
                {
                    RouteInteractable pending = members.FirstOrDefault(step => !step.IsCompleted) ?? first;
                    if (pending.RequiredItems != null)
                    {
                        foreach (ItemAmount item in pending.RequiredItems)
                        {
                            if (string.IsNullOrEmpty(item.itemId))
                                continue;
                            int have = pending.HaveCount(item.itemId, inventory);
                            row.Needs.Add((item.itemId, Mathf.Min(have, item.SafeCount), item.SafeCount));
                        }
                    }

                    bool ready = pending.Prerequisites == null || pending.Prerequisites.All(p => p == null || p.IsCompleted);
                    if (!nextChosen && ready)
                    {
                        row.IsNext = true;
                        nextChosen = true;
                    }
                }

                progress.Rows.Add(row);
            }

            result.Add(progress);
        }

        return result;
    }

    /// <summary>
    /// Orders steps by dependency: a step comes after its prerequisites and after any step on the
    /// same route that grants an item it needs (e.g. the stair door after the key cabinet).
    /// </summary>
    private static Dictionary<RouteInteractable, int> ComputeDepths(List<RouteInteractable> steps)
    {
        Dictionary<RouteInteractable, int> depth = new();

        int Depth(RouteInteractable step, int guard)
        {
            if (depth.TryGetValue(step, out int known))
                return known;
            if (guard > 12)
                return 0;

            int best = 0;
            if (step.Prerequisites != null)
            {
                foreach (RouteInteractable prerequisite in step.Prerequisites)
                {
                    if (prerequisite != null && steps.Contains(prerequisite))
                        best = Mathf.Max(best, Depth(prerequisite, guard + 1) + 1);
                }
            }

            if (step.RequiredItems != null)
            {
                foreach (ItemAmount item in step.RequiredItems)
                {
                    foreach (RouteInteractable other in steps)
                    {
                        if (other != step && other.GrantedItems != null && other.GrantedItems.Any(g => g.itemId == item.itemId))
                            best = Mathf.Max(best, Depth(other, guard + 1) + 1);
                    }
                }
            }

            depth[step] = best;
            return best;
        }

        foreach (RouteInteractable step in steps)
            Depth(step, 0);
        return depth;
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? "Use" : char.ToUpperInvariant(text[0]) + text.Substring(1);

    private static string DescribeNextStep(NetworkEscapePoint exit, NetworkInventory inventory)
    {
        if (exit.IsUnlocked(inventory))
            return "EXIT OPEN — go!";

        List<RouteInteractable> steps = RouteInteractable.All
            .Where(step => step != null && step.IsActive && step.RouteName == exit.RouteName && !step.IsCompleted)
            .ToList();
        if (steps.Count == 0)
            return "exit blocked";

        // Prefer a step whose prerequisites are all done.
        RouteInteractable next = steps.FirstOrDefault(step =>
            step.Prerequisites == null || step.Prerequisites.All(p => p == null || p.IsCompleted)) ?? steps[0];

        StringBuilder builder = new(next.DisplayName);
        if (next.RequiredItems != null)
        {
            List<string> missing = new();
            foreach (ItemAmount item in next.RequiredItems)
            {
                if (string.IsNullOrEmpty(item.itemId))
                    continue;

                int have = next.HaveCount(item.itemId, inventory);
                if (have < item.SafeCount)
                    missing.Add(item.SafeCount > 1 ? $"{ItemIds.DisplayName(item.itemId)} {have}/{item.SafeCount}" : ItemIds.DisplayName(item.itemId));
            }

            if (missing.Count > 0)
                builder.Append(" — find ").Append(string.Join(", ", missing));
        }

        if (steps.Count > 1)
            builder.Append($"  (+{steps.Count - 1} more)");
        return builder.ToString();
    }

    private void OnGUI()
    {
        if (!IsSpawned)
            return;

        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        NetworkPlayerController local = manager != null && manager.LocalClient?.PlayerObject != null
            ? manager.LocalClient.PlayerObject.GetComponent<NetworkPlayerController>()
            : null;

        if (local != null && local.IsGranny && Phase.Value == RoundPhase.Playing)
        {
            float y = Screen.height * 0.3f;
            for (int index = grannyPings.Count - 1; index >= 0; index--)
            {
                NoisePing ping = grannyPings[index];
                float age = Time.time - ping.Time;
                if (age > 6f)
                    continue;

                GUI.color = new Color(1f, 0.55f, 0.4f, Mathf.Clamp01(1.5f - age / 6f));
                GUI.Box(new Rect(16f, y, 420f, 26f), ping.Text);
                y += 30f;
            }

            GUI.color = Color.white;
        }

        DrawTimer();
        if (Phase.Value != RoundPhase.Ended)
            return;

        Rect box = new(Screen.width * 0.5f - 220f, Screen.height * 0.35f, 440f, 120f);
        GUI.Box(box, string.Empty);
        GUIStyle style = new(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(box.x + 10f, box.y + 12f, box.width - 20f, 50f), ResultText.Value.ToString(), style);

        int players = 0;
        foreach (NetworkPlayerController _ in AllPlayers())
            players++;
        GUI.enabled = !votedLocally;
        if (GUI.Button(new Rect(box.center.x - 90f, box.y + 66f, 180f, 30f), votedLocally ? "Waiting for the other player" : "Play again"))
        {
            votedLocally = true;
            RequestRematchRpc();
        }

        GUI.enabled = true;
        GUIStyle small = new(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        string footer = $"{RematchVotes.Value}/{Mathf.Max(players, 1)} ready" + (IsServer ? "   (host: R starts now)" : "");
        GUI.Label(new Rect(box.x + 10f, box.y + 98f, box.width - 20f, 20f), footer, small);
    }

    private void DrawTimer()
    {
        double left = SecondsLeft;
        if (Phase.Value != RoundPhase.Playing || left < 0)
            return;

        int seconds = Mathf.Max(0, (int)Math.Ceiling(left));
        GUIStyle style = new(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
        style.normal.textColor = seconds <= 60 ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 1f, 1f, 0.85f);
        GUI.Label(new Rect(Screen.width * 0.5f - 80f, 8f, 160f, 32f), $"{seconds / 60}:{seconds % 60:00}", style);
    }
}
