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

    public override void OnNetworkSpawn()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (board == null || build == null)
        {
            Debug.LogError("NetworkCatanManager: Assign 'board' and 'build' in inspector (or ensure they exist in scene).", this);
            return;
        }

        // Server chooses a seed ONCE
        if (IsServer && MapSeed.Value == 0)
            MapSeed.Value = UnityEngine.Random.Range(1, 999999999);

        // Whenever seed changes (or is already set), regenerate deterministically on everyone
        MapSeed.OnValueChanged += (_, seed) =>
        {
            if (seed == 0) return;

            board.GenerateFromSeed(seed);

            // Server is authoritative: start setup only on server
            if (IsServer)
                build.BeginSetup();
        };

        // Client joining late: apply existing seed immediately
        if (MapSeed.Value != 0)
            board.GenerateFromSeed(MapSeed.Value);
    }

    // -----------------------
    // RPCs (1 int arg each)
    // -----------------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlaceSettlementServerRpc(int nodeId)
    {
        var node = GetNodeById(nodeId);
        if (node == null) return;

        build.TryPlaceSettlement(node);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPlaceRoadServerRpc(int edgeId)
    {
        var edge = GetEdgeById(edgeId);
        if (edge == null) return;

        build.TryPlaceRoad(edge);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestMoveRobberServerRpc(int tileIndex)
    {
        if (board == null || board.Tiles == null) return;
        if (tileIndex < 0 || tileIndex >= board.Tiles.Count) return;

        var tile = board.Tiles[tileIndex];
        if (tile == null) return;

        build.TryMoveRobber(tile);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestUpgradeCityServerRpc(int nodeId)
    {
        var node = GetNodeById(nodeId);
        if (node == null) return;

        build.TryUpgradeCity(node);
    }

    // -----------------------
    // Helpers
    // -----------------------
    private Intersection GetNodeById(int id)
    {
        if (board == null || board.Nodes == null) return null;

        for (int i = 0; i < board.Nodes.Count; i++)
        {
            var n = board.Nodes[i];
            if (n != null && n.id == id) return n;
        }
        return null;
    }

    private RoadEdge GetEdgeById(int id)
    {
        if (board == null || board.Edges == null) return null;

        for (int i = 0; i < board.Edges.Count; i++)
        {
            var e = board.Edges[i];
            if (e != null && e.id == id) return e;
        }
        return null;
    }
}