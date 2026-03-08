using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    [Header("Refs")]
    public BuildController build;

    [Header("Text")]
    public TMP_Text statusText;
    public TMP_Text resourcesText;

    [Header("Buttons")]
    public Button rollButton;
    public Button settlementButton;
    public Button roadButton;
    public Button cancelButton;
    public Button endTurnButton;

    private void Awake()
    {
        if (build == null) build = FindFirstObjectByType<BuildController>();

        // Hook up button events in code (so you don’t have to do OnClick manually)
        if (rollButton) rollButton.onClick.AddListener(() => build.RollDiceAndDistribute());
        if (settlementButton) settlementButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.Settlement);
        if (roadButton) roadButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.Road);
        if (cancelButton) cancelButton.onClick.AddListener(() => build.mode = BuildController.BuildMode.None);
        if (endTurnButton) endTurnButton.onClick.AddListener(() => build.EndTurn());
    }

    private void Update()
    {
        if (build == null) return;
        bool inSetup = (build.phase == BuildController.GamePhase.Setup);

        if (rollButton) rollButton.interactable = !inSetup;
        if (endTurnButton) endTurnButton.interactable = !inSetup;

        if (cancelButton) cancelButton.interactable = !inSetup;

        if (settlementButton) settlementButton.interactable = !inSetup;
        if (roadButton) roadButton.interactable = !inSetup;
        // Status line
        if (statusText)
        {
            statusText.text =
                $"Player: {build.currentPlayerId}\n" +
                $"Phase: {build.phase}\n" +
                $"Mode: {build.mode}";
        }

        if (statusText)
        {
            var p = build.players[build.currentPlayerId];
            statusText.color = p.playerColor;   // uses your PlayerState color
        }

        // Resources for current player
        if (resourcesText)
        {
            var p = build.players[build.currentPlayerId];
            resourcesText.text =
                $"Brick: {p.brick}\n" +
                $"Lumber: {p.lumber}\n" +
                $"Wool: {p.wool}\n" +
                $"Grain: {p.grain}\n" +
                $"Ore: {p.ore}";
        }
    }
}