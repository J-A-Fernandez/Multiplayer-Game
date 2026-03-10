using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs (assign in Inspector)")]
    public BoardGenerator board;
    public BuildController build;

    [Header("Deterministic map seed (0 = random on host)")]
    public NetworkVariable<int> mapSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Cached lookup by id (re-built after board generation)
    private Dictionary<int, Intersection> _nodeById = new Dictionary<int, Intersection>();
    private Dictionary<int, RoadEdge> _edgeById = new Dictionary<int, RoadEdge>();

    private bool _callbacksHooked = false;

    // -----------------------------
    // Unity lifecycle
    // -----------------------------
    private void Awake()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        HookNetworkCallbacks();

        // Whenever seed changes, regenerate board locally, then rebuild id maps.
        mapSeed.OnValueChanged += OnSeedChanged;

        if (IsServer)
        {
            // Make sure we have a valid seed (server controls it)
            if (mapSeed.Value == 0)
            {
                mapSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
            }
        }

        // Apply current seed immediately on spawn
        ApplySeedAndRebuild(mapSeed.Value);

        // Server sends an initial snapshot after everything is ready
        if (IsServer)
            StartCoroutine(BroadcastSnapshotNextFrame());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopAllCoroutines();
        UnhookNetworkCallbacks();

        mapSeed.OnValueChanged -= OnSeedChanged;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        UnhookNetworkCallbacks();
    }

    // -----------------------------
    // Networking callbacks
    // -----------------------------
    private void HookNetworkCallbacks()
    {
        if (_callbacksHooked) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        _callbacksHooked = true;
    }

    private void UnhookNetworkCallbacks()
    {
        if (!_callbacksHooked) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) { _callbacksHooked = false; return; }

        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;

        _callbacksHooked = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        // Update player count when someone joins (max 4)
        EnsurePlayerCountFromConnections();

        // Push seed snapshot + game snapshot
        StartCoroutine(BroadcastSnapshotNextFrame());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        // Optional: adjust player count down, or keep fixed
        EnsurePlayerCountFromConnections();

        StartCoroutine(BroadcastSnapshotNextFrame());
    }

    // -----------------------------
    // Seed / board generation sync
    // -----------------------------
    private void OnSeedChanged(int oldSeed, int newSeed)
    {
        ApplySeedAndRebuild(newSeed);

        // If server changes seed, re-snapshot after board rebuild
        if (IsServer)
            StartCoroutine(BroadcastSnapshotNextFrame());
    }

    private void ApplySeedAndRebuild(int seed)
    {
        if (board == null) return;

        // You MUST have this method in your BoardGenerator:
        // public void GenerateFromSeed(int s) { seed=s; useSeed=true; Generate(); }
        board.GenerateFromSeed(seed);

        RebuildIdMaps();
    }

    private void RebuildIdMaps()
    {
        _nodeById.Clear();
        _edgeById.Clear();

        if (board == null) return;

        // Nodes
        if (board.Nodes != null)
        {
            foreach (var n in board.Nodes)
            {
                if (n == null) continue;
                _nodeById[n.id] = n;
            }
        }

        // Edges
        if (board.Edges != null)
        {
            foreach (var e in board.Edges)
            {
                if (e == null) continue;
                _edgeById[e.id] = e;
            }
        }
    }

    // -----------------------------
    // Player mapping
    // Host = 0, first client = 1, etc.
    // -----------------------------
    private int GetPlayerIdForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return 0;

        // Build deterministic list: host first, then clients in ascending id
        var ids = nm.ConnectedClientsIds.OrderBy(x => x).ToList();
        // Ensure host is index 0
        if (ids.Remove(nm.LocalClientId))
        {
            ids.Insert(0, nm.LocalClientId);
        }

        int idx = ids.IndexOf(clientId);
        if (idx < 0) idx = 0;

        // Clamp to players array
        if (build != null && build.players != null)
            idx = Mathf.Clamp(idx, 0, build.players.Length - 1);

        return idx;
    }

    private void EnsurePlayerCountFromConnections()
    {
        if (build == null) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        int want = Mathf.Clamp(nm.ConnectedClientsIds.Count, 1, 4);
        build.EnsurePlayerCount(want);
    }

    // -----------------------------
    // Safe RPC sending guard (fixes your shutdown error)
    // -----------------------------
    private bool CanSendRpc()
    {
        if (!IsSpawned) return false;
        if (!IsServer) return false;

        var nm = NetworkManager.Singleton;
        if (nm == null) return false;
        if (!nm.IsListening) return false;
        if (nm.ShutdownInProgress) return false;

        return true;
    }

    private IEnumerator BroadcastSnapshotNextFrame()
    {
        yield return null;
        if (!CanSendRpc()) yield break;
        BroadcastSnapshot();
    }

    // -----------------------------
    // Server authoritative requests
    // -----------------------------
    [ServerRpc(RequireOwnership = false)]
    public void RequestSetModeServerRpc(int modeInt, ServerRpcParams rpcParams = default)
    {
        if (build == null) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);

        // Only current player can change mode (optional but recommended)
        if (pid != build.currentPlayerId) return;

        build.mode = (BuildController.BuildMode)modeInt;

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRollServerRpc(ServerRpcParams rpcParams = default)
    {
        if (build == null) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.RollDiceAndDistribute();

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        if (build == null) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.EndTurn();

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (build == null) return;
        if (!_nodeById.TryGetValue(nodeId, out var node)) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.TryPlaceSettlement(node);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (build == null) return;
        if (!_nodeById.TryGetValue(nodeId, out var node)) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.TryUpgradeCity(node);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int edgeId, ServerRpcParams rpcParams = default)
    {
        if (build == null) return;
        if (!_edgeById.TryGetValue(edgeId, out var edge)) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.TryPlaceRoad(edge);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int tileIndex, ServerRpcParams rpcParams = default)
    {
        if (build == null || build.board == null) return;
        if (tileIndex < 0 || tileIndex >= build.board.Tiles.Count) return;

        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        var tile = build.board.Tiles[tileIndex];
        build.TryMoveRobber(tile);

        BroadcastSnapshot();
    }

    // -----------------------------
    // Snapshot broadcasting
    // -----------------------------
    private void BroadcastSnapshot()
    {
        if (!CanSendRpc()) return;
        if (build == null || build.board == null) return;

        // Make sure arrays match current board size
        int nodeCount = build.board.Nodes.Count;
        int edgeCount = build.board.Edges.Count;
        int tileCount = build.board.Tiles.Count;

        var nodeOwner = new int[nodeCount];
        var nodeType = new int[nodeCount]; // 0 none, 1 settlement, 2 city

        // Node array index is NOT id; we send by index order in board.Nodes
        for (int i = 0; i < nodeCount; i++)
        {
            var n = build.board.Nodes[i];
            if (n == null || n.building == null)
            {
                nodeOwner[i] = -1;
                nodeType[i] = 0;
                continue;
            }

            nodeOwner[i] = n.building.ownerId;
            nodeType[i] = (n.building.type == BuildingType.City) ? 2 : 1;
        }

        var edgeOwner = new int[edgeCount];
        for (int i = 0; i < edgeCount; i++)
        {
            var e = build.board.Edges[i];
            edgeOwner[i] = (e == null) ? -1 : e.ownerId;
        }

        int robberIndex = -1;
        for (int i = 0; i < tileCount; i++)
        {
            var t = build.board.Tiles[i];
            if (t != null && t.hasRobber) { robberIndex = i; break; }
        }

        SnapshotClientRpc(
            mapSeed.Value,
            build.currentPlayerId,
            (int)build.phase,
            (int)build.mode,
            build.HasRolledThisTurn,
            build.AwaitingRobberMove,
            robberIndex,
            nodeOwner,
            nodeType,
            edgeOwner
        );
    }

    [ClientRpc]
    private void SnapshotClientRpc(
        int seed,
        int currentPlayerId,
        int phaseInt,
        int modeInt,
        bool hasRolled,
        bool awaitingRobber,
        int robberTileIndex,
        int[] nodeOwner,
        int[] nodeType,
        int[] edgeOwner
    )
    {
        // Client-side safety
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        // 1) Ensure same board
        if (board != null && seed != 0 && seed != mapSeed.Value)
        {
            // mapSeed is NetworkVariable, but clients can still regen safely
            ApplySeedAndRebuild(seed);
        }
        else
        {
            // Ensure maps are cached
            RebuildIdMaps();
        }

        if (build == null || build.board == null) return;

        // 2) Apply turn flags + phase/mode
        build.currentPlayerId = currentPlayerId;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.mode = (BuildController.BuildMode)modeInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);

        // 3) Apply robber
        for (int i = 0; i < build.board.Tiles.Count; i++)
        {
            var t = build.board.Tiles[i];
            if (t == null) continue;
            bool shouldRobber = (i == robberTileIndex);
            if (t.hasRobber != shouldRobber)
            {
                t.hasRobber = shouldRobber;
                t.RefreshVisual();
            }
        }

        // 4) Apply nodes
        int nCount = Mathf.Min(build.board.Nodes.Count, nodeOwner.Length);
        for (int i = 0; i < nCount; i++)
        {
            var n = build.board.Nodes[i];
            if (n == null) continue;

            int owner = nodeOwner[i];
            int type = nodeType[i];

            if (owner < 0 || type == 0)
            {
                n.building = null;
                HideMarker(n);
            }
            else
            {
                var bType = (type == 2) ? BuildingType.City : BuildingType.Settlement;
                if (n.building == null) n.building = new Building(owner, bType);
                n.building.ownerId = owner;
                n.building.type = bType;

                ShowMarker(n, owner, bType);
            }
        }

        // 5) Apply edges
        int eCount = Mathf.Min(build.board.Edges.Count, edgeOwner.Length);
        for (int i = 0; i < eCount; i++)
        {
            var e = build.board.Edges[i];
            if (e == null) continue;

            e.ownerId = edgeOwner[i];
            ColorRoad(e, e.ownerId);
        }
    }

    // -----------------------------
    // Local visuals (so clients update)
    // -----------------------------
    private void ShowMarker(Intersection node, int ownerId, BuildingType type)
    {
        if (build == null) return;

        var markerT = node.transform.Find("Marker");
        if (markerT == null)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(node.transform, false);
            markerT = go.transform;
        }

        markerT.gameObject.SetActive(true);
        markerT.localPosition = Vector3.zero;

        var sr = markerT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = markerT.gameObject.AddComponent<SpriteRenderer>();

        if (sr.sprite == null)
            sr.sprite = build.markerSprite;

        // Colors from BuildController player states
        Color c = Color.white;
        if (build.players != null && ownerId >= 0 && ownerId < build.players.Length)
            c = build.players[ownerId].playerColor;

        sr.color = c;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1000;

        float size = (type == BuildingType.City) ? 0.45f : 0.30f;
        markerT.localScale = Vector3.one * size;
    }

    private void HideMarker(Intersection node)
    {
        var markerT = node.transform.Find("Marker");
        if (markerT != null)
            markerT.gameObject.SetActive(false);
    }

    private void ColorRoad(RoadEdge edge, int ownerId)
    {
        var visualT = edge.transform.Find("Visual");
        if (visualT == null) return;

        var sr = visualT.GetComponent<SpriteRenderer>();
        if (sr == null) return;

        if (ownerId < 0)
        {
            sr.color = Color.white;
            return;
        }

        if (build != null && build.players != null && ownerId < build.players.Length)
            sr.color = build.players[ownerId].playerColor;

        sr.sortingLayerName = "Default";
        sr.sortingOrder = 500;
    }
}