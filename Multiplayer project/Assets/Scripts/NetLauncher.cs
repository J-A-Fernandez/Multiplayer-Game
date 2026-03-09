using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetLauncher : MonoBehaviour
{
    public TMP_InputField addressInput;
    public ushort port = 7777;

    public void Host()
    {
        var nm = NetworkManager.Singleton;
        var utp = nm.GetComponent<UnityTransport>();

        // Signature: SetConnectionData(string ipv4Address, ushort port, string listenAddress = null)
        // For host/server: listenAddress should usually be 0.0.0.0
        utp.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        nm.StartHost();
        Debug.Log("Hosting...");
    }

    public void Join()
    {
        var nm = NetworkManager.Singleton;
        var utp = nm.GetComponent<UnityTransport>();

        string addr = (addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text))
            ? addressInput.text.Trim()
            : "127.0.0.1";

        utp.SetConnectionData(addr, port);
        nm.StartClient();
        Debug.Log($"Joining {addr}:{port}...");
    }
}