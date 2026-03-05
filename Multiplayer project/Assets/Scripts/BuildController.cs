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
        // Quick test hotkeys
        if (Input.GetKeyDown(KeyCode.Escape)) mode = BuildMode.None;
        if (Input.GetKeyDown(KeyCode.S)) mode = BuildMode.Settlement;
        if (Input.GetKeyDown(KeyCode.R)) mode = BuildMode.Road;

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

        // Basic rule: empty + no neighbor buildings
        if (node.building != null) return;
        if (node.HasNeighborBuilding()) return;

        node.building = new Building(currentPlayerId, BuildingType.Setlement);

        // Simple visual feedback: tint the node sprite to player color (if it has one)
        var sr = node.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = CurrentPlayer.playerColor;

        Debug.Log($"Placed settlement for P{currentPlayerId} on node {node.id}");
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
