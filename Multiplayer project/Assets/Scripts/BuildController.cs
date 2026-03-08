using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("How many ports to place (classic base game uses 9).")]
    public int portCount = 9;

    // Setup state
    private SetupStep setupStep = SetupStep.PlaceSettlement;
    private bool setupReverse = false;
    private Intersection lastSetupSettlement = null;

    // Main state
    [SerializeField] private bool hasRolledThisTurn = false;
    [SerializeField] private bool awaitingRobberMove = false;

    // Game over
    private bool gameOver = false;
    private int winnerId = -1;

    // Longest road state
    private int longestRoadHolderId = -1;
    private int longestRoadLength = 0;

    // Largest army state
    private int largestArmyHolderId = -1;
    private int largestArmyCount = 0;

    // Ports state
    private bool portsAssigned = false;

    // ===== Player-to-player trade state (NEW) =====
    private TradeOffer activeOffer = new TradeOffer();
    private int nextOfferId = 1;

    public bool HasActiveOffer => activeOffer.active;
    public TradeOffer ActiveOffer => activeOffer;

    public PlayerState CurrentPlayer => players[currentPlayerId];
    public bool HasRolledThisTurn => hasRolledThisTurn;
    public bool AwaitingRobberMove => awaitingRobberMove;
    public bool GameOver => gameOver;
    public int WinnerId => winnerId;

    public int LongestRoadHolderId => longestRoadHolderId;
    public int LongestRoadLength => longestRoadLength;

    public int LargestArmyHolderId => largestArmyHolderId;
    public int LargestArmyCount => largestArmyCount;

    private void Awake()
    {
        if (players == null || players.Length == 0)
            players = new PlayerState[4];

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerState();
            players[i].playerId = i;
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
        }

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

        mode = BuildMode.Settlement;
    }

    private void EndSetup()
    {
        phase = GamePhase.Main;
        currentPlayerId = 0;

        hasRolledThisTurn = false;
        awaitingRobberMove = false;

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
            if (currentPlayerId == n - 1)
            {
                setupReverse = true;
            }
            else currentPlayerId++;
        }
        else
        {
            if (currentPlayerId == 0) EndSetup();
            else currentPlayerId--;
        }
    }

    // =========================
    // ROLL / PAYOUT / TURN
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

        // cancel any open offer at end of turn (optional rule)
        if (activeOffer.active) CancelOffer(currentPlayerId);

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

        if (enforceBuildCosts && !CanAffordRoad(currentPlayerId)) return;

        edge.ownerId = currentPlayerId;
        ColorRoad(edge, CurrentPlayer.playerColor);

        if (enforceBuildCosts) PayRoad(currentPlayerId);

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
    // BANK TRADING + PORTS (your existing)
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

            if (PortMatchesResource2to1(node.port, give))
                best = Mathf.Min(best, 2);
            else if (node.port == PortType.ThreeToOne)
                best = Mathf.Min(best, 3);
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
        int have = GetResourceCount(CurrentPlayer, give);
        return have >= ratio;
    }

    public void TradeWithBank(ResourceType give, ResourceType get)
    {
        if (!CanTradeWithBank(give, get, out int ratio)) return;

        int have = GetResourceCount(CurrentPlayer, give);
        SetResourceCount(CurrentPlayer, give, have - ratio);
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
            board.Nodes.Count == 0 || board.Edges.Count == 0) return;

        foreach (var n in board.Nodes)
            if (n != null) n.port = PortType.None;

        var boundaryEdges = new List<RoadEdge>();
        foreach (var e in board.Edges)
        {
            if (e == null) continue;
            if (e.adjacentTiles != null && e.adjacentTiles.Count == 1)
                boundaryEdges.Add(e);
        }
        if (boundaryEdges.Count == 0) return;

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
                done = true;
                break;
            }
            if (!done) break;
        }

        portsAssigned = true;
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
    // A) PLAYER-TO-PLAYER TRADING (NEW)
    // =========================
    public bool CanProposeOffer(int fromPlayerId, ResourceType giveType, int giveAmount, ResourceType getType, int getAmount)
    {
        if (gameOver) return false;
        if (phase != GamePhase.Main) return false;
        if (awaitingRobberMove) return false;
        if (requireRollBeforeBuild && !hasRolledThisTurn) return false;

        if (fromPlayerId != currentPlayerId) return false; // only current turn player proposes
        if (!giveType.IsTradeableResource() || !getType.IsTradeableResource()) return false;
        if (giveType == getType) return false;
        if (giveAmount <= 0 || getAmount <= 0) return false;

        return GetResourceCount(players[fromPlayerId], giveType) >= giveAmount;
    }

    public bool ProposeOffer(int toPlayerId, ResourceType giveType, int giveAmount, ResourceType getType, int getAmount)
    {
        if (!CanProposeOffer(currentPlayerId, giveType, giveAmount, getType, getAmount))
            return false;

        activeOffer = new TradeOffer
        {
            active = true,
            offerId = nextOfferId++,
            fromPlayerId = currentPlayerId,
            toPlayerId = toPlayerId, // -1 = any
            giveType = giveType,
            giveAmount = giveAmount,
            getType = getType,
            getAmount = getAmount
        };

        Debug.Log($"OFFER #{activeOffer.offerId}: P{activeOffer.fromPlayerId} offers {giveAmount} {giveType} for {getAmount} {getType} -> to {(toPlayerId < 0 ? "ANY" : $"P{toPlayerId}")}");
        return true;
    }

    public bool CancelOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId != activeOffer.fromPlayerId) return false;

        Debug.Log($"OFFER #{activeOffer.offerId} cancelled by P{byPlayerId}");
        activeOffer = TradeOffer.None;
        return true;
    }

    public bool DeclineOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId == activeOffer.fromPlayerId) return false;

        // If targeted, only that target can decline (or anyone if open)
        if (activeOffer.toPlayerId != -1 && byPlayerId != activeOffer.toPlayerId) return false;

        Debug.Log($"OFFER #{activeOffer.offerId} declined by P{byPlayerId}");
        activeOffer = TradeOffer.None;
        return true;
    }

    public bool AcceptOffer(int byPlayerId)
    {
        if (!activeOffer.active) return false;
        if (byPlayerId == activeOffer.fromPlayerId) return false;

        if (activeOffer.toPlayerId != -1 && byPlayerId != activeOffer.toPlayerId) return false;

        // verify both sides can pay
        var proposer = players[activeOffer.fromPlayerId];
        var accepter = players[byPlayerId];

        if (GetResourceCount(proposer, activeOffer.giveType) < activeOffer.giveAmount) return false;
        if (GetResourceCount(accepter, activeOffer.getType) < activeOffer.getAmount) return false;

        // transfer proposer -> accepter
        SetResourceCount(proposer, activeOffer.giveType, GetResourceCount(proposer, activeOffer.giveType) - activeOffer.giveAmount);
        SetResourceCount(accepter, activeOffer.giveType, GetResourceCount(accepter, activeOffer.giveType) + activeOffer.giveAmount);

        // transfer accepter -> proposer
        SetResourceCount(accepter, activeOffer.getType, GetResourceCount(accepter, activeOffer.getType) - activeOffer.getAmount);
        SetResourceCount(proposer, activeOffer.getType, GetResourceCount(proposer, activeOffer.getType) + activeOffer.getAmount);

        Debug.Log($"OFFER #{activeOffer.offerId} accepted by P{byPlayerId}: trade completed.");
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
        if (board == null || board.Nodes == null || board.Edges == null) return;

        int n = players.Length;
        var bestPlayers = new List<int>(4);
        int bestLen = 0;

        for (int pid = 0; pid < n; pid++)
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
            int len = DFSLongestFromNode(node, playerId, used);
            if (len > best) best = len;
        }

        return best;
    }

    private int DFSLongestFromNode(Intersection node, int playerId, HashSet<RoadEdge> used)
    {
        if (node == null) return 0;

        // Opponent building blocks continuation
        if (node.building != null && node.building.ownerId != playerId)
            return 0;

        int best = 0;

        foreach (var e in node.edges)
        {
            if (e == null) continue;
            if (e.ownerId != playerId) continue;
            if (used.Contains(e)) continue;

            used.Add(e);
            var next = (e.A == node) ? e.B : e.A;

            int len = 1 + DFSLongestFromNode(next, playerId, used);
            if (len > best) best = len;

            used.Remove(e);
        }

        return best;
    }

    // =========================
    // LARGEST ARMY (+2 VP)
    // (You already increment knightsPlayed when you play Knights)
    // =========================
    private void RecomputeLargestArmyAndUpdateCard()
    {
        int n = players.Length;
        int best = 0;
        var bestPlayers = new List<int>(4);

        for (int pid = 0; pid < n; pid++)
        {
            int k = players[pid].knightsPlayed;
            if (k > best) { best = k; bestPlayers.Clear(); bestPlayers.Add(pid); }
            else if (k == best) bestPlayers.Add(pid);
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

            int discardCount = total / 2;
            for (int i = 0; i < discardCount; i++)
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
}