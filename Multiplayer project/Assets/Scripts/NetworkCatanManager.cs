using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs")]
    public BoardGenerator board;
    public BuildController build;

    [Header("Sync")]
    [Range(0.05f, 1f)]
    public float syncInterval = 0.25f;

    private Coroutine syncLoop;

    private void Awake()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    public override void OnNetworkSpawn()
    {
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

        if (IsServer)
            syncLoop = StartCoroutine(ServerSyncLoop());
    }

    public override void OnNetworkDespawn()
    {
        if (syncLoop != null) StopCoroutine(syncLoop);
        syncLoop = null;
    }

    private IEnumerator ServerSyncLoop()
    {
        for (int i = 0; i < 300; i++)
        {
            if (board != null && board.Tiles != null && board.Nodes != null && board.Edges != null &&
                board.Tiles.Count > 0 && board.Nodes.Count > 0 && board.Edges.Count > 0)
                break;

            yield return null;
        }

        while (IsServer)
        {
            SendSnapshotToClients();
            yield return new WaitForSeconds(syncInterval);
        }
    }

    // ========= Public API called by NetworkBoardClickManager =========
    public void RequestPlaceSettlement(int nodeId) => SubmitPlaceSettlementServerRpc(nodeId);
    public void RequestPlaceRoad(int aNodeId, int bNodeId) => SubmitPlaceRoadServerRpc(aNodeId, bNodeId);
    public void RequestUpgradeCity(int nodeId) => SubmitUpgradeCityServerRpc(nodeId);
    public void RequestMoveRobber(int q, int r) => SubmitMoveRobberServerRpc(q, r);

    // ========= Server validation =========
    private bool IsTurnOwner(RpcParams rpcParams)
    {
        // MVP: playerId == clientId (Host=0, first client=1)
        ulong sender = rpcParams.Receive.SenderClientId;
        return (int)sender == build.currentPlayerId;
    }

    // ========= RPCs (Client -> Server) =========

    [Rpc(SendTo.Server)]
    private void SubmitPlaceSettlementServerRpc(int nodeId, RpcParams rpcParams = default)
    {
        if (!IsServer || build == null || board == null) return;
        if (!IsTurnOwner(rpcParams)) return;

        var node = board.Nodes.FirstOrDefault(n => n != null && n.id == nodeId);
        if (node == null) return;

        // ✅ IMPORTANT: set server mode
        var prev = build.mode;
        build.mode = BuildController.BuildMode.Settlement;
        build.TryPlaceSettlement(node);
        build.mode = prev;
    }

    [Rpc(SendTo.Server)]
    private void SubmitPlaceRoadServerRpc(int aId, int bId, RpcParams rpcParams = default)
    {
        if (!IsServer || build == null || board == null) return;
        if (!IsTurnOwner(rpcParams)) return;

        var edge = FindEdge(aId, bId);
        if (edge == null) return;

        // ✅ IMPORTANT: set server mode
        var prev = build.mode;
        build.mode = BuildController.BuildMode.Road;
        build.TryPlaceRoad(edge);
        build.mode = prev;
    }

    [Rpc(SendTo.Server)]
    private void SubmitUpgradeCityServerRpc(int nodeId, RpcParams rpcParams = default)
    {
        if (!IsServer || build == null || board == null) return;
        if (!IsTurnOwner(rpcParams)) return;

        var node = board.Nodes.FirstOrDefault(n => n != null && n.id == nodeId);
        if (node == null) return;

        // ✅ IMPORTANT: set server mode
        var prev = build.mode;
        build.mode = BuildController.BuildMode.City;
        build.TryUpgradeCity(node);
        build.mode = prev;
    }

    [Rpc(SendTo.Server)]
    private void SubmitMoveRobberServerRpc(int q, int r, RpcParams rpcParams = default)
    {
        if (!IsServer || build == null || board == null) return;
        if (!IsTurnOwner(rpcParams)) return;

        var tile = board.Tiles.FirstOrDefault(t => t != null && t.coord.q == q && t.coord.r == r);
        if (tile == null) return;

        // ✅ IMPORTANT: set server mode
        var prev = build.mode;
        build.mode = BuildController.BuildMode.Robber;
        build.TryMoveRobber(tile);
        build.mode = prev;
    }

    // ========= Snapshot Sync (Server -> Clients) =========

    private void SendSnapshotToClients()
    {
        if (!IsServer || build == null || board == null) return;

        // buildings packed: nodeId, ownerId, typeInt (0 settlement, 1 city)
        var buildings = new List<int>(256);
        foreach (var n in board.Nodes)
        {
            if (n == null || n.building == null) continue;
            buildings.Add(n.id);
            buildings.Add(n.building.ownerId);
            buildings.Add(n.building.type == BuildingType.City ? 1 : 0);
        }

        // roads packed: aId, bId, ownerId
        var roads = new List<int>(512);
        foreach (var e in board.Edges)
        {
            if (e == null || e.A == null || e.B == null) continue;
            if (e.ownerId == -1) continue;
            roads.Add(e.A.id);
            roads.Add(e.B.id);
            roads.Add(e.ownerId);
        }

        // ✅ tiles packed: q, r, resourceInt, number, robber(0/1)
        var tiles = new List<int>(board.Tiles.Count * 5);
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            tiles.Add(t.coord.q);
            tiles.Add(t.coord.r);
            tiles.Add((int)t.resource);
            tiles.Add(t.number);
            tiles.Add(t.hasRobber ? 1 : 0);
        }

        // players packed: brick,lumber,wool,grain,ore,vp,knights
        int nPlayers = build.players.Length;
        var pdata = new int[nPlayers * 7];
        for (int i = 0; i < nPlayers; i++)
        {
            var p = build.players[i];
            int b = i * 7;
            pdata[b + 0] = p.brick;
            pdata[b + 1] = p.lumber;
            pdata[b + 2] = p.wool;
            pdata[b + 3] = p.grain;
            pdata[b + 4] = p.ore;
            pdata[b + 5] = p.victoryPoints;
            pdata[b + 6] = p.knightsPlayed;
        }

        SnapshotClientRpc(
            build.currentPlayerId,
            (int)build.phase,
            build.HasRolledThisTurn,
            build.AwaitingRobberMove,
            build.GameOver,
            build.WinnerId,
            buildings.ToArray(),
            roads.ToArray(),
            tiles.ToArray(),
            pdata
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SnapshotClientRpc(
        int currentPid,
        int phaseInt,
        bool hasRolled,
        bool awaitingRobber,
        bool isGameOver,
        int winnerId,
        int[] buildingsPacked,
        int[] roadsPacked,
        int[] tilesPacked,
        int[] playerPacked
    )
    {
        if (build == null || board == null) return;
        if (board.Nodes == null || board.Edges == null || board.Tiles == null) return;

        // meta/turn
        build.currentPlayerId = currentPid;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);
        build.Net_SetGameMeta(isGameOver, winnerId);

        // players
        int nPlayers = build.players.Length;
        for (int i = 0; i < nPlayers; i++)
        {
            int b = i * 7;
            var p = build.players[i];
            p.brick = playerPacked[b + 0];
            p.lumber = playerPacked[b + 1];
            p.wool = playerPacked[b + 2];
            p.grain = playerPacked[b + 3];
            p.ore = playerPacked[b + 4];
            p.victoryPoints = playerPacked[b + 5];
            p.knightsPlayed = playerPacked[b + 6];
        }

        // tiles lookup
        var tileByCoord = new Dictionary<(int q, int r), HexTile>(board.Tiles.Count);
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            tileByCoord[(t.coord.q, t.coord.r)] = t;
        }

        // apply tiles
        for (int i = 0; i + 4 < tilesPacked.Length; i += 5)
        {
            int q = tilesPacked[i + 0];
            int r = tilesPacked[i + 1];
            var res = (ResourceType)tilesPacked[i + 2];
            int num = tilesPacked[i + 3];
            bool rob = tilesPacked[i + 4] == 1;

            if (tileByCoord.TryGetValue((q, r), out var tile))
            {
                tile.resource = res;
                tile.number = num;
                tile.hasRobber = rob;
            }
        }

        foreach (var t in board.Tiles)
            if (t != null) t.RefreshVisual();

        // clear buildings
        foreach (var n in board.Nodes)
        {
            if (n == null) continue;
            n.building = null;
            var marker = n.transform.Find("Marker");
            if (marker != null) marker.gameObject.SetActive(false);
        }

        // apply buildings
        for (int i = 0; i + 2 < buildingsPacked.Length; i += 3)
        {
            int nodeId = buildingsPacked[i];
            int ownerId = buildingsPacked[i + 1];
            bool isCityB = buildingsPacked[i + 2] == 1;

            var node = board.Nodes.FirstOrDefault(n => n != null && n.id == nodeId);
            if (node == null) continue;

            node.building = new Building(ownerId, isCityB ? BuildingType.City : BuildingType.Settlement);
            ShowMarker(node, build.players[ownerId].playerColor, isCityB ? 0.45f : 0.30f, build.markerSprite);
        }

        // clear roads
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            e.ownerId = -1;
            TintRoad(e, Color.white);
        }

        // apply roads
        for (int i = 0; i + 2 < roadsPacked.Length; i += 3)
        {
            int aId = roadsPacked[i];
            int bId = roadsPacked[i + 1];
            int ownerId = roadsPacked[i + 2];

            var edge = FindEdge(aId, bId);
            if (edge == null) continue;

            edge.ownerId = ownerId;
            TintRoad(edge, build.players[ownerId].playerColor);
        }
    }

    // ---------- helpers ----------
    private RoadEdge FindEdge(int aId, int bId)
    {
        foreach (var e in board.Edges)
        {
            if (e == null || e.A == null || e.B == null) continue;
            int ea = e.A.id, eb = e.B.id;
            if ((ea == aId && eb == bId) || (ea == bId && eb == aId)) return e;
        }
        return null;
    }

    private static void ShowMarker(Intersection node, Color color, float size, Sprite sprite)
    {
        if (sprite == null) return;

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
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = 1000;

        markerT.localScale = Vector3.one * size;
    }

    private static void TintRoad(RoadEdge edge, Color c)
    {
        var vis = edge.transform.Find("Visual");
        var sr = vis ? vis.GetComponent<SpriteRenderer>() : null;
        if (sr != null)
        {
            sr.color = c;
            sr.sortingOrder = 500;
        }
    }
}