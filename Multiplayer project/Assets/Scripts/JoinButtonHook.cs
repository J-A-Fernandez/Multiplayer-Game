using UnityEngine;

public class JoinButtonHook : MonoBehaviour
{
    public UGSRoomManager room;

    private void Awake()
    {
        if (room == null) room = FindFirstObjectByType<UGSRoomManager>();
    }

    public void Host()
    {
        if (room != null) room.Host();
    }

    public void Join()
    {
        if (room != null) room.Join();
    }

    public void Shutdown()
    {
        if (room != null) room.Shutdown();
    }
}