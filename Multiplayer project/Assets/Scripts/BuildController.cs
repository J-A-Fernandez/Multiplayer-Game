using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BuildController : MonoBehaviour
{
    public enum BuildMode { None, Settlement, Road, Robber, City }
    public enum GamePhase { Setup, Main }
    private enum SetupStep { PlaceSettlement, PlaceRoad }

    // Dev cards (internal)
    private enum DevCardType { Knight, VictoryPoint, RoadBuilding, YearOfPlenty, Monopoly }

    [Header("Refs")]
    public BoardGenerator board;

    [Header("Marker Sprite (settlement/city)")]
    public Sprite markerSprite;

    [Header("Players")]
    public PlayerState[] players = new PlayerState[4];

    [Header("Turn State")]
    public int currentPlayerId = 0;
    public GamePhase phase = GamePhase.Setup;
    public BuildMode mode = BuildMode.None;

    [Header("Rules")]
    public bool requireRollBeforeBuild = true;
    public bool enforceBuildCosts = true;

    [Header("Win Condition")]
    public int targetVictoryPoints = 10;

    [Header("Longest Road")]
    public int longestRoadMinLength = 5;
    public int longestRoadBonusVP = 2;

    [Header("Largest Army")]
    public int largestArmyMinKnights = 3;
    public int largestArmyBonusVP = 2;

    [Header("Ports (auto-assigned)")]
    public bool autoAssignPorts = true;
    public int portCount = 9;

    // ===== Setup state =====
    private SetupStep setupStep = SetupStep.PlaceSettlement;
    private bool setupReverse = false;
    private Intersection lastSetupSettlement = null;

    // ===== Main state =====
    [SerializeField] private bool hasRolledThisTurn = false;
    [SerializeField] private bool awaitingRobberMove = false;

    // ===== Game Over =====
    private bool gameOver = false;
    private int winnerId = -1;

    // ===== Longest road =====
    private int longestRoadHolderId = -1;
    private int longestRoadLength = 0;

    // ===== Largest army =====
    private int largestArmyHolderId = -1;
    private int largestArmyCount = 0;

    // ===== Ports state =====
    //private bool portsAssigned = false;

    // ===== Player-to-player trade state =====
    private TradeOffer activeOffer = TradeOffer.None;
    private int nextOfferId = 1;

    // ===== Dev cards state (NEW) =====
    private List<DevCardType> devDeck;
    private List<DevCardType>[] devHand;
    private List<DevCardType>[] devBoughtThisTurn; // cannot play these until next turn
    private bool hasPlayedDevCardThisTurn = false;

    // Road Building dev card
    private int freeRoadsRemaining = 0;

    // Year of Plenty / Monopoly selections (HUD can set these before calling Play…)
    [Header("Dev Card Selections (HUD sets these)")]
    public ResourceType yearOfPlentyChoiceA = ResourceType.Grain;
    public ResourceType yearOfPlentyChoiceB = ResourceType.Wool;
    public ResourceType monopolyChoice = ResourceType.Brick;

    public PlayerState CurrentPlayer => players[currentPlayerId];

    public bool HasRolledThisTurn => hasRolledThisTurn;
    public bool AwaitingRobberMove => awaitingRobberMove;

    public bool GameOver => gameOver;
    public int WinnerId => winnerId;

    public int LongestRoadHolderId => longestRoadHolderId;
    public int LongestRoadLength => longestRoadLength;

    public int LargestArmyHolderId => largestArmyHolderId;
    public int LargestArmyCount => largestArmyCount;

    // ✅ GameHud expects this
    public int DevDeckCount => devDeck?.Count ?? 0;

    public TradeOffer ActiveOffer => activeOffer;

    private void Awake()
    {
        if (players == null || players.Length == 0)
            players = new PlayerState[4];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerState();
            players[i].playerId = i;
        }

        devHand = new List<DevCardType>[players.Length];
        devBoughtThisTurn = new List<DevCardType>[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            devHand[i] = new List<DevCardType>();
            devBoughtThisTurn[i] = new List<DevCardType>();
        }
    }

    private void Start()
    {
        BeginSetup();

        if (autoAssignPorts)
            StartCoroutine(WaitForBoardThenAssignPorts());
    }

    private IEnumerator WaitForBoardThenAssignPorts()
    {
        for (int i = 0; i < 120; i++)
        {
            if (board != null && board.Nodes != null && board.Edges != null &&
                board.Nodes.Count > 0 && board.Edges.Count > 0)
            {
                AssignPortsRandom();
                yield break;
            }
            yield return null;
        }
        Debug.LogWarning("Ports not assigned: board not ready (Nodes/Edges empty).");
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

            devHand[i].Clear();
            devBoughtThisTurn[i].Clear();
        }

        // (Re)build dev deck
        BuildDevDeck();

        longestRoadHolderId = -1;
        longestRoadLength = 0;

        largestArmyHolderId = -1;
        largestArmyCount = 0;

        activeOffer = TradeOffer.None;
        nextOfferId = 1;

        phase = GamePhase.Setup;
        setupStep = SetupStep.PlaceSettlement;
        setupReverse = false;

        currentPlayerId = 0;
        lastSetupSettlement = null;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;

        mode = BuildMode.Settlement;
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;

        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;

        mode = BuildMode.None;
    }

    private void AdvanceSetupAfterRoad()
    {
        setupStep = SetupStep.PlaceSettlement;
        mode = BuildMode.Settlement;
        lastSetupSettlement = null;

        int n = players.Length;

        if (!setupReverse)
        {
            if (currentPlayerId == n - 1) setupReverse = true;
            else currentPlayerId++;
        }
        else
        {
            if (currentPlayerId == 0) EndSetup();
            else currentPlayerId--;
        }
    }

    // =========================
    // ROLL / TURN
    // =========================
    public void RollDiceAndDistribute()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;
        if (hasRolledThisTurn) return;

        int dice = Random.Range(1, 7) + Random.Range(1, 7);
        hasRolledThisTurn = true;

        if (board == null) return;

        if (dice == 7)
        {
            HandleDiscardForSeven();
            awaitingRobberMove = true;
            mode = BuildMode.Robber;
            Debug.Log("Rolled 7 -> discard done, move robber.");
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
        if (awaitingRobberMove) return;

        // clear “bought this turn” restriction for the player who is ending their turn
        devBoughtThisTurn[currentPlayerId].Clear();
        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;

        // Optional: cancel offer at end turn
        if (activeOffer.active) CancelOffer(activeOffer.fromPlayerId);

        currentPlayerId = (currentPlayerId + 1) % players.Length;
        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        mode = BuildMode.None;
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
            ShowMarker(node, CurrentPlayer.playerColor, 0.30f);
            AddVP(currentPlayerId, 1);

            if (setupReverse) GrantStartingResources(node, currentPlayerId);

            lastSetupSettlement = node;
            setupStep = SetupStep.PlaceRoad;
            mode = BuildMode.Road;

            RecomputeLongestRoadAndUpdateCard();
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
        ShowMarker(node, CurrentPlayer.playerColor, 0.30f);

        if (enforceBuildCosts) PaySettlement(currentPlayerId);
        AddVP(currentPlayerId, 1);

        RecomputeLongestRoadAndUpdateCard();
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
            ColorRoad(edge, CurrentPlayer.playerColor);

            RecomputeLongestRoadAndUpdateCard();
            AdvanceSetupAfterRoad();
            return;
        }

        if (mode != BuildMode.Road) return;
        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;
        if (edge.ownerId != -1) return;

        bool connected = EndpointConnects(edge.A, currentPlayerId) || EndpointConnects(edge.B, currentPlayerId);
        if (!connected) return;

        bool free = freeRoadsRemaining > 0;
        if (!free && enforceBuildCosts && !CanAffordRoad(currentPlayerId)) return;

        edge.ownerId = currentPlayerId;
        ColorRoad(edge, CurrentPlayer.playerColor);

        if (!free && enforceBuildCosts) PayRoad(currentPlayerId);

        if (freeRoadsRemaining > 0)
        {
            freeRoadsRemaining--;
            if (freeRoadsRemaining == 0)
            {
                // done placing the 2 free roads
                mode = BuildMode.None;
            }
        }

        RecomputeLongestRoadAndUpdateCard();
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
        ShowMarker(node, CurrentPlayer.playerColor, 0.45f);

        if (enforceBuildCosts) PayCity(currentPlayerId);

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
    }

    // =========================
    // DEV CARDS (✅ fixes your compile errors)
    // =========================

    // GameHud calls this
    public bool CanBuyDevCard()
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;
        if (DevDeckCount <= 0) return false;

        // cost: 1 wool, 1 grain, 1 ore
        if (enforceBuildCosts)
            return CurrentPlayer.wool >= 1 && CurrentPlayer.grain >= 1 && CurrentPlayer.ore >= 1;

        return true;
    }

    // GameHud calls this
    public void BuyDevCard()
    {
        if (!CanBuyDevCard()) return;

        if (enforceBuildCosts)
        {
            CurrentPlayer.wool -= 1;
            CurrentPlayer.grain -= 1;
            CurrentPlayer.ore -= 1;
        }

        var card = DrawDevCard();
        if (card == null) return;

        // VP cards: immediately add VP (simpler)
        if (card.Value == DevCardType.VictoryPoint)
        {
            AddVP(currentPlayerId, 1);
            Debug.Log("Bought VP dev card (+1 VP).");
            return;
        }

        devHand[currentPlayerId].Add(card.Value);
        devBoughtThisTurn[currentPlayerId].Add(card.Value);
        Debug.Log($"Bought dev card: {card.Value}");
    }

    // GameHud calls this
    public void PlayKnightDevCard()
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.Knight)) return;

        hasPlayedDevCardThisTurn = true;

        // Knight effect: move robber + steal
        players[currentPlayerId].knightsPlayed += 1;
        RecomputeLargestArmyAndUpdateCard();

        awaitingRobberMove = true;
        mode = BuildMode.Robber;

        Debug.Log("Played Knight -> move robber.");
    }

    // GameHud calls this
    public void PlayRoadBuildingDevCard()
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.RoadBuilding)) return;

        hasPlayedDevCardThisTurn = true;

        // place 2 roads free
        freeRoadsRemaining = 2;
        mode = BuildMode.Road;

        Debug.Log("Played Road Building -> place 2 free roads.");
    }

    // GameHud calls this (parameterless)
    public void PlayYearOfPlentyDevCard()
    {
        PlayYearOfPlentyDevCard(yearOfPlentyChoiceA, yearOfPlentyChoiceB);
    }

    // Optional overload (if you want to call with args)
    public void PlayYearOfPlentyDevCard(ResourceType a, ResourceType b)
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.YearOfPlenty)) return;

        hasPlayedDevCardThisTurn = true;

        if (!a.IsTradeableResource() || !b.IsTradeableResource()) return;

        CurrentPlayer.AddResource(a, 1);
        CurrentPlayer.AddResource(b, 1);

        Debug.Log($"Played Year of Plenty -> +1 {a}, +1 {b}");
    }

    // GameHud calls this (parameterless)
    public void PlayMonopolyDevCard()
    {
        PlayMonopolyDevCard(monopolyChoice);
    }

    public void PlayMonopolyDevCard(ResourceType type)
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.Monopoly)) return;

        hasPlayedDevCardThisTurn = true;

        if (!type.IsTradeableResource()) return;

        int totalTaken = 0;
        for (int pid = 0; pid < players.Length; pid++)
        {
            if (pid == currentPlayerId) continue;
            int have = GetResourceCount(players[pid], type);
            if (have <= 0) continue;

            SetResourceCount(players[pid], type, 0);
            totalTaken += have;
        }

        CurrentPlayer.AddResource(type, totalTaken);
        Debug.Log($"Played Monopoly ({type}) -> took {totalTaken}");
    }

    private bool CanPlayDevCardThisTurn()
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;
        if (hasPlayedDevCardThisTurn) return false; // 1 dev card per turn (classic rule)
        return true;
    }

    private bool ConsumeDevCard(int pid, DevCardType type)
    {
        // cannot play dev cards you bought this turn
        if (devBoughtThisTurn[pid].Contains(type))
        {
            Debug.Log("Blocked: can't play a dev card the same turn you bought it.");
            return false;
        }

        int idx = devHand[pid].IndexOf(type);
        if (idx < 0) return false;

        devHand[pid].RemoveAt(idx);
        return true;
    }

    private void BuildDevDeck()
    {
        devDeck = new List<DevCardType>();

        // Base game deck:
        // Knights: 14, VP: 5, Road Building: 2, Year of Plenty: 2, Monopoly: 2
        devDeck.AddRange(Enumerable.Repeat(DevCardType.Knight, 14));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.VictoryPoint, 5));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.RoadBuilding, 2));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.YearOfPlenty, 2));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.Monopoly, 2));

        Shuffle(devDeck, new System.Random());
    }

    private DevCardType? DrawDevCard()
    {
        if (devDeck == null || devDeck.Count == 0) return null;
        var c = devDeck[0];
        devDeck.RemoveAt(0);
        return c;
    }

    // =========================
    // BANK TRADING + PORTS
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

    public bool CanTradeWithBank(ResourceType give, ResourceType get, out int ratio)
    {
        ratio = 4;

        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;

        if (!give.IsTradeableResource() || !get.IsTradeableResource()) return false;
        if (give == get) return false;

        ratio = GetBestTradeRatio(currentPlayerId, give);
        return GetResourceCount(CurrentPlayer, give) >= ratio;
    }

    public void TradeWithBank(ResourceType give, ResourceType get)
    {
        if (!CanTradeWithBank(give, get, out int ratio)) return;

        SetResourceCount(CurrentPlayer, give, GetResourceCount(CurrentPlayer, give) - ratio);
        CurrentPlayer.AddResource(get, 1);
    }

    private bool PortMatchesResource2to1(PortType port, ResourceType give)
    {
        return (port == PortType.Brick2to1 && give == ResourceType.Brick) ||
               (port == PortType.Lumber2to1 && give == ResourceType.Lumber) ||
               (port == PortType.Wool2to1 && give == ResourceType.Wool) ||
               (port == PortType.Grain2to1 && give == ResourceType.Grain) ||
               (port == PortType.Ore2to1 && give == ResourceType.Ore);
    }

    [ContextMenu("Assign Ports Random")]
    public void AssignPortsRandom()
    {
        if (board == null || board.Nodes == null || board.Edges == null ||
            board.Nodes.Count == 0 || board.Edges.Count == 0)
        {
            Debug.LogWarning("AssignPortsRandom failed: board not ready.");
            return;
        }

        foreach (var n in board.Nodes)
            if (n != null) n.port = PortType.None;

        var boundaryEdges = new List<RoadEdge>();
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            if (e.adjacentTiles != null && e.adjacentTiles.Count == 1)
                boundaryEdges.Add(e);
        }

        if (boundaryEdges.Count == 0)
        {
            Debug.LogWarning("No boundary edges found for ports (need edge.adjacentTiles.Count==1).");
            return;
        }

        var portList = new List<PortType>
        {
            PortType.ThreeToOne, PortType.ThreeToOne, PortType.ThreeToOne, PortType.ThreeToOne,
            PortType.Brick2to1, PortType.Lumber2to1, PortType.Wool2to1, PortType.Grain2to1, PortType.Ore2to1
        };

        if (portCount < portList.Count) portList.RemoveRange(portCount, portList.Count - portCount);
        if (portCount > portList.Count) while (portList.Count < portCount) portList.Add(PortType.ThreeToOne);

        var rng = new System.Random();
        Shuffle(boundaryEdges, rng);
        Shuffle(portList, rng);

        int placed = 0;
        var usedNodes = new HashSet<int>();

        foreach (var port in portList)
        {
            bool done = false;
            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                var e = boundaryEdges[i];
                if (e == null || e.A == null || e.B == null) continue;

                if (usedNodes.Contains(e.A.id) || usedNodes.Contains(e.B.id)) continue;

                e.A.port = port;
                e.B.port = port;
                usedNodes.Add(e.A.id);
                usedNodes.Add(e.B.id);
                placed++;
                done = true;
                break;
            }
            if (!done) break;
        }

        //portsAssigned = true;
        Debug.Log($"Assigned ports: {placed}.");
    }

    private void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // =========================
    // PLAYER-TO-PLAYER OFFERS
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

    public bool CancelOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId != activeOffer.fromPlayerId) return false;
        activeOffer = TradeOffer.None;
        return true;
    }

    public bool DeclineOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId == activeOffer.fromPlayerId) return false;
        if (activeOffer.toPlayerId != -1 && byPlayerId != activeOffer.toPlayerId) return false;
        activeOffer = TradeOffer.None;
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

    // =========================
    // VP + WIN
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
            Debug.Log($"GAME OVER: P{pid} wins with {players[pid].victoryPoints} VP");
        }
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
    // SETUP HELPERS + CONNECTIVITY
    // =========================
    private void GrantStartingResources(Intersection node, int ownerId)
    {
        foreach (var tile in node.adjacentTiles)
        {
            if (tile == null) continue;
            if (tile.resource == ResourceType.Desert) continue;
            players[ownerId].AddResource(tile.resource, 1);
        }
    }

    private bool NodeConnectsToPlayer(Intersection node, int playerId)
    {
        if (node.building != null && node.building.ownerId == playerId) return true;
        foreach (var e in node.edges)
            if (e != null && e.ownerId == playerId) return true;
        return false;
    }

    private bool EndpointConnects(Intersection node, int playerId)
    {
        if (node.building != null && node.building.ownerId != playerId) return false;
        return NodeConnectsToPlayer(node, playerId);
    }

    private void RevealMarker(Intersection node)
    {
        if (node.building == null) return;
        int owner = node.building.ownerId;
        float size = (node.building.type == BuildingType.City) ? 0.45f : 0.30f;
        ShowMarker(node, players[owner].playerColor, size);
    }

    private void ShowMarker(Intersection node, Color color, float size)
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

        var sr = markerT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = markerT.gameObject.AddComponent<SpriteRenderer>();
        if (sr.sprite == null) sr.sprite = markerSprite;

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
    // LONGEST ROAD (+2 VP)
    // =========================
    private void RecomputeLongestRoadAndUpdateCard()
    {
        if (board == null || board.Nodes == null) return;

        int bestLen = 0;
        var bestPlayers = new List<int>();

        for (int pid = 0; pid < players.Length; pid++)
        {
            int len = ComputeLongestRoadForPlayer(pid);
            if (len > bestLen)
            {
                bestLen = len;
                bestPlayers.Clear();
                bestPlayers.Add(pid);
            }
            else if (len == bestLen)
            {
                bestPlayers.Add(pid);
            }
        }

        int newHolder = -1;
        if (bestLen >= longestRoadMinLength)
        {
            if (bestPlayers.Count == 1) newHolder = bestPlayers[0];
            else if (longestRoadHolderId != -1 && bestPlayers.Contains(longestRoadHolderId))
                newHolder = longestRoadHolderId;
        }

        if (newHolder != longestRoadHolderId)
        {
            if (longestRoadHolderId != -1)
                players[longestRoadHolderId].victoryPoints = Mathf.Max(0, players[longestRoadHolderId].victoryPoints - longestRoadBonusVP);

            if (newHolder != -1)
            {
                players[newHolder].victoryPoints += longestRoadBonusVP;
                CheckWin(newHolder);
            }

            longestRoadHolderId = newHolder;
        }

        longestRoadLength = (longestRoadHolderId == -1) ? 0 : bestLen;
    }

    private int ComputeLongestRoadForPlayer(int playerId)
    {
        int best = 0;
        var used = new HashSet<RoadEdge>();

        foreach (var node in board.Nodes)
        {
            if (node == null) continue;
            int len = DFSRoad(node, playerId, used);
            if (len > best) best = len;
        }

        return best;
    }

    private int DFSRoad(Intersection node, int playerId, HashSet<RoadEdge> used)
    {
        if (node == null) return 0;

        // opponent building blocks continuation
        if (node.building != null && node.building.ownerId != playerId) return 0;

        int best = 0;
        foreach (var e in node.edges)
        {
            if (e == null) continue;
            if (e.ownerId != playerId) continue;
            if (used.Contains(e)) continue;

            used.Add(e);
            var next = (e.A == node) ? e.B : e.A;

            int len = 1 + DFSRoad(next, playerId, used);
            best = Mathf.Max(best, len);

            used.Remove(e);
        }

        return best;
    }

    // =========================
    // LARGEST ARMY (+2 VP)
    // =========================
    private void RecomputeLargestArmyAndUpdateCard()
    {
        int best = 0;
        var bestPlayers = new List<int>();

        for (int pid = 0; pid < players.Length; pid++)
        {
            int k = players[pid].knightsPlayed;
            if (k > best)
            {
                best = k;
                bestPlayers.Clear();
                bestPlayers.Add(pid);
            }
            else if (k == best)
            {
                bestPlayers.Add(pid);
            }
        }

        int newHolder = -1;
        if (best >= largestArmyMinKnights)
        {
            if (bestPlayers.Count == 1) newHolder = bestPlayers[0];
            else if (largestArmyHolderId != -1 && bestPlayers.Contains(largestArmyHolderId))
                newHolder = largestArmyHolderId;
        }

        if (newHolder != largestArmyHolderId)
        {
            if (largestArmyHolderId != -1)
                players[largestArmyHolderId].victoryPoints = Mathf.Max(0, players[largestArmyHolderId].victoryPoints - largestArmyBonusVP);

            if (newHolder != -1)
            {
                players[newHolder].victoryPoints += largestArmyBonusVP;
                CheckWin(newHolder);
            }

            largestArmyHolderId = newHolder;
        }

        largestArmyCount = (largestArmyHolderId == -1) ? 0 : best;
    }

    // =========================
    // ROBBER: DISCARD + STEAL
    // =========================
    private void HandleDiscardForSeven()
    {
        for (int pid = 0; pid < players.Length; pid++)
        {
            int total = players[pid].TotalResources();
            if (total <= 7) continue;

            int discard = total / 2;
            for (int i = 0; i < discard; i++)
                TryRemoveRandomResource(pid, out _);
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

        var eligible = new List<int>();
        foreach (var v in victims)
            if (players[v].TotalResources() > 0) eligible.Add(v);

        if (eligible.Count == 0) return;

        int victimId = eligible[Random.Range(0, eligible.Count)];
        if (TryRemoveRandomResource(victimId, out var stolen))
            CurrentPlayer.AddResource(stolen, 1);
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
    public void Net_SetTurnFlags(bool hasRolled, bool awaitingRobber)
    {
        // these fields exist in your BuildController already
        hasRolledThisTurn = hasRolled;
        awaitingRobberMove = awaitingRobber;
    }

    public void Net_SetGameMeta(bool isGameOver, int winId)
    {
        gameOver = isGameOver;
        winnerId = winId;
    }
}