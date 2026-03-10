using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class QuickNetUI : MonoBehaviour
{
    [Header("Room")]
    public string roomName = "Catan Room";
    public ushort gamePort = 7777;

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

        float w = 320f;
        float h = 320f;
        float x = Screen.width - w - 10f;
        float y = 10f;

        GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);

        GUILayout.Label($"Host:{nm.IsHost} Server:{nm.IsServer} Client:{nm.IsClient}");

        GUILayout.Space(6);
        GUILayout.Label("Room Name:");
        roomName = GUILayout.TextField(roomName);

        GUILayout.Label("Game Port:");
        ushort.TryParse(GUILayout.TextField(gamePort.ToString()), out gamePort);

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null)
        {
            GUILayout.Label("ERROR: UnityTransport missing on NetworkManager");
            GUILayout.EndArea();
            return;
        }

        GUI.enabled = !nm.IsListening;

        if (GUILayout.Button("Start Host (LAN)"))
        {
            // Configure transport to listen on gamePort
            utp.SetConnectionData("0.0.0.0", gamePort);

            // Enable host broadcaster
            if (discoveryHost != null)
            {
                discoveryHost.roomName = roomName;
                discoveryHost.gamePort = gamePort;
                discoveryHost.enabled = true;
            }

            nm.StartHost();
        }

        GUILayout.Space(8);
        GUILayout.Label("Discovered Rooms:");

        if (discoveryClient == null)
        {
            GUILayout.Label("(No LanDiscoveryClient in scene)");
        }
        else
        {
            var list = discoveryClient.Sessions;
            if (list.Count == 0) GUILayout.Label("(none found yet)");

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{s.roomName}", GUILayout.Width(160));
                GUILayout.Label($"{s.ip}:{s.gamePort}", GUILayout.Width(120));

                if (GUILayout.Button("Join", GUILayout.Width(50)))
                {
                    // Configure transport to connect to host IP
                    utp.SetConnectionData(s.ip, s.gamePort);
                    nm.StartClient();
                }

                GUILayout.EndHorizontal();
            }
        }

        GUI.enabled = nm.IsListening;

        if (GUILayout.Button("Shutdown"))
        {
            nm.Shutdown();

            // Stop broadcasting if we were host
            if (discoveryHost != null) discoveryHost.enabled = false;
        }

        GUI.enabled = true;

        GUILayout.EndArea();
    }
}