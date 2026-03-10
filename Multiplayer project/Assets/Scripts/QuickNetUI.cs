using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class QuickNetUI : MonoBehaviour
{
    public string hostIp = "192.168.1.23";
    public ushort port = 7777;

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);
        GUILayout.Label("Host IP:");
        hostIp = GUILayout.TextField(hostIp);

        GUILayout.Label("Port:");
        ushort.TryParse(GUILayout.TextField(port.ToString()), out port);

        var nm = NetworkManager.Singleton;
        if (nm == null) { GUILayout.Label("No NetworkManager in scene"); GUILayout.EndArea(); return; }

        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null) { GUILayout.Label("No UnityTransport on NetworkManager"); GUILayout.EndArea(); return; }

        if (GUILayout.Button("Start Host"))
        {
            utp.SetConnectionData("0.0.0.0", port); // listen
            nm.StartHost();
        }

        if (GUILayout.Button("Start Client"))
        {
            utp.SetConnectionData(hostIp, port);    // connect to host
            nm.StartClient();
        }

        GUILayout.Label($"IsServer={nm.IsServer} IsClient={nm.IsClient} IsHost={nm.IsHost}");
        GUILayout.EndArea();
    }
}