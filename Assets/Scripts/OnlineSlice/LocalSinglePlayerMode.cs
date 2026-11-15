using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>Allows a local editor/desktop playtest to use the networked gameplay with no Relay session.</summary>
public static class LocalSinglePlayerMode
{
    public static bool IsActive { get; private set; }
    public static bool TestAsGranny { get; private set; }
    public static string LastStartError { get; private set; }

    public static bool Start(NetworkManager manager, bool asGranny)
    {
        LastStartError = null;
        if (manager == null)
        {
            LastStartError = "The lobby has no NetworkManager reference.";
            return false;
        }

        if (manager.IsListening)
        {
            LastStartError = "NetworkManager is already running. Disconnect it, then try again.";
            return false;
        }

        if (manager.NetworkConfig.PlayerPrefab == null)
        {
            LastStartError = "NetworkManager has no Player Prefab assigned.";
            return false;
        }

        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            LastStartError = "NetworkManager needs a Unity Transport component.";
            return false;
        }

        TestAsGranny = asGranny;
        IsActive = true;
        // Other editor instances or a previous local build may still own port 7777.
        // Use a fresh loopback port for this one-process test; Relay hosting sets its own data later.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            ushort localPort = (ushort)Random.Range(20000, 60000);
            transport.SetConnectionData("127.0.0.1", localPort, "127.0.0.1");
            if (manager.StartHost())
                return true;

            if (manager.IsListening)
                manager.Shutdown();
        }

        IsActive = false;
        LastStartError = "Could not start a local host on five loopback ports. Check the Unity Console for the transport error.";
        return false;
    }

    public static void Stop()
    {
        IsActive = false;
    }
}
