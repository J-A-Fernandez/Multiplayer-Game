using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BuildController : MonoBehaviour
{

    [Header("Multiplayer")]
    public bool autoStartSingleplayerOnly = true;
    public enum BuildMode { None, Settlement, Road, Robber, City }
    public enum GamePhase { Setup, Main }
    private enum SetupStep { PlaceSettlement, PlaceRoad }



    // =========================
    // DEV CARDS
    // =========================
    public enum DevCardType { Knight, RoadBuilding, YearOfPlenty, Monopoly, VictoryPoint }

    [Serializable]
    public class DevCard
    {
        public DevCardType type;
        public DevCard(DevCardType t) { type = t; }
    }

    // =========================
    // REFS
    // =========================
    [Header("Refs")]
    public BoardGenerator board;

    [Header("Marker Sprite (settlement/city)")]
    public Sprite markerSprite;

    // =========================
    // PLAYERS
    // =========================
    [Header("Players")]
    public PlayerState[] players = new PlayerState[4];

    // =========================
    // TURN STATE
    // =========================
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
    [Tooltip("How many ports to place (classic base game uses 9).")]
    public int portCount = 9;

    // =========================
    // SETUP STATE
    // =========================
    private SetupStep setupStep = SetupStep.PlaceSettlement;
    private bool setupReverse = false;
    private Intersection lastSetupSettlement = null;

    // =========================
    // MAIN STATE
    // =========================
    private bool hasRolledThisTurn = false;
    private bool awaitingRobberMove = false;

    // Dev-card limiter (Knight)
    private bool hasPlayedKnightThisTurn = false;

    // =========================
    // GAME OVER
    // =========================
    private bool gameOver = false;
    private int winnerId = -1;

    // =========================
    // LONGEST ROAD
    // =========================
    private int longestRoadHolderId = -1;
    private int longestRoadLength = 0;

    // =========================
    // LARGEST ARMY
    // =========================
    private int largestArmyHolderId = -1;
    private int largestArmyCount = 0;

    // =========================
    // DEV DECK + HANDS
    // =========================
    private readonly List<DevCard> devDeck = new();
    private readonly System.Random devRng = new();
    private List<DevCardType>[] devHands;

    // RoadBuilding effect: allow 2 free roads
    private int freeRoadsToPlace = 0;

    // Ports
    private bool portsAssigned = false;

    // =========================
    // PUBLIC READONLY (GameHud uses these)
    // =========================
    public PlayerState CurrentPlayer => players[currentPlayerId];
    public bool HasRolledThisTurn => hasRolledThisTurn;
    public bool AwaitingRobberMove => awaitingRobberMove;
    public bool GameOver => gameOver;
    public int WinnerId => winnerId;

    public int LongestRoadHolderId => longestRoadHolderId;
    public int LongestRoadLength => longestRoadLength;
    public int DevDeckCount => devDeck.Count;

    public int LargestArmyHolderId => largestArmyHolderId;
    public int LargestArmyCount => largestArmyCount;

    // =========================
    // UNITY
    // =========================
    private void Awake()
    {
        if (players == null || players.Length == 0)
            players = new PlayerState[4];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerState();
            players[i].playerId = i;
        }

        devHands = new List<DevCardType>[players.Length];
        for (int i = 0; i < devHands.Length; i++)
            devHands[i] = new List<DevCardType>();
    }

    private void Start()
    {
        // Multiplayer: NetworkCatanManager controls setup + ports.
        // Singleplayer: BuildController can auto-start.
        bool isMultiplayerSession = false;

    #if UNITY_NETCODE_GAMEOBJECTS
        var nm = Unity.Netcode.NetworkManager.Singleton;
        isMultiplayerSession = (nm != null && nm.IsListening);
    #endif

    if (autoStartSingleplayerOnly && isMultiplayerSession)
        return;

    BeginSetup();
    BuildDevDeck();

    if (autoAssignPorts)
        StartCoroutine(WaitForBoardThenAssignPorts());
}

    // =========================
    // (OPTIONAL) NETWORK HELPERS
    // =========================
    public void EnsurePlayerCount(int count)
    {
        if (count < 1) count = 1;

        if (players == null || players.Length != count)
        {
            var newArr = new PlayerState[count];
            for (int i = 0; i < count; i++)
            {
                newArr[i] = (players != null && i < players.Length && players[i] != null)
                    ? players[i]
                    : new PlayerState();
                newArr[i].playerId = i;
            }
            players = newArr;
        }

        // resize hands too
        devHands = new List<DevCardType>[players.Length];
        for (int i = 0; i < devHands.Length; i++)
            devHands[i] = new List<DevCardType>();
    }

    public void Net_SetTurnFlags(bool rolled, bool awaitingRobber)
    {
        hasRolledThisTurn = rolled;
        awaitingRobberMove = awaitingRobber;
    }

    public void Net_SetGameMeta(bool over, int winId)
    {
        gameOver = over;
        winnerId = winId;
        if (gameOver) mode = BuildMode.None;
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
            devHands[i].Clear();
        }

        longestRoadHolderId = -1;
        longestRoadLength = 0;

        largestArmyHolderId = -1;
        largestArmyCount = 0;

        phase = GamePhase.Setup;
        setupStep = SetupStep.PlaceSettlement;
        setupReverse = false;

        currentPlayerId = 0;
        lastSetupSettlement = null;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        hasPlayedKnightThisTurn = false;

        freeRoadsToPlace = 0;

        mode = BuildMode.Settlement;

        Debug.Log("=== SETUP START === P0 place a Settlement");
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        hasPlayedKnightThisTurn = false;

        freeRoadsToPlace = 0;

        mode = BuildMode.None;

        Debug.Log("=== SETUP COMPLETE === Main game begins. Roll dice to start.");
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
            if (currentPlayerId == 0) EndSetup();
            else
            {
                currentPlayerId--;
                Debug.Log($"SETUP: Next player P{currentPlayerId} place your 2nd Settlement.");
            }
        }
    }

    // =========================
    // ROLL / PAYOUT
    // =========================
    public void RollDiceAndDistribute()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;

        if (awaitingRobberMove) { Debug.Log("Blocked: move robber first."); return; }
        if (hasRolledThisTurn) { Debug.Log("Blocked: already rolled."); return; }

        int dice = UnityEngine.Random.Range(1, 7) + UnityEngine.Random.Range(1, 7);
        hasRolledThisTurn = true;

        Debug.Log($"Rolled: {dice}");

        if (board == null) { Debug.LogWarning("BuildController.board not assigned."); return; }

        if (dice == 7)
        {
            HandleDiscardForSeven();
            awaitingRobberMove = true;
            mode = BuildMode.Robber;
            Debug.Log("Rolled 7! Click a tile to move robber (then steal 1).");
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

        if (awaitingRobberMove)
        {
            Debug.Log("Blocked: move robber before ending turn.");
            return;
        }

        currentPlayerId = (currentPlayerId + 1) % players.Length;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        hasPlayedKnightThisTurn = false;
        mode = BuildMode.None;

        freeRoadsToPlace = 0;

        Debug.Log($"END TURN -> Player {currentPlayerId}. Roll dice.");
    }

    // =========================
    // UI MODE SETTERS
    // =========================
    public void SetModeSettlement() { if (!gameOver) mode = BuildMode.Settlement; }
    public void SetModeRoad() { if (!gameOver) mode = BuildMode.Road; }
    public void SetModeCity() { if (!gameOver) mode = BuildMode.City; }
    public void SetModeNone() { if (!gameOver) mode = BuildMode.None; }

    // =========================
    // PLACEMENT
    // =========================
    public void TryPlaceSettlement(Intersection node)
    {
        if (gameOver) return;
        if (node == null) return;

        // ---- Setup placement ----
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

        // ---- Main placement ----
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

        // ---- Setup placement ----
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

        // ---- Main placement ----
        if (mode != BuildMode.Road) return;
        if (awaitingRobberMove) return;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return;

        if (edge.ownerId != -1) return;

        bool connected = EndpointConnects(edge.A, currentPlayerId) || EndpointConnects(edge.B, currentPlayerId);
        if (!connected) return;

        // RoadBuilding dev card gives 2 free roads
        bool isFree = freeRoadsToPlace > 0;

        if (!isFree && enforceBuildCosts && !CanAffordRoad(currentPlayerId)) return;

        edge.ownerId = currentPlayerId;
        ColorRoad(edge, CurrentPlayer.playerColor);

        if (isFree)
        {
            freeRoadsToPlace--;
        }
        else if (enforceBuildCosts)
        {
            PayRoad(currentPlayerId);
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

        AddVP(currentPlayerId, 1); // upgrade adds +1
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

        // clear old robber
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

        // set new
        target.hasRobber = true;
        target.RefreshVisual();

        // steal
        StealOneFromRobberTile(target);

        awaitingRobberMove = false;
        mode = BuildMode.None;
    }

    private void HandleDiscardForSeven()
    {
        for (int i = 0; i < players.Length; i++)
        {
            int total = players[i].brick + players[i].lumber + players[i].wool + players[i].grain + players[i].ore;
            if (total <= 7) continue;

            int discard = total / 2;
            DiscardRandom(i, discard);
        }
    }

    private void DiscardRandom(int pid, int count)
    {
        for (int k = 0; k < count; k++)
        {
            var bag = new List<ResourceType>();
            if (players[pid].brick > 0) bag.Add(ResourceType.Brick);
            if (players[pid].lumber > 0) bag.Add(ResourceType.Lumber);
            if (players[pid].wool > 0) bag.Add(ResourceType.Wool);
            if (players[pid].grain > 0) bag.Add(ResourceType.Grain);
            if (players[pid].ore > 0) bag.Add(ResourceType.Ore);

            if (bag.Count == 0) return;

            var pick = bag[UnityEngine.Random.Range(0, bag.Count)];
            players[pid].AddResource(pick, -1);
        }
    }

    private void StealOneFromRobberTile(HexTile tile)
    {
        var victims = new HashSet<int>();
        foreach (var node in tile.corners)
        {
            if (node == null || node.building == null) continue;
            int owner = node.building.ownerId;
            if (owner != currentPlayerId) victims.Add(owner);
        }

        if (victims.Count == 0)
        {
            Debug.Log("Robber: no victims on that tile.");
            return;
        }

        var victimList = victims.ToList();
        int victimId = victimList[UnityEngine.Random.Range(0, victimList.Count)];

        var victimBag = new List<ResourceType>();
        if (players[victimId].brick > 0) victimBag.Add(ResourceType.Brick);
        if (players[victimId].lumber > 0) victimBag.Add(ResourceType.Lumber);
        if (players[victimId].wool > 0) victimBag.Add(ResourceType.Wool);
        if (players[victimId].grain > 0) victimBag.Add(ResourceType.Grain);
        if (players[victimId].ore > 0) victimBag.Add(ResourceType.Ore);

        if (victimBag.Count == 0)
        {
            Debug.Log("Robber: victim has no cards.");
            return;
        }

        var stolen = victimBag[UnityEngine.Random.Range(0, victimBag.Count)];
        players[victimId].AddResource(stolen, -1);
        players[currentPlayerId].AddResource(stolen, +1);

        Debug.Log($"Robber: P{currentPlayerId} stole 1 {stolen} from P{victimId}");
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

    private bool CanAffordDevCard(int pid) => players[pid].ore >= 1 && players[pid].grain >= 1 && players[pid].wool >= 1;
    private void PayDevCard(int pid) { players[pid].ore--; players[pid].grain--; players[pid].wool--; }

    // =========================
    // DEV CARDS API (GameHud CALLS THESE)
    // =========================
    public bool CanBuyDevCard()
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;
        if (!CanAffordDevCard(currentPlayerId)) return false;
        return devDeck.Count > 0;
    }

    //public int DevDeckCount() => devDeck.Count;

    public void BuyDevCard()
    {
        if (!CanBuyDevCard())
        {
            Debug.Log("BuyDevCard blocked.");
            return;
        }

        PayDevCard(currentPlayerId);

        int idx = devRng.Next(devDeck.Count);
        var card = devDeck[idx];
        devDeck.RemoveAt(idx);

        devHands[currentPlayerId].Add(card.type);

        if (card.type == DevCardType.VictoryPoint)
            AddVP(currentPlayerId, 1);

        Debug.Log($"P{currentPlayerId} bought Dev Card: {card.type}");
    }

    public void PlayKnightDevCard()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;
        if (hasPlayedKnightThisTurn) { Debug.Log("Knight blocked: already played this turn."); return; }

        if (!ConsumeDevCard(currentPlayerId, DevCardType.Knight)) { Debug.Log("No Knight card."); return; }

        hasPlayedKnightThisTurn = true;
        players[currentPlayerId].knightsPlayed += 1;

        awaitingRobberMove = true;
        mode = BuildMode.Robber;

        RecomputeLargestArmyAndUpdateCard();

        Debug.Log("Knight played: move robber.");
    }

    public void PlayRoadBuildingDevCard()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;

        if (!ConsumeDevCard(currentPlayerId, DevCardType.RoadBuilding)) { Debug.Log("No RoadBuilding card."); return; }

        freeRoadsToPlace = 2;
        mode = BuildMode.Road;

        Debug.Log("Road Building: place 2 free roads now.");
    }

    public void PlayYearOfPlentyDevCard(ResourceType a, ResourceType b)
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;

        if (!ConsumeDevCard(currentPlayerId, DevCardType.YearOfPlenty)) { Debug.Log("No YearOfPlenty card."); return; }

        if (!a.IsTradeableResource() || !b.IsTradeableResource())
        {
            Debug.Log("YearOfPlenty blocked: invalid resource.");
            return;
        }

        players[currentPlayerId].AddResource(a, 1);
        players[currentPlayerId].AddResource(b, 1);

        Debug.Log($"Year of Plenty: +1 {a}, +1 {b}");
    }

    public void PlayMonopolyDevCard(ResourceType take)
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;

        if (!ConsumeDevCard(currentPlayerId, DevCardType.Monopoly)) { Debug.Log("No Monopoly card."); return; }
        if (!take.IsTradeableResource()) { Debug.Log("Monopoly blocked: invalid resource."); return; }

        int totalTaken = 0;
        for (int i = 0; i < players.Length; i++)
        {
            if (i == currentPlayerId) continue;
            int have = GetResourceCount(players[i], take);
            if (have <= 0) continue;

            SetResourceCount(players[i], take, 0);
            totalTaken += have;
        }

        players[currentPlayerId].AddResource(take, totalTaken);
        Debug.Log($"Monopoly: P{currentPlayerId} took {totalTaken} {take}");
    }

    private bool ConsumeDevCard(int pid, DevCardType type)
    {
        int idx = devHands[pid].IndexOf(type);
        if (idx < 0) return false;
        devHands[pid].RemoveAt(idx);
        return true;
    }

    private void BuildDevDeck()
    {
        devDeck.Clear();

        // 25-card base-like deck:
        // 14 Knights, 2 RoadBuilding, 2 YearOfPlenty, 2 Monopoly, 5 VP
        for (int i = 0; i < 14; i++) devDeck.Add(new DevCard(DevCardType.Knight));
        for (int i = 0; i < 2; i++) devDeck.Add(new DevCard(DevCardType.RoadBuilding));
        for (int i = 0; i < 2; i++) devDeck.Add(new DevCard(DevCardType.YearOfPlenty));
        for (int i = 0; i < 2; i++) devDeck.Add(new DevCard(DevCardType.Monopoly));
        for (int i = 0; i < 5; i++) devDeck.Add(new DevCard(DevCardType.VictoryPoint));

        // shuffle
        for (int i = devDeck.Count - 1; i > 0; i--)
        {
            int j = devRng.Next(i + 1);
            (devDeck[i], devDeck[j]) = (devDeck[j], devDeck[i]);
        }
    }

    // =========================
    // TRADING (BANK + PORTS)
    // =========================
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
        if (!CanTradeWithBank(give, get, out int ratio))
        {
            Debug.Log("Trade blocked.");
            return;
        }

        SetResourceCount(CurrentPlayer, give, GetResourceCount(CurrentPlayer, give) - ratio);
        CurrentPlayer.AddResource(get, 1);

        Debug.Log($"TRADE: P{currentPlayerId} traded {ratio} {give} -> 1 {get}");
    }

    public int GetBestTradeRatio(int playerId, ResourceType give)
    {
        if (!give.IsTradeableResource()) return 999;

        int best = 4; // bank default
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

    private bool PortMatchesResource2to1(PortType port, ResourceType give)
    {
        return (port == PortType.Brick2to1 && give == ResourceType.Brick) ||
               (port == PortType.Lumber2to1 && give == ResourceType.Lumber) ||
               (port == PortType.Wool2to1 && give == ResourceType.Wool) ||
               (port == PortType.Grain2to1 && give == ResourceType.Grain) ||
               (port == PortType.Ore2to1 && give == ResourceType.Ore);
    }

    private IEnumerator WaitForBoardThenAssignPorts()
    {
        for (int i = 0; i < 240; i++)
        {
            if (board != null && board.Nodes != null && board.Edges != null &&
                board.Nodes.Count > 0 && board.Edges.Count > 0)
            {
                AssignPortsRandom();
                yield break;
            }
            yield return null;
        }

        Debug.LogWarning("Ports not assigned: board not ready.");
    }

    [ContextMenu("Assign Ports Random")]
    public void AssignPortsRandom()
    {
    // Multiplayer: only the server assigns ports
    #if UNITY_NETCODE_GAMEOBJECTS
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.IsServer)
            return;
    #endif

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
            Debug.LogWarning("No boundary edges found for ports.");
            return;
        }

        var portList = new List<PortType>
        {
            PortType.ThreeToOne, PortType.ThreeToOne, PortType.ThreeToOne, PortType.ThreeToOne,
            PortType.Brick2to1, PortType.Lumber2to1, PortType.Wool2to1, PortType.Grain2to1, PortType.Ore2to1
        };

        if (portCount < portList.Count) portList.RemoveRange(portCount, portList.Count - portCount);
        while (portList.Count < portCount) portList.Add(PortType.ThreeToOne);

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

                if (usedNodes.Contains(e.A.id) || usedNodes.Contains(e.B.id))
                    continue;

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

        portsAssigned = true;
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
            Debug.Log($"=== GAME OVER === Winner: Player {pid} ({players[pid].victoryPoints} VP)");
        }
    }

    // =========================
    // LONGEST ROAD
    // =========================
    private void RecomputeLongestRoadAndUpdateCard()
    {
        if (board == null || board.Edges == null || board.Nodes == null) return;

        int bestPlayer = -1;
        int bestLen = 0;

        for (int p = 0; p < players.Length; p++)
        {
            int len = ComputeLongestRoadForPlayer(p);
            if (len > bestLen)
            {
                bestLen = len;
                bestPlayer = p;
            }
        }

        if (bestPlayer != -1 && bestLen >= longestRoadMinLength)
        {
            if (longestRoadHolderId != -1 && longestRoadHolderId != bestPlayer)
                players[longestRoadHolderId].victoryPoints -= longestRoadBonusVP;

            if (longestRoadHolderId != bestPlayer)
                players[bestPlayer].victoryPoints += longestRoadBonusVP;

            longestRoadHolderId = bestPlayer;
            longestRoadLength = bestLen;
        }
        else
        {
            if (longestRoadHolderId != -1)
                players[longestRoadHolderId].victoryPoints -= longestRoadBonusVP;

            longestRoadHolderId = -1;
            longestRoadLength = 0;
        }
    }

    private int ComputeLongestRoadForPlayer(int playerId)
    {
        var ownedEdges = board.Edges.Where(e => e != null && e.ownerId == playerId).ToList();
        if (ownedEdges.Count == 0) return 0;

        var incident = new Dictionary<Intersection, List<RoadEdge>>();
        foreach (var e in ownedEdges)
        {
            if (e.A != null)
            {
                if (!incident.TryGetValue(e.A, out var listA)) { listA = new List<RoadEdge>(); incident[e.A] = listA; }
                listA.Add(e);
            }
            if (e.B != null)
            {
                if (!incident.TryGetValue(e.B, out var listB)) { listB = new List<RoadEdge>(); incident[e.B] = listB; }
                listB.Add(e);
            }
        }

        int best = 0;
        foreach (var e in ownedEdges)
            best = Mathf.Max(best, DFSLongestFromEdge(e, playerId, incident, new HashSet<RoadEdge>()));

        return best;
    }

    private int DFSLongestFromEdge(RoadEdge start, int playerId, Dictionary<Intersection, List<RoadEdge>> incident, HashSet<RoadEdge> used)
    {
        used.Add(start);
        int best = 1;

        best = Mathf.Max(best, 1 + DFSFromNode(start.A, playerId, incident, used));
        best = Mathf.Max(best, 1 + DFSFromNode(start.B, playerId, incident, used));

        used.Remove(start);
        return best;
    }

    private int DFSFromNode(Intersection node, int playerId, Dictionary<Intersection, List<RoadEdge>> incident, HashSet<RoadEdge> used)
    {
        if (node == null) return 0;

        if (node.building != null && node.building.ownerId != playerId)
            return 0;

        if (!incident.TryGetValue(node, out var edgesHere)) return 0;

        int best = 0;
        foreach (var e in edgesHere)
        {
            if (e == null || used.Contains(e)) continue;

            used.Add(e);
            var next = (e.A == node) ? e.B : e.A;
            best = Mathf.Max(best, 1 + DFSFromNode(next, playerId, incident, used));
            used.Remove(e);
        }

        return best;
    }

    // =========================
    // LARGEST ARMY
    // =========================
    private void RecomputeLargestArmyAndUpdateCard()
    {
        int bestPlayer = -1;
        int bestCount = 0;

        for (int i = 0; i < players.Length; i++)
        {
            int k = players[i].knightsPlayed;
            if (k > bestCount)
            {
                bestCount = k;
                bestPlayer = i;
            }
        }

        if (bestPlayer != -1 && bestCount >= largestArmyMinKnights)
        {
            if (largestArmyHolderId != -1 && largestArmyHolderId != bestPlayer)
                players[largestArmyHolderId].victoryPoints -= largestArmyBonusVP;

            if (largestArmyHolderId != bestPlayer)
                players[bestPlayer].victoryPoints += largestArmyBonusVP;

            largestArmyHolderId = bestPlayer;
            largestArmyCount = bestCount;
        }
        else
        {
            if (largestArmyHolderId != -1)
                players[largestArmyHolderId].victoryPoints -= largestArmyBonusVP;

            largestArmyHolderId = -1;
            largestArmyCount = 0;
        }
    }

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

    // =========================
    // VISUALS (MARKERS + ROADS)
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
    // RESOURCE HELPERS
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

// =========================
// EXTENSION (REMOVE if you already have ResourceTypeExtensions.cs)
// =========================
public static class ResourceTypeExtensions
{
    public static bool IsTradeableResource(this ResourceType r)
    {
        return r == ResourceType.Brick ||
               r == ResourceType.Lumber ||
               r == ResourceType.Wool ||
               r == ResourceType.Grain ||
               r == ResourceType.Ore;
    }
}