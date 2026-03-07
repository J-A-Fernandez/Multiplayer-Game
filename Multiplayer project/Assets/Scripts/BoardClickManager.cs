using System.Linq;
using UnityEngine;

public class BoardClickManager : MonoBehaviour
{
    public BuildController build;

    [Header("Click priority")]
    public float intersectionPickRadius = 0.35f; // larger = easier to click nodes
    public float roadPickRadius = 0.20f;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    private void Update()
    {
        if (build == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector2 world = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // ? Decide what to pick based on mode
        if (build.mode == BuildController.BuildMode.Settlement)
        {
            var hitsN = Physics2D.OverlapCircleAll(world, intersectionPickRadius);
            var node = hitsN
                .Select(h => h.GetComponent<Intersection>())
                .Where(n => n != null)
                .OrderBy(n => Vector2.Distance(world, n.transform.position))
                .FirstOrDefault();

            if (node != null) build.TryPlaceSettlement(node);
            return;
        }

        if (build.mode == BuildController.BuildMode.Road)
        {
            var hitsE = Physics2D.OverlapCircleAll(world, roadPickRadius);
            var edge = hitsE
                .Select(h => h.GetComponent<RoadEdge>())
                .Where(e => e != null)
                .OrderBy(e => Vector2.Distance(world, e.transform.position))
                .FirstOrDefault();

            if (edge != null) build.TryPlaceRoad(edge);
            return;
        }

        // mode == None -> do nothing (for now)
    }

    private void OnDrawGizmosSelected()
    {
        if (!Camera.main) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(Camera.main.ScreenToWorldPoint(Input.mousePosition), intersectionPickRadius);
    }
}