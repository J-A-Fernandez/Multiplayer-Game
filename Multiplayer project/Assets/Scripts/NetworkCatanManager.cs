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

    [Header("Deterministic map seed (0 = host picks random)")]
    public int mapSeed = 0;

    private readonly NetworkVariable<int> netSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // clientId -> playerIndex (host=0, first client=1, etc.)
    private readonly Dictionary<ulong, int> clientToPlayer = new Dictionary<ulong, int>();

    public int LocalPlayerId { get; private set; } = -1;

    public override void OnNetworkSpawn()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (IsServer)
        {
            // Choose seed once on host
            if (mapSeed == 0) mapSeed = UnityEngine.Random.Range(1, int.MaxValue);
            netSeed.Value = mapSeed;

            // Setup connection callbacks
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Host is player 0
            clientToPlayer[NetworkManager.Singleton.LocalClientId] = 0;

            // Ensure BuildController has right player count for now (will expand as clients join)
            build.EnsurePlayerCount(Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count));

            // Generate board on server
            board.GenerateFromSeed(netSeed.Value);

            // Start game fresh on server
            build.BeginSetup();

            // Broadcast initial snapshot after a short frame
            StartCoroutine(BroadcastSnapshotNextFrame());
        }
        else
        {
            // Client waits for seed, generates same board locally, then requests snapshot.
            StartCoroutine(ClientInitRoutine());
        }

        // local player id for THIS machine
        LocalPlayerId = GetPlayerIdForClient(NetworkManager.Singleton.LocalClientId);
    }

    private IEnumerator ClientInitRoutine()
    {
        // Wait until seed arrives
        while (netSeed.Value == 0) yield return null;

        // Generate identical board on client
        board.GenerateFromSeed(netSeed.Value);

        // Ask server for state
        RequestSnapshotServerRpc();
    }

    private void OnClientConnected(ulong clientId)
    {
        // Assign player slot
        int next = clientToPlayer.Values.Count == 0 ? 0 : (clientToPlayer.Values.Max() + 1);
        clientToPlayer[clientId] = next;

        // Ensure BuildController player array length is big enough
        build.EnsurePlayerCount(Mathf.Max(1, clientToPlayer.Count));

        // Send updated snapshot
        StartCoroutine(BroadcastSnapshotNextFrame());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientToPlayer.ContainsKey(clientId))
            clientToPlayer.Remove(clientId);

        StartCoroutine(BroadcastSnapshotNextFrame());
    }

    private IEnumerator BroadcastSnapshotNextFrame()
    {
        yield return null;
        BroadcastSnapshot();
    }

    private int GetPlayerIdForClient(ulong clientId)
    {
        if (clientToPlayer.TryGetValue(clientId, out var pid)) return pid;
        // If client joins before dictionary arrives, default to 0 (will be corrected after snapshot)
        return 0;
    }

    // ============================
    // RPCs clients call -> server
    // ============================

    [ServerRpc(RequireOwnership = false)]
    public void RequestSnapshotServerRpc(ServerRpcParams rpcParams = default)
    {
        SendSnapshotToClient(rpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestSetModeServerRpc(int modeInt, ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.mode = (BuildController.BuildMode)modeInt;
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRollServerRpc(ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.RollDiceAndDistribute();
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        build.EndTurn();
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        var node = FindNodeById(nodeId);
        if (node == null) return;

        build.TryPlaceSettlement(node);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        var node = FindNodeById(nodeId);
        if (node == null) return;

        build.TryUpgradeCity(node);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int edgeId, ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        var edge = FindEdgeById(edgeId);
        if (edge == null) return;

        build.TryPlaceRoad(edge);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int tileIndex, ServerRpcParams rpcParams = default)
    {
        int pid = GetPlayerIdForClient(rpcParams.Receive.SenderClientId);
        if (pid != build.currentPlayerId) return;

        if (board == null || tileIndex < 0 || tileIndex >= board.Tiles.Count) return;

        build.TryMoveRobber(board.Tiles[tileIndex]);
        BroadcastSnapshot();
    }

    // ============================
    // Snapshot replication
    // ============================

    private void BroadcastSnapshot()
    {
        // Send to everyone
        var nodeOwner = new int[board.Nodes.Count];
        var nodeType = new int[board.Nodes.Count]; // 0 none, 1 settlement, 2 city
        for (int i = 0; i < board.Nodes.Count; i++)
        {
            var n = board.Nodes[i];
            if (n == null || n.building == null) { nodeOwner[i] = -1; nodeType[i] = 0; }
            else
            {
                nodeOwner[i] = n.building.ownerId;
                nodeType[i] = (n.building.type == BuildingType.City) ? 2 : 1;
            }
        }

        var edgeOwner = new int[board.Edges.Count];
        for (int i = 0; i < board.Edges.Count; i++)
            edgeOwner[i] = board.Edges[i] ? board.Edges[i].ownerId : -1;

        int robberIndex = -1;
        for (int i = 0; i < board.Tiles.Count; i++)
        {
            if (board.Tiles[i] != null && board.Tiles[i].hasRobber) { robberIndex = i; break; }
        }

        SnapshotClientRpc(
            netSeed.Value,
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

    private void SendSnapshotToClient(ulong clientId)
    {
        // targeted snapshot = easiest is still broadcast; but we can just broadcast
        BroadcastSnapshot();
    }

    [ClientRpc]
    private void SnapshotClientRpc(
        int seed,
        int currentPid,
        int phaseInt,
        int modeInt,
        bool hasRolled,
        bool awaitingRobber,
        int robberTileIndex,
        int[] nodeOwner,
        int[] nodeType,
        int[] edgeOwner)
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        // If client somehow generated wrong map, regenerate
        if (netSeed.Value == 0) netSeed.Value = seed;
        if (board.Tiles.Count == 0 || board.Nodes.Count == 0 || board.Edges.Count == 0)
        {
            board.GenerateFromSeed(seed);
        }

        // Apply turn state
        build.currentPlayerId = currentPid;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.mode = (BuildController.BuildMode)modeInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);

        // Apply robber
        for (int i = 0; i < board.Tiles.Count; i++)
        {
            if (board.Tiles[i] == null) continue;
            board.Tiles[i].hasRobber = (i == robberTileIndex);
            board.Tiles[i].RefreshVisual();
        }

        // Safety checks (avoids IndexOutOfRange)
        int nCount = Mathf.Min(board.Nodes.Count, nodeOwner.Length, nodeType.Length);
        int eCount = Mathf.Min(board.Edges.Count, edgeOwner.Length);

        // Apply node buildings + markers
        for (int i = 0; i < nCount; i++)
        {
            var node = board.Nodes[i];
            if (node == null) continue;

            int owner = nodeOwner[i];
            int type = nodeType[i];

            if (type == 0 || owner < 0)
            {
                node.building = null;
                // hide marker if exists
                var m = node.transform.Find("Marker");
                if (m != null) m.gameObject.SetActive(false);
            }
            else
            {
                var bType = (type == 2) ? BuildingType.City : BuildingType.Settlement;
                node.building = new Building(owner, bType);

                // show marker (same method you already use)
                float size = (bType == BuildingType.City) ? 0.45f : 0.30f;
                var color = build.players[owner].playerColor;
                // Force marker
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
                if (sr.sprite == null) sr.sprite = build.markerSprite;

                sr.color = color;
                sr.sortingLayerName = "Default";
                sr.sortingOrder = 1000;
                markerT.localScale = Vector3.one * size;
            }
        }

        // Apply edges
        for (int i = 0; i < eCount; i++)
        {
            var edge = board.Edges[i];
            if (edge == null) continue;

            int owner = edgeOwner[i];
            if (owner < 0)
            {
                edge.ClearOwnerVisual();
            }
            else
            {
                edge.ApplyOwnerVisual(owner, build.players[owner].playerColor);
            }
        }

        // Update local player id on each machine
        LocalPlayerId = GetPlayerIdForClient(NetworkManager.Singleton.LocalClientId);
    }

    // ============================
    // Find by id helpers
    // ============================

    private Intersection FindNodeById(int nodeId)
    {
        if (board == null || board.Nodes == null) return null;
        return board.Nodes.FirstOrDefault(n => n != null && n.id == nodeId);
    }

    private RoadEdge FindEdgeById(int edgeId)
    {
        if (board == null || board.Edges == null) return null;
        return board.Edges.FirstOrDefault(e => e != null && e.id == edgeId);
    }
}