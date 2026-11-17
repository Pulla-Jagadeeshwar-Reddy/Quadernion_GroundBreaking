using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayConnectionUI : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;

    private string joinCode = "";
    private string status = "Host a lobby or enter a host's join code.";
    private bool busy;
    private string lastError;

    private static RelayConnectionUI active;

    private void Awake()
    {
        if (networkManager == null)
            networkManager = NetworkManager.Singleton;
    }

    private void OnEnable()
    {
        // A joining client re-loads the scene while syncing with the host, which brings a second
        // copy of this panel (on the scene's idle NetworkManager copy). Only one panel may draw:
        // the one whose NetworkManager is the live singleton.
        if (active != null && active != this && active.isActiveAndEnabled)
        {
            if (active.networkManager == NetworkManager.Singleton || networkManager != NetworkManager.Singleton)
            {
                enabled = false;
                return;
            }

            active.enabled = false;
        }

        active = this;
    }

    private void OnDisable()
    {
        if (active == this)
            active = null;
    }

    private void OnGUI()
    {
        if (active != this)
            return;

        if (networkManager == null || (NetworkManager.Singleton != null && networkManager != NetworkManager.Singleton))
            networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            DrawLobby();
            return;
        }

        GUI.Box(new Rect(10f, 10f, 380f, string.IsNullOrEmpty(lastError) ? 285f : 330f), "First Floor — Playtest / Online Lobby");
        GUI.Label(new Rect(25f, 42f, 340f, 38f), status);
        GUI.enabled = !busy;

        GUI.Label(new Rect(25f, 67f, 340f, 22f), "Local solo test (no Relay / second copy needed):");
        if (GUI.Button(new Rect(25f, 88f, 150f, 32f), "Test as Granny"))
            StartLocalTest(true);
        if (GUI.Button(new Rect(190f, 88f, 175f, 32f), "Test as Player"))
            StartLocalTest(false);

        GUI.Label(new Rect(25f, 130f, 340f, 22f), "Online 1v1 (Relay):");
        if (GUI.Button(new Rect(25f, 152f, 150f, 34f), "Host as Granny"))
            _ = StartHostAsync();

        GUI.Label(new Rect(25f, 198f, 72f, 24f), "Join code:");
        joinCode = GUI.TextField(new Rect(100f, 195f, 130f, 28f), joinCode, 12).Trim().ToUpperInvariant();
        GUI.enabled = !busy && !string.IsNullOrWhiteSpace(joinCode);
        if (GUI.Button(new Rect(240f, 194f, 125f, 30f), "Join as Runner"))
            _ = JoinAsync();
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(lastError))
            GUI.Label(new Rect(25f, 232f, 340f, 45f), lastError);
    }

    private void DrawLobby()
    {
        NetworkPlayerController[] players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
        Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        NetworkPlayerController localPlayer = null;
        int readyCount = 0;
        int spawnedCount = 0;

        foreach (NetworkPlayerController player in players)
        {
            if (player == null || !player.IsSpawned)
                continue;

            spawnedCount++;
            if (player.IsReady.Value)
                readyCount++;
            if (player.IsOwner)
                localPlayer = player;
        }

        bool matchStarted = localPlayer != null && localPlayer.MatchStarted.Value;

        // During a match the panel stays out of the way (it covered the room label); press Esc
        // (cursor unlocked) to bring up a small one at the top centre with the Disconnect button.
        if (matchStarted)
        {
            if (Cursor.lockState == CursorLockMode.Locked || PauseMenu.IsOpen)
                return;

            Rect panel = new(Screen.width * 0.5f - 160f, 10f, 320f, 64f);
            GUI.Box(panel, LocalSinglePlayerMode.IsActive
                ? $"Local solo test: {(localPlayer.IsGranny ? "Granny" : "Player")}"
                : (localPlayer.IsGranny ? "Online match: you are Granny" : "Online match: you are the Runner") +
                  (string.IsNullOrEmpty(joinCode) ? "" : $"  ({joinCode})"));
            if (GUI.Button(new Rect(panel.x + 92f, panel.y + 30f, 136f, 26f), "Disconnect"))
                Disconnect();
            return;
        }

        float height = matchStarted ? 150f : 285f;
        GUI.Box(new Rect(10f, 10f, 390f, height), matchStarted ? "Online Match" : "Pre-game Lobby — 1 Granny vs 1 Runner");

        if (!string.IsNullOrEmpty(joinCode))
            GUI.Label(new Rect(25f, 40f, 350f, 24f), "Room code: " + joinCode);

        if (matchStarted)
        {
            GUI.Label(new Rect(25f, 68f, 350f, 24f), LocalSinglePlayerMode.IsActive
                ? $"Local solo test: {(localPlayer.IsGranny ? "Granny" : "Player")}."
                : localPlayer.IsGranny ? "You are Granny." : "You are the Runner.");
            if (GUI.Button(new Rect(25f, 105f, 135f, 25f), "Disconnect"))
                Disconnect();
            return;
        }

        GUI.Label(new Rect(25f, 68f, 350f, 24f), "Players connected: " + spawnedCount + " / 2");
        DrawPlayerSlot(players, true, 25f, 98f);
        DrawPlayerSlot(players, false, 25f, 130f);

        if (localPlayer == null)
        {
            GUI.Label(new Rect(25f, 166f, 350f, 24f), status);
        }
        else
        {
            string readyLabel = localPlayer.IsReady.Value ? "Not Ready" : "Ready";
            if (GUI.Button(new Rect(25f, 166f, 135f, 30f), readyLabel))
                localPlayer.SetReadyRpc(!localPlayer.IsReady.Value);
        }

        GUI.Label(new Rect(25f, 205f, 350f, 24f), "Ready: " + readyCount + " / 2");
        GUI.enabled = networkManager.IsServer && spawnedCount == 2 && readyCount == 2;
        if (GUI.Button(new Rect(25f, 235f, 160f, 32f), "Start Match"))
            localPlayer.RequestStartMatchRpc();
        GUI.enabled = true;

        if (GUI.Button(new Rect(215f, 235f, 135f, 32f), "Disconnect"))
            Disconnect();
    }

    private static void DrawPlayerSlot(NetworkPlayerController[] players, bool grannySlot, float x, float y)
    {
        string role = grannySlot ? "Granny (Host)" : "Runner";
        string slotStatus = "Waiting for player...";
        foreach (NetworkPlayerController player in players)
        {
            if (player == null || !player.IsSpawned || player.IsGranny != grannySlot)
                continue;

            slotStatus = player.IsReady.Value ? "Ready" : "Not ready";
            break;
        }

        GUI.Label(new Rect(x, y, 340f, 26f), role + "  —  " + slotStatus);
    }

    private async Task StartHostAsync()
    {
        busy = true;
        lastError = null;
        status = "Creating a Relay lobby...";
        try
        {
            await EnsureServicesAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (!networkManager.StartHost())
                throw new InvalidOperationException("NetworkManager could not start the host.");

            status = "Lobby created. Share the room code with the Runner.";
        }
        catch (Exception exception)
        {
            status = "Could not create the lobby.";
            lastError = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task JoinAsync()
    {
        busy = true;
        lastError = null;
        status = "Joining the Relay lobby...";
        try
        {
            await EnsureServicesAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            if (!networkManager.StartClient())
                throw new InvalidOperationException("NetworkManager could not start the client.");

            status = "Connected. Waiting for your Runner player to appear...";
        }
        catch (Exception exception)
        {
            status = "Could not join the lobby.";
            lastError = exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Leave the match from anywhere (pause menu).</summary>
    public static void DisconnectActive()
    {
        if (active != null)
            active.Disconnect();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    private void Disconnect()
    {
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        LocalSinglePlayerMode.Stop();

        status = "Disconnected. Host or join a lobby to play.";
        joinCode = "";
    }

    private void StartLocalTest(bool asGranny)
    {
        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (LocalSinglePlayerMode.Start(networkManager, asGranny))
        {
            status = asGranny ? "Starting a local Granny test..." : "Starting a local Player test...";
            lastError = null;
        }
        else
        {
            status = "Could not start local test.";
            lastError = LocalSinglePlayerMode.LastStartError ?? "Check the Unity Console for the transport startup error.";
        }
    }

    private static async Task EnsureServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
