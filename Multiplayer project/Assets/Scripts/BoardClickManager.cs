using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public class BoardClickManager : MonoBehaviour
{
    public BuildController build;
    public float intersectionPickRadius = 0.35f;
    public float roadPickRadius = 0.35f;
    public float tilePickRadius = 0.60f;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    private void Update()
    {
        if (build == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // prevent clicks through UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        var clickMode = build.mode;

        // --- Settlement placement ---
        if (clickMode == BuildController.BuildMode.Settlement)
        {
            var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
            var node = hits.Select(h => h.GetComponent<Intersection>())
                           .Where(n => n != null)
                           .OrderBy(n => Vector2.Distance(world, n.transform.position))
                           .FirstOrDefault();

            if (node != null) build.TryPlaceSettlement(node);
            return;
        }

        // --- Road placement ---
        if (clickMode == BuildController.BuildMode.Road)
        {
            var hits = Physics2D.OverlapCircleAll(world, roadPickRadius);
            var edge = hits.Select(h => h.GetComponent<RoadEdge>())
                           .Where(e => e != null)
                           .OrderBy(e => Vector2.Distance(world, e.transform.position))
                           .FirstOrDefault();

            if (edge != null) build.TryPlaceRoad(edge);
            return;
        }

        // --- City upgrade ---
        if (clickMode == BuildController.BuildMode.City)
        {
            var hits = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
            var node = hits.Select(h => h.GetComponent<Intersection>())
                           .Where(n => n != null)
                           .OrderBy(n => Vector2.Distance(world, n.transform.position))
                           .FirstOrDefault();

            if (node != null) build.TryUpgradeCity(node);
            return;
        }

        // --- Robber move ---
        if (clickMode == BuildController.BuildMode.Robber)
        {
            var hits = Physics2D.OverlapCircleAll(world, tilePickRadius);
            var tile = hits.Select(h => h.GetComponent<HexTile>())
                           .Where(t => t != null)
                           .OrderBy(t => Vector2.Distance(world, t.transform.position))
                           .FirstOrDefault();

            if (tile != null) build.TryMoveRobber(tile);
            return;
        }
    }
}