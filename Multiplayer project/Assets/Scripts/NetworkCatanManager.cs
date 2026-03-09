using System.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs")]
    public BoardGenerator board;
    public BuildController build;

    // Everyone reads, only server writes
    public NetworkVariable<int> mapSeed = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

public override void OnNetworkSpawn()
{
    // Auto-find references if not assigned in inspector
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

    // Host controls state
    if (IsServer)
    {
        // Make sure board is generated before using it
        StartCoroutine(WaitThenHostInit());
    }
}
private IEnumerator WaitThenHostInit()
{
    for (int i = 0; i < 300; i++)
    {
        if (board != null && board.Tiles != null && board.Nodes != null && board.Edges != null &&
            board.Tiles.Count > 0 && board.Nodes.Count > 0 && board.Edges.Count > 0)
        {
            break;
        }
        yield return null;
    }

    if (board == null || board.Tiles == null || board.Tiles.Count == 0)
    {
        Debug.LogError("NetworkCatanManager: Board never became ready (Tiles empty).");
        yield break;
    }

    // host init here...
}
    private void GenerateBoard(int seed)
    {
        if (board == null) return;

        board.useRandomSeed = false;
        board.randomSeed = seed;
        board.Generate();
    }

    private int SenderPid(ServerRpcParams rpcParams) => (int)rpcParams.Receive.SenderClientId;

    private bool IsAllowedTurn(ServerRpcParams rpcParams)
    {
        int pid = SenderPid(rpcParams);
        if (pid < 0 || pid >= build.players.Length) return false;
        return pid == build.currentPlayerId;
    }

    // ----------- FULL STATE SYNC (for joiners) -----------

    [ServerRpc(RequireOwnership = false)]
    private void RequestFullStateServerRpc(ServerRpcParams rpcParams = default)
    {
        SendFullStateToClient(rpcParams.Receive.SenderClientId);
    }

    private void SendFullStateToClient(ulong clientId)
    {
        // pack buildings: (nodeId, ownerId, typeInt) where typeInt 0=Settlement 1=City
        var buildings = new List<int>(256);
        foreach (var n in board.Nodes)
        {
            if (n == null || n.building == null) continue;
            buildings.Add(n.id);
            buildings.Add(n.building.ownerId);
            buildings.Add(n.building.type == BuildingType.City ? 1 : 0);
        }

        // pack roads: (aId, bId, ownerId)
        var roads = new List<int>(512);
        foreach (var e in board.Edges)
        {
            if (e == null || e.ownerId == -1 || e.A == null || e.B == null) continue;
            roads.Add(e.A.id);
            roads.Add(e.B.id);
            roads.Add(e.ownerId);
        }

        int robberQ = 999, robberR = 999;
        foreach (var t in board.Tiles)
        {
            if (t != null && t.hasRobber)
            {
                robberQ = t.coord.q;
                robberR = t.coord.r;
                break;
            }
        }

        // pack players: per player [brick,lumber,wool,grain,ore,vp,knights]
        int pCount = build.players.Length;
        var pdata = new int[pCount * 7];
        for (int i = 0; i < pCount; i++)
        {
            var p = build.players[i];
            int baseIdx = i * 7;
            pdata[baseIdx + 0] = p.brick;
            pdata[baseIdx + 1] = p.lumber;
            pdata[baseIdx + 2] = p.wool;
            pdata[baseIdx + 3] = p.grain;
            pdata[baseIdx + 4] = p.ore;
            pdata[baseIdx + 5] = p.victoryPoints;
            pdata[baseIdx + 6] = p.knightsPlayed;
        }

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        FullStateClientRpc(
            mapSeed.Value,
            build.currentPlayerId,
            (int)build.phase,
            build.HasRolledThisTurn,
            build.AwaitingRobberMove,
            buildings.ToArray(),
            roads.ToArray(),
            robberQ,
            robberR,
            pdata,
            target
        );
    }

    [ClientRpc]
    private void FullStateClientRpc(
        int seed,
        int currentPlayerId,
        int phaseInt,
        bool hasRolled,
        bool awaitingRobber,
        int[] buildingsPacked,
        int[] roadsPacked,
        int robberQ,
        int robberR,
        int[] playerPacked,
        ClientRpcParams _ = default)
    {
        // ensure board exists
        if (mapSeed.Value == 0) mapSeed.Value = seed;
        GenerateBoard(seed);

        // clear visuals/state
        ClearAllBoardState();

        // apply buildings
        for (int i = 0; i + 2 < buildingsPacked.Length; i += 3)
        {
            int nodeId = buildingsPacked[i];
            int ownerId = buildingsPacked[i + 1];
            bool isCity = buildingsPacked[i + 2] == 1;
            ApplySettlementVisual(nodeId, ownerId, isCity);
        }

        // apply roads
        for (int i = 0; i + 2 < roadsPacked.Length; i += 3)
        {
            int aId = roadsPacked[i];
            int bId = roadsPacked[i + 1];
            int ownerId = roadsPacked[i + 2];
            ApplyRoadVisual(aId, bId, ownerId);
        }

        // apply robber
        if (robberQ != 999)
            ApplyRobberVisual(robberQ, robberR);

        // turn state
        build.currentPlayerId = currentPlayerId;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);

        // players
        int pCount = build.players.Length;
        for (int p = 0; p < pCount; p++)
        {
            int baseIdx = p * 7;
            var ps = build.players[p];
            ps.brick = playerPacked[baseIdx + 0];
            ps.lumber = playerPacked[baseIdx + 1];
            ps.wool = playerPacked[baseIdx + 2];
            ps.grain = playerPacked[baseIdx + 3];
            ps.ore = playerPacked[baseIdx + 4];
            ps.victoryPoints = playerPacked[baseIdx + 5];
            ps.knightsPlayed = playerPacked[baseIdx + 6];
        }
    }

    private void ClearAllBoardState()
    {
        // clear buildings/markers
        foreach (var n in board.Nodes)
        {
            if (n == null) continue;
            n.building = null;
            var marker = n.transform.Find("Marker");
            if (marker != null) marker.gameObject.SetActive(false);
        }

        // clear roads
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            e.ownerId = -1;
            var vis = e.transform.Find("Visual");
            var sr = vis ? vis.GetComponent<SpriteRenderer>() : null;
            if (sr != null) sr.color = Color.white;
        }

        // clear robber
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.hasRobber = false;
            t.RefreshVisual();
        }
    }

    // ----------- SERVER RPCs: intents from clients -----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestRollServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;
        build.RollDiceAndDistribute();
        SendFullStateToClient(rpcParams.Receive.SenderClientId); // feedback to caller
        // and broadcast to everyone
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndTurnServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;
        build.EndTurn();
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;

        var node = FindNode(nodeId);
        if (node == null) return;

        var prevMode = build.mode;
        build.mode = BuildController.BuildMode.Settlement;
        build.TryPlaceSettlement(node);
        build.mode = prevMode;

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;

        var node = FindNode(nodeId);
        if (node == null) return;

        var prevMode = build.mode;
        build.mode = BuildController.BuildMode.City;
        build.TryUpgradeCity(node);
        build.mode = prevMode;

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int aId, int bId, ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;

        var edge = FindEdge(aId, bId);
        if (edge == null) return;

        var prevMode = build.mode;
        build.mode = BuildController.BuildMode.Road;
        build.TryPlaceRoad(edge);
        build.mode = prevMode;

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int q, int r, ServerRpcParams rpcParams = default)
    {
        if (!IsAllowedTurn(rpcParams)) return;

        // only when server says awaiting robber
        if (!build.AwaitingRobberMove) return;

        var tile = FindTile(q, r);
        if (tile == null) return;

        build.TryMoveRobber(tile);

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            SendFullStateToClient(id);
    }

    // ----------- Local lookups + visuals -----------

    private Intersection FindNode(int nodeId)
    {
        foreach (var n in board.Nodes)
            if (n != null && n.id == nodeId) return n;
        return null;
    }

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

    private HexTile FindTile(int q, int r)
    {
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            if (t.coord.q == q && t.coord.r == r) return t;
        }
        return null;
    }

    private void ApplySettlementVisual(int nodeId, int ownerId, bool isCity)
    {
        var node = FindNode(nodeId);
        if (node == null) return;

        node.building = new Building(ownerId, isCity ? BuildingType.City : BuildingType.Settlement);

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

        sr.color = build.players[ownerId].playerColor;
        sr.sortingOrder = 1000;

        markerT.localScale = Vector3.one * (isCity ? 0.45f : 0.30f);
    }

    private void ApplyRoadVisual(int aId, int bId, int ownerId)
    {
        var edge = FindEdge(aId, bId);
        if (edge == null) return;

        edge.ownerId = ownerId;

        var vis = edge.transform.Find("Visual");
        var sr = vis ? vis.GetComponent<SpriteRenderer>() : null;
        if (sr != null)
        {
            sr.color = build.players[ownerId].playerColor;
            sr.sortingOrder = 500;
        }
    }

    private void ApplyRobberVisual(int q, int r)
    {
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.hasRobber = false;
            t.RefreshVisual();
        }

        var tile = FindTile(q, r);
        if (tile == null) return;
        tile.hasRobber = true;
        tile.RefreshVisual();
    }
}