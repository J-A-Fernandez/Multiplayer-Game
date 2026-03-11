using TMPro;
using UnityEngine;

public class JoinButtonHook : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public UGSRoomManager room;
    public TMP_InputField joinCodeInput;

    public void Host()
    {
        room.HostRelay();
    }

    public void Join()
    {
        room.JoinRelay(joinCodeInput.text);
    }
}