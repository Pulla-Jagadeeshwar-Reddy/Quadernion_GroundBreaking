using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local player's aim-and-press/hold-E interaction, Granny's catch, and the match HUD.
/// </summary>
public class NetworkInteractor : NetworkBehaviour
{
    [SerializeField] private UnityEngine.Camera viewCamera;
    [SerializeField] private float interactionRange = 3.5f;

    private INetworkInteractable focused;
    private INetworkInteractable holding;
    private INetworkInteractable lastBegun;
    private ISecondaryInteractable holdingSecondary;
    private NetworkPlayerController focusedRunner;
    private NetworkPlayerController player;
    private NetworkInventory inventory;

    private void Awake()
    {
        if (viewCamera == null)
            viewCamera = GetComponentInChildren<UnityEngine.Camera>(true);
        player = GetComponent<NetworkPlayerController>();
        inventory = GetComponent<NetworkInventory>();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || viewCamera == null || !viewCamera.enabled)
            return;

        if (player == null || !player.MatchStarted.Value || player.ActionsLocked)
        {
            StopHolding();
            StopSecondary();
            focused = null;
            EnvInteractionManager.Instance?.ShowFocus(null);
            focusedRunner = null;
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        // While E (or F) is held on something, keep it focused even if the aim wobbles off its edge;
        // the server still checks range, and letting go cancels.
        bool keepHold = holding != null && keyboard.eKey.isPressed && holding.HoldSeconds > 0f &&
                        !string.IsNullOrEmpty(holding.GetPrompt(player, inventory));
        if (!keyboard.eKey.isPressed)
            lastBegun = null;
        bool keepSecondary = holdingSecondary != null && keyboard.fKey.isPressed;
        if (keepHold || keepSecondary)
            focusedRunner = null;
        if (keepHold)
            focused = holding;
        else if (keepSecondary)
            focused = holdingSecondary as INetworkInteractable;
        else
            FindFocus();

        EnvInteractionManager.Instance?.ShowFocus(focused);

        // Granny catches with E or left click when the Runner is in reach.
        if (player.IsGranny && focusedRunner != null &&
            (keyboard.eKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)))
        {
            CatchRpc(focusedRunner.NetworkObjectId);
            return;
        }

        if (holding != null && (focused != holding || !keyboard.eKey.isPressed))
            StopHolding();

        // Secondary action (F), e.g. jamming a door shut.
        ISecondaryInteractable secondary = focused as ISecondaryInteractable;
        if (holdingSecondary != null && (secondary != holdingSecondary || !keyboard.fKey.isPressed))
            StopSecondary();
        if (secondary != null && keyboard.fKey.wasPressedThisFrame &&
            !string.IsNullOrEmpty(secondary.GetSecondaryPrompt(player, inventory)))
        {
            secondary.BeginSecondary();
            if (secondary.SecondaryHoldSeconds > 0f)
                holdingSecondary = secondary;
        }

        if (focused == null)
            return;

        // A fresh press starts anything. Keeping E held and sweeping onto a new hold target starts
        // that one too, so there is no need to let go and re-press when the aim lands late.
        bool begin = keyboard.eKey.wasPressedThisFrame ||
                     (keyboard.eKey.isPressed && holding == null && focused != lastBegun && focused.HoldSeconds > 0f);
        if (!begin || string.IsNullOrEmpty(focused.GetPrompt(player, inventory)))
            return;

        focused.BeginInteract();
        lastBegun = focused;
        if (focused.HoldSeconds > 0f)
            holding = focused;
    }

    private void StopSecondary()
    {
        if (holdingSecondary == null)
            return;

        holdingSecondary.CancelSecondary();
        holdingSecondary = null;
    }

    private void StopHolding()
    {
        if (holding == null)
            return;

        holding.CancelInteract();
        holding = null;
    }

    private void FindFocus()
    {
        focused = null;
        focusedRunner = null;

        Ray viewRay = new(viewCamera.transform.position, viewCamera.transform.forward);
        float firstSolid = float.PositiveInfinity;
        if (Physics.Raycast(viewRay, out RaycastHit hit, interactionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            firstSolid = hit.distance;
            NetworkPlayerController other = hit.collider.GetComponentInParent<NetworkPlayerController>();
            if (other != null && other != player)
            {
                if (player.IsGranny && !other.IsGranny &&
                    Vector3.Distance(transform.position, other.transform.position) <= CatchRange())
                    focusedRunner = other;
                return;
            }

            focused = ResolveInteractable(hit.collider);
        }

        // Forgiving aim: items often sit on a desk whose collision box is slightly bigger than the
        // desk, so the first hit is the desk. Accept an interactable hit just behind the first
        // solid hit, then try a thin sphere so small items do not need pixel-perfect aim.
        if (focused == null)
            focused = FindForgiving(viewRay, 0f) ?? FindForgiving(viewRay, 0.18f);

        // The floor model's own desks, fans and windows (ray-vs-box, no extra colliders).
        if (focused == null && EnvInteractionManager.Instance != null &&
            EnvInteractionManager.Instance.TryFindAimed(viewRay, interactionRange, firstSolid, player, inventory,
                out INetworkInteractable envTarget))
            focused = envTarget;

        // The first-floor door blocker is a separate proxy, and approximate wall proxies can be the
        // first ray hit. Resolve a visible door by its rendered bounds as a fallback.
        if (focused == null &&
            NetworkLockedDoor.TryFindAimedDoor(viewRay, transform.position, interactionRange, out NetworkLockedDoor aimedDoor))
            focused = aimedDoor;
    }

    private INetworkInteractable ResolveInteractable(Collider collider)
    {
        INetworkInteractable found = collider.GetComponentInParent<INetworkInteractable>();
        if (found == null && NetworkLockedDoor.TryFindForCollider(collider, out NetworkLockedDoor door))
            found = door;

        // A locked exit's hitbox sits in front of the step that unlocks it (ladder build spot,
        // lift chain): aim at the step it is waiting on instead of the "locked" message.
        if (found is NetworkEscapePoint exit && !exit.IsUnlocked(inventory) && exit.RequiredCompleted != null)
        {
            foreach (RouteInteractable step in exit.RequiredCompleted)
            {
                if (step != null && !step.IsCompleted && !string.IsNullOrEmpty(step.GetPrompt(player, inventory)))
                    return step;
            }
        }

        return found;
    }

    private INetworkInteractable FindForgiving(Ray ray, float radius)
    {
        RaycastHit[] hits = radius > 0f
            ? Physics.SphereCastAll(ray, radius, interactionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastAll(ray, interactionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            return null;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        float firstSolid = float.PositiveInfinity;
        foreach (RaycastHit candidate in hits)
        {
            if (candidate.collider.transform.IsChildOf(transform))
                continue;

            INetworkInteractable interactable = ResolveInteractable(candidate.collider);
            if (interactable != null && !string.IsNullOrEmpty(interactable.GetPrompt(player, inventory)))
            {
                // Through at most ~0.6 m of furniture box, never through a wall.
                if (candidate.distance <= firstSolid + 0.6f)
                    return interactable;
                return null;
            }

            if (float.IsPositiveInfinity(firstSolid))
                firstSolid = candidate.distance;
        }

        return null;
    }

    private static float CatchRange() => RouteManager.Instance != null ? RouteManager.Instance.CatchRange : 1.8f;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void CatchRpc(ulong runnerObjectId, RpcParams rpcParams = default)
    {
        if (player == null || !player.IsGranny || !player.MatchStarted.Value || player.ActionsLocked)
            return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(runnerObjectId, out NetworkObject target) ||
            !target.TryGetComponent(out NetworkPlayerController runner) || runner.IsGranny)
            return;

        // Small allowance for latency on top of the client-side range.
        if (Vector3.Distance(transform.position, runner.transform.position) > CatchRange() + 0.7f)
            return;

        RouteManager.Instance?.ServerRunnerCaught(player, runner);
    }

    // ================================================================ HUD

    private void OnGUI()
    {
        if (!IsSpawned || !IsOwner || player == null)
            return;

        if (player.MatchStarted.Value)
            DrawMatchHud();

        if (player.ActionsLocked)
            return;

        // Crosshair: always visible (white), yellow when aiming at something usable.
        bool aimingAtSomething = focusedRunner != null || (focused != null && !string.IsNullOrEmpty(focused.GetPrompt(player, inventory)));
        Rect dot = new(Screen.width * 0.5f - 3f, Screen.height * 0.5f - 3f, 6f, 6f);
        Color before = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(dot.x - 1f, dot.y - 1f, dot.width + 2f, dot.height + 2f), Texture2D.whiteTexture);
        GUI.color = aimingAtSomething ? new Color(1f, 0.85f, 0.3f) : Color.white;
        GUI.DrawTexture(dot, Texture2D.whiteTexture);
        GUI.color = before;

        string message = null;
        float progress = 0f;
        if (focusedRunner != null)
        {
            message = "E / Click  CATCH!";
        }
        else if (focused != null)
        {
            message = focused.GetPrompt(player, inventory);
            progress = focused.GetHoldProgress();
            if (focused is ISecondaryInteractable secondary)
            {
                string extra = secondary.GetSecondaryPrompt(player, inventory);
                if (!string.IsNullOrEmpty(extra))
                    message = string.IsNullOrEmpty(message) ? extra : message + "\n" + extra;
                if (holdingSecondary == secondary)
                    progress = secondary.GetSecondaryProgress();
            }
        }

        if (string.IsNullOrEmpty(message))
            return;

        float boxHeight = message.Contains("\n") ? 52f : 36f;
        Rect box = new(Screen.width * 0.5f - 210f, Screen.height * 0.72f, 420f, boxHeight);
        GUI.Box(box, message);
        if (progress > 0f && focused != null && (focused.HoldSeconds > 0f || holdingSecondary != null))
        {
            Rect bar = new(box.x + 8f, box.yMax + 4f, box.width - 16f, 10f);
            GUI.Box(bar, GUIContent.none);
            Color previous = GUI.color;
            GUI.color = new Color(0.35f, 0.85f, 0.45f);
            GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * progress, bar.height - 2f), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }

    private void DrawMatchHud()
    {
        // Where am I: room name and floor position, top-left (handy for reporting spots).
        Vector3 here = transform.position;
        GUI.Box(new Rect(12f, 12f, 260f, 26f), $"{RoomVolume.NameAt(here)}   ({here.x:0}, {here.z:0})");

        RouteManager routes = RouteManager.Instance;
        const float width = 380f;
        float x = Screen.width - width - 16f;

        // Head start: Granny waits at her spawn while the Runner gets away.
        if (RouteManager.GrannyHeld)
        {
            int seconds = (int)System.Math.Ceiling(RouteManager.GrannyReleaseLeft);
            string text = player.IsGranny ? $"Released in {seconds} s" : $"Granny is released in {seconds} s";
            GUIStyle style = new(GUI.skin.box) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = player.IsGranny ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.9f, 0.5f);
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, 70f, 440f, 52f), text, style);
        }

        if (player.IsGranny)
        {
            GUI.Box(new Rect(x, 12f, width, 158f), "GRANNY");
            GUI.Label(new Rect(x + 14f, 38f, width - 28f, 22f), "Catch the Runner before they escape. Listen for noises.");
            GUI.Label(new Rect(x + 14f, 62f, width - 28f, 22f), "Move: WASD   Catch: E / Click   Esc: menu");
            GUI.Label(new Rect(x + 14f, 84f, width - 28f, 22f), "Jammed door? Hold E to break it down.");
            GUI.Label(new Rect(x + 14f, 106f, width - 28f, 22f), "Running fans hide quiet noises: switch them off.");
            GUI.Label(new Rect(x + 14f, 128f, width - 28f, 22f), "You can't sprint or crouch: cut the Runner off.");
            return;
        }

        if (routes == null)
        {
            // Original vertical slice HUD.
            bool hasKey = inventory != null && inventory.HasTestKey.Value;
            GUI.Box(new Rect(x, 12f, width, 105f), "RUNNER");
            GUI.Label(new Rect(x + 14f, 38f, width - 28f, 24f), hasKey ? "Test key: Collected" : "Test key: Find it in the room");
            GUI.Label(new Rect(x + 14f, 62f, width - 28f, 24f), hasKey ? "Next: Open the exit, then escape" : "Goal: Find key → open exit → escape");
            GUI.Label(new Rect(x + 14f, 83f, width - 28f, 18f), "Move: Arrow keys    Look: Mouse    C: Crouch");
            return;
        }

        RunnerHud.Draw(routes, inventory, player);

        // The item finder is a hint: it unlocks only after a long stretch with no progress.
        if (RunnerHud.HintUnlocked && Keyboard.current != null && Keyboard.current.tabKey.isPressed)
            DrawItemFinder();
    }

    /// <summary>Runner aid: which rooms still have items lying around.</summary>
    private static void DrawItemFinder()
    {
        SortedDictionary<string, List<string>> byRoom = new();
        foreach (NetworkPickup pickup in NetworkPickup.Active)
        {
            if (pickup == null || !pickup.IsSpawned)
                continue;

            string room = RoomVolume.NameAt(pickup.transform.position);
            if (!byRoom.TryGetValue(room, out List<string> names))
                byRoom[room] = names = new List<string>();
            string name = ItemIds.DisplayName(pickup.ItemId);
            names.Add(pickup.Count > 1 ? $"{name} x{pickup.Count}" : name);
        }

        string hiddenRooms = EnvInteractionManager.Instance != null ? EnvInteractionManager.Instance.HiddenRooms.Value.ToString() : "";
        if (!string.IsNullOrEmpty(hiddenRooms))
            byRoom["~ In desks"] = new List<string> { hiddenRooms };

        const float width = 380f;
        float height = 34f + Mathf.Max(1, byRoom.Count) * 22f;
        Rect panel = new(16f, Screen.height * 0.5f - height * 0.5f, width, height);
        GUI.Box(panel, "ITEM FINDER — items still lying around");
        float y = panel.y + 26f;
        if (byRoom.Count == 0)
            GUI.Label(new Rect(panel.x + 12f, y, width - 24f, 22f), "Nothing left to pick up.");
        foreach (KeyValuePair<string, List<string>> room in byRoom)
        {
            GUI.Label(new Rect(panel.x + 12f, y, width - 24f, 22f), $"{room.Key}: {string.Join(", ", room.Value)}");
            y += 22f;
        }
    }

    private string DescribeInventory()
    {
        if (inventory == null || inventory.Items == null || inventory.Items.Count == 0)
            return "nothing";

        List<string> parts = new();
        foreach (InventoryEntry entry in inventory.Items)
        {
            string name = ItemIds.DisplayName(entry.Id.ToString());
            parts.Add(entry.Count > 1 ? $"{name} x{entry.Count}" : name);
        }

        return string.Join(", ", parts);
    }
}
