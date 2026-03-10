using UnityEngine;
using System.Collections.Generic;

public class RoadEdge : MonoBehaviour
{
    [Header("Endpoints")]
    public Intersection A;
    public Intersection B;

    [Header("Identity / Ownership")]
    public int id = -1;          // assigned in BoardGenerator.BuildGraph()
    public int ownerId = -1;     // -1 means empty

    [Header("Adjacency")]
    public readonly List<HexTile> adjacentTiles = new();

    public bool IsOccupied => ownerId >= 0;

    public void Init(Intersection a, Intersection b)
    {
        A = a;
        B = b;
    }

    /// <summary>
    /// Call this when generating a new board to ensure no stale state remains.
    /// </summary>
    public void ResetState()
    {
        ownerId = -1;
        adjacentTiles.Clear();
        // keep id as-is (it will be overwritten by generator when instantiating)
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (A == null || B == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(A.transform.position, B.transform.position);
    }
#endif
}