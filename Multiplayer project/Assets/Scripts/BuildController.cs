using UnityEngine;
using UnityEngine.Playables;

public class BuildController : MonoBehaviour
{

    public enum BuildMode { None, Settlement, Road }

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

        // If occupied, show marker for the owner (so you can SEE it) then stop
        if (node.building != null)
        {
            Debug.Log("Blocked: already occupied");
            ShowMarker(node, players[node.building.ownerId].playerColor);
            return;
        }

        if (node.HasNeighborBuilding())
        {
            Debug.Log("Blocked: neighbor building too close");
            return;
        }

        node.building = new Building(currentPlayerId, BuildingType.Settlement);
        Debug.Log($"PLACED settlement on node {node.id} for P{currentPlayerId}");

        ShowMarker(node, CurrentPlayer.playerColor);
    }

    private void ShowMarker(Intersection node, Color color)
    {
        var marker = node.transform.Find("Marker");
        var sr = marker ? marker.GetComponent<SpriteRenderer>() : null;

        if (sr == null)
        {
            Debug.LogWarning("Marker not found. Add a child named 'Marker' with a SpriteRenderer to the Intersection prefab.");
            return;
        }

        sr.enabled = true;
        sr.color = color;
        sr.sortingOrder = 200;                 // always on top
        marker.localScale = Vector3.one * 0.25f; // make it visible
    }

    public void TryPlaceRoad(RoadEdge edge)
    {
        if (mode != BuildMode.Road) return;
        if (edge == null) return;

        if (edge.ownerId != -1) return;

        // Super simple for now: allow any road placement (we'll add connection rules next)
        edge.ownerId = currentPlayerId;

        // Tint the road visual (child named "Visual" is common)
        var visual = edge.transform.Find("Visual");
        var sr = visual ? visual.GetComponent<SpriteRenderer>() : edge.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = CurrentPlayer.playerColor;

        Debug.Log($"Placed road for P{currentPlayerId} on edge {edge.A.id}-{edge.B.id}");
    }

}
