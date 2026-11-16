using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runner HUD for the first floor:
/// - top right: one card per route with a progress bar and a step checklist (next step highlighted,
///   needed items shown green when held and red when missing);
/// - bottom centre: an inventory bar with a slot per item and its count;
/// - toasts for items gained/used, steps completed and exits opening.
/// H toggles compact cards (bars only).
/// </summary>
public static class RunnerHud
{
    private const float CardWidth = 360f;
    private const float RowHeight = 20f;
    private const float NeedsHeight = 17f;
    private const float SlotSize = 76f;

    private static GUIStyle titleStyle, rowStyle, nextStyle, doneStyle, needsStyle, rightStyle;
    private static GUIStyle codeStyle, slotNameStyle, badgeStyle, toastStyle, bigToastStyle, hintStyle;
    private static bool compact;
    private static int lastToggleFrame = -1;

    private static int cacheFrame = -1;
    private static List<RouteManager.RouteProgress> cache;

    /// <summary>Seconds without picking anything up or finishing a step before the Tab hint unlocks.</summary>
    public const float HintDelay = 90f;

    /// <summary>Last time the Runner gained an item or finished a step (or the round started).</summary>
    public static float LastProgressTime { get; private set; }

    public static bool HintUnlocked => Time.time - LastProgressTime >= HintDelay;

    private static bool hintAnnounced;

    private static readonly Dictionary<string, int> lastCounts = new();
    private static readonly Dictionary<string, int> lastDone = new();
    private static readonly Dictionary<string, bool> lastUnlocked = new();
    private static bool hasBaseline;
    private static int trackedFrame = -1;

    private struct Toast
    {
        public string Text;
        public Color Color;
        public float Time;
        public bool Big;
    }

    private static readonly List<Toast> toasts = new();

    public static void Draw(RouteManager routes, NetworkInventory inventory, NetworkPlayerController player = null)
    {
        EnsureStyles();
        DrawMovementState(player);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame && lastToggleFrame != Time.frameCount)
        {
            lastToggleFrame = Time.frameCount;
            compact = !compact;
        }

        if (cacheFrame != Time.frameCount)
        {
            cacheFrame = Time.frameCount;
            cache = routes.GetRouteProgress(inventory);
        }

        Track(cache, inventory);

        HashSet<string> neededNow = new();
        foreach (RouteManager.RouteProgress route in cache)
        {
            foreach (RouteManager.RouteStepRow row in route.Rows)
            {
                if (!row.IsNext)
                    continue;
                foreach ((string itemId, int _, int _) in row.Needs)
                    neededNow.Add(itemId);
            }
        }

        DrawRouteCards(cache);
        DrawInventory(inventory, neededNow);
        DrawToasts();
        DrawControls();
    }

    // ------------------------------------------------------------------ route cards

    private static void DrawRouteCards(List<RouteManager.RouteProgress> routes)
    {
        float x = Screen.width - CardWidth - 16f;
        float y = 12f;

        foreach (RouteManager.RouteProgress route in routes)
        {
            Color accent = RouteColor(route.Name);
            float height = 40f;
            if (!compact)
            {
                foreach (RouteManager.RouteStepRow row in route.Rows)
                    height += RowHeight + (!row.Complete && row.Needs.Count > 0 ? NeedsHeight : 0f);
                height += RowHeight + 6f;
            }

            Rect card = new(x, y, CardWidth, height);
            Fill(card, new Color(0.05f, 0.06f, 0.08f, route.Unlocked ? 0.85f : 0.72f));
            Fill(new Rect(card.x, card.y, 5f, card.height), accent);
            if (route.Unlocked)
                Frame(card, Color.Lerp(accent, Color.white, Pulse(4f) * 0.5f), 2f);

            // Header: name, x/y or EXIT OPEN, progress bar.
            GUI.Label(new Rect(card.x + 14f, card.y + 4f, 220f, 22f), route.Name.ToUpperInvariant(), titleStyle);
            int done = route.DoneUnits;
            int total = Mathf.Max(1, route.TotalUnits);
            string state = route.Escaped ? "<color=#7CFF8A>ESCAPED</color>"
                : route.Unlocked ? $"<color=#7CFF8A>EXIT OPEN</color>"
                : $"{done}/{total}";
            GUI.Label(new Rect(card.xMax - 130f, card.y + 4f, 118f, 22f), state, rightStyle);

            Rect bar = new(card.x + 14f, card.y + 28f, CardWidth - 28f, 6f);
            Fill(bar, new Color(1f, 1f, 1f, 0.12f));
            float fraction = route.Unlocked ? 1f : done / (float)total;
            Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), route.Unlocked ? new Color(0.45f, 1f, 0.55f) : accent);

            if (!compact)
            {
                float rowY = card.y + 40f;
                foreach (RouteManager.RouteStepRow row in route.Rows)
                    rowY = DrawRow(card.x + 14f, rowY, row, accent);

                // Final row: the exit itself.
                RouteManager.RouteStepRow exitRow = new()
                {
                    Label = route.Unlocked ? "Escape! The exit is open" : "Escape",
                    Done = route.Escaped ? 1 : 0,
                    Total = 1,
                    IsNext = route.Unlocked && !route.Escaped,
                };
                DrawRow(card.x + 14f, rowY, exitRow, route.Unlocked ? new Color(0.45f, 1f, 0.55f) : accent);
            }

            y += height + 8f;
        }
    }

    private static float DrawRow(float x, float y, RouteManager.RouteStepRow row, Color accent)
    {
        Rect marker = new(x, y + 5f, 10f, 10f);
        if (row.Complete)
        {
            Fill(marker, new Color(0.4f, 0.85f, 0.45f));
        }
        else if (row.IsNext)
        {
            Fill(marker, Color.Lerp(accent, Color.white, Pulse(3f) * 0.4f));
        }
        else
        {
            Frame(marker, new Color(1f, 1f, 1f, 0.4f), 1f);
        }

        string count = row.Total > 1 ? $"  {row.Done}/{row.Total}" : "";
        GUIStyle style = row.Complete ? doneStyle : row.IsNext ? nextStyle : rowStyle;
        GUI.Label(new Rect(x + 18f, y, CardWidth - 46f, RowHeight), row.Label + count, style);
        y += RowHeight;

        if (!row.Complete && row.Needs.Count > 0)
        {
            List<string> parts = new();
            foreach ((string itemId, int have, int need) in row.Needs)
            {
                string name = ItemIds.DisplayName(itemId);
                string text = need > 1 ? $"{name} {have}/{need}" : name;
                parts.Add(have >= need ? $"<color=#7CFF8A>{text}</color>" : $"<color=#FF7A6E>{text}</color>");
            }

            GUI.Label(new Rect(x + 18f, y - 2f, CardWidth - 46f, NeedsHeight), "needs  " + string.Join("   ", parts), needsStyle);
            y += NeedsHeight;
        }

        return y;
    }

    // ------------------------------------------------------------------ inventory bar

    private static void DrawInventory(NetworkInventory inventory, HashSet<string> neededNow)
    {
        List<(string id, int count)> items = new();
        if (inventory != null && inventory.Items != null)
        {
            foreach (InventoryEntry entry in inventory.Items)
            {
                if (entry.Count > 0)
                    items.Add((entry.Id.ToString(), entry.Count));
            }
        }

        if (inventory != null && inventory.HasTestKey.Value)
            items.Add((ItemIds.TestKey, 1));

        float gap = 6f;
        int slots = Mathf.Max(items.Count, 1);
        float width = slots * SlotSize + (slots - 1) * gap;
        float x = Screen.width * 0.5f - width * 0.5f;
        float y = Screen.height - SlotSize - 14f;

        GUI.Label(new Rect(x, y - 20f, 200f, 18f), "INVENTORY", hintStyle);
        if (items.Count == 0)
        {
            Rect empty = new(x, y, SlotSize * 3f, SlotSize);
            empty.x = Screen.width * 0.5f - empty.width * 0.5f;
            Fill(empty, new Color(0.05f, 0.06f, 0.08f, 0.6f));
            GUI.Label(empty, "Empty — look for glowing items", slotNameStyle);
            return;
        }

        foreach ((string id, int count) in items)
        {
            Rect slot = new(x, y, SlotSize, SlotSize);
            Fill(slot, new Color(0.05f, 0.06f, 0.08f, 0.75f));
            (string code, Color color) = ItemLook(id);
            if (neededNow.Contains(id))
                Frame(slot, Color.Lerp(color, Color.white, Pulse(3f) * 0.5f), 2f);

            Rect tile = new(slot.x + 10f, slot.y + 8f, SlotSize - 20f, 30f);
            Fill(tile, color);
            GUI.Label(tile, code, codeStyle);
            GUI.Label(new Rect(slot.x + 2f, slot.y + 40f, SlotSize - 4f, 34f), ItemIds.DisplayName(id), slotNameStyle);
            if (count > 1)
            {
                Rect badge = new(slot.xMax - 26f, slot.y + 2f, 24f, 18f);
                Fill(badge, new Color(0f, 0f, 0f, 0.85f));
                GUI.Label(badge, "x" + count, badgeStyle);
            }

            x += SlotSize + gap;
        }
    }

    private static (string code, Color color) ItemLook(string id)
    {
        switch (id)
        {
            case ItemIds.Hammer: return ("HAM", new Color(0.55f, 0.58f, 0.64f));
            case ItemIds.Screwdriver: return ("SCR", new Color(0.35f, 0.55f, 0.85f));
            case ItemIds.Crowbar: return ("BAR", new Color(0.8f, 0.25f, 0.2f));
            case ItemIds.R1DoorKey: return ("KEY", new Color(0.95f, 0.75f, 0.25f));
            case ItemIds.R1Passcode: return ("CODE", new Color(0.3f, 0.8f, 0.55f));
            case ItemIds.R2Fuse: return ("FUSE", new Color(1f, 0.55f, 0.15f));
            case ItemIds.R3Plank: return ("WOOD", new Color(0.7f, 0.47f, 0.26f));
            case ItemIds.R3JanitorKey: return ("JKEY", new Color(0.85f, 0.7f, 0.35f));
            case ItemIds.R3Nails: return ("NAIL", new Color(0.62f, 0.62f, 0.68f));
            case ItemIds.R5IceBlock: return ("ICE", new Color(0.55f, 0.85f, 1f));
            case ItemIds.R5BellKey: return ("BELL", new Color(0.95f, 0.85f, 0.4f));
            default: return ("KEY", new Color(0.95f, 0.75f, 0.25f));
        }
    }

    // ------------------------------------------------------------------ toasts

    private static void Track(List<RouteManager.RouteProgress> routes, NetworkInventory inventory)
    {
        if (trackedFrame == Time.frameCount)
            return;
        trackedFrame = Time.frameCount;

        Dictionary<string, int> counts = new();
        if (inventory != null && inventory.Items != null)
        {
            foreach (InventoryEntry entry in inventory.Items)
                counts[entry.Id.ToString()] = entry.Count;
        }

        bool roundReset = hasBaseline && counts.Count == 0 && lastCounts.Count > 0 &&
                          routes.TrueForAll(r => r.DoneUnits == 0);
        if (!hasBaseline || roundReset)
            MarkProgress();
        if (hasBaseline && !roundReset)
        {
            foreach (KeyValuePair<string, int> pair in counts)
            {
                lastCounts.TryGetValue(pair.Key, out int before);
                if (pair.Value > before)
                {
                    AddToast($"+{pair.Value - before}  {ItemIds.DisplayName(pair.Key)}", new Color(0.55f, 1f, 0.6f));
                    MarkProgress();
                }
            }

            foreach (KeyValuePair<string, int> pair in lastCounts)
            {
                counts.TryGetValue(pair.Key, out int now);
                if (now < pair.Value)
                    AddToast($"-{pair.Value - now}  {ItemIds.DisplayName(pair.Key)} used", new Color(1f, 0.7f, 0.35f));
            }

            foreach (RouteManager.RouteProgress route in routes)
            {
                if (lastDone.TryGetValue(route.Name, out int before) && route.DoneUnits > before)
                {
                    MarkProgress();
                    if (!route.Unlocked)
                        AddToast($"{route.Name}: step done ({route.DoneUnits}/{route.TotalUnits})", RouteColor(route.Name));
                }
                if (lastUnlocked.TryGetValue(route.Name, out bool wasOpen) && !wasOpen && route.Unlocked)
                    AddToast($"{route.Name.ToUpperInvariant()} EXIT OPEN!", new Color(0.45f, 1f, 0.55f), true);
            }
        }

        if (HintUnlocked && !hintAnnounced)
        {
            hintAnnounced = true;
            AddToast("Stuck? Hold Tab for a hint", new Color(1f, 0.9f, 0.5f), true);
        }

        lastCounts.Clear();
        foreach (KeyValuePair<string, int> pair in counts)
            lastCounts[pair.Key] = pair.Value;
        foreach (RouteManager.RouteProgress route in routes)
        {
            lastDone[route.Name] = route.DoneUnits;
            lastUnlocked[route.Name] = route.Unlocked;
        }

        hasBaseline = true;
    }

    private static void MarkProgress()
    {
        LastProgressTime = Time.time;
        hintAnnounced = false;
    }

    public static void AddToast(string text, Color color, bool big = false)
    {
        toasts.Add(new Toast { Text = text, Color = color, Time = Time.time, Big = big });
        if (toasts.Count > 6)
            toasts.RemoveAt(0);
    }

    private static void DrawToasts()
    {
        float y = Screen.height - SlotSize - 60f;
        for (int index = toasts.Count - 1; index >= 0; index--)
        {
            Toast toast = toasts[index];
            float age = Time.time - toast.Time;
            float life = toast.Big ? 4f : 2.5f;
            if (age > life)
            {
                toasts.RemoveAt(index);
                continue;
            }

            float alpha = Mathf.Clamp01((life - age) / 0.6f);
            GUIStyle style = toast.Big ? bigToastStyle : toastStyle;
            float height = toast.Big ? 40f : 24f;
            y -= height;
            Color previous = GUI.color;
            GUI.color = new Color(toast.Color.r, toast.Color.g, toast.Color.b, alpha);
            GUI.Label(new Rect(Screen.width * 0.5f - 300f, y - age * 6f, 600f, height), toast.Text, style);
            GUI.color = previous;
        }
    }

    private static void DrawMovementState(NetworkPlayerController player)
    {
        if (player == null)
            return;

        string text = player.IsSprinting ? "<color=#7CFF8A>SPRINTING</color>"
            : !string.IsNullOrEmpty(player.SprintBlockedReason) ? $"<color=#FF7A6E>{player.SprintBlockedReason}</color>"
            : player.IsCrouched.Value ? "<color=#9FD3FF>CROUCHED</color>"
            : null;
        if (text != null)
            GUI.Label(new Rect(16f, Screen.height - 70f, 400f, 22f), text, toastStyle != null ? hintStyle : GUI.skin.label);
    }

    private static void DrawControls()
    {
        const string sprint = "Shift";
        string other = ControlSettings.RunnerUsesWasd ? "arrows" : "WASD";
        GUI.Label(new Rect(16f, Screen.height - 46f, 520f, 40f),
            $"{ControlSettings.RunnerMoveLabel} move   {sprint} sprint   C crouch   Hold E use\n" +
            $"F jam door   H {(compact ? "expand" : "compact")} routes   F2 use {other}" +
            (HintUnlocked ? "   Tab: hint" : ""), hintStyle);
    }

    // ------------------------------------------------------------------ helpers

    private static Color RouteColor(string route)
    {
        switch (route)
        {
            case "Locksmith": return new Color(0.98f, 0.78f, 0.3f);
            case "Blackout": return new Color(1f, 0.5f, 0.15f);
            case "Ladder": return new Color(0.78f, 0.52f, 0.3f);
            case "Frozen Key": return new Color(0.5f, 0.85f, 1f);
            default: return new Color(0.8f, 0.8f, 0.8f);
        }
    }

    private static float Pulse(float speed) => 0.5f + 0.5f * Mathf.Sin(Time.time * speed);

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Frame(Rect rect, Color color, float thickness)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private static void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        GUIStyle Label(int size, FontStyle font, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            GUIStyle style = new(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = font,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            style.normal.textColor = color;
            return style;
        }

        titleStyle = Label(15, FontStyle.Bold, Color.white);
        rightStyle = Label(14, FontStyle.Bold, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleRight);
        rowStyle = Label(13, FontStyle.Normal, new Color(0.68f, 0.68f, 0.7f));
        nextStyle = Label(13, FontStyle.Bold, Color.white);
        doneStyle = Label(13, FontStyle.Normal, new Color(0.45f, 0.75f, 0.5f));
        needsStyle = Label(11, FontStyle.Normal, new Color(0.75f, 0.75f, 0.75f));
        hintStyle = Label(11, FontStyle.Bold, new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperLeft);
        codeStyle = Label(14, FontStyle.Bold, new Color(0.05f, 0.05f, 0.05f), TextAnchor.MiddleCenter);
        slotNameStyle = Label(11, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
        slotNameStyle.wordWrap = true;
        badgeStyle = Label(12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        toastStyle = Label(17, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        bigToastStyle = Label(28, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
    }
}
