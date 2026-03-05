using UnityEngine;

public class InsertionClick : MonoBehaviour
{
    BuildController build;
    Intersection node;

    void Awake()
    {
        build = FindFirstObjectByType<BuildController>();
        node = GetComponent<Intersection>();
    }

    void OnMouseDown()
    {
        Debug.Log($"CLICK node {(node != null ? node.id : -1)}  buildNull={(build == null)}");
        build?.TryPlaceSettlement(node);
    }
}
