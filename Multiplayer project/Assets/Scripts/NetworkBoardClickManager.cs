using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Netcode;

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

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // only allow clicking on your turn (quick usability)
        int localPid = (int)NetworkManager.Singleton.LocalClientId;
        if (localPid != build.currentPlayerId) return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // robber overrides everything
        if (build.AwaitingRobberMove)
        {
            var hits = Physics2D.OverlapCircleAll(world, tilePickRadius);
            var tile = hits.Select(h => h.GetComponent<HexTile>())
                .Where(t => t != null)
                .OrderBy(t => Vector2.Distance(world, t.transform.position))
                .FirstOrDefault();

            if (tile != null)
                net.RequestMoveRobberServerRpc(tile.coord.q, tile.coord.r);

            return;
        }

        if (build.mode == BuildController.BuildMode.Settlement)
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

        if (build.mode == BuildController.BuildMode.City)
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

        if (build.mode == BuildController.BuildMode.Road)
        {
            var hits = Physics2D.OverlapCircleAll(world, roadPickRadius);
            var edge = hits.Select(h => h.GetComponent<RoadEdge>())
                .Where(e => e != null)
                .OrderBy(e => Vector2.Distance(world, e.transform.position))
                .FirstOrDefault();

            if (edge != null && edge.A != null && edge.B != null)
                net.RequestPlaceRoadServerRpc(edge.A.id, edge.B.id);

            return;
        }
    }
}