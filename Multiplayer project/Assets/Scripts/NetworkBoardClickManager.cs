using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkBoardClickManager : MonoBehaviour
{
    public BuildController build;
    public NetworkCatanManager net;

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

        // prevent clicks through UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        var clickMode = build.mode;

        // Settlement
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

        // Road
        if (clickMode == BuildController.BuildMode.Road)
        {
            var hits = Physics2D.OverlapCircleAll(world, roadPickRadius);
            var edge = hits.Select(h => h.GetComponent<RoadEdge>())
                           .Where(e => e != null)
                           .OrderBy(e => Vector2.Distance(world, e.transform.position))
                           .FirstOrDefault();

            if (edge != null)
                net.RequestPlaceRoadServerRpc(edge.id);

            return;
        }

        // Robber
        if (clickMode == BuildController.BuildMode.Robber)
        {
            var hits = Physics2D.OverlapCircleAll(world, tilePickRadius);
            var tile = hits.Select(h => h.GetComponent<HexTile>())
                           .Where(t => t != null)
                           .OrderBy(t => Vector2.Distance(world, t.transform.position))
                           .FirstOrDefault();

            if (tile != null && build.board != null)
            {
                int tileIndex = build.board.Tiles.IndexOf(tile);
                if (tileIndex >= 0)
                    net.RequestMoveRobberServerRpc(tileIndex);
            }
            return;
        }

        // City
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