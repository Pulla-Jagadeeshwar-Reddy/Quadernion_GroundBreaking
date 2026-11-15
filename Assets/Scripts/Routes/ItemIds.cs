using System;
using UnityEngine;

/// <summary>Stable item ids shared by pickups, route props, the route manager and the HUD.</summary>
public static class ItemIds
{
    public const string TestKey = "TestKey";

    public const string Hammer = "T_Hammer";
    public const string Screwdriver = "T_Screwdriver";
    public const string Crowbar = "T_Crowbar";

    public const string R1DoorKey = "R1_DoorKey";
    public const string R1Passcode = "R1_Passcode";

    public const string R2Fuse = "R2_Fuse";

    public const string R3Plank = "R3_Plank";
    public const string R3JanitorKey = "R3_JanitorKey";
    public const string R3Nails = "R3_Nails";

    public const string R5IceBlock = "R5_IceBlock";
    public const string R5BellKey = "R5_BellKey";

    public static string DisplayName(string id)
    {
        switch (id)
        {
            case TestKey: return "Test Key";
            case Hammer: return "Hammer";
            case Screwdriver: return "Screwdriver";
            case Crowbar: return "Crowbar";
            case R1DoorKey: return "Back Door Key";
            case R1Passcode: return "Door Passcode";
            case R2Fuse: return "Fuse";
            case R3Plank: return "Wooden Plank";
            case R3JanitorKey: return "Janitor Key";
            case R3Nails: return "Nails";
            case R5IceBlock: return "Frozen Ice Block";
            case R5BellKey: return "Bell Key";
            default: return string.IsNullOrEmpty(id) ? string.Empty : id.Replace('_', ' ');
        }
    }
}

/// <summary>An item id with a count, used in inspector lists.</summary>
[Serializable]
public struct ItemAmount
{
    public string itemId;
    [Min(1)] public int count;

    public ItemAmount(string itemId, int count)
    {
        this.itemId = itemId;
        this.count = Mathf.Max(1, count);
    }

    public int SafeCount => Mathf.Max(1, count);
}
