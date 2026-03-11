using System;
using System.Threading.Tasks;
using UnityEngine;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

using Unity.Networking.Transport.Relay; // ✅ RelayServerData lives here

public class UGSRoomManager : MonoBehaviour
{
    [Header("Refs")]
    public NetworkManager networkManager;
    public UnityTransport transport;

    [Header("UI (optional)")]
    [Tooltip("Read-only join code for host to display")]
    public string lastJoinCode;

    private bool servicesReady = false;

    private void Awake()
    {
        if (networkManager == null) networkManager = FindFirstObjectByType<NetworkManager>();
        if (transport == null) transport = FindFirstObjectByType<UnityTransport>();

        if (networkManager == null) Debug.LogError("UGSRoomManager: NetworkManager not found in scene.");
        if (transport == null) Debug.LogError("UGSRoomManager: UnityTransport not found in scene.");
    }

    private async void Start()
    {
        await EnsureUGSReady();
    }

    // =========================
    // PUBLIC BUTTON HOOKS
    // =========================

    // Hook this to a “Host” button
    public async void HostRelay()
    {
        try
        {
            await EnsureUGSReady();
            if (!servicesReady) return;

            // Create allocation (2 players total: host + 1 client)
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(1);

            // Get join code
            lastJoinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            Debug.Log($"[RELAY] Host Join Code: {lastJoinCode}");

            // Configure transport with relay data
            var relayServerData = new RelayServerData(alloc, "dtls");
            transport.SetRelayServerData(relayServerData);

            // IMPORTANT: StartHost is NOT async. Do NOT await.
            bool ok = networkManager.StartHost();
            Debug.Log($"[NET] StartHost: {ok}");
        }
        catch (Exception e)
        {
            Debug.LogError($"HostRelay failed: {e}");
        }
    }

    // Hook this to a “Join” button + pass code from input field
    public async void JoinRelay(string joinCode)
    {
        try
        {
            await EnsureUGSReady();
            if (!servicesReady) return;

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Debug.LogError("JoinRelay: joinCode is empty.");
                return;
            }

            joinCode = joinCode.Trim().ToUpperInvariant();

            JoinAllocation alloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log($"[RELAY] Joined allocation with code: {joinCode}");

            var relayServerData = new RelayServerData(alloc, "dtls");
            transport.SetRelayServerData(relayServerData);

            // IMPORTANT: StartClient is NOT async. Do NOT await.
            bool ok = networkManager.StartClient();
            Debug.Log($"[NET] StartClient: {ok}");
        }
        catch (Exception e)
        {
            Debug.LogError($"JoinRelay failed: {e}");
        }
    }

    public void Shutdown()
    {
        if (networkManager != null)
            networkManager.Shutdown();
    }

    // =========================
    // UGS INIT
    // =========================

    private async Task EnsureUGSReady()
    {
        if (servicesReady) return;

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            servicesReady = true;
            Debug.Log($"[UGS] Ready. PlayerId={AuthenticationService.Instance.PlayerId}");
        }
        catch (Exception e)
        {
            servicesReady = false;
            Debug.LogError($"UGS init failed: {e}");
        }
    }
}