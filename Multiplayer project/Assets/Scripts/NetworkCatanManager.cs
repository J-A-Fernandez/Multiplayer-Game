using Unity.Netcode;
using UnityEngine;

public class NetworkCatanManager : NetworkBehaviour
{
    [Header("Refs (assign in inspector if you can)")]
    public BoardGenerator board;
    public BuildController build;

    [Header("Deterministic map seed (server writes, everyone reads)")]
    public NetworkVariable<int> mapSeed = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool appliedSeed = false;

    private void Awake()
    {
        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    public override void OnNetworkSpawn()
    {
        if (board == null)
        {
            Debug.LogError("NetworkCatanManager: BoardGenerator not found. Assign 'board' in inspector.");
            return;
        }
        if (build == null)
        {
            Debug.LogError("NetworkCatanManager: BuildController not found. Assign 'build' in inspector.");
            return;
        }

        mapSeed.OnValueChanged += OnSeedChanged;

        if (IsServer)
        {
            if (mapSeed.Value == 0)
            {
                int seed = UnityEngine.Random.Range(1, int.MaxValue);
                mapSeed.Value = seed;
                Debug.Log($"[NET] Host chose seed: {seed}");
            }
        }

        if (mapSeed.Value != 0 && !appliedSeed)
            ApplySeed(mapSeed.Value);
    }

    public override void OnNetworkDespawn()
    {
        mapSeed.OnValueChanged -= OnSeedChanged;
    }

    private void OnSeedChanged(int oldValue, int newValue)
    {
        if (newValue == 0) return;
        if (appliedSeed) return;

        ApplySeed(newValue);
    }

    private void ApplySeed(int seed)
    {
        appliedSeed = true;

        // IMPORTANT: your BoardGenerator must implement GenerateFromSeed(int)
        board.GenerateFromSeed(seed);

        // Make sure clients don’t run “local authority” turn logic.
        // They should request via RPC; server applies; then everyone sees results.
        if (!IsServer)
        {
            // Don’t disable the script if you rely on it for visuals,
            // but do ensure clicks call RPCs, not local build logic.
            // build.enabled = false;
        }
    }

    // ============================================================
    // RPCs called by NetworkBoardClickManager
    // ============================================================

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceSettlementServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (build == null || board == null) return;

        // Optional: enforce that only the current player's client can act:
        // int sender = (int)rpcParams.Receive.SenderClientId;
        // if (sender != build.currentPlayerId) return;

        var node = GetNodeById(nodeId);
        if (node == null) return;

        build.TryPlaceSettlement(node);

        BroadcastSnapshotClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPlaceRoadServerRpc(int edgeId, ServerRpcParams rpcParams = default)
    {
        if (build == null || board == null) return;

        var edge = GetEdgeById(edgeId);
        if (edge == null) return;

        build.TryPlaceRoad(edge);

        BroadcastSnapshotClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestUpgradeCityServerRpc(int nodeId, ServerRpcParams rpcParams = default)
    {
        if (build == null || board == null) return;

        var node = GetNodeById(nodeId);
        if (node == null) return;

        build.TryUpgradeCity(node);

        BroadcastSnapshotClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveRobberServerRpc(int tileIndex, ServerRpcParams rpcParams = default)
    {
        if (build == null || board == null) return;
        if (board.Tiles == null) return;
        if (tileIndex < 0 || tileIndex >= board.Tiles.Count) return;

        var tile = board.Tiles[tileIndex];
        if (tile == null) return;

        build.TryMoveRobber(tile);

        BroadcastSnapshotClientRpc();
    }

    // ============================================================
    // Snapshot sync (MVP): after each action, rebuild visuals on clients
    // ============================================================

    [ClientRpc]
    private void BroadcastSnapshotClientRpc(ClientRpcParams rpcParams = default)
    {
        if (build == null || board == null) return;

        // Re-show markers & road colors based on current board state
        // (This assumes your markers/road visuals are driven by build+board state.)

        // 1) Repaint roads
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            if (e.ownerId < 0) continue;
            var c = build.players[e.ownerId].playerColor;
            ForceColorRoad(e, c);
        }

        // 2) Repaint buildings/markers
        foreach (var n in board.Nodes)
        {
            if (n == null) continue;
            if (n.building == null) continue;

            int owner = n.building.ownerId;
            var c = build.players[owner].playerColor;

            float size = (n.building.type == BuildingType.City) ? 0.45f : 0.30f;
            ForceShowMarker(n, c, size);
        }

        // 3) Robber visual refresh
        foreach (var t in board.Tiles)
        {
            if (t == null) continue;
            t.RefreshVisual();
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private Intersection GetNodeById(int id)
    {
        foreach (var n in board.Nodes)
            if (n != null && n.id == id)
                return n;
        return null;
    }

    private RoadEdge GetEdgeById(int id)
    {
        foreach (var e in board.Edges)
            if (e != null && e.id == id)
                return e;
        return null;
    }

    private void ForceShowMarker(Intersection node, Color color, float size)
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
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1000;

        markerT.localScale = Vector3.one * size;
    }

    private void ForceColorRoad(RoadEdge edge, Color color)
    {
        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr == null) return;

        sr.color = color;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 500;
    }
}