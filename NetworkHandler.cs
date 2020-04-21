using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class NetworkHandler
{
    private const long ACK_INTERVAL = TimeSpan.TicksPerMillisecond * 20;
    private ConcurrentDictionary<int, TcpClient> tcpMappings = new ConcurrentDictionary<int, TcpClient>();
    private ConcurrentDictionary<int, IPEndPoint> udpMappings = new ConcurrentDictionary<int, IPEndPoint>();
    private ConcurrentDictionary<int, long> udpIgnore = new ConcurrentDictionary<int, long>();
    private ConcurrentDictionary<int, ReliableHandler> reliableHandlers = new ConcurrentDictionary<int, ReliableHandler>();
    private TunnelServer tunnelServer;
    private TunnelClient tunnelClient;
    private byte[] sendBuffer = new byte[516];
    //Stats
    private long tcpBytesReceivedSecond = 0;
    private long tcpBytesSentSecond = 0;
    private long tcpBytesReceivedTotal = 0;
    private long tcpBytesSentTotal = 0;
    private long udpBytesReceivedSecond = 0;
    private long udpBytesSentSecond = 0;
    private long udpBytesReceivedTotal = 0;
    private long udpBytesSentTotal = 0;
    private long retransmitBytes = 0;

    public void SetServer(TunnelServer tunnelServer)
    {
        this.tunnelServer = tunnelServer;
    }

    public void SetClient(TunnelClient tunnelClient)
    {
        this.tunnelClient = tunnelClient;
    }

    public void HandleUDPMessage(byte[] data, int length, IPEndPoint endpoint)
    {
        if (length < 16)
        {
            return;
        }
        int clientID = ReadInt32(data, 0);
        int sequence = ReadInt32(data, 4);
        int ack = ReadInt32(data, 8);
        int dataLength = ReadInt32(data, 12);
        if (length != (16 + dataLength))
        {
            return;
        }
        udpBytesReceivedSecond += length;
        udpBytesReceivedTotal += length;
        if (udpIgnore.TryGetValue(clientID, out long ignoreTime))
        {
            long currentTime = DateTime.UtcNow.Ticks;
            if (ignoreTime > currentTime)
            {
                return;
            }
            else
            {
                udpIgnore.TryRemove(clientID, out long _);
            }
        }
        if (!udpMappings.ContainsKey(clientID))
        {
            Console.WriteLine("Adding new connection: " + clientID);
            if (tunnelServer != null)
            {
                TcpClient newClient = tunnelServer.GetNewConnection(clientID);
                tcpMappings.TryAdd(clientID, newClient);
            }
            udpMappings.TryAdd(clientID, new IPEndPoint(endpoint.Address, endpoint.Port));
        }
        ReliableHandler rh = GetClient(clientID);
        rh.HandleUDPMessage(data, dataLength, sequence, ack);
    }

    public void HandleTCPMessage(int clientID, byte[] data, int length)
    {
        tcpBytesReceivedSecond += length;
        tcpBytesReceivedTotal += length;
        ReliableHandler rh = GetClient(clientID);
        rh.HandleTCPData(data, length);
    }

    public void ConnectClient(int clientID, TcpClient newClient, IPEndPoint udpServer)
    {
        tcpMappings.TryAdd(clientID, newClient);
        udpMappings.TryAdd(clientID, udpServer);
    }

    public ReliableHandler GetClient(int clientID)
    {
        ReliableHandler retVal = null;
        if (!reliableHandlers.TryGetValue(clientID, out retVal))
        {
            retVal = new ReliableHandler(clientID);
            long currentTime = DateTime.UtcNow.Ticks;
            retVal.lastUDPSend = currentTime;
            retVal.lastUDPReceive = currentTime;
            retVal.lastTCPReceive = currentTime;
            reliableHandlers.TryAdd(clientID, retVal);
        }
        return retVal;
    }

    public bool ShouldDisconnect(int clientID)
    {
        if (reliableHandlers.TryGetValue(clientID, out ReliableHandler reliableHandler))
        {
            long currentTime = DateTime.UtcNow.Ticks;
            return (currentTime > (reliableHandler.lastUDPReceive + TimeSpan.TicksPerMinute)) || (currentTime > (reliableHandler.lastTCPReceive + TimeSpan.TicksPerMinute));
        }
        return udpIgnore.ContainsKey(clientID);
    }

    public void ForgetClient(int clientID)
    {
        //Ignore client
        udpIgnore.TryAdd(clientID, DateTime.UtcNow.Ticks + TimeSpan.TicksPerHour);
        reliableHandlers.TryRemove(clientID, out ReliableHandler _);
    }

    public bool GetUDPMessage(out byte[] data, out int length, out IPEndPoint endPoint)
    {
        long currentTime = DateTime.UtcNow.Ticks;
        foreach (KeyValuePair<int, ReliableHandler> kvp in reliableHandlers)
        {
            int clientID = kvp.Key;
            if (udpMappings.TryGetValue(clientID, out endPoint))
            {
                ReliableHandler rh = kvp.Value;
                rh.SendToUDP(out int sendSequence, out int sendACK, out byte[] sendData);
                //Skip ACK only messages if we sent a message within ACK_INTERVAL.
                if (sendSequence == -1 && (currentTime - rh.lastUDPSend) < ACK_INTERVAL)
                {
                    continue;
                }
                rh.lastUDPSend = currentTime;
                WriteInt32(clientID, sendBuffer, 0);
                WriteInt32(sendSequence, sendBuffer, 4);
                WriteInt32(sendACK, sendBuffer, 8);
                WriteInt32(0, sendBuffer, 12);
                length = 16;
                if (sendData != null)
                {
                    WriteInt32(sendData.Length, sendBuffer, 12);
                    Array.Copy(sendData, 0, sendBuffer, 16, sendData.Length);
                    length = 16 + sendData.Length;
                }
                data = sendBuffer;
                udpBytesSentSecond += length;
                udpBytesSentTotal += length;
                return true;
            }
        }
        retransmitBytes = 0;
        foreach (KeyValuePair<int, ReliableHandler> kvp in reliableHandlers)
        {
            retransmitBytes += kvp.Value.retransmitBytes;
        }
        data = null;
        endPoint = null;
        length = 0;
        return false;
    }

    public byte[] GetTCPMessage(int clientID)
    {
        if (reliableHandlers.TryGetValue(clientID, out ReliableHandler rh))
        {
            byte[] sendMessage = rh.SendToTCP();
            if (sendMessage != null)
            {
                tcpBytesSentSecond += sendMessage.Length;
                tcpBytesSentTotal += sendMessage.Length;
            }
            return sendMessage;
        }
        return null;
    }

    private int ReadInt32(byte[] data, int start)
    {
        return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, start));
    }

    public static void WriteInt32(int number, byte[] data, int index)
    {
        uint unumber = (uint)number;
        data[index] = (byte)((unumber >> 24) & 0xFF);
        data[index + 1] = (byte)((unumber >> 16) & 0xFF);
        data[index + 2] = (byte)((unumber >> 8) & 0xFF);
        data[index + 3] = (byte)((unumber) & 0xFF);
    }

    public void PrintStats(int sleepTime)
    {
        Console.WriteLine("==========");
        Console.WriteLine("TCP Sent: {0} Total: {1}", (tcpBytesSentSecond / (sleepTime * 1024)), (tcpBytesSentTotal / 1024));
        Console.WriteLine("TCP Receive: {0} Total: {1}", (tcpBytesReceivedSecond / (sleepTime * 1024)), (tcpBytesReceivedTotal / 1024));
        Console.WriteLine("UDP Sent: {0} Total: {1}", (udpBytesSentSecond / (sleepTime * 1024)), (udpBytesSentTotal / 1024));
        Console.WriteLine("UDP Receive: {0} Total: {1}", (udpBytesReceivedSecond / (sleepTime * 1024)), (udpBytesReceivedTotal / 1024));
        Console.WriteLine("Retransmit queue: " + (retransmitBytes / 1024));
        tcpBytesSentSecond = 0;
        tcpBytesReceivedSecond = 0;
        udpBytesReceivedSecond = 0;
        udpBytesSentSecond = 0;
        Thread.Sleep(1000 * sleepTime);
    }
}