using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class LanDiscoveryClient : MonoBehaviour
{
    [Serializable]
    public class Session
    {
        public string roomName;
        public string ip;
        public ushort gamePort;
        public float lastSeenTime;
    }

    [Header("Ports")]
    public int discoveryPort = 47777;

    [Header("Cleanup")]
    public float sessionTimeoutSeconds = 3.0f;

    private UdpClient udp;
    private IPEndPoint anyEP;

    private readonly List<Session> sessions = new();
    public IReadOnlyList<Session> Sessions => sessions;

    private void OnEnable()
    {
        try
        {
            anyEP = new IPEndPoint(IPAddress.Any, discoveryPort);
            udp = new UdpClient();
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(anyEP);
        }
        catch (Exception e)
        {
            Debug.LogError("LanDiscoveryClient init failed: " + e.Message);
        }
    }

    private void OnDisable()
    {
        try { udp?.Close(); } catch { }
        udp = null;
        sessions.Clear();
    }

    private void Update()
    {
        if (udp == null) return;

        // Receive all packets available this frame
        while (udp.Available > 0)
        {
            try
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udp.Receive(ref sender);
                string msg = Encoding.UTF8.GetString(data);

                // CATAN|room|port
                if (!msg.StartsWith("CATAN|")) continue;

                string[] parts = msg.Split('|');
                if (parts.Length < 3) continue;

                string room = parts[1];
                if (!ushort.TryParse(parts[2], out ushort gamePort)) continue;

                string ip = sender.Address.ToString();

                // ✅ filter bad/unusable addresses
                if (string.IsNullOrEmpty(ip)) continue;
                if (ip == "127.0.0.1" || ip == "::1") continue;
                if (ip.StartsWith("fe80:")) continue; // ignore IPv6 link-local

                Upsert(room, ip, gamePort);
            }
            catch
            {
                break;
            }
        }

        // Expire old sessions
        float now = Time.unscaledTime;
        for (int i = sessions.Count - 1; i >= 0; i--)
        {
            if (now - sessions[i].lastSeenTime > sessionTimeoutSeconds)
                sessions.RemoveAt(i);
        }
    }

    private void Upsert(string room, string ip, ushort port)
    {
        float now = Time.unscaledTime;

        for (int i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].ip == ip && sessions[i].gamePort == port)
            {
                sessions[i].roomName = room;
                sessions[i].lastSeenTime = now;
                return;
            }
        }

        sessions.Add(new Session
        {
            roomName = room,
            ip = ip,
            gamePort = port,
            lastSeenTime = now
        });
    }
}