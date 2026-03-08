using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    public BuildController build;

    [Header("Text")]
    public TMP_Text statusText;
    public TMP_Text resourcesText;
    public TMP_Text devCardsText;
    public TMP_Text tradeInfoText;

    [Header("Buttons (Core)")]
    public Button rollButton;
    public Button settlementButton;
    public Button roadButton;
    public Button cityButton;
    public Button cancelButton;
    public Button endTurnButton;

    [Header("Dev Cards UI")]
    public Button buyDevCardButton;
    public Button playKnightButton;
    public Button playRoadBuildingButton;
    public Button playYearOfPlentyButton;
    public Button playMonopolyButton;

    public TMP_Dropdown yearA;
    public TMP_Dropdown yearB;
    public TMP_Dropdown monopolyType;

    [Header("Trading UI")]
    public TMP_Dropdown tradeGive;
    public TMP_Dropdown tradeGet;
    public Button tradeButton;

    private readonly ResourceType[] resourceOptions =
    {
        ResourceType.Brick, ResourceType.Lumber, ResourceType.Wool, ResourceType.Grain, ResourceType.Ore
    };

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();

        if (rollButton) rollButton.onClick.AddListener(() => build.RollDiceAndDistribute());
        if (settlementButton) settlementButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.Settlement);
        if (roadButton) roadButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.Road);
        if (cityButton) cityButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.City);
        if (cancelButton) cancelButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.None);
        if (endTurnButton) endTurnButton.onClick.AddListener(() => build.EndTurn());

        SetupDropdown(yearA);
        SetupDropdown(yearB);
        SetupDropdown(monopolyType);
        SetupDropdown(tradeGive);
        SetupDropdown(tradeGet);

        if (buyDevCardButton) buyDevCardButton.onClick.AddListener(() => build.BuyDevCard());
        if (playKnightButton) playKnightButton.onClick.AddListener(() => build.PlayKnightDevCard());
        if (playRoadBuildingButton) playRoadBuildingButton.onClick.AddListener(() => build.PlayRoadBuildingDevCard());

        if (playYearOfPlentyButton) playYearOfPlentyButton.onClick.AddListener(() =>
        {
            var a = resourceOptions[Mathf.Clamp(yearA ? yearA.value : 0, 0, resourceOptions.Length - 1)];
            var b = resourceOptions[Mathf.Clamp(yearB ? yearB.value : 0, 0, resourceOptions.Length - 1)];
            build.PlayYearOfPlentyDevCard(a, b);
        });

        if (playMonopolyButton) playMonopolyButton.onClick.AddListener(() =>
        {
            var t = resourceOptions[Mathf.Clamp(monopolyType ? monopolyType.value : 0, 0, resourceOptions.Length - 1)];
            build.PlayMonopolyDevCard(t);
        });

        if (tradeButton) tradeButton.onClick.AddListener(() =>
        {
            var give = resourceOptions[Mathf.Clamp(tradeGive ? tradeGive.value : 0, 0, resourceOptions.Length - 1)];
            var get = resourceOptions[Mathf.Clamp(tradeGet ? tradeGet.value : 0, 0, resourceOptions.Length - 1)];
            build.TradeWithBank(give, get);
        });
    }

    private void SetupDropdown(TMP_Dropdown dd)
    {
        if (dd == null) return;
        if (dd.options != null && dd.options.Count > 0) return;

        dd.options.Clear();
        dd.options.Add(new TMP_Dropdown.OptionData("Brick"));
        dd.options.Add(new TMP_Dropdown.OptionData("Lumber"));
        dd.options.Add(new TMP_Dropdown.OptionData("Wool"));
        dd.options.Add(new TMP_Dropdown.OptionData("Grain"));
        dd.options.Add(new TMP_Dropdown.OptionData("Ore"));
        dd.value = 0;
        dd.RefreshShownValue();
    }

    private void Update()
    {
        if (build == null) return;

        bool inSetup = (build.phase == BuildController.GamePhase.Setup);
        bool gameOver = build.GameOver;

        var p = build.players[build.currentPlayerId];

        if (rollButton) rollButton.interactable = !inSetup && !gameOver && !build.HasRolledThisTurn && !build.AwaitingRobberMove;
        if (endTurnButton) endTurnButton.interactable = !inSetup && !gameOver && !build.AwaitingRobberMove;

        bool canBuild = !inSetup && !gameOver && !build.AwaitingRobberMove;
        if (settlementButton) settlementButton.interactable = canBuild;
        if (roadButton) roadButton.interactable = canBuild;
        if (cityButton) cityButton.interactable = canBuild;
        if (cancelButton) cancelButton.interactable = canBuild;

        // Dev buttons
        bool canPlayDev = !inSetup && !gameOver && build.HasRolledThisTurn && !build.AwaitingRobberMove;
        if (buyDevCardButton) buyDevCardButton.interactable = !inSetup && !gameOver && build.CanBuyDevCard();
        if (playKnightButton) playKnightButton.interactable = canPlayDev && p.devKnight > 0;
        if (playRoadBuildingButton) playRoadBuildingButton.interactable = canPlayDev && p.devRoadBuilding > 0;
        if (playYearOfPlentyButton) playYearOfPlentyButton.interactable = canPlayDev && p.devYearOfPlenty > 0;
        if (playMonopolyButton) playMonopolyButton.interactable = canPlayDev && p.devMonopoly > 0;

        // Trade UI
        if (tradeButton || tradeInfoText)
        {
            var give = resourceOptions[Mathf.Clamp(tradeGive ? tradeGive.value : 0, 0, resourceOptions.Length - 1)];
            var get = resourceOptions[Mathf.Clamp(tradeGet ? tradeGet.value : 0, 0, resourceOptions.Length - 1)];

            bool ok = build.CanTradeWithBank(give, get, out int ratio);
            if (tradeInfoText) tradeInfoText.text = $"Best rate for {give}: {ratio}:1";
            if (tradeButton) tradeButton.interactable = ok && canBuild;
        }

        if (statusText)
        {
            statusText.text =
                $"Player: {build.currentPlayerId}\n" +
                $"Phase: {build.phase}\n" +
                $"Mode: {build.mode}\n" +
                $"VP: {p.victoryPoints}/{build.targetVictoryPoints}\n" +
                $"Longest Road: {(build.LongestRoadHolderId == -1 ? "None" : $"P{build.LongestRoadHolderId} ({build.LongestRoadLength})")}\n" +
                $"Largest Army: {(build.LargestArmyHolderId == -1 ? "None" : $"P{build.LargestArmyHolderId} ({build.LargestArmyCount})")}\n" +
                $"Dev Deck: {build.DevDeckCount}";
            statusText.color = p.playerColor;
        }

        if (resourcesText)
        {
            resourcesText.text =
                $"Brick: {p.brick}\n" +
                $"Lumber: {p.lumber}\n" +
                $"Wool: {p.wool}\n" +
                $"Grain: {p.grain}\n" +
                $"Ore: {p.ore}";
        }

        if (devCardsText)
        {
            devCardsText.text =
                $"Dev (playable): K={p.devKnight}, RB={p.devRoadBuilding}, YP={p.devYearOfPlenty}, M={p.devMonopoly}, VP={p.devVictoryPoint}\n" +
                $"New (locked): K={p.newDevKnight}, RB={p.newDevRoadBuilding}, YP={p.newDevYearOfPlenty}, M={p.newDevMonopoly}";
        }
    }
}