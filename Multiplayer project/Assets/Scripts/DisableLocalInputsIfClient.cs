using Unity.Netcode;
using UnityEngine;

public class DisableLocalInputsIfClient : NetworkBehaviour
{
    [Header("Drag any scripts here that should NOT run on pure clients")]
    public Behaviour[] disableOnClient;

    public override void OnNetworkSpawn()
    {
        if (IsServer) return;

        // On pure clients, disable these
        if (disableOnClient != null)
        {
            foreach (var b in disableOnClient)
                if (b != null) b.enabled = false;
        }
    }
}