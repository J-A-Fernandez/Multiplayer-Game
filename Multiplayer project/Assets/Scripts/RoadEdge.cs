using UnityEngine;
using System.Collections.Generic;

public class RoadEdge : MonoBehaviour
{
    public Intersection A;
    public Intersection B;

    public int id;
    public int ownerId = -1; // -1 = empty

    public readonly List<HexTile> adjacentTiles = new();

    [Header("Visual")]
    [SerializeField] private SpriteRenderer visualSR;

    public bool IsOccupied => ownerId >= 0;

    private void Awake()
    {
        // Find child "Visual" SpriteRenderer if not assigned
        if (visualSR == null)
        {
            var visualT = transform.Find("Visual");
            if (visualT != null) visualSR = visualT.GetComponent<SpriteRenderer>();
        }
    }

    public void Init(Intersection a, Intersection b)
    {
        A = a;
        B = b;
    }

    public void ApplyOwnerVisual(int newOwnerId, Color roadColor)
    {
        ownerId = newOwnerId;
        if (visualSR != null && ownerId >= 0)
        {
            visualSR.color = roadColor;
            visualSR.sortingLayerName = "Default";
            visualSR.sortingOrder = 500;
        }
    }

    public void ClearOwnerVisual()
    {
        ownerId = -1;
        if (visualSR != null)
        {
            visualSR.color = Color.white;
            visualSR.sortingLayerName = "Default";
            visualSR.sortingOrder = 500;
        }
    }
}