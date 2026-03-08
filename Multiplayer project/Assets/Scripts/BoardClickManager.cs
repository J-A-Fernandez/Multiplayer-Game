using System.Linq;
using UnityEngine;

public class BoardClickManager : MonoBehaviour
{
    public BuildController build;
    public float intersectionPickRadius = 0.35f;
    public float roadPickRadius = 0.35f;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    private void Update()
    {
        if (build == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // ? capture mode BEFORE we call any placement (mode might change during the call)
        var clickMode = build.mode;

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
    }
}