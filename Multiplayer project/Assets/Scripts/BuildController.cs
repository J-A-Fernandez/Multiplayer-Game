using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BuildController : MonoBehaviour
{
    public enum BuildMode { None, Settlement, Road, Robber, City }
    public enum GamePhase { Setup, Main }
    private enum SetupStep { PlaceSettlement, PlaceRoad }

    // Dev cards (for GameHud compatibility)
    private enum DevCardType { Knight, VictoryPoint, RoadBuilding, YearOfPlenty, Monopoly }

    [Header("Refs")]
    public BoardGenerator board;

    [Header("Marker Sprite (settlement/city)")]
    public Sprite markerSprite;

    [Header("Players")]
    public PlayerState[] players = new PlayerState[2]; // set in Inspector

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

    // Dev cards
    private List<DevCardType> devDeck = new();
    private List<DevCardType>[] devHand;
    private List<DevCardType>[] devBoughtThisTurn;
    private bool hasPlayedDevCardThisTurn = false;

    private int freeRoadsRemaining = 0; // Road Building effect

    // selections (kept)
    public ResourceType yearOfPlentyChoiceA = ResourceType.Grain;
    public ResourceType yearOfPlentyChoiceB = ResourceType.Wool;
    public ResourceType monopolyChoice = ResourceType.Brick;

    // GameHud expects
    public int DevDeckCount => devDeck.Count;

    // ===== Longest road / largest army (HUD wants these properties) =====
    public int LongestRoadHolderId { get; private set; } = -1;
    public int LongestRoadLength { get; private set; } = 0;

    public int LargestArmyHolderId { get; private set; } = -1;
    public int LargestArmyCount { get; private set; } = 0;

    public PlayerState CurrentPlayer => players[currentPlayerId];
    public bool HasRolledThisTurn => hasRolledThisTurn;
    public bool AwaitingRobberMove => awaitingRobberMove;
    public bool GameOver => gameOver;
    public int WinnerId => winnerId;

    private void Awake()
    {
        if (players == null || players.Length == 0)
            players = new PlayerState[2];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerState();
            players[i].playerId = i;

            // Avoid Random ambiguity:
            if (players[i].playerColor.a <= 0.01f)
                players[i].playerColor = UnityEngine.Random.ColorHSV();
        }

        devHand = new List<DevCardType>[players.Length];
        devBoughtThisTurn = new List<DevCardType>[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            devHand[i] = new List<DevCardType>();
            devBoughtThisTurn[i] = new List<DevCardType>();
        }

        if (board == null) board = FindFirstObjectByType<BoardGenerator>();
    }

    private void Start()
    {
        BeginSetup();
        RecomputeLargestArmy();
        RecomputeLongestRoad();
    }

    // =========================
    // NETWORK HELPERS (used by NetworkCatanManager)
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

            devHand[i].Clear();
            devBoughtThisTurn[i].Clear();
        }

        BuildDevDeck();
        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;

        phase = GamePhase.Setup;
        setupStep = SetupStep.PlaceSettlement;
        setupReverse = false;

        currentPlayerId = 0;
        lastSetupSettlement = null;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;

        mode = BuildMode.Settlement;

        RecomputeLargestArmy();
        RecomputeLongestRoad();
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;
        mode = BuildMode.None;

        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;
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
    // TURN / ROLL
    // =========================
    public void RollDiceAndDistribute()
    {
        if (gameOver) return;
        if (phase != GamePhase.Main) return;
        if (awaitingRobberMove) return;
        if (hasRolledThisTurn) return;

        int dice = UnityEngine.Random.Range(1, 7) + UnityEngine.Random.Range(1, 7);
        hasRolledThisTurn = true;

        if (board == null) return;

        if (dice == 7)
        {
            HandleDiscardForSeven();
            awaitingRobberMove = true;
            mode = BuildMode.Robber;
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

        devBoughtThisTurn[currentPlayerId].Clear();
        hasPlayedDevCardThisTurn = false;
        freeRoadsRemaining = 0;

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
            ShowMarker(node, players[currentPlayerId].playerColor, 0.30f);
            AddVP(currentPlayerId, 1);

            if (setupReverse) GrantStartingResources(node, currentPlayerId);

            lastSetupSettlement = node;
            setupStep = SetupStep.PlaceRoad;
            mode = BuildMode.Road;
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

        RecomputeLongestRoad();
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

            RecomputeLongestRoad();
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
        ColorRoad(edge, players[currentPlayerId].playerColor);

        if (!free && enforceBuildCosts) PayRoad(currentPlayerId);

        if (freeRoadsRemaining > 0)
        {
            freeRoadsRemaining--;
            if (freeRoadsRemaining == 0) mode = BuildMode.None;
        }

        RecomputeLongestRoad();
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

        AddVP(currentPlayerId, 1);

        RecomputeLongestRoad();
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

        int victimId = eligible[UnityEngine.Random.Range(0, eligible.Count)];
        if (TryRemoveRandomResource(victimId, out var stolen))
            CurrentPlayer.AddResource(stolen, 1);
    }

    private bool TryRemoveRandomResource(int pid, out ResourceType removed)
    {
        var p = players[pid];
        int total = p.brick + p.lumber + p.wool + p.grain + p.ore;
        removed = ResourceType.Desert;
        if (total <= 0) return false;

        int r = UnityEngine.Random.Range(0, total);

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
    // DEV CARDS (signatures match GameHud)
    // =========================
    public bool CanBuyDevCard()
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;
        if (devDeck.Count <= 0) return false;

        if (!enforceBuildCosts) return true;
        return CurrentPlayer.wool >= 1 && CurrentPlayer.grain >= 1 && CurrentPlayer.ore >= 1;
    }

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

        if (card.Value == DevCardType.VictoryPoint)
        {
            AddVP(currentPlayerId, 1);
            return;
        }

        devHand[currentPlayerId].Add(card.Value);
        devBoughtThisTurn[currentPlayerId].Add(card.Value);
    }

    public void PlayKnightDevCard()
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.Knight)) return;

        hasPlayedDevCardThisTurn = true;
        players[currentPlayerId].knightsPlayed += 1;

        RecomputeLargestArmy();

        awaitingRobberMove = true;
        mode = BuildMode.Robber;
    }

    public void PlayRoadBuildingDevCard()
    {
        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.RoadBuilding)) return;

        hasPlayedDevCardThisTurn = true;
        freeRoadsRemaining = 2;
        mode = BuildMode.Road;
    }

    // ✅ GameHud expects TWO args
    public void PlayYearOfPlentyDevCard(ResourceType a, ResourceType b)
    {
        yearOfPlentyChoiceA = a;
        yearOfPlentyChoiceB = b;

        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.YearOfPlenty)) return;

        hasPlayedDevCardThisTurn = true;

        if (!a.IsTradeableResource() || !b.IsTradeableResource()) return;
        CurrentPlayer.AddResource(a, 1);
        CurrentPlayer.AddResource(b, 1);
    }

    // ✅ GameHud expects ONE arg
    public void PlayMonopolyDevCard(ResourceType resource)
    {
        monopolyChoice = resource;

        if (!CanPlayDevCardThisTurn()) return;
        if (!ConsumeDevCard(currentPlayerId, DevCardType.Monopoly)) return;

        hasPlayedDevCardThisTurn = true;
        if (!resource.IsTradeableResource()) return;

        int taken = 0;
        for (int pid = 0; pid < players.Length; pid++)
        {
            if (pid == currentPlayerId) continue;
            int have = GetResourceCount(players[pid], resource);
            if (have <= 0) continue;
            SetResourceCount(players[pid], resource, 0);
            taken += have;
        }
        CurrentPlayer.AddResource(resource, taken);
    }

    private bool CanPlayDevCardThisTurn()
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;
        if (hasPlayedDevCardThisTurn) return false;
        return true;
    }

    private bool ConsumeDevCard(int pid, DevCardType type)
    {
        if (devBoughtThisTurn[pid].Contains(type)) return false;

        int idx = devHand[pid].IndexOf(type);
        if (idx < 0) return false;

        devHand[pid].RemoveAt(idx);
        return true;
    }

    private void BuildDevDeck()
    {
        devDeck.Clear();
        devDeck.AddRange(Enumerable.Repeat(DevCardType.Knight, 14));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.VictoryPoint, 5));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.RoadBuilding, 2));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.YearOfPlenty, 2));
        devDeck.AddRange(Enumerable.Repeat(DevCardType.Monopoly, 2));

        var rng = new System.Random();
        for (int i = devDeck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (devDeck[i], devDeck[j]) = (devDeck[j], devDeck[i]);
        }
    }

    private DevCardType? DrawDevCard()
    {
        if (devDeck.Count == 0) return null;
        var c = devDeck[0];
        devDeck.RemoveAt(0);
        return c;
    }

    // =========================
    // BANK TRADING (GameHud expects these)
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

        // MVP: no ports yet -> always 4:1
        ratio = 4;

        return GetResourceCount(CurrentPlayer, give) >= ratio;
    }

    public void TradeWithBank(ResourceType give, ResourceType get)
    {
        if (!CanTradeWithBank(give, get, out int ratio)) return;

        int have = GetResourceCount(CurrentPlayer, give);
        SetResourceCount(CurrentPlayer, give, have - ratio);
        CurrentPlayer.AddResource(get, 1);
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
        }
    }

    // =========================
    // LONGEST ROAD (MVP: recompute by brute force each time)
    // =========================
    private void RecomputeLongestRoad()
    {
        if (board == null || board.Edges == null) { LongestRoadHolderId = -1; LongestRoadLength = 0; return; }

        int bestPid = -1;
        int bestLen = 0;

        for (int pid = 0; pid < players.Length; pid++)
        {
            int len = ComputeLongestRoadForPlayer(pid);
            if (len > bestLen)
            {
                bestLen = len;
                bestPid = pid;
            }
        }

        LongestRoadHolderId = bestPid;
        LongestRoadLength = bestLen;
    }

    // simple DFS on edge graph (ok for now)
    private int ComputeLongestRoadForPlayer(int pid)
    {
        var edges = board.Edges.Where(e => e != null && e.ownerId == pid && e.A != null && e.B != null).ToList();
        if (edges.Count == 0) return 0;

        int best = 0;
        foreach (var e in edges)
        {
            best = Mathf.Max(best,
                DFSLongest(pid, e.A, null, new HashSet<RoadEdge>()) ,
                DFSLongest(pid, e.B, null, new HashSet<RoadEdge>()));
        }
        return best;

        int DFSLongest(int player, Intersection node, Intersection from, HashSet<RoadEdge> used)
        {
            int localBest = 0;
            foreach (var ed in node.edges)
            {
                if (ed == null || ed.ownerId != player) continue;
                if (used.Contains(ed)) continue;

                var next = (ed.A == node) ? ed.B : ed.A;
                if (next == null) continue;

                used.Add(ed);
                int length = 1 + DFSLongest(player, next, node, used);
                used.Remove(ed);

                if (length > localBest) localBest = length;
            }
            return localBest;
        }
    }

    // =========================
    // LARGEST ARMY (MVP)
    // =========================
    private void RecomputeLargestArmy()
    {
        int bestPid = -1;
        int best = 0;
        for (int pid = 0; pid < players.Length; pid++)
        {
            int k = players[pid].knightsPlayed;
            if (k > best)
            {
                best = k;
                bestPid = pid;
            }
        }
        LargestArmyHolderId = bestPid;
        LargestArmyCount = best;
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