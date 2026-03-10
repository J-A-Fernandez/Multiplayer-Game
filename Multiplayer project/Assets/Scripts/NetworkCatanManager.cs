using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs")]
    public BoardGenerator board;
    public BuildController build;

    [Header("Deterministic Map Seed (server sets once)")]
    public NetworkVariable<int> MapSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Local lookups (rebuilt after GenerateFromSeed)
    private Dictionary<int, Intersection> nodeById = new();
    private Dictionary<int, RoadEdge> edgeById = new();

    private bool seededAndBuilt = false;

    private void Awake()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    public override void OnNetworkSpawn()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (board == null || build == null)
        {
            Debug.LogError("NetworkCatanManager missing board/build refs. Assign them in Inspector.");
            return;
        }

        // Server chooses seed once
        if (IsServer && MapSeed.Value == 0)
            MapSeed.Value = UnityEngine.Random.Range(1, 999999999);

        MapSeed.OnValueChanged += (_, seed) =>
        {
            if (seed == 0) return;
            ApplySeed(seed);
        };

        // Late join / host start
        if (MapSeed.Value != 0)
            ApplySeed(MapSeed.Value);
    }

    private void ApplySeed(int seed)
    {
        if (seededAndBuilt) return;

        board.GenerateFromSeed(seed);
        build.board = board;

        RebuildLookups();

        seededAndBuilt = true;

        // IMPORTANT: only server starts the actual game state
        if (IsServer)
        {
            // Make sure player count matches connected players for your test (2)
            // If you want always 2 players:
            build.EnsurePlayerCount(2);

            build.BeginSetup();

            // Push initial snapshot so everyone starts in sync
            BroadcastSnapshot();
        }
        else
        {
            // Client asks server for a snapshot (in case it joined late)
            RequestSnapshotServerRpc();
        }
    }

    private void RebuildLookups()
    {
        nodeById.Clear();
        edgeById.Clear();

        if (board.Nodes != null)
        {
            foreach (var n in board.Nodes)
                if (n != null) nodeById[n.id] = n;
        }

        if (board.Edges != null)
        {
            foreach (var e in board.Edges)
                if (e != null) edgeById[e.id] = e;
        }
    }

    // ============================================================
    // PLAYER MAPPING
    // Host clientId usually 0 -> player 0 (Player 1)
    // First client clientId 1 -> player 1 (Player 2)
    // ============================================================
    private int PlayerIdFromClientId(ulong clientId) => (int)clientId;

    private bool SenderIsCurrentPlayer(ulong senderClientId)
    {
        int pid = PlayerIdFromClientId(senderClientId);
        return pid == build.currentPlayerId;
    }

    // ============================================================
    // SNAPSHOT
    // ============================================================

    [ServerRpc(RequireOwnership = false)]
    private void RequestSnapshotServerRpc(ServerRpcParams rpc = default)
    {
        BroadcastSnapshot();
    }

    private void BroadcastSnapshot()
    {
        if (!IsServer) return;
        if (board == null || build == null) return;

        // Players
        int playerCount = build.players != null ? build.players.Length : 0;
        int[] brick = new int[playerCount];
        int[] lumber = new int[playerCount];
        int[] wool = new int[playerCount];
        int[] grain = new int[playerCount];
        int[] ore = new int[playerCount];
        int[] vp = new int[playerCount];

        for (int i = 0; i < playerCount; i++)
        {
            brick[i] = build.players[i].brick;
            lumber[i] = build.players[i].lumber;
            wool[i] = build.players[i].wool;
            grain[i] = build.players[i].grain;
            ore[i] = build.players[i].ore;
            vp[i] = build.players[i].victoryPoints;
        }

        // Nodes (buildings)
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

        // Edges (roads)
        int edgeCount = board.Edges.Count;
        int[] edgeIds = new int[edgeCount];
        int[] edgeOwner = new int[edgeCount];

        for (int i = 0; i < edgeCount; i++)
        {
            var e = board.Edges[i];
            edgeIds[i] = e != null ? e.id : -1;
            edgeOwner[i] = e != null ? e.ownerId : -1;
        }

        // Robber tile index
        int robberTileIndex = -1;
        for (int i = 0; i < board.Tiles.Count; i++)
        {
            if (board.Tiles[i] != null && board.Tiles[i].hasRobber)
            {
                robberTileIndex = i;
                break;
            }
        }

        SnapshotClientRpc(
            playerCount,
            build.currentPlayerId,
            (int)build.phase,
            (int)build.mode,
            build.HasRolledThisTurn,
            build.AwaitingRobberMove,
            brick, lumber, wool, grain, ore, vp,
            nodeIds, nodeOwner, nodeType,
            edgeIds, edgeOwner,
            robberTileIndex
        );
    }

    [ClientRpc]
    private void SnapshotClientRpc(
        int playerCount,
        int currentPlayerId,
        int phaseInt,
        int modeInt,
        bool hasRolled,
        bool awaitingRobber,
        int[] brick, int[] lumber, int[] wool, int[] grain, int[] ore, int[] vp,
        int[] nodeIds, int[] nodeOwner, byte[] nodeType,
        int[] edgeIds, int[] edgeOwner,
        int robberTileIndex)
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
        if (board == null || build == null) return;

        // Ensure lookups exist (client may have generated already but dictionary not built)
        if (nodeById.Count == 0 || edgeById.Count == 0)
            RebuildLookups();

        build.EnsurePlayerCount(playerCount);

        // Apply meta
        build.currentPlayerId = Mathf.Clamp(currentPlayerId, 0, build.players.Length - 1);
        build.phase = (BuildController.GamePhase)phaseInt;
        build.mode = (BuildController.BuildMode)modeInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);

        // Apply players
        int pn = Mathf.Min(build.players.Length, brick.Length, lumber.Length, wool.Length, grain.Length, ore.Length, vp.Length);
        for (int i = 0; i < pn; i++)
        {
            build.players[i].brick = brick[i];
            build.players[i].lumber = lumber[i];
            build.players[i].wool = wool[i];
            build.players[i].grain = grain[i];
            build.players[i].ore = ore[i];
            build.players[i].victoryPoints = vp[i];
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
                HideMarker(node);
            }
            else
            {
                var bt = (nodeType[i] == 2) ? BuildingType.City : BuildingType.Settlement;
                node.building = new Building(nodeOwner[i], bt);
                ShowMarker(node, build.players[nodeOwner[i]].playerColor, bt == BuildingType.City ? 0.45f : 0.30f);
            }
        }

        // Apply roads
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

        // Robber
        for (int i = 0; i < board.Tiles.Count; i++)
        {
            if (board.Tiles[i] == null) continue;
            board.Tiles[i].hasRobber = (i == robberTileIndex);
            board.Tiles[i].RefreshVisual();
        }
    }

    // ============================================================
    // SERVER RPC ACTIONS
    // ============================================================

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpc = default)
    {
        if (!IsServer) return;
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;

        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;
        build.TryPlaceSettlement(node);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpc = default)
    {
        if (!IsServer) return;
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;

        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;
        build.TryUpgradeCity(node);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int edgeId, ServerRpcParams rpc = default)
    {
        if (!IsServer) return;
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;

        if (!edgeById.TryGetValue(edgeId, out var edge) || edge == null) return;
        build.TryPlaceRoad(edge);

        BroadcastSnapshot();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int tileIndex, ServerRpcParams rpc = default)
    {
        if (!IsServer) return;
        if (!SenderIsCurrentPlayer(rpc.Receive.SenderClientId)) return;

        if (tileIndex < 0 || tileIndex >= board.Tiles.Count) return;
        var tile = board.Tiles[tileIndex];
        if (tile == null) return;

        build.TryMoveRobber(tile);

        BroadcastSnapshot();
    }

    // ============================================================
    // VISUAL HELPERS
    // ============================================================
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

        if (sr.sprite == null && build.markerSprite != null)
            sr.sprite = build.markerSprite;

        sr.enabled = true;
        sr.color = color;
        sr.sortingOrder = 1000;
        markerT.localScale = Vector3.one * size;
    }

    private void HideMarker(Intersection node)
    {
        var markerT = node.transform.Find("Marker");
        if (markerT != null) markerT.gameObject.SetActive(false);
    }

    private void ColorRoad(RoadEdge edge, Color color)
    {
        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr != null) sr.color = color;
    }
}