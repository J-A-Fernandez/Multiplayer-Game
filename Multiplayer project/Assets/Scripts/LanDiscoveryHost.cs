using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class LanDiscoveryHost : MonoBehaviour
{
    [Header("Room")]
    public string roomName = "Catan Room";

    [Header("Ports")]
    public int discoveryPort = 47777;
    public ushort gamePort = 7777;

    [Header("Broadcast")]
    public float broadcastInterval = 1.0f;

    private UdpClient udp;
    private IPEndPoint broadcastEP;
    private float t;

    private void OnEnable()
    {
        try
        {
            udp = new UdpClient();
            udp.EnableBroadcast = true;
            broadcastEP = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
        }
        catch (Exception e)
        {
            Debug.LogError("LanDiscoveryHost init failed: " + e.Message);
        }
    }

    private void OnDisable()
    {
        try { udp?.Close(); } catch { }
        udp = null;
    }

    private void Update()
    {
        if (udp == null) return;

        t += Time.unscaledDeltaTime;
        if (t < broadcastInterval) return;
        t = 0f;

        string msg = $"CATAN|{roomName}|{gamePort}";
        byte[] data = Encoding.UTF8.GetBytes(msg);

        try
        {
            udp.Send(data, data.Length, broadcastEP);
        }
        catch
        {
            // ignore transient errors
        }
    }
}