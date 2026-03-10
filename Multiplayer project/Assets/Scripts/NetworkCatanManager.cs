using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs (assign in Inspector)")]
    public BoardGenerator board;
    public BuildController build;

    [Header("Deterministic map seed")]
    public int mapSeed = 12345;

    // Cached lookups (built locally on each client)
    private Dictionary<int, Intersection> nodeById;
    private Dictionary<(int, int), RoadEdge> edgeByPair;
    private Dictionary<(int q, int r), HexTile> tileByCoord;

    private bool initializedLocal = false;

    // -----------------------------
    // Unity / NGO lifecycle
    // -----------------------------
    public override void OnNetworkSpawn()
    {
        // If you didn’t assign these in inspector, try to find them
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (board == null)
        {
            Debug.LogError("NetworkCatanManager: BoardGenerator not found. Assign 'board' in inspector.", this);
            return;
        }
        if (build == null)
        {
            Debug.LogError("NetworkCatanManager: BuildController not found. Assign 'build' in inspector.", this);
            return;
        }

        // Host chooses seed; everyone generates same map locally
        if (IsServer)
        {
            // Make sure board uses deterministic seed if you have these fields
            // (If your BoardGenerator uses different field names, tell me and I’ll adjust)
            board.useRandomSeed = false;
            board.randomSeed = mapSeed;

            board.Generate();

            // Make sure BuildController uses the same board ref
            build.board = board;

            // Send seed to clients so they generate same board
            SendSeedClientRpc(mapSeed);
        }
        else
        {
            // Clients will generate when they receive seed
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SendSeedClientRpc(int seed)
    {
        mapSeed = seed;

        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (board == null || build == null)
        {
            Debug.LogError("NetworkCatanManager: Missing board/build on client. Make sure both exist in the scene.");
            return;
        }

        board.useRandomSeed = false;
        board.randomSeed = mapSeed;

        board.Generate();
        build.board = board;

        BuildLocalLookups();

        initializedLocal = true;

        // Ask server for a full snapshot so we are in sync
        if (!IsServer)
            RequestSnapshotServerRpc();
    }

    private void Update()
    {
        // Host can initialize lookups after generation
        if (!initializedLocal && IsServer && board != null && board.Nodes != null && board.Nodes.Count > 0)
        {
            BuildLocalLookups();
            initializedLocal = true;

            // Host broadcasts initial snapshot once ready
            BroadcastSnapshot();
        }
    }

    private void BuildLocalLookups()
    {
        nodeById = new Dictionary<int, Intersection>();
        edgeByPair = new Dictionary<(int, int), RoadEdge>();
        tileByCoord = new Dictionary<(int q, int r), HexTile>();

        // Nodes by id
        foreach (var n in board.Nodes)
        {
            if (n == null) continue;
            nodeById[n.id] = n;
        }

        // Edges by pair of endpoint node ids (min,max)
        foreach (var e in board.Edges)
        {
            if (e == null || e.A == null || e.B == null) continue;
            int a = e.A.id;
            int b = e.B.id;
            if (a > b) (a, b) = (b, a);
            edgeByPair[(a, b)] = e;
        }

        // Tiles by axial coord (q,r) - assumes your HexTile.coord has q and r fields
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            tileByCoord[(t.coord.q, t.coord.r)] = t;
        }

        Debug.Log($"[Net] Local lookups built: Nodes={nodeById.Count} Edges={edgeByPair.Count} Tiles={tileByCoord.Count}");
    }

    // -----------------------------
    // Public helpers used by click manager / HUD
    // -----------------------------

    public void RequestSetMode(BuildController.BuildMode m)
    {
        RequestSetModeServerRpc((int)m);
    }

    public void RequestRoll()
    {
        RequestRollServerRpc();
    }

    public void RequestEndTurn()
    {
        RequestEndTurnServerRpc();
    }

    public void RequestPlaceSettlement(int nodeId)
    {
        RequestPlaceSettlementServerRpc(nodeId);
    }

    public void RequestUpgradeCity(int nodeId)
    {
        RequestUpgradeCityServerRpc(nodeId);
    }

    public void RequestPlaceRoad(int aNodeId, int bNodeId)
    {
        RequestPlaceRoadServerRpc(aNodeId, bNodeId);
    }

    public void RequestMoveRobber(int tileQ, int tileR)
    {
        RequestMoveRobberServerRpc(tileQ, tileR);
    }

    // -----------------------------
    // SERVER RPCs (authority)
    // -----------------------------

    [Rpc(SendTo.Server)]
    private void RequestSnapshotServerRpc()
    {
        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestSetModeServerRpc(int modeInt)
    {
        if (!ServerReady()) return;

        build.mode = (BuildController.BuildMode)modeInt;
        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestRollServerRpc()
    {
        if (!ServerReady()) return;

        build.RollDiceAndDistribute();
        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestEndTurnServerRpc()
    {
        if (!ServerReady()) return;

        build.EndTurn();
        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestPlaceSettlementServerRpc(int nodeId)
    {
        if (!ServerReady()) return;

        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;

        // Only server changes game state
        build.TryPlaceSettlement(node);

        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestUpgradeCityServerRpc(int nodeId)
    {
        if (!ServerReady()) return;

        if (!nodeById.TryGetValue(nodeId, out var node) || node == null) return;

        build.TryUpgradeCity(node);

        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestPlaceRoadServerRpc(int nodeAId, int nodeBId)
    {
        if (!ServerReady()) return;

        int a = nodeAId;
        int b = nodeBId;
        if (a > b) (a, b) = (b, a);

        if (!edgeByPair.TryGetValue((a, b), out var edge) || edge == null) return;

        build.TryPlaceRoad(edge);

        BroadcastSnapshot();
    }

    [Rpc(SendTo.Server)]
    public void RequestMoveRobberServerRpc(int q, int r)
    {
        if (!ServerReady()) return;

        if (!tileByCoord.TryGetValue((q, r), out var tile) || tile == null) return;

        build.TryMoveRobber(tile);

        BroadcastSnapshot();
    }

    private bool ServerReady()
    {
        if (!IsServer) return false;
        if (board == null || build == null) return false;
        if (!initializedLocal) BuildLocalLookups();
        return true;
    }

    // -----------------------------
    // SNAPSHOT: server -> clients
    // -----------------------------

    private void BroadcastSnapshot()
    {
        if (!IsServer) return;
        if (!ServerReady()) return;

        // --- Nodes (buildings) ---
        int nodeCount = board.Nodes.Count;
        int[] nodeIds = new int[nodeCount];
        int[] nodeOwner = new int[nodeCount];
        int[] nodeBType = new int[nodeCount]; // 0 none, 1 settlement, 2 city

        for (int i = 0; i < nodeCount; i++)
        {
            var n = board.Nodes[i];
            nodeIds[i] = (n != null) ? n.id : -1;

            if (n == null || n.building == null)
            {
                nodeOwner[i] = -1;
                nodeBType[i] = 0;
            }
            else
            {
                nodeOwner[i] = n.building.ownerId;
                nodeBType[i] = (n.building.type == BuildingType.City) ? 2 : 1;
            }
        }

        // --- Edges (roads) ---
        int edgeCount = board.Edges.Count;
        int[] edgeA = new int[edgeCount];
        int[] edgeB = new int[edgeCount];
        int[] edgeOwner = new int[edgeCount];

        for (int i = 0; i < edgeCount; i++)
        {
            var e = board.Edges[i];
            if (e == null || e.A == null || e.B == null)
            {
                edgeA[i] = -1;
                edgeB[i] = -1;
                edgeOwner[i] = -1;
            }
            else
            {
                edgeA[i] = e.A.id;
                edgeB[i] = e.B.id;
                edgeOwner[i] = e.ownerId;
            }
        }

        // --- Tiles (robber) ---
        int tileCount = board.Tiles.Count;
        int[] tileQ = new int[tileCount];
        int[] tileR = new int[tileCount];
        int[] tileRobber = new int[tileCount];

        for (int i = 0; i < tileCount; i++)
        {
            var t = board.Tiles[i];
            if (t == null)
            {
                tileQ[i] = 0; tileR[i] = 0; tileRobber[i] = 0;
            }
            else
            {
                tileQ[i] = t.coord.q;
                tileR[i] = t.coord.r;
                tileRobber[i] = t.hasRobber ? 1 : 0;
            }
        }

        // --- Players ---
        int pCount = build.players.Length;
        int[] brick = new int[pCount];
        int[] lumber = new int[pCount];
        int[] wool = new int[pCount];
        int[] grain = new int[pCount];
        int[] ore = new int[pCount];
        int[] vp = new int[pCount];
        int[] knights = new int[pCount];

        for (int i = 0; i < pCount; i++)
        {
            brick[i] = build.players[i].brick;
            lumber[i] = build.players[i].lumber;
            wool[i] = build.players[i].wool;
            grain[i] = build.players[i].grain;
            ore[i] = build.players[i].ore;
            vp[i] = build.players[i].victoryPoints;
            knights[i] = build.players[i].knightsPlayed;
        }

        SnapshotClientRpc(
            build.currentPlayerId,
            (int)build.phase,
            (int)build.mode,
            build.HasRolledThisTurn ? 1 : 0,
            build.AwaitingRobberMove ? 1 : 0,
            build.GameOver ? 1 : 0,
            build.WinnerId,

            nodeIds, nodeOwner, nodeBType,
            edgeA, edgeB, edgeOwner,
            tileQ, tileR, tileRobber,

            brick, lumber, wool, grain, ore, vp, knights
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SnapshotClientRpc(
        int currentPlayerId,
        int phaseInt,
        int modeInt,
        int rolled,
        int awaitingRobber,
        int gameOverInt,
        int winnerId,

        int[] nodeIds, int[] nodeOwner, int[] nodeBType,
        int[] edgeA, int[] edgeB, int[] edgeOwner,
        int[] tileQ, int[] tileR, int[] tileRobber,

        int[] brick, int[] lumber, int[] wool, int[] grain, int[] ore, int[] vp, int[] knights
    )
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (board == null || build == null) return;

        if (!initializedLocal)
        {
            BuildLocalLookups();
            initializedLocal = true;
        }

        // Ensure player array size matches snapshot
        build.EnsurePlayerCount(brick.Length);

        // Apply meta
        build.currentPlayerId = currentPlayerId;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.mode = (BuildController.BuildMode)modeInt;
        build.Net_SetTurnFlags(rolled == 1, awaitingRobber == 1);
        build.Net_SetGameMeta(gameOverInt == 1, winnerId);

        // Apply players
        for (int i = 0; i < build.players.Length; i++)
        {
            build.players[i].brick = brick[i];
            build.players[i].lumber = lumber[i];
            build.players[i].wool = wool[i];
            build.players[i].grain = grain[i];
            build.players[i].ore = ore[i];
            build.players[i].victoryPoints = vp[i];
            build.players[i].knightsPlayed = knights[i];
        }

        // Apply buildings (nodes)
        for (int i = 0; i < nodeIds.Length; i++)
        {
            int id = nodeIds[i];
            if (id < 0) continue;
            if (!nodeById.TryGetValue(id, out var node) || node == null) continue;

            int owner = nodeOwner[i];
            int type = nodeBType[i];

            if (type == 0 || owner < 0)
            {
                node.building = null;
                HideMarker(node);
            }
            else
            {
                var bType = (type == 2) ? BuildingType.City : BuildingType.Settlement;
                node.building = new Building(owner, bType);
                RevealMarker(node);
            }
        }

        // Apply roads
        for (int i = 0; i < edgeOwner.Length; i++)
        {
            int a = edgeA[i];
            int b = edgeB[i];
            if (a < 0 || b < 0) continue;
            if (a > b) (a, b) = (b, a);

            if (!edgeByPair.TryGetValue((a, b), out var edge) || edge == null) continue;
            edge.ownerId = edgeOwner[i];

            // recolor visuals if you have player colors
            if (edge.ownerId >= 0 && edge.ownerId < build.players.Length)
                ColorRoad(edge, build.players[edge.ownerId].playerColor);
            else
                ColorRoad(edge, Color.white);
        }

        // Apply robber
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.hasRobber = false;
        }
        for (int i = 0; i < tileRobber.Length; i++)
        {
            if (tileRobber[i] != 1) continue;
            if (tileByCoord.TryGetValue((tileQ[i], tileR[i]), out var tile) && tile != null)
                tile.hasRobber = true;
        }
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.RefreshVisual();
        }
    }

    // -----------------------------
    // Marker + road coloring helpers (client-side visual)
    // -----------------------------
    private void RevealMarker(Intersection node)
    {
        if (node == null || node.building == null) return;

        int owner = node.building.ownerId;
        float size = (node.building.type == BuildingType.City) ? 0.45f : 0.30f;

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

        sr.enabled = true;

        // If you stored colors in PlayerState
        if (owner >= 0 && owner < build.players.Length)
            sr.color = build.players[owner].playerColor;

        sr.sortingOrder = 1000;
        markerT.localScale = Vector3.one * size;
    }

    private void HideMarker(Intersection node)
    {
        if (node == null) return;
        var markerT = node.transform.Find("Marker");
        if (markerT != null) markerT.gameObject.SetActive(false);
    }

    private void ColorRoad(RoadEdge edge, Color color)
    {
        if (edge == null) return;
        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr != null) sr.color = color;
    }
}