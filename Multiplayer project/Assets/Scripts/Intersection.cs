using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class Intersection : MonoBehaviour
{
    public int id;

    public readonly List<HexTile> adjacentTiles = new();
    public readonly List<RoadEdge> edges = new();

    public Building building;

    public bool IsOccupied => building != null;

    public IEnumerable<Intersection> Neighbors()
    {
        // HashSet prevents duplicates if edges list ever contains duplicates
        var seen = new HashSet<Intersection>();

        foreach (var e in edges)
        {
            if (e == null) continue;

            Intersection other = null;
            if (e.A == this) other = e.B;
            else if (e.B == this) other = e.A;

            if (other == null) continue;

            if (seen.Add(other))
                yield return other;
        }
    }

    public bool HasNeighborBuilding()
    {
        return Neighbors().Any(n => n != null && n.building != null);
    }
}