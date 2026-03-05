using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [Header("Layout")]
    public float hexSize = 1.0f;

    [Header("Prefabs")]
    public HexTile hexPrefab;
    public Intersection nodePrefab;
    public RoadEdge edgePrefab;

    [Header("Randomization")]
    public int randomSeed = 0;
    public bool useRandomSeed = true;
    public bool avoidAdjacentSixEight = true;

    public readonly List<HexTile> Tiles = new();
    public readonly List<Intersection> Nodes = new();
    public readonly List<RoadEdge> Edges = new();

    private Dictionary<AxialCoord, HexTile> tileByCoord = new();

    public void Generate()
    {
        if (hexPrefab == null) Debug.LogError("hexPrefab is NULL", this);
        if (nodePrefab == null) Debug.LogError("nodePrefab is NULL", this);
        if (edgePrefab == null) Debug.LogError("edgePrefab is NULL", this);
        ClearOld();

        var rng = useRandomSeed ? new System.Random(Environment.TickCount) : new System.Random(randomSeed);

        // 1) Create standard 19 hex coords: all axial coords within radius 2
        var coords = GenerateRadiusCoords(radius: 2);

        // 2) Prepare resource bag: (base-style distribution)
        var resources = new List<ResourceType>();
        resources.AddRange(Enumerable.Repeat(ResourceType.Lumber, 4));
        resources.AddRange(Enumerable.Repeat(ResourceType.Wool, 4));
        resources.AddRange(Enumerable.Repeat(ResourceType.Grain, 4));
        resources.AddRange(Enumerable.Repeat(ResourceType.Brick, 3));
        resources.AddRange(Enumerable.Repeat(ResourceType.Ore, 3));
        resources.Add(ResourceType.Desert); // 19th

        Shuffle(resources, rng);

        // 3) Prepare number tokens (18 numbers; desert gets 0)
        var numbers = new List<int> { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 };

        // We'll try a few times to avoid adjacent 6/8 if enabled
        List<int> finalNumbers = null;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var tryNumbers = new List<int>(numbers);
            Shuffle(tryNumbers, rng);

            if (!avoidAdjacentSixEight)
            {
                finalNumbers = tryNumbers;
                break;
            }

            if (NumbersPassAdjacencyRule(coords, resources, tryNumbers))
            {
                finalNumbers = tryNumbers;
                break;
            }
        }

        if (finalNumbers == null)
        {
            // fallback
            finalNumbers = new List<int>(numbers);
            Shuffle(finalNumbers, rng);
        }

        // 4) Spawn tiles and assign resource + number
        int numIndex = 0;
        for (int i = 0; i < coords.Count; i++)
        {
            var coord = coords[i];
            var res = resources[i];

            var tile = Instantiate(hexPrefab, coord.ToWorld(hexSize), Quaternion.identity);
            tile.coord = coord;
            tile.resource = res;

            if (res == ResourceType.Desert)
            {
                tile.number = 0;
                tile.hasRobber = true;
            }
            else
            {
                tile.number = finalNumbers[numIndex++];
                tile.hasRobber = false;
            }

            Tiles.Add(tile);
            tileByCoord[coord] = tile;
        }

        // 5) Build node+edge graph
        BuildGraph();
    }

    private List<AxialCoord> GenerateRadiusCoords(int radius)
    {
        var list = new List<AxialCoord>();
        for (int q = -radius; q <= radius; q++)
        {
            int rMin = Mathf.Max(-radius, -q - radius);
            int rMax = Mathf.Min(radius, -q + radius);
            for (int r = rMin; r <= rMax; r++)
                list.Add(new AxialCoord(q, r));
        }
        return list;
    }

    private void BuildGraph()
    {
        var nodeByPosKey = new Dictionary<Vector2Int, Intersection>();
        var edgeByNodePair = new Dictionary<(int, int), RoadEdge>();

        int nextNodeId = 0;

        foreach (var tile in Tiles)
        {
            Vector2 center = tile.transform.position;

            // Create/Get 6 corner nodes for this tile
            for (int i = 0; i < 6; i++)
            {
                Vector2 cornerPos = center + CornerOffset(i, hexSize);
                var key = Quantize(cornerPos, 1000f); // positional merge tolerance

                if (!nodeByPosKey.TryGetValue(key, out var node))
                {
                    node = Instantiate(nodePrefab, cornerPos, Quaternion.identity);
                    node.id = nextNodeId++;
                    nodeByPosKey[key] = node;
                    Nodes.Add(node);
                }

                tile.corners[i] = node;
                if (!node.adjacentTiles.Contains(tile))
                    node.adjacentTiles.Add(tile);
            }

            // Create/Get edges around this tile
            for (int i = 0; i < 6; i++)
            {
                var a = tile.corners[i];
                var b = tile.corners[(i + 1) % 6];

                int idA = a.id;
                int idB = b.id;
                if (idA > idB) (idA, idB) = (idB, idA);

                var pairKey = (idA, idB);

                if (!edgeByNodePair.TryGetValue(pairKey, out var edge))
                {
                    Vector2 mid = (a.transform.position + b.transform.position) * 0.5f;
                    float angle = Mathf.Atan2(
                        b.transform.position.y - a.transform.position.y,
                        b.transform.position.x - a.transform.position.x) * Mathf.Rad2Deg;

                    edge = Instantiate(edgePrefab, mid, Quaternion.Euler(0, 0, angle));
                    edge.Init(a, b);

                    edgeByNodePair[pairKey] = edge;
                    Edges.Add(edge);

                    if (!a.edges.Contains(edge)) a.edges.Add(edge);
                    if (!b.edges.Contains(edge)) b.edges.Add(edge);
                }

                if (!edge.adjacentTiles.Contains(tile))
                    edge.adjacentTiles.Add(tile);
            }
        }
    }

    // Pointy-top hex corners: angle = 60*i - 30 degrees
    private Vector2 CornerOffset(int i, float size)
    {
        float angleDeg = 60f * i - 30f;
        float angleRad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * size;
    }

    private Vector2Int Quantize(Vector2 p, float scale)
    {
        return new Vector2Int(
            Mathf.RoundToInt(p.x * scale),
            Mathf.RoundToInt(p.y * scale)
        );
    }

    private void ClearOld()
    {
        foreach (var t in Tiles) if (t) DestroyImmediate(t.gameObject);
        foreach (var n in Nodes) if (n) DestroyImmediate(n.gameObject);
        foreach (var e in Edges) if (e) DestroyImmediate(e.gameObject);

        Tiles.Clear();
        Nodes.Clear();
        Edges.Clear();
        tileByCoord.Clear();
    }

    private void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private bool NumbersPassAdjacencyRule(List<AxialCoord> coords, List<ResourceType> resources, List<int> numbers)
    {
        // Build a temp map: coord -> number (0 for desert)
        var temp = new Dictionary<AxialCoord, int>();
        int idx = 0;
        for (int i = 0; i < coords.Count; i++)
        {
            if (resources[i] == ResourceType.Desert) temp[coords[i]] = 0;
            else temp[coords[i]] = numbers[idx++];
        }

        foreach (var kv in temp)
        {
            int num = kv.Value;
            if (num != 6 && num != 8) continue;

            // ensure none of 6 neighbors is also 6/8
            for (int d = 0; d < 6; d++)
            {
                var n = kv.Key.Neighbor(d);
                if (temp.TryGetValue(n, out int other))
                {
                    if (other == 6 || other == 8) return false;
                }
            }
        }
        return true;
    }

    private void Start()
    {
        Debug.Log("BoardGenerator Start -> Generate()", this);
        Generate();
    }

}