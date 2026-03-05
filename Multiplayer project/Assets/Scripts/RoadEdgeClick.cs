using UnityEngine;

public class RoadEdgeClick : MonoBehaviour
{
    private BuildController build;
    private RoadEdge edge;

    private void Awake()
    {
        build = FindFirstObjectByType<BuildController>();
        edge = GetComponent<RoadEdge>();
    }

    private void OnMouseDown()
    {
        if (build != null && edge != null)
            build.TryPlaceRoad(edge);
    }
}
