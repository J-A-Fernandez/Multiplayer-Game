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

    [Header("Seed sync (server sets once)")]
    public NetworkVariable<int> mapSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Lookups by deterministic ids
    private readonly Dictionary<int, Intersection> nodeById = new();
    private readonly Dictionary<int, RoadEdge> edgeById = new();

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

        if (board == null || build == null)
        {
            Debug.LogError("NetworkCatanManager missing board/build refs. Assign them in Inspector.");
            return;
        }

        // Seed handler
        mapSeed.OnValueChanged += (_, newSeed) =>
        {
            if (newSeed == 0) return;
            ApplySeed(newSeed);
        };

        if (IsServer)
        {
            if (mapSeed.Value == 0)
                mapSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
        }

        // Apply immediately if already set
        if (mapSeed.Value != 0)
            ApplySeed(mapSeed.Value);

        // Server sends initial snapshot after spawn
        if (IsServer)
            StartCoroutine(BroadcastSnapshotNextFrame());
        else
            StartCoroutine(RequestSnapshotSoon());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopAllCoroutines();
        mapSeed.OnValueChanged -= (_, __) => { };
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private void ApplySeed(int seed)
    {
        // Everyone generates same board
        board.GenerateFromSeed(seed);
        build.board = board;

        RebuildLookups();

        // Server is authoritative: start game only on server
        if (IsServer)
        {
            // Make sure player array matches connected client count (2 for your test)
            int playerCount = Mathf.Clamp(NetworkManager.Singleton.ConnectedClientsIds.Count, 1, 4);
            build.EnsurePlayerCount(playerCount);

            // Fresh setup once
            build.BeginSetup();
        }
    }

    private void RebuildLookups()
    {
        nodeById.Clear();
        edgeById.Clear();

        foreach (var n in board.Nodes)
            if (n != null) nodeById[n.id] = n;

        foreach (var e in board.Edges)
            if (e != null) edgeById[e.id] = e;
    }

    // Host(0)=P0, first client(1)=P1, etc.
    private int PlayerIdFromClientId(ulong clientId) => (int)clientId;

    private bool SenderIsCurrentPlayer(ulong senderClientId)
    {
        int pid = PlayerIdFromClientId(senderClientId);
        return pid == build.currentPlayerId;
    }

    // --------- RPC safety (fixes shutdown spam) ----------
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

    private IEnumerator RequestSnapshotSoon()
    {
        // client: wait 2 frames so scene objects exist
        yield return null;
        yield return null;
        RequestSnapshotServerRpc();
    }

    // =========================
    // SERVER RPCs (client -> server)
    // =========================

    [ServerRpc(RequireOwnership = false)]
    public void RequestSnapshotServerRpc(ServerRpcParams rpc = default)
    {
        // Just broadcast for MVP
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;

        build.TryPlaceSettlement(node);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;

        build.TryUpgradeCity(node);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int edgeId, ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        if (!edgeById.TryGetValue(edgeId, out var edge) || edge == null) return;

        build.TryPlaceRoad(edge);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int tileIndex, ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        if (tileIndex < 0 || tileIndex >= board.Tiles.Count) return;

        build.TryMoveRobber(board.Tiles[tileIndex]);
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestRollServerRpc(ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        build.RollDiceAndDistribute();
        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndTurnServerRpc(ServerRpcParams rpc = default)
    {
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;
        build.EndTurn();
        BroadcastSnapshot();
    }

    // =========================
    // SNAPSHOT (server -> all clients)
    // =========================
    private void BroadcastSnapshot()
    {
        if (!CanSendRpc()) return;

        // Pack nodes
        int nodeCount = board.Nodes.Count;
        int[] nodeIds = new int[nodeCount];
        int[] nodeOwner = new int[nodeCount];
        byte[] nodeType = new byte[nodeCount]; // 0 none, 1 settlement, 2 city

        for (int i = 0; i < nodeCount; i++)
        {
            var n = board.Nodes[i];
            nodeIds[i] = n != null ? n.id : -1;

            if (n == null || n.building == null)
            {
                nodeOwner[i] = -1;
                nodeType[i] = 0;
            }
            else
            {
                nodeOwner[i] = n.building.ownerId;
                nodeType[i] = (byte)(n.building.type == BuildingType.City ? 2 : 1);
            }
        }

        // Pack edges
        int edgeCount = board.Edges.Count;
        int[] edgeIds = new int[edgeCount];
        int[] edgeOwner = new int[edgeCount];

        for (int i = 0; i < edgeCount; i++)
        {
            var e = board.Edges[i];
            edgeIds[i] = e != null ? e.id : -1;
            edgeOwner[i] = e != null ? e.ownerId : -1;
        }

        // Robber
        int robberIndex = -1;
        for (int i = 0; i < board.Tiles.Count; i++)
        {
            if (board.Tiles[i] != null && board.Tiles[i].hasRobber) { robberIndex = i; break; }
        }

        SnapshotClientRpc(
            mapSeed.Value,
            build.currentPlayerId,
            (int)build.phase,
            (int)build.mode,
            build.HasRolledThisTurn,
            build.AwaitingRobberMove,
            robberIndex,
            nodeIds, nodeOwner, nodeType,
            edgeIds, edgeOwner
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
        int[] nodeIds, int[] nodeOwner, byte[] nodeType,
        int[] edgeIds, int[] edgeOwner)
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
        if (board == null || build == null) return;

        // Ensure same map
        if (board.Tiles.Count == 0 || board.Nodes.Count == 0 || board.Edges.Count == 0)
        {
            board.GenerateFromSeed(seed);
            build.board = board;
        }

        RebuildLookups();

        // Apply turn state
        build.currentPlayerId = currentPlayerId;
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

        // Apply buildings
        for (int i = 0; i < nodeIds.Length; i++)
        {
            int id = nodeIds[i];
            if (id < 0) continue;
            if (!nodeById.TryGetValue(id, out var node) || node == null) continue;

            if (nodeOwner[i] < 0 || nodeType[i] == 0)
            {
                node.building = null;
                var m = node.transform.Find("Marker");
                if (m != null) m.gameObject.SetActive(false);
            }
            else
            {
                var bt = (nodeType[i] == 2) ? BuildingType.City : BuildingType.Settlement;
                node.building = new Building(nodeOwner[i], bt);

                // show marker
                float size = (bt == BuildingType.City) ? 0.45f : 0.30f;
                ShowMarker(node, build.players[nodeOwner[i]].playerColor, size);
            }
        }

        // Apply roads (THIS is what you were missing)
        for (int i = 0; i < edgeIds.Length; i++)
        {
            int id = edgeIds[i];
            if (id < 0) continue;
            if (!edgeById.TryGetValue(id, out var edge) || edge == null) continue;

            edge.ownerId = edgeOwner[i];

            if (edge.ownerId >= 0 && edge.ownerId < build.players.Length)
                ColorRoad(edge, build.players[edge.ownerId].playerColor);
            else
                ColorRoad(edge, Color.white);
        }
    }

    // =========================
    // Visual helpers
    // =========================
    private void ShowMarker(Intersection node, Color color, float size)
    {
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
        if (sr.sprite == null && build.markerSprite != null) sr.sprite = build.markerSprite;

        sr.color = color;
        sr.sortingOrder = 1000;
        markerT.localScale = Vector3.one * size;
    }

    private void ColorRoad(RoadEdge edge, Color color)
    {
        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr != null) sr.color = color;
    }
}