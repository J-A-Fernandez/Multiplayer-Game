using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class QuickNetUI : MonoBehaviour
{
    public string connectAddress = "127.0.0.1";
    public ushort port = 7777;

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        var utp = nm.GetComponent<UnityTransport>();

        GUILayout.BeginArea(new Rect(10, 10, 340, 230), GUI.skin.box);
        GUILayout.Label($"Mode: {(nm.IsHost ? "Host" : nm.IsClient ? "Client" : "Stopped")}");
        GUILayout.Space(6);

        GUILayout.Label("Client connect address:");
        connectAddress = GUILayout.TextField(connectAddress);

        GUILayout.Label("Port:");
        ushort.TryParse(GUILayout.TextField(port.ToString()), out port);

        GUILayout.Space(10);

        if (!nm.IsListening)
        {
            if (GUILayout.Button("Start Host (LAN)", GUILayout.Height(30)))
            {
                // listen on LAN (0.0.0.0) so other machines can connect
                if (utp != null)
                    utp.SetConnectionData("127.0.0.1", port, "0.0.0.0");

                nm.StartHost();
            }

            if (GUILayout.Button("Start Client", GUILayout.Height(30)))
            {
                if (utp != null)
                    utp.SetConnectionData(connectAddress, port);

                nm.StartClient();
            }
        }
        else
        {
            GUILayout.Label($"LocalClientId: {nm.LocalClientId}");
            if (nm.IsServer) GUILayout.Label($"Connected: {nm.ConnectedClientsList.Count}");

            if (GUILayout.Button("Shutdown", GUILayout.Height(30)))
                nm.Shutdown();
        }

        GUILayout.EndArea();
    }
}