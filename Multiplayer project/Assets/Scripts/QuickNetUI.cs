using Unity.Netcode;
using UnityEngine;

public class QuickNetUI : MonoBehaviour
{
    [Header("Client connect target")]
    public string address = "127.0.0.1";
    public ushort port = 7777;

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 220), GUI.skin.box);

        GUILayout.Label("Multiplayer");
        GUILayout.Space(5);

        GUILayout.Label("Address:");
        address = GUILayout.TextField(address);

        GUILayout.Label("Port:");
        ushort.TryParse(GUILayout.TextField(port.ToString()), out port);

        GUILayout.Space(8);

        if (!NetworkManager.Singleton.IsListening)
        {
            if (GUILayout.Button("Start Host", GUILayout.Height(30)))
            {
                NetworkManager.Singleton.StartHost();
            }

            if (GUILayout.Button("Start Client", GUILayout.Height(30)))
            {
                // Set transport address/port before connecting
                var utp = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (utp != null)
                {
                    utp.SetConnectionData(address, port);
                }
                NetworkManager.Singleton.StartClient();
            }
        }
        else
        {
            GUILayout.Label($"Running as: " +
                (NetworkManager.Singleton.IsHost ? "Host" :
                 NetworkManager.Singleton.IsServer ? "Server" : "Client"));

            if (GUILayout.Button("Shutdown", GUILayout.Height(30)))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        GUILayout.EndArea();
    }
}