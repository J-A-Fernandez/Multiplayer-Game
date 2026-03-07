using UnityEngine;
using UnityEngine.Playables;

public class BuildController : MonoBehaviour
{

    public enum BuildMode { None, Settlement, Road }
    public Sprite markerSprite;

    [Header("Refs")]
    public BoardGenerator board; // drag BoardRoot (with BoardGenerator) here

    [Header("Players")]
    public Playerstate[] players = new Playerstate[4];

    [Header("Turn")]
    public int currentPlayerId = 0;

    [Header("Build Mode")]
    public BuildMode mode = BuildMode.None;

    private void Awake()
    {
        // Auto-assign ids if not set
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new Playerstate();
            players[i].playerId = i;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            mode = BuildMode.None;
            Debug.Log("MODE=None");
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            mode = BuildMode.Settlement;
            Debug.Log("S -> MODE=Settlement");
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            mode = BuildMode.Road;
            Debug.Log("R -> MODE=Road");
        }

        if (Input.GetKeyDown(KeyCode.Tab))
            NextPlayer();

        if (Input.GetKeyDown(KeyCode.Space))
            RollDiceAndDistribute();
    }

    public Playerstate CurrentPlayer => players[currentPlayerId];

    public void NextPlayer()
    {
        currentPlayerId = (currentPlayerId + 1) % players.Length;
        mode = BuildMode.None;
        Debug.Log($"Current Player = {currentPlayerId}");
    }

    // These will be called by click scripts in Step B
    public void TryPlaceSettlement(Intersection node)
    {
        if (mode != BuildMode.Settlement) return;
        if (node == null) return;

        // If occupied, reveal marker for owner and stop
        if (node.building != null)
        {
            ShowMarker(node, players[node.building.ownerId].playerColor);
            Debug.Log("Blocked: already occupied (revealing marker)");
            return;
        }

        // Distance rule
        if (node.HasNeighborBuilding())
        {
            Debug.Log("Blocked: neighbor building too close");
            return;
        }

        // Place
        node.building = new Building(currentPlayerId, BuildingType.Settlement);
        Debug.Log($"PLACED settlement on node {node.id}");

        ShowMarker(node, CurrentPlayer.playerColor);
    }

    private void ShowMarker(Intersection node, Color color)
    {
        // Find or create Marker child
        var markerT = node.transform.Find("Marker");
        if (markerT == null)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(node.transform, false);
            markerT = go.transform;
        }

        markerT.gameObject.SetActive(true);
        markerT.localPosition = Vector3.zero;
        markerT.localRotation = Quaternion.identity;

        // Renderer
        var sr = markerT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = markerT.gameObject.AddComponent<SpriteRenderer>();

        // Ensure sprite
        if (sr.sprite == null) sr.sprite = markerSprite; // assign Circle sprite in inspector

        sr.enabled = true;
        sr.color = color;

        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1000;

        float parentScale = node.transform.lossyScale.x;
        if (parentScale <= 0.0001f) parentScale = .15f;

        float desiredWorldSize = 0.30f; // adjust 0.25–0.40 to taste
        markerT.localScale = Vector3.one * (desiredWorldSize / parentScale);
    }

    public void TryPlaceRoad(RoadEdge edge)
    {
        if (mode != BuildMode.Road) return;
        if (edge == null) return;
        if (edge.ownerId != -1) { Debug.Log("Road blocked: already owned"); return; }

        edge.ownerId = currentPlayerId;

        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr != null)
        {
            sr.color = CurrentPlayer.playerColor;
            sr.sortingOrder = 200;
        }

        Debug.Log($"Placed road on edge {edge.A.id}-{edge.B.id}");
    }
    public void RollDiceAndDistribute()
    {
        int dice = UnityEngine.Random.Range(1, 7) + UnityEngine.Random.Range(1, 7);
        Debug.Log($"Rolled: {dice}");

        if (board == null)
        {
            Debug.LogWarning("BuildController.board not assigned");
            return;
        }

        foreach (var tile in board.Tiles)
        {
            if (tile == null) continue;
            if (tile.resource == ResourceType.Desert) continue;
            if (tile.hasRobber) continue;
            if (tile.number != dice) continue;

            // Pay each corner with a building
            foreach (var node in tile.corners)
            {
                if (node == null || node.building == null) continue;

                int owner = node.building.ownerId;
                int amount = (node.building.type == BuildingType.City) ? 2 : 1;

                players[owner].AddResource(tile.resource, amount);

                Debug.Log($"P{owner} +{amount} {tile.resource} (tile {tile.coord} num {tile.number})");
            }
        }

        // Print player inventories after payout
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            Debug.Log($"P{i} inv: B={p.brick} L={p.lumber} W={p.wool} G={p.grain} O={p.ore}");
        }
    }
}
