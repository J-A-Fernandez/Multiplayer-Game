using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BuildController : MonoBehaviour
{
    public enum BuildMode { None, Settlement, Road, Robber, City }
    public enum GamePhase { Setup, Main }
    private enum SetupStep { PlaceSettlement, PlaceRoad }

    [Header("Refs")]
    public BoardGenerator board;

    [Header("Marker Sprite (settlement/city)")]
    public Sprite markerSprite;

    [Header("Players")]
    public PlayerState[] players = new PlayerState[2]; // set size in Inspector (2 for now)

    [Header("Turn State")]
    public int currentPlayerId = 0;
    public GamePhase phase = GamePhase.Setup;
    public BuildMode mode = BuildMode.None;

    [Header("Rules")]
    public bool requireRollBeforeBuild = true;
    public bool enforceBuildCosts = true;

    [Header("Win Condition")]
    public int targetVictoryPoints = 10;

    // Setup state
    private SetupStep setupStep = SetupStep.PlaceSettlement;
    private bool setupReverse = false;
    private Intersection lastSetupSettlement = null;

    // Main state
    [SerializeField] private bool hasRolledThisTurn = false;
    [SerializeField] private bool awaitingRobberMove = false;

    // Game over
    [SerializeField] private bool gameOver = false;
    [SerializeField] private int winnerId = -1;

    // ===== Player-to-player trade state (optional) =====
    private TradeOffer activeOffer = TradeOffer.None;
    private int nextOfferId = 1;

    public PlayerState CurrentPlayer => players[currentPlayerId];
    public bool HasRolledThisTurn => hasRolledThisTurn;
    public bool AwaitingRobberMove => awaitingRobberMove;
    public bool GameOver => gameOver;
    public int WinnerId => winnerId;
    public TradeOffer ActiveOffer => activeOffer;

    private void Awake()
    {
        if (players == null || players.Length == 0)
            players = new PlayerState[2];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerState();
            players[i].playerId = i;
            if (players[i].playerColor.a <= 0.01f) players[i].playerColor = Random.ColorHSV();
        }

        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
    }

    private void Start()
    {
        BeginSetup();
    }

    // =========================
    // NETWORK HELPERS (ONLY ONCE)
    // =========================
    public void Net_SetTurnFlags(bool hasRolled, bool awaitingRobber)
    {
        hasRolledThisTurn = hasRolled;
        awaitingRobberMove = awaitingRobber;
    }

    public void Net_SetGameMeta(bool isGameOver, int winId)
    {
        gameOver = isGameOver;
        winnerId = winId;
    }

    // =========================
    // SETUP
    // =========================
    public void BeginSetup()
    {
        gameOver = false;
        winnerId = -1;

        for (int i = 0; i < players.Length; i++)
        {
            players[i].brick = players[i].lumber = players[i].wool = players[i].grain = players[i].ore = 0;
            players[i].victoryPoints = 0;
            players[i].knightsPlayed = 0;
        }

        phase = GamePhase.Setup;
        setupStep = SetupStep.PlaceSettlement;
        setupReverse = false;

        currentPlayerId = 0;
        lastSetupSettlement = null;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;

        activeOffer = TradeOffer.None;
        nextOfferId = 1;

        mode = BuildMode.Settlement;

        Debug.Log("SETUP: Player 0 place a settlement.");
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        mode = BuildMode.None;

        Debug.Log("SETUP COMPLETE: Main game starts. Roll dice.");
    }

    private void AdvanceSetupAfterRoad()
    {
        setupStep = SetupStep.PlaceSettlement;
        mode = BuildMode.Settlement;
        lastSetupSettlement = null;

        int n = players.Length;

        if (!setupReverse)
        {
            if (currentPlayerId == n - 1)
            {
                setupReverse = true;
                Debug.Log($"SETUP: Reverse pass begins. P{currentPlayerId} place settlement.");
            }
            else
            {
                currentPlayerId++;
                Debug.Log($"SETUP: Next player P{currentPlayerId} place settlement.");
            }
        }
        else
        {
            if (currentPlayerId == 0) EndSetup();
            else
            {
                currentPlayerId--;
                Debug.Log($"SETUP: Next player P{currentPlayerId} place settlement.");
            }
        }
    }

    // =========================
    // TURN / ROLL
    // =========================
    public void RollDiceAndDistribute()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) { Debug.Log("Move robber first."); return; }
        if (hasRolledThisTurn) { Debug.Log("Already rolled."); return; }

        int dice = Random.Range(1, 7) + Random.Range(1, 7);
        hasRolledThisTurn = true;
        Debug.Log($"Rolled {dice}");

        if (board == null) return;

        if (dice == 7)
        {
            HandleDiscardForSeven();
            awaitingRobberMove = true;
            mode = BuildMode.Robber;
            Debug.Log("Rolled 7: discard + move robber.");
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
            }
        }
    }

    public void EndTurn()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) { Debug.Log("Move robber first."); return; }

        // optional: cancel any active offer at end turn
        if (activeOffer.active) activeOffer = TradeOffer.None;

        currentPlayerId = (currentPlayerId + 1) % players.Length;
        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        mode = BuildMode.None;

        Debug.Log($"Turn -> Player {currentPlayerId}. Roll dice.");
    }

    // =========================
    // PLACEMENT
    // =========================
    public void TryPlaceSettlement(Intersection node)
    {
        if (gameOver) return;
        if (node == null) return;

        if (phase == GamePhase.Setup)
        {
            if (setupStep != SetupStep.PlaceSettlement) return;

            if (node.building != null) { RevealMarker(node); return; }
            if (node.HasNeighborBuilding()) return;

            node.building = new Building(currentPlayerId, BuildingType.Settlement);
            ShowMarker(node, players[currentPlayerId].playerColor, 0.30f);
            AddVP(currentPlayerId, 1);

            // On reverse pass, grant starting resources (classic)
            if (setupReverse) GrantStartingResources(node, currentPlayerId);

            lastSetupSettlement = node;
            setupStep = SetupStep.PlaceRoad;
            mode = BuildMode.Road;

            Debug.Log($"SETUP: P{currentPlayerId} placed settlement. Now place a road touching it.");
            return;
        }

        if (mode != BuildMode.Settlement) return;
        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;

        if (node.building != null) { RevealMarker(node); return; }
        if (node.HasNeighborBuilding()) return;
        if (!NodeConnectsToPlayer(node, currentPlayerId)) return;

        if (enforceBuildCosts && !CanAffordSettlement(currentPlayerId)) return;

        node.building = new Building(currentPlayerId, BuildingType.Settlement);
        ShowMarker(node, players[currentPlayerId].playerColor, 0.30f);

        if (enforceBuildCosts) PaySettlement(currentPlayerId);
        AddVP(currentPlayerId, 1);
    }

    public void TryPlaceRoad(RoadEdge edge)
    {
        if (gameOver) return;
        if (edge == null) return;

        if (phase == GamePhase.Setup)
        {
            if (setupStep != SetupStep.PlaceRoad) return;
            if (edge.ownerId != -1) return;
            if (lastSetupSettlement == null) return;
            if (edge.A != lastSetupSettlement && edge.B != lastSetupSettlement) return;

            edge.ownerId = currentPlayerId;
            ColorRoad(edge, players[currentPlayerId].playerColor);

            Debug.Log($"SETUP: P{currentPlayerId} placed road.");
            AdvanceSetupAfterRoad();
            return;
        }

        if (mode != BuildMode.Road) return;
        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;
        if (edge.ownerId != -1) return;

        bool connected = EndpointConnects(edge.A, currentPlayerId) || EndpointConnects(edge.B, currentPlayerId);
        if (!connected) return;

        if (enforceBuildCosts && !CanAffordRoad(currentPlayerId)) return;

        edge.ownerId = currentPlayerId;
        ColorRoad(edge, players[currentPlayerId].playerColor);

        if (enforceBuildCosts) PayRoad(currentPlayerId);
    }

    public void TryUpgradeCity(Intersection node)
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (mode != BuildMode.City) return;
        if (node == null) return;

        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;

        if (node.building == null) return;
        if (node.building.ownerId != currentPlayerId) return;
        if (node.building.type == BuildingType.City) return;

        if (enforceBuildCosts && !CanAffordCity(currentPlayerId)) return;

        node.building.type = BuildingType.City;
        ShowMarker(node, players[currentPlayerId].playerColor, 0.45f);

        if (enforceBuildCosts) PayCity(currentPlayerId);

        // Settlement was already 1 VP; City is 2 total => +1 more
        AddVP(currentPlayerId, 1);
    }

    // =========================
    // ROBBER
    // =========================
    public void TryMoveRobber(HexTile target)
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (!awaitingRobberMove) return;
        if (target == null) return;

        // clear existing robber
        if (board != null)
        {
            foreach (var t in board.Tiles)
            {
                if (t != null && t.hasRobber)
                {
                    t.hasRobber = false;
                    t.RefreshVisual();
                    break;
                }
            }
        }

        target.hasRobber = true;
        target.RefreshVisual();

        StealOneFromRobberTile(target);

        awaitingRobberMove = false;
        mode = BuildMode.None;
        Debug.Log("Robber moved.");
    }

    private void HandleDiscardForSeven()
    {
        for (int pid = 0; pid < players.Length; pid++)
        {
            int total = players[pid].TotalResources();
            if (total <= 7) continue;

            int discardCount = total / 2;
            for (int i = 0; i < discardCount; i++)
                TryRemoveRandomResource(pid, out _);

            Debug.Log($"P{pid} discarded {discardCount} cards.");
        }
    }

    private void StealOneFromRobberTile(HexTile tile)
    {
        if (tile == null) return;

        var victims = new HashSet<int>();
        foreach (var node in tile.corners)
        {
            if (node == null || node.building == null) continue;
            int owner = node.building.ownerId;
            if (owner != currentPlayerId) victims.Add(owner);
        }

        var eligible = victims.Where(v => players[v].TotalResources() > 0).ToList();
        if (eligible.Count == 0) return;

        int victimId = eligible[Random.Range(0, eligible.Count)];
        if (TryRemoveRandomResource(victimId, out var stolen))
        {
            CurrentPlayer.AddResource(stolen, 1);
            Debug.Log($"P{currentPlayerId} stole 1 {stolen} from P{victimId}");
        }
    }

    private bool TryRemoveRandomResource(int pid, out ResourceType removed)
    {
        var p = players[pid];
        int total = p.brick + p.lumber + p.wool + p.grain + p.ore;
        removed = ResourceType.Desert;
        if (total <= 0) return false;

        int r = Random.Range(0, total);

        if (r < p.brick) { p.brick--; removed = ResourceType.Brick; return true; }
        r -= p.brick;

        if (r < p.lumber) { p.lumber--; removed = ResourceType.Lumber; return true; }
        r -= p.lumber;

        if (r < p.wool) { p.wool--; removed = ResourceType.Wool; return true; }
        r -= p.wool;

        if (r < p.grain) { p.grain--; removed = ResourceType.Grain; return true; }

        p.ore--;
        removed = ResourceType.Ore;
        return true;
    }

    // =========================
    // TRADING (BANK + PORTS)
    // =========================
    public int GetBestTradeRatio(int playerId, ResourceType give)
    {
        if (!give.IsTradeableResource()) return 999;
        int best = 4;

        if (board == null || board.Nodes == null) return best;

        foreach (var node in board.Nodes)
        {
            if (node == null) continue;
            if (node.building == null) continue;
            if (node.building.ownerId != playerId) continue;

            if (node.port == PortType.None) continue;

            if (PortMatchesResource2to1(node.port, give)) best = Mathf.Min(best, 2);
            else if (node.port == PortType.ThreeToOne) best = Mathf.Min(best, 3);
        }

        return best;
    }

    public void TradeWithBank(ResourceType give, ResourceType get)
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;

        if (!give.IsTradeableResource() || !get.IsTradeableResource()) return;
        if (give == get) return;

        int ratio = GetBestTradeRatio(currentPlayerId, give);
        if (GetResourceCount(CurrentPlayer, give) < ratio) return;

        SetResourceCount(CurrentPlayer, give, GetResourceCount(CurrentPlayer, give) - ratio);
        CurrentPlayer.AddResource(get, 1);

        Debug.Log($"Trade: P{currentPlayerId} {ratio}:{1} {give}->{get}");
    }

    private bool PortMatchesResource2to1(PortType port, ResourceType give)
    {
        return (port == PortType.Brick2to1 && give == ResourceType.Brick) ||
               (port == PortType.Lumber2to1 && give == ResourceType.Lumber) ||
               (port == PortType.Wool2to1 && give == ResourceType.Wool) ||
               (port == PortType.Grain2to1 && give == ResourceType.Grain) ||
               (port == PortType.Ore2to1 && give == ResourceType.Ore);
    }

    // =========================
    // Player-to-player offers (optional)
    // =========================
    public bool ProposeOffer(int toPlayerId, ResourceType giveType, int giveAmount, ResourceType getType, int getAmount)
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;

        if (!giveType.IsTradeableResource() || !getType.IsTradeableResource()) return false;
        if (giveType == getType) return false;
        if (giveAmount <= 0 || getAmount <= 0) return false;

        if (GetResourceCount(CurrentPlayer, giveType) < giveAmount) return false;

        activeOffer = new TradeOffer
        {
            active = true,
            offerId = nextOfferId++,
            fromPlayerId = currentPlayerId,
            toPlayerId = toPlayerId,
            giveType = giveType,
            giveAmount = giveAmount,
            getType = getType,
            getAmount = getAmount
        };

        return true;
    }

    public bool AcceptOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId == activeOffer.fromPlayerId) return false;
        if (activeOffer.toPlayerId != -1 && byPlayerId != activeOffer.toPlayerId) return false;

        var proposer = players[activeOffer.fromPlayerId];
        var accepter = players[byPlayerId];

        if (GetResourceCount(proposer, activeOffer.giveType) < activeOffer.giveAmount) return false;
        if (GetResourceCount(accepter, activeOffer.getType) < activeOffer.getAmount) return false;

        // proposer -> accepter
        SetResourceCount(proposer, activeOffer.giveType, GetResourceCount(proposer, activeOffer.giveType) - activeOffer.giveAmount);
        SetResourceCount(accepter, activeOffer.giveType, GetResourceCount(accepter, activeOffer.giveType) + activeOffer.giveAmount);

        // accepter -> proposer
        SetResourceCount(accepter, activeOffer.getType, GetResourceCount(accepter, activeOffer.getType) - activeOffer.getAmount);
        SetResourceCount(proposer, activeOffer.getType, GetResourceCount(proposer, activeOffer.getType) + activeOffer.getAmount);

        activeOffer = TradeOffer.None;
        return true;
    }

    public bool CancelOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId != activeOffer.fromPlayerId) return false;
        activeOffer = TradeOffer.None;
        return true;
    }

    // =========================
    // COSTS
    // =========================
    private bool CanAffordRoad(int pid) => players[pid].brick >= 1 && players[pid].lumber >= 1;
    private void PayRoad(int pid) { players[pid].brick--; players[pid].lumber--; }

    private bool CanAffordSettlement(int pid)
        => players[pid].brick >= 1 && players[pid].lumber >= 1 && players[pid].wool >= 1 && players[pid].grain >= 1;

    private void PaySettlement(int pid)
    {
        players[pid].brick--;
        players[pid].lumber--;
        players[pid].wool--;
        players[pid].grain--;
    }

    private bool CanAffordCity(int pid) => players[pid].ore >= 3 && players[pid].grain >= 2;
    private void PayCity(int pid) { players[pid].ore -= 3; players[pid].grain -= 2; }

    // =========================
    // CONNECTIVITY RULES
    // =========================
    private bool NodeConnectsToPlayer(Intersection node, int playerId)
    {
        // touching your road or your existing building
        if (node.building != null && node.building.ownerId == playerId) return true;
        foreach (var e in node.edges)
            if (e != null && e.ownerId == playerId) return true;
        return false;
    }

    private bool EndpointConnects(Intersection node, int playerId)
    {
        // if opponent building on node, you can't pass through that node
        if (node.building != null && node.building.ownerId != playerId) return false;
        return NodeConnectsToPlayer(node, playerId);
    }

    private void GrantStartingResources(Intersection node, int ownerId)
    {
        foreach (var tile in node.adjacentTiles)
        {
            if (tile == null) continue;
            if (tile.resource == ResourceType.Desert) continue;
            players[ownerId].AddResource(tile.resource, 1);
        }
    }

    // =========================
    // VISUALS
    // =========================
    private void RevealMarker(Intersection node)
    {
        if (node.building == null) return;
        int owner = node.building.ownerId;
        float size = (node.building.type == BuildingType.City) ? 0.45f : 0.30f;
        ShowMarker(node, players[owner].playerColor, size);
    }

    private void ShowMarker(Intersection node, Color color, float size)
    {
        if (markerSprite == null) return;

        var markerT = node.transform.Find("Marker");
        if (markerT == null)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(node.transform, false);
            markerT = go.transform;
        }

        markerT.gameObject.SetActive(true);
        markerT.localPosition = Vector3.zero;

        var sr = markerT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = markerT.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = markerSprite;
        sr.enabled = true;
        sr.color = color;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 1000;

        markerT.localScale = Vector3.one * size;
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

    // =========================
    // VP / WIN
    // =========================
    private void AddVP(int pid, int amount)
    {
        players[pid].victoryPoints += amount;
        CheckWin(pid);
    }

    private void CheckWin(int pid)
    {
        if (gameOver) return;
        if (players[pid].victoryPoints >= targetVictoryPoints)
        {
            gameOver = true;
            winnerId = pid;
            awaitingRobberMove = false;
            mode = BuildMode.None;
            Debug.Log($"GAME OVER: Player {pid} wins!");
        }
    }

    // =========================
    // Resource helpers
    // =========================
    private int GetResourceCount(PlayerState p, ResourceType t)
    {
        return t switch
        {
            ResourceType.Brick => p.brick,
            ResourceType.Lumber => p.lumber,
            ResourceType.Wool => p.wool,
            ResourceType.Grain => p.grain,
            ResourceType.Ore => p.ore,
            _ => 0
        };
    }

    private void SetResourceCount(PlayerState p, ResourceType t, int value)
    {
        switch (t)
        {
            case ResourceType.Brick: p.brick = value; break;
            case ResourceType.Lumber: p.lumber = value; break;
            case ResourceType.Wool: p.wool = value; break;
            case ResourceType.Grain: p.grain = value; break;
            case ResourceType.Ore: p.ore = value; break;
        }
    }
}