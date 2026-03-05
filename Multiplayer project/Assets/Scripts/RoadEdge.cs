using UnityEngine;
using System.Collections.Generic;
public class RoadEdge : MonoBehaviour
{
    public Intersection A;
    public Intersection B;

    public int ownerId = -1; //empty

    public readonly List<HexTile> adjacentTiles = new();

    public bool isOccupied => ownerId >= 0;

    public void Init(Intersection a, Intersection b)
    {
        A = a;
        B = b;
    }
}
