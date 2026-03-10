using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class QuickNetUI : MonoBehaviour
{
    public string address = "127.0.0.1";
    public ushort port = 7777;

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);

        GUILayout.Label($"Host: {nm.IsHost}  Server: {nm.IsServer}  Client: {nm.IsClient}");

        GUILayout.BeginHorizontal();
        GUILayout.Label("IP:", GUILayout.Width(30));
        address = GUILayout.TextField(address, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Port:", GUILayout.Width(30));
        ushort.TryParse(GUILayout.TextField(port.ToString(), GUILayout.Width(150)), out port);
        GUILayout.EndHorizontal();

        var utp = nm.GetComponent<UnityTransport>();
        if (utp != null)
        {
            utp.SetConnectionData(address, port);
        }

        GUI.enabled = !nm.IsListening;

        if (GUILayout.Button("Start Host"))
            nm.StartHost();

        if (GUILayout.Button("Start Client"))
            nm.StartClient();

        GUI.enabled = nm.IsListening;

        if (GUILayout.Button("Shutdown"))
            nm.Shutdown();

        GUI.enabled = true;

        GUILayout.EndArea();
    }
}