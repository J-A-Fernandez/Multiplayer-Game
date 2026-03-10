using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class QuickNetUI : MonoBehaviour
{
    [Header("Room")]
    public string roomName = "Catan Room";
    public ushort gamePort = 7777;

    [Header("Game Scene Name (must be in Build Settings)")]
    public string gameSceneName = "Catan_Multiplayer";

    [Header("Discovery")]
    public LanDiscoveryClient discoveryClient;
    public LanDiscoveryHost discoveryHost;

    private void Awake()
    {
        if (discoveryClient == null) discoveryClient = FindFirstObjectByType<LanDiscoveryClient>();
        if (discoveryHost == null) discoveryHost = FindFirstObjectByType<LanDiscoveryHost>();
    }

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        float w = 360f;
        float h = 360f;
        float x = Screen.width - w - 10f;
        float y = 10f;

        GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);

        GUILayout.Label($"Host:{nm.IsHost}  Server:{nm.IsServer}  Client:{nm.IsClient}");

        GUILayout.Space(6);
        GUILayout.Label("Room Name:");
        roomName = GUILayout.TextField(roomName);

        GUILayout.Label("Game Port:");
        ushort.TryParse(GUILayout.TextField(gamePort.ToString()), out gamePort);

        GUILayout.Label("Game Scene:");
        gameSceneName = GUILayout.TextField(gameSceneName);

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null)
        {
            GUILayout.Label("ERROR: UnityTransport missing on NetworkManager");
            GUILayout.EndArea();
            return;
        }

        // ---------- HOST ----------
        GUI.enabled = !nm.IsListening;

        if (GUILayout.Button("Start Host (LAN)"))
        {
            // listen on port
            utp.SetConnectionData("0.0.0.0", gamePort);

            // enable broadcaster
            if (discoveryHost != null)
            {
                discoveryHost.roomName = roomName;
                discoveryHost.gamePort = gamePort;
                discoveryHost.enabled = true;
            }

            // start host
            bool ok = nm.StartHost();
            if (ok)
            {
                // ✅ load game scene via Netcode SceneManager (requires "Enable Scene Management")
                if (nm.SceneManager != null)
                {
                    nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
                }
                else
                {
                    // fallback if SceneManager is null
                    SceneManager.LoadScene(gameSceneName);
                }
            }
        }

        GUILayout.Space(10);

        // ---------- CLIENT ----------
        GUILayout.Label("Discovered Rooms:");

        if (discoveryClient == null)
        {
            GUILayout.Label("(No LanDiscoveryClient in scene)");
        }
        else
        {
            var list = discoveryClient.Sessions;

            if (list.Count == 0)
                GUILayout.Label("(none found yet)");

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{s.roomName}", GUILayout.Width(160));
                GUILayout.Label($"{s.ip}:{s.gamePort}", GUILayout.Width(180));

                if (GUILayout.Button("Join", GUILayout.Width(50)))
                {
                    utp.SetConnectionData(s.ip, s.gamePort);
                    nm.StartClient();
                }

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(10);

        // ---------- SHUTDOWN ----------
        GUI.enabled = nm.IsListening;

        if (GUILayout.Button("Shutdown"))
        {
            nm.Shutdown();
            if (discoveryHost != null) discoveryHost.enabled = false;
        }

        GUI.enabled = true;

        GUILayout.EndArea();
    }
}