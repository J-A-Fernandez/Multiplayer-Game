using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.EventSystems;

public class BuildController : MonoBehaviour
{
    public enum BuildMode { None, Settlement, Road }
    public enum GamePhase { Setup, Main }
    private enum SetupStep { PlaceSettlement, PlaceRoad }

    [Header("Refs")]
    public BoardGenerator board;

    [Header("Settlement Marker")]
    public Sprite markerSprite; // drag your circle sprite here

    [Header("Players (serialized in inspector)")]
    public Playerstate[] players = new Playerstate[4];

    [Header("Turn State")]
    public int currentPlayerId = 0;
    public GamePhase phase = GamePhase.Setup;
    public BuildMode mode = BuildMode.None;

    [Header("Rules")]
    public bool requireRollBeforeBuild = true;
    public bool enforceBuildCosts = true;

    // --- Setup state ---
    private SetupStep setupStep = SetupStep.PlaceSettlement;
    private bool setupReverse = false; // false: 0->N-1, true: N-1->0
    private Intersection lastSetupSettlement = null;

    // --- Main state ---
    private bool hasRolledThisTurn = false;

    public Playerstate CurrentPlayer => players[currentPlayerId];

    private void Awake()
    {
        // Ensure player objects exist and have ids
        if (players == null || players.Length == 0)
            players = new Playerstate[4];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new Playerstate();
            players[i].playerId = i;
        }
    }

    private void Start()
    {
        BeginSetup();
    }

    private void Update()
    {
        // Main phase hotkeys (optional)
        if (phase == GamePhase.Main)
        {
            if (Input.GetKeyDown(KeyCode.Space)) RollDiceAndDistribute();
            if (Input.GetKeyDown(KeyCode.E)) EndTurn();

            if (Input.GetKeyDown(KeyCode.S)) mode = BuildMode.Settlement;
            if (Input.GetKeyDown(KeyCode.R)) mode = BuildMode.Road;
            if (Input.GetKeyDown(KeyCode.Escape)) mode = BuildMode.None;
        }
    }

    // =========================
    // SETUP PHASE
    // =========================
    public void BeginSetup()
    {
        phase = GamePhase.Setup;
        setupStep = SetupStep.PlaceSettlement;
        setupReverse = false;

        currentPlayerId = 0;
        lastSetupSettlement = null;

        mode = BuildMode.Settlement;

        Debug.Log("=== SETUP START === P0 place a Settlement");
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        mode = BuildMode.None;

        Debug.Log("=== SETUP COMPLETE === Main game begins. Roll dice to start.");
    }

    private void AdvanceSetupAfterRoad()
    {
        // After a road, the next action is always settlement
        setupStep = SetupStep.PlaceSettlement;
        mode = BuildMode.Settlement;
        lastSetupSettlement = null;

        int n = players.Length;

        if (!setupReverse)
        {
            // Forward pass 0 -> ... -> n-1
            if (currentPlayerId == n - 1)
            {
                setupReverse = true;
                Debug.Log($"SETUP: Reverse pass begins. P{currentPlayerId} place your 2nd Settlement.");
            }
            else
            {
                currentPlayerId++;
                Debug.Log($"SETUP: Next player P{currentPlayerId} place a Settlement.");
            }
        }
        else
        {
            // Reverse pass n-1 -> ... -> 0 then end
            if (currentPlayerId == 0)
            {
                EndSetup();
            }
            else
            {
                currentPlayerId--;
                Debug.Log($"SETUP: Next player P{currentPlayerId} place your 2nd Settlement.");
            }
        }
    }


    public void TryPlaceSettlement(Intersection node)
    {
        if (node == null) return;

        // -------- SETUP --------
        if (phase == GamePhase.Setup)
        {
            if (setupStep != SetupStep.PlaceSettlement) return;

            if (node.building != null)
            {
                Debug.Log("Setup settlement blocked: occupied");
                RevealMarker(node);
                return;
            }

            if (node.HasNeighborBuilding())
            {
                Debug.Log("Setup settlement blocked: too close");
                return;
            }

            node.building = new Building(currentPlayerId, BuildingType.Settlement);
            ShowMarker(node, CurrentPlayer.playerColor);

            Debug.Log($"SETUP: P{currentPlayerId} Settlement on node {node.id}");

            // Reverse pass = second settlement => grant starting resources
            if (setupReverse)
                GrantStartingResources(node, currentPlayerId);

            lastSetupSettlement = node;
            setupStep = SetupStep.PlaceRoad;
            mode = BuildMode.Road;

            Debug.Log($"SETUP: P{currentPlayerId} now place a Road connected to that settlement.");
            return;
        }

        // -------- MAIN --------
        if (mode != BuildMode.Settlement) return;

        if (requireRollBeforeBuild && !hasRolledThisTurn)
        {
            Debug.Log("Blocked: roll dice before building this turn.");
            return;
        }

        if (node.building != null)
        {
            Debug.Log("Blocked: occupied");
            RevealMarker(node);
            return;
        }

        if (node.HasNeighborBuilding())
        {
            Debug.Log("Blocked: too close");
            return;
        }

        // Must connect to your road network in main game
        if (!NodeConnectsToPlayer(node, currentPlayerId))
        {
            Debug.Log("Blocked: settlement must connect to your roads.");
            return;
        }

        // Cost check
        if (enforceBuildCosts && !CanAffordSettlement(currentPlayerId))
        {
            Debug.Log("Blocked: Need 1 Brick + 1 Lumber + 1 Wool + 1 Grain for Settlement.");
            return;
        }

        node.building = new Building(currentPlayerId, BuildingType.Settlement);
        ShowMarker(node, CurrentPlayer.playerColor);

        if (enforceBuildCosts)
            PaySettlement(currentPlayerId);

        Debug.Log($"MAIN: P{currentPlayerId} Settlement on node {node.id}");
    }

    public void TryPlaceRoad(RoadEdge edge)
    {
        if (edge == null) return;

        // -------- SETUP --------
        if (phase == GamePhase.Setup)
        {
            if (setupStep != SetupStep.PlaceRoad) return;

            if (edge.ownerId != -1)
            {
                Debug.Log("Setup road blocked: already owned");
                return;
            }

            if (lastSetupSettlement == null)
            {
                Debug.Log("Setup road blocked: no last settlement tracked");
                return;
            }

            // Must touch the settlement just placed
            if (edge.A != lastSetupSettlement && edge.B != lastSetupSettlement)
            {
                Debug.Log("Setup road blocked: must touch last settlement");
                return;
            }

            edge.ownerId = currentPlayerId;
            ColorRoad(edge, CurrentPlayer.playerColor);

            Debug.Log($"SETUP: P{currentPlayerId} Road on {edge.A.id}-{edge.B.id}");

            AdvanceSetupAfterRoad();
            return;
        }

        // -------- MAIN --------
        if (mode != BuildMode.Road) return;

        if (requireRollBeforeBuild && !hasRolledThisTurn)
        {
            Debug.Log("Blocked: roll dice before building this turn.");
            return;
        }

        if (edge.ownerId != -1)
        {
            Debug.Log("Blocked: already owned");
            return;
        }

        // Must connect to your network (opponent building blocks pass-through)
        bool connected =
            EndpointConnects(edge.A, currentPlayerId) ||
            EndpointConnects(edge.B, currentPlayerId);

        if (!connected)
        {
            Debug.Log("Blocked: road must connect to your roads/settlements.");
            return;
        }

        // Cost check
        if (enforceBuildCosts && !CanAffordRoad(currentPlayerId))
        {
            Debug.Log("Blocked: Need 1 Brick + 1 Lumber for Road.");
            return;
        }

        edge.ownerId = currentPlayerId;
        ColorRoad(edge, CurrentPlayer.playerColor);

        if (enforceBuildCosts)
            PayRoad(currentPlayerId);

        Debug.Log($"MAIN: P{currentPlayerId} Road on {edge.A.id}-{edge.B.id}");
    }

    // =========================
    // MAIN: DICE + TURN
    // =========================
    public void RollDiceAndDistribute()
    {
        if (phase != GamePhase.Main) return;
        if (hasRolledThisTurn)
        {
            Debug.Log("Blocked: you already rolled this turn.");
            return;
        }
        int dice = Random.Range(1, 7) + Random.Range(1, 7);
        hasRolledThisTurn = true;

        Debug.Log($"Rolled: {dice}");

        if (board == null)
        {
            Debug.LogWarning("BuildController.board not assigned.");
            return;
        }

        if (dice == 7)
        {
            Debug.Log("Rolled 7 (robber not implemented yet).");
            return;
        }

        foreach (var tile in board.Tiles)
        {
            if (tile == null) continue;
            if (tile.resource == ResourceType.Desert) continue;
            if (tile.hasRobber) continue;
            if (tile.number != dice) continue;

            foreach (var node in tile.corners)
            {
                if (node == null || node.building == null) continue;

                int owner = node.building.ownerId;
                int amount = (node.building.type == BuildingType.City) ? 2 : 1;

                players[owner].AddResource(tile.resource, amount);
                Debug.Log($"P{owner} +{amount} {tile.resource} (tile {tile.coord} num {tile.number})");
            }
        }
    }

    public void EndTurn()
    {
        if (phase != GamePhase.Main) return;

        currentPlayerId = (currentPlayerId + 1) % players.Length;
        hasRolledThisTurn = false;
        mode = BuildMode.None;
        hasRolledThisTurn = false;
        Debug.Log($"END TURN -> Player {currentPlayerId}. Roll dice to start.");
    }

    // =========================
    // COSTS
    // =========================
    private bool CanAffordRoad(int pid)
    {
        var p = players[pid];
        return p.brick >= 1 && p.lumber >= 1;
    }

    private void PayRoad(int pid)
    {
        var p = players[pid];
        p.brick -= 1;
        p.lumber -= 1;
    }

    private bool CanAffordSettlement(int pid)
    {
        var p = players[pid];
        return p.brick >= 1 && p.lumber >= 1 && p.wool >= 1 && p.grain >= 1;
    }

    private void PaySettlement(int pid)
    {
        var p = players[pid];
        p.brick -= 1;
        p.lumber -= 1;
        p.wool -= 1;
        p.grain -= 1;
    }

    // =========================
    // HELPERS
    // =========================
    private void GrantStartingResources(Intersection node, int ownerId)
    {
        foreach (var tile in node.adjacentTiles)
        {
            if (tile == null) continue;
            if (tile.resource == ResourceType.Desert) continue;

            players[ownerId].AddResource(tile.resource, 1);
            Debug.Log($"SETUP START: P{ownerId} +1 {tile.resource} (adj tile {tile.coord})");
        }
    }

    private bool NodeConnectsToPlayer(Intersection node, int playerId)
    {
        if (node.building != null && node.building.ownerId == playerId) return true;

        foreach (var e in node.edges)
        {
            if (e != null && e.ownerId == playerId)
                return true;
        }

        return false;
    }

    private bool EndpointConnects(Intersection node, int playerId)
    {
        // Opponent building blocks passing through this node
        if (node.building != null && node.building.ownerId != playerId)
            return false;

        return NodeConnectsToPlayer(node, playerId);
    }

    private void RevealMarker(Intersection node)
    {
        if (node.building == null) return;
        int owner = node.building.ownerId;
        ShowMarker(node, players[owner].playerColor);
    }

    private void ShowMarker(Intersection node, Color color)
    {
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

        var sr = markerT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = markerT.gameObject.AddComponent<SpriteRenderer>();
        if (sr.sprite == null) sr.sprite = markerSprite;

        sr.enabled = true;
        sr.color = color;

        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1000;

        markerT.localScale = Vector3.one * 0.30f;
    }

    private void ColorRoad(RoadEdge edge, Color color)
    {
        var visualT = edge.transform.Find("Visual");
        var sr = visualT ? visualT.GetComponent<SpriteRenderer>() : null;
        if (sr != null)
        {
            sr.color = color;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 500;
        }
    }
}