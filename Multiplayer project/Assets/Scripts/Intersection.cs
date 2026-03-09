using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class Intersection : MonoBehaviour
{
    public int id;

    public readonly List<HexTile> adjacentTiles = new();
    public readonly List<RoadEdge> edges = new();

    public Building building;

    public PortType port = PortType.None;

    public bool IsOccupied => building != null;

    public IEnumerable<Intersection> Neighbors()
    {
        foreach (var e in edges)
        {
            if (e == null) continue;
            if (e.A == this) yield return e.B;
            else if (e.B == this) yield return e.A;
        }
    }

    public bool HasNeighborBuilding()
    {
        return Neighbors().Any(n => n != null && n.building != null);
    }
}