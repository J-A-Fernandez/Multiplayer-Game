using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class Intersection : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public int id;

    public readonly List <HexTile> adjacentTiles = new();
    public readonly List<RoadEdge> edges = new();

    public Building building;

    public bool IsOccupied => building != null;

    public IEnumerable<Intersection> Neighbors()
    {
        foreach (var e in edges)
            yield return e.A == this ? e.B : e.A;
    }

    public bool HasNeighborBuilding()
        {
            return Neighbors().Any(n => n.building != null);
        }
    
}
