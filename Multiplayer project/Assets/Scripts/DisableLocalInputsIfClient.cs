using Unity.Netcode;
using UnityEngine;

public class DisableLocalInputsIfClient : MonoBehaviour
{
    [Header("Disable these on CLIENT-only")]
    public Behaviour[] disableOnClient;

    [Header("Enable these on CLIENT-only")]
    public Behaviour[] enableOnClient;

    private void Start()
    {
        Apply();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += _ => Apply();
            NetworkManager.Singleton.OnServerStarted += Apply;
        }
    }

    private void Apply()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        bool clientOnly = nm.IsClient && !nm.IsServer;

        if (disableOnClient != null)
            foreach (var b in disableOnClient)
                if (b != null) b.enabled = !clientOnly;

        if (enableOnClient != null)
            foreach (var b in enableOnClient)
                if (b != null) b.enabled = clientOnly;
    }
}