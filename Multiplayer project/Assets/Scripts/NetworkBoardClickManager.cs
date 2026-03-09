using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkBoardClickManager : MonoBehaviour
{
    [Header("Refs")]
    public BuildController build;
    public NetworkCatanManager net;

    [Header("Pick Radius")]
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
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsConnectedClient) return;

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);

        switch (build.mode)
        {
            case BuildController.BuildMode.Settlement:
            {
                var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
                var node = hits.Select(h => h.GetComponent<Intersection>())
                               .Where(n => n != null)
                               .OrderBy(n => Vector2.Distance(world, n.transform.position))
                               .FirstOrDefault();
                if (node != null) net.RequestPlaceSettlement(node.id);
                break;
            }

            case BuildController.BuildMode.City:
            {
                var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
                var node = hits.Select(h => h.GetComponent<Intersection>())
                               .Where(n => n != null)
                               .OrderBy(n => Vector2.Distance(world, n.transform.position))
                               .FirstOrDefault();
                if (node != null) net.RequestUpgradeCity(node.id);
                break;
            }

            case BuildController.BuildMode.Road:
            {
                var hits = Physics2D.OverlapCircleAll(world, roadPickRadius);
                var edge = hits.Select(h => h.GetComponent<RoadEdge>())
                               .Where(e => e != null && e.A != null && e.B != null)
                               .OrderBy(e => Vector2.Distance(world, e.transform.position))
                               .FirstOrDefault();
                if (edge != null) net.RequestPlaceRoad(edge.A.id, edge.B.id);
                break;
            }

            case BuildController.BuildMode.Robber:
            {
                var hits = Physics2D.OverlapCircleAll(world, tilePickRadius);
                var tile = hits.Select(h => h.GetComponent<HexTile>())
                               .Where(t => t != null)
                               .OrderBy(t => Vector2.Distance(world, t.transform.position))
                               .FirstOrDefault();
                if (tile != null) net.RequestMoveRobber(tile.coord.q, tile.coord.r);
                break;
            }
        }
    }
}