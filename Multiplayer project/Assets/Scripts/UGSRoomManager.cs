using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using TMPro;

public class UGSRoomManager : MonoBehaviour
{
    [Header("Netcode")]
    public NetworkManager netManager;
    public UnityTransport transport;

    [Header("UI (optional)")]
    public TMP_Text statusText;
    public TMP_Text roomsText;
    public TMP_InputField roomNameInput;

    [Header("Lobby Settings")]
    public int maxPlayers = 2;
    public float heartbeatSeconds = 15f;
    public float lobbyPollSeconds = 2f;

    // Lobby data keys
    private const string KEY_RELAY_JOIN = "relayJoinCode";

    // current lobby info
    private Lobby _hostLobby;
    private float _heartbeatTimer;
    private float _pollTimer;

    // ✅ Fix for "already signing in": shared init task
    private Task _initTask;

    private void Awake()
    {
        if (netManager == null) netManager = FindFirstObjectByType<NetworkManager>();
        if (transport == null && netManager != null) transport = netManager.GetComponent<UnityTransport>();

        if (roomNameInput != null && string.IsNullOrWhiteSpace(roomNameInput.text))
            roomNameInput.text = "Catan_Room";
    }

    private async void Start()
    {
        try
        {
            await EnsureUGSReady();
            SetStatus("UGS Ready ");
        }
        catch
        {
            // EnsureUGSReady already logs
        }
    }

    private void Update()
    {
        // Host heartbeat (keeps lobby alive)
        if (_hostLobby != null)
        {
            _heartbeatTimer -= Time.deltaTime;
            if (_heartbeatTimer <= 0f)
            {
                _heartbeatTimer = heartbeatSeconds;
                _ = SendHeartbeat();
            }
        }

        // Optional lobby list polling (for UI)
        _pollTimer -= Time.deltaTime;
        if (_pollTimer <= 0f)
        {
            _pollTimer = lobbyPollSeconds;
            _ = RefreshLobbyList();
        }
    }

    // ==========================
    // ✅ UGS INIT (RACE-PROOF)
    // ==========================
    public Task EnsureUGSReady()
    {
        _initTask ??= EnsureUGSReadyImpl();
        return _initTask;
    }

    private async Task EnsureUGSReadyImpl()
    {
        try
        {
            SetStatus("UGS: Initializing...");
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                SetStatus("UGS: Signing in...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            SetStatus("UGS: Signed in ");
            Debug.Log($"UGS Signed In. PlayerId={AuthenticationService.Instance.PlayerId}");
        }
        catch (Exception e)
        {
            // allow retry later
            _initTask = null;
            SetStatus("UGS init failed ");
            Debug.LogError("UGS init failed: " + e);
            throw;
        }
    }

    // ==========================
    // HOST / JOIN
    // ==========================

    public async void Host()
    {
        try
        {
            await EnsureUGSReady();

            string roomName = roomNameInput != null ? roomNameInput.text.Trim() : "Catan_Room";
            if (string.IsNullOrWhiteSpace(roomName)) roomName = "Catan_Room";

            SetStatus("Creating Relay allocation...");
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

            ConfigureTransportAsHost(alloc);

            SetStatus("Creating Lobby...");
            var options = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_RELAY_JOIN, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            _hostLobby = await LobbyService.Instance.CreateLobbyAsync(roomName, maxPlayers, options);
            _heartbeatTimer = heartbeatSeconds;

            SetStatus($"HOSTING ✅ Room: {roomName}  Code: {joinCode}");

            if (!netManager.IsListening)
                netManager.StartHost();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            SetStatus("Host failed ❌ (see console)");
        }
    }

    public async void Join()
    {
        try
        {
            await EnsureUGSReady();

            string roomName = roomNameInput != null ? roomNameInput.text.Trim() : "";
            if (string.IsNullOrWhiteSpace(roomName))
            {
                SetStatus("Enter a room name first.");
                return;
            }

            SetStatus("Searching lobbies...");
            Lobby lobby = await FindLobbyByName(roomName);
            if (lobby == null)
            {
                SetStatus("No lobby found with that name.");
                return;
            }

            if (!lobby.Data.ContainsKey(KEY_RELAY_JOIN))
            {
                SetStatus("Lobby missing relay join code.");
                return;
            }

            string joinCode = lobby.Data[KEY_RELAY_JOIN].Value;

            SetStatus("Joining Relay...");
            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

            ConfigureTransportAsClient(joinAlloc);

            SetStatus($"JOINED ✅ Room: {lobby.Name}");

            if (!netManager.IsListening)
                netManager.StartClient();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            SetStatus("Join failed  (see console)");
        }
    }

    public async void Shutdown()
    {
        try
        {
            if (netManager != null && netManager.IsListening)
                netManager.Shutdown();

            if (_hostLobby != null)
            {
                SetStatus("Deleting lobby...");
                await LobbyService.Instance.DeleteLobbyAsync(_hostLobby.Id);
                _hostLobby = null;
            }

            SetStatus("Shutdown ");
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            SetStatus("Shutdown error (see console)");
        }
    }

    // ==========================
    // TRANSPORT SETUP
    // ==========================

    private void ConfigureTransportAsHost(Allocation alloc)
    {
        if (transport == null) throw new Exception("UnityTransport missing");
        var rsd = new RelayServerData(alloc, "dtls");
        transport.SetRelayServerData(rsd);
    }

    private void ConfigureTransportAsClient(JoinAllocation joinAlloc)
    {
        if (transport == null) throw new Exception("UnityTransport missing");
        var rsd = new RelayServerData(joinAlloc, "dtls");
        transport.SetRelayServerData(rsd);
    }

    // ==========================
    // LOBBY HELPERS
    // ==========================

    private async Task<Lobby> FindLobbyByName(string name)
    {
        var query = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
        {
            Count = 25
        });

        foreach (var l in query.Results)
        {
            if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                return l;
        }

        return null;
    }

    private async Task SendHeartbeat()
    {
        try
        {
            if (_hostLobby != null)
                await LobbyService.Instance.SendHeartbeatPingAsync(_hostLobby.Id);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Heartbeat failed: " + e.Message);
        }
    }

    private async Task RefreshLobbyList()
    {
        try
        {
            if (roomsText == null) return;
            if (!UnityServices.State.Equals(ServicesInitializationState.Initialized)) return;

            var query = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 25 });

            if (query?.Results == null || query.Results.Count == 0)
            {
                roomsText.text = "(no rooms found)";
                return;
            }

            string s = "";
            foreach (var l in query.Results)
            {
                s += $"{l.Name} ({l.Players.Count}/{l.MaxPlayers})\n";
            }
            roomsText.text = s;
        }
        catch
        {
            // keep UI stable
        }
    }

    // ==========================
    // UI HELPER
    // ==========================
    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }
}