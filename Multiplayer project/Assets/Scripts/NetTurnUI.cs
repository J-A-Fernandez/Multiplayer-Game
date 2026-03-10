using Unity.Netcode;
using UnityEngine;

public class NetTurnUI : MonoBehaviour
{
    public NetworkCatanManager net;
    public BuildController build;

    private void Awake()
    {
        if (net == null) net = FindFirstObjectByType<NetworkCatanManager>();
        if (build == null) build = FindFirstObjectByType<BuildController>();
    }

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || net == null || build == null) return;

        int localPid = (int)nm.LocalClientId;
        bool myTurn = (localPid == build.currentPlayerId);

        GUILayout.BeginArea(new Rect(10, 220, 300, 240), GUI.skin.box);
        GUILayout.Label($"You: P{localPid}    Turn: P{build.currentPlayerId}");
        GUILayout.Label($"Phase: {build.phase}   Mode: {build.mode}");
        GUILayout.Label($"Rolled: {build.HasRolledThisTurn}   Robber: {build.AwaitingRobberMove}");

        GUI.enabled = myTurn;

        if (build.phase == BuildController.GamePhase.Main)
        {
            if (GUILayout.Button("ROLL"))
                net.RequestRollServerRpc();

            if (GUILayout.Button("END TURN"))
                net.RequestEndTurnServerRpc();
        }
        else
        {
            GUILayout.Label("Setup: place Settlement then Road (no roll/end turn)");
        }

        GUI.enabled = true;
        GUILayout.EndArea();
    }
}