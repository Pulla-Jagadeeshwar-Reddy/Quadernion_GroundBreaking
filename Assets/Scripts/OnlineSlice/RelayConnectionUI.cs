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
    private string status = "Start a host or enter a host's join code.";
    private bool busy;

    private void Awake()
    {
        if (networkManager == null)
            networkManager = NetworkManager.Singleton;
    }

    private void OnGUI()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            GUI.Box(new Rect(10f, 10f, 360f, 92f), "Online 1v1  |  " + status);
            if (GUI.Button(new Rect(25f, 65f, 130f, 28f), "Disconnect"))
            {
                networkManager.Shutdown();
                status = "Disconnected.";
            }
            return;
        }

        GUI.Box(new Rect(10f, 10f, 360f, 170f), "First Floor: Online Test");
        GUI.Label(new Rect(25f, 40f, 320f, 24f), status);

        GUI.enabled = !busy;
        if (GUI.Button(new Rect(25f, 72f, 145f, 32f), "Host as Granny"))
            _ = StartHostAsync();

        GUI.Label(new Rect(25f, 115f, 70f, 24f), "Join code:");
        joinCode = GUI.TextField(new Rect(95f, 112f, 120f, 28f), joinCode, 12);
        if (GUI.Button(new Rect(225f, 112f, 115f, 28f), "Join as Player"))
            _ = JoinAsync();
        GUI.enabled = true;
    }

    private async Task StartHostAsync()
    {
        busy = true;
        try
        {
            await EnsureServicesAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (!networkManager.StartHost())
                throw new InvalidOperationException("NetworkManager could not start the host.");

            status = "You are Granny. Share this code: " + joinCode;
        }
        catch (Exception exception)
        {
            status = "Host failed: " + exception.Message;
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
        try
        {
            await EnsureServicesAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());
            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            if (!networkManager.StartClient())
                throw new InvalidOperationException("NetworkManager could not start the client.");

            status = "Connecting as Player...";
        }
        catch (Exception exception)
        {
            status = "Join failed: " + exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            busy = false;
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
