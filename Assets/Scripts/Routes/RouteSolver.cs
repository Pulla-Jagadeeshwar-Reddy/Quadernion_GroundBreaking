using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Plays a round layout on paper: the Runner picks up every reachable item, completes every
/// step whose requirements are met, and repeats until nothing changes. Used at runtime to
/// reject unwinnable layouts and in the editor validator.
/// </summary>
public static class RouteSolver
{
    public static bool AllRoutesCompletable(RouteManager.RoutePlan plan, out string report)
    {
        HashSet<RouteInteractable> done = Simulate(plan, out Dictionary<string, int> _);
        StringBuilder failures = new();

        foreach (NetworkEscapePoint exit in plan.Exits)
        {
            if (exit == null || exit.RequiredCompleted == null)
                continue;

            List<RouteInteractable> missing = exit.RequiredCompleted.Where(step => step != null && !done.Contains(step)).ToList();
            if (missing.Count > 0)
                failures.AppendLine($"{exit.RouteName}: cannot complete {string.Join(", ", missing.Select(step => step.ObjectId))}");
        }

        report = failures.ToString();
        return failures.Length == 0;
    }

    public static HashSet<RouteInteractable> Simulate(RouteManager.RoutePlan plan, out Dictionary<string, int> inventory)
    {
        inventory = new Dictionary<string, int>();
        HashSet<RouteInteractable> done = new();
        if (plan == null)
            return done;

        List<(string itemId, string room)> remaining = plan.Items.Select(item => (item.itemId, item.room)).ToList();
        bool changed = true;
        int guard = 0;

        while (changed && guard++ < 200)
        {
            changed = false;

            for (int index = remaining.Count - 1; index >= 0; index--)
            {
                (string itemId, string room) item = remaining[index];
                if (plan.LockedRooms.TryGetValue(item.room ?? string.Empty, out string keyItem) && Count(inventory, keyItem) <= 0)
                    continue;

                Add(inventory, item.itemId, 1);
                remaining.RemoveAt(index);
                changed = true;
            }

            foreach (RouteInteractable step in plan.Interactables)
            {
                if (step == null || done.Contains(step) || !CanComplete(step, plan, done, inventory))
                    continue;

                if (step.ConsumedItems != null)
                {
                    foreach (ItemAmount item in step.ConsumedItems)
                        Add(inventory, item.itemId, -item.SafeCount);
                }

                if (step.GrantedItems != null)
                {
                    foreach (ItemAmount item in step.GrantedItems)
                        Add(inventory, item.itemId, item.SafeCount);
                }

                if (step == plan.BonusPc && !string.IsNullOrEmpty(plan.BonusItem))
                    Add(inventory, plan.BonusItem, 1);

                done.Add(step);
                changed = true;
            }
        }

        return done;
    }

    private static bool CanComplete(RouteInteractable step, RouteManager.RoutePlan plan, HashSet<RouteInteractable> done,
        Dictionary<string, int> inventory)
    {
        if (step.Prerequisites != null)
        {
            foreach (RouteInteractable prerequisite in step.Prerequisites)
            {
                if (prerequisite != null && !done.Contains(prerequisite))
                    return false;
            }
        }

        if (step.RequiredItems != null)
        {
            foreach (ItemAmount item in step.RequiredItems)
            {
                if (!string.IsNullOrEmpty(item.itemId) && Count(inventory, item.itemId) < item.SafeCount)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Item ids that are spawned or granted but never required by any step.</summary>
    public static List<string> FindUnusedItems(IEnumerable<string> producedItems, IEnumerable<RouteInteractable> steps)
    {
        HashSet<string> needed = new();
        foreach (RouteInteractable step in steps)
        {
            if (step?.RequiredItems == null)
                continue;

            foreach (ItemAmount item in step.RequiredItems)
                needed.Add(item.itemId);
        }

        return producedItems.Where(id => !string.IsNullOrEmpty(id) && !needed.Contains(id)).Distinct().ToList();
    }

    private static int Count(Dictionary<string, int> inventory, string itemId) =>
        itemId != null && inventory.TryGetValue(itemId, out int count) ? count : 0;

    private static void Add(Dictionary<string, int> inventory, string itemId, int amount)
    {
        if (string.IsNullOrEmpty(itemId))
            return;

        inventory[itemId] = Count(inventory, itemId) + amount;
    }
}
