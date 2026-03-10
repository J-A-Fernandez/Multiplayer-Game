using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkBoardClickManager : MonoBehaviour
{
    [Header("Refs")]
    public BuildController build;           // local build controller (for current mode, radii, etc.)
    public NetworkCatanManager net;         // sends requests to host

    [Header("Pick Radii")]
    public float intersectionPickRadius = 0.35f;
    public float roadPickRadius = 0.35f;
    public float tilePickRadius = 0.60f;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();
        if (net == null) net = FindFirstObjectByType<NetworkCatanManager>();
    }

    private void Update()
    {
        if (build == null || net == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Block clicks through UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        var clickMode = build.mode;

        // =========================
        // Settlement -> send nodeId
        // =========================
        if (clickMode == BuildController.BuildMode.Settlement)
        {
            var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
            var node = hits.Select(h => h.GetComponent<Intersection>())
                           .Where(n => n != null)
                           .OrderBy(n => Vector2.Distance(world, n.transform.position))
                           .FirstOrDefault();

            if (node != null)
                net.RequestPlaceSettlementServerRpc(node.id);

            return;
        }

        // =========================
        // Road -> send BOTH endpoint ids (A.id, B.id)
        // (This fixes your "missing nodeBId" error)
        // =========================
        if (clickMode == BuildController.BuildMode.Road)
        {
            var hits = Physics2D.OverlapCircleAll(world, roadPickRadius);
            var edge = hits.Select(h => h.GetComponent<RoadEdge>())
                           .Where(e => e != null)
                           .OrderBy(e => Vector2.Distance(world, e.transform.position))
                           .FirstOrDefault();

            if (edge != null && edge.A != null && edge.B != null)
            {
                int aId = edge.A.id;
                int bId = edge.B.id;
                net.RequestPlaceRoadServerRpc(aId, bId);
            }

            return;
        }

        // =========================
        // Robber -> send tile axial coords (q,r)
        // (This fixes your "missing r" error)
        // =========================
        if (clickMode == BuildController.BuildMode.Robber)
        {
            var hits = Physics2D.OverlapCircleAll(world, tilePickRadius);
            var tile = hits.Select(h => h.GetComponent<HexTile>())
                           .Where(t => t != null)
                           .OrderBy(t => Vector2.Distance(world, t.transform.position))
                           .FirstOrDefault();

            if (tile != null)
            {
                // IMPORTANT:
                // This assumes HexTile has tile.coord.q and tile.coord.r
                // If your AxialCoord fields are named differently, adjust here.
                net.RequestMoveRobberServerRpc(tile.coord.q, tile.coord.r);
            }

            return;
        }

        // City upgrade (optional, if you want networked clicking for it too)
        if (clickMode == BuildController.BuildMode.City)
        {
            var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
            var node = hits.Select(h => h.GetComponent<Intersection>())
                           .Where(n => n != null)
                           .OrderBy(n => Vector2.Distance(world, n.transform.position))
                           .FirstOrDefault();

            if (node != null)
                net.RequestUpgradeCityServerRpc(node.id);

            return;
        }
    }
}