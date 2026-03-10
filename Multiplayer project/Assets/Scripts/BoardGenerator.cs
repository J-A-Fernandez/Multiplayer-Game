using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoardGenerator : MonoBehaviour
{
    [Header("Layout")]
    public float hexSize = .5f;
    public int boardRadius = 2;

    [Header("Prefabs")]
    public HexTile hexPrefab;
    public Intersection nodePrefab;
    public RoadEdge edgePrefab;

    [Header("Seed / Determinism")]
    public bool useSeed = true;
    public int seed = 12345;

    [Header("Randomization")]
    public int randomSeed = 0;
    public bool useRandomSeed = true;
    public bool avoidAdjacentSixEight = true;

    public readonly List<HexTile> Tiles = new();
    public readonly List<Intersection> Nodes = new();
    public readonly List<RoadEdge> Edges = new();

    private Dictionary<AxialCoord, HexTile> tileByCoord = new();

    [ContextMenu("Generate Board")]
    public void Generate()
    {
        if (hexPrefab == null || nodePrefab == null || edgePrefab == null)
        {
            Debug.LogError($"Missing prefab refs! hex={hexPrefab} node={nodePrefab} edge={edgePrefab}", this);
            return;
        }

        ClearOld();

        // ✅ Deterministic RNG when useSeed is true
        var rng =
            useSeed ? new System.Random(seed) :
            useRandomSeed ? new System.Random(Environment.TickCount) :
            new System.Random(randomSeed);

        // 1) Coords for any radius
        var coords = GenerateRadiusCoords(radius: boardRadius);
        int tileCount = coords.Count;

        // 2) Resources
        var resources = GenerateResources(tileCount, rng);

        // 3) Number tokens
        var numbersBase = GenerateNumberTokens(tileCount, resources, rng);

        // 4) Shuffle numbers + avoid adjacent 6/8
        List<int> finalNumbers = null;
        for (int attempt = 0; attempt < 300; attempt++)
        {
            var tryNumbers = new List<int>(numbersBase);
            Shuffle(tryNumbers, rng);

            if (!avoidAdjacentSixEight || NumbersPassAdjacencyRule(coords, resources, tryNumbers))
            {
                finalNumbers = tryNumbers;
                break;
            }
        }
        if (finalNumbers == null) finalNumbers = numbersBase;

        // 5) Spawn tiles
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

            tile.RefreshVisual();

            Tiles.Add(tile);
            tileByCoord[coord] = tile;
        }

        // 6) Build graph (nodes + edges)
        BuildGraph();

        Debug.Log($"Spawned Tiles={Tiles.Count} Nodes={Nodes.Count} Edges={Edges.Count} (radius={boardRadius}) seed={seed}", this);
    }

    // ✅ Multiplayer: call this from NetworkCatanManager
    public void GenerateFromSeed(int s)
    {
        useSeed = true;
        seed = s;

        // IMPORTANT: disable tick-based random
        useRandomSeed = false;

        Generate();
    }

    private List<ResourceType> GenerateResources(int tileCount, System.Random rng)
    {
        int desertCount = Mathf.Max(1, Mathf.RoundToInt(tileCount / 19f));
        int nonDesert = tileCount - desertCount;

        float rLumber = 4f / 18f;
        float rWool = 4f / 18f;
        float rGrain = 4f / 18f;
        float rBrick = 3f / 18f;
        float rOre = 3f / 18f;

        int lumber = Mathf.RoundToInt(nonDesert * rLumber);
        int wool = Mathf.RoundToInt(nonDesert * rWool);
        int grain = Mathf.RoundToInt(nonDesert * rGrain);
        int brick = Mathf.RoundToInt(nonDesert * rBrick);
        int ore = Mathf.RoundToInt(nonDesert * rOre);

        int sum = lumber + wool + grain + brick + ore;
        while (sum < nonDesert) { lumber++; sum++; }
        while (sum > nonDesert && lumber > 0) { lumber--; sum--; }

        var list = new List<ResourceType>(tileCount);
        list.AddRange(Enumerable.Repeat(ResourceType.Desert, desertCount));
        list.AddRange(Enumerable.Repeat(ResourceType.Lumber, lumber));
        list.AddRange(Enumerable.Repeat(ResourceType.Wool, wool));
        list.AddRange(Enumerable.Repeat(ResourceType.Grain, grain));
        list.AddRange(Enumerable.Repeat(ResourceType.Brick, brick));
        list.AddRange(Enumerable.Repeat(ResourceType.Ore, ore));

        while (list.Count < tileCount) list.Add(ResourceType.Lumber);
        if (list.Count > tileCount) list.RemoveRange(tileCount, list.Count - tileCount);

        Shuffle(list, rng);
        return list;
    }

    private List<int> GenerateNumberTokens(int tileCount, List<ResourceType> resources, System.Random rng)
    {
        int nonDesert = resources.Count(r => r != ResourceType.Desert);

        var baseCounts = new Dictionary<int, int>
        {
            {2,1},{3,2},{4,2},{5,2},{6,2},{8,2},{9,2},{10,2},{11,2},{12,1}
        };

        float scale = nonDesert / 18f;

        var counts = baseCounts.ToDictionary(
            kv => kv.Key,
            kv => Mathf.Max(0, Mathf.RoundToInt(kv.Value * scale))
        );

        int total = counts.Values.Sum();
        int[] adjustOrder = { 6, 8, 5, 9, 4, 10, 3, 11, 2, 12 };

        while (total < nonDesert)
        {
            foreach (var n in adjustOrder)
            {
                if (total >= nonDesert) break;
                counts[n]++; total++;
            }
        }

        while (total > nonDesert)
        {
            foreach (var n in adjustOrder)
            {
                if (total <= nonDesert) break;
                if (counts[n] > 0) { counts[n]--; total--; }
            }
        }

        var tokens = new List<int>(nonDesert);
        foreach (var kv in counts)
            tokens.AddRange(Enumerable.Repeat(kv.Key, kv.Value));

        while (tokens.Count < nonDesert) tokens.Add(6);
        if (tokens.Count > nonDesert) tokens.RemoveRange(nonDesert, tokens.Count - nonDesert);

        Shuffle(tokens, rng);
        return tokens;
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
        int nextEdgeId = 0; // ✅ NEW

        foreach (var tile in Tiles)
        {
            Vector2 center = tile.transform.position;

            // nodes
            for (int i = 0; i < 6; i++)
            {
                Vector2 cornerPos = center + CornerOffset(i, hexSize);
                var key = Quantize(cornerPos, 1000f);

                if (!nodeByPosKey.TryGetValue(key, out var node))
                {
                    node = Instantiate(nodePrefab, cornerPos, Quaternion.identity);
                    node.transform.localScale = Vector3.one;
                    node.transform.rotation = Quaternion.identity;

                    node.id = nextNodeId++;

                    // reset state
                    node.building = null;
                    node.adjacentTiles.Clear();
                    node.edges.Clear();

                    nodeByPosKey[key] = node;
                    Nodes.Add(node);
                }

                tile.corners[i] = node;
                if (!node.adjacentTiles.Contains(tile))
                    node.adjacentTiles.Add(tile);
            }

            // edges
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
                        b.transform.position.x - a.transform.position.x
                    ) * Mathf.Rad2Deg;

                    edge = Instantiate(edgePrefab, mid, Quaternion.Euler(0, 0, angle));

                    edge.id = nextEdgeId++;  // ✅ CRITICAL
                    edge.ownerId = -1;       // ✅ reset

                    edge.transform.localScale = Vector3.one;

                    var visual = edge.transform.Find("Visual");
                    if (visual != null)
                    {
                        visual.localPosition = Vector3.zero;
                        visual.localRotation = Quaternion.identity;
                    }

                    edge.adjacentTiles.Clear();
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
        // ✅ Disabled for multiplayer scene.
        // Singleplayer scene can call Generate() manually or re-enable this.
        // Generate();
    }
}