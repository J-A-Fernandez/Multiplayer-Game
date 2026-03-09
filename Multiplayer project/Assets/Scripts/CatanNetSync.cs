using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class CatanNetSync : NetworkBehaviour
{
    public BuildController build;
    public BoardGenerator board;

    [Range(0.05f, 1f)]
    public float syncInterval = 0.25f;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            StartCoroutine(ServerSyncLoop());
    }

    private IEnumerator ServerSyncLoop()
    {
        while (IsServer)
        {
            SendSnapshotToClients();
            yield return new WaitForSeconds(syncInterval);
        }
    }

    private void SendSnapshotToClients()
    {
        if (build == null || board == null) return;
        if (board.Nodes == null || board.Edges == null || board.Tiles == null) return;

        // Pack buildings: (nodeId, ownerId, typeInt 0=Settlement 1=City)
        var buildings = new List<int>(256);
        foreach (var n in board.Nodes)
        {
            if (n == null || n.building == null) continue;
            buildings.Add(n.id);
            buildings.Add(n.building.ownerId);
            buildings.Add(n.building.type == BuildingType.City ? 1 : 0);
        }

        // Pack roads: (aId, bId, ownerId)
        var roads = new List<int>(512);
        foreach (var e in board.Edges)
        {
            if (e == null || e.A == null || e.B == null) continue;
            if (e.ownerId == -1) continue;
            roads.Add(e.A.id);
            roads.Add(e.B.id);
            roads.Add(e.ownerId);
        }

        // Robber
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

        // Players: per player [brick,lumber,wool,grain,ore,vp,knights]
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
            robberQ, robberR,
            pdata
        );
    }

    [ClientRpc]
    private void SnapshotClientRpc(
        int currentPid,
        int phaseInt,
        bool hasRolled,
        bool awaitingRobber,
        bool isGameOver,
        int winnerId,
        int[] buildingsPacked,
        int[] roadsPacked,
        int robberQ, int robberR,
        int[] playerPacked
    )
    {
        if (build == null || board == null) return;
        if (board.Nodes == null || board.Edges == null || board.Tiles == null) return;

        // Update meta/turn flags on clients
        build.currentPlayerId = currentPid;
        build.phase = (BuildController.GamePhase)phaseInt;
        build.Net_SetTurnFlags(hasRolled, awaitingRobber);
        build.Net_SetGameMeta(isGameOver, winnerId);

        // Update players
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

        // Clear buildings visuals/state
        foreach (var n in board.Nodes)
        {
            if (n == null) continue;
            n.building = null;
            var marker = n.transform.Find("Marker");
            if (marker != null) marker.gameObject.SetActive(false);
        }

        // Apply buildings
        for (int i = 0; i + 2 < buildingsPacked.Length; i += 3)
        {
            int nodeId = buildingsPacked[i];
            int ownerId = buildingsPacked[i + 1];
            bool isCity = buildingsPacked[i + 2] == 1;

            var node = FindNode(nodeId);
            if (node == null) continue;

            node.building = new Building(ownerId, isCity ? BuildingType.City : BuildingType.Settlement);
            ShowMarker(node, build.players[ownerId].playerColor, isCity ? 0.45f : 0.30f, build.markerSprite);
        }

        // Clear roads
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            e.ownerId = -1;
            TintRoad(e, Color.white);
        }

        // Apply roads
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

        // Robber
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.hasRobber = false;
        }
        if (robberQ != 999)
        {
            var tile = FindTile(robberQ, robberR);
            if (tile != null) tile.hasRobber = true;
        }
        foreach (var t in board.Tiles)
            if (t != null) t.RefreshVisual();
    }

    // ---- helpers ----
    private Intersection FindNode(int id)
    {
        foreach (var n in board.Nodes)
            if (n != null && n.id == id) return n;
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
            if (t != null && t.coord.q == q && t.coord.r == r) return t;
        return null;
    }

    private static void ShowMarker(Intersection node, Color color, float size, Sprite sprite)
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