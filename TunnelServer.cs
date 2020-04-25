using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class TunnelServer
{
    private bool running = true;
    private NetworkHandler networkHandler;
    private ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
    private byte[] readBuffer = new byte[16 * 1024];
    private Thread networkLoop;
    private IPEndPoint connectEndpoint;

    public TunnelServer(NetworkHandler networkHandler, Settings settings)
    {
        this.networkHandler = networkHandler;
        this.connectEndpoint = settings.ipEndpoint;
        networkHandler.SetServer(this);
        networkLoop = new Thread(new ThreadStart(NetworkLoop));
        networkLoop.Start();
    }

    public void NetworkLoop()
    {
        while (running)
        {
            int disconnectClient = 0;
            bool activity = false;
            foreach (KeyValuePair<int, TcpClient> kvp in clients)
            {
                int clientID = kvp.Key;
                TcpClient readClient = kvp.Value;
                if (readClient.Connected && readClient.Available > 0)
                {
                    activity = true;
                    try
                    {
                        int readBytes = readClient.GetStream().Read(readBuffer, 0, readBuffer.Length);
                        networkHandler.HandleTCPMessage(clientID, readBuffer, readBytes);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error during TCP read: " + e);
                        disconnectClient = clientID;
                    }
                }
                if (readClient.Connected)
                {
                    byte[] sendMessage = networkHandler.GetTCPMessage(clientID);
                    if (sendMessage != null)
                    {
                        activity = true;
                        try
                        {
                            readClient.GetStream().Write(sendMessage, 0, sendMessage.Length);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Error during TCP write: " + e);
                            disconnectClient = clientID;
                        }
                    }
                }
                if (networkHandler.ShouldDisconnect(clientID))
                {
                    Console.WriteLine("UDP Timeout for connection " + clientID);
                    disconnectClient = clientID;
                }
            }
            if (disconnectClient != 0)
            {
                clients.TryRemove(disconnectClient, out TcpClient _);
                networkHandler.ForgetClient(disconnectClient);
            }
            if (!activity)
            {
                Thread.Sleep(10);
            }
        }
    }

    public TcpClient GetNewConnection(int clientID)
    {
        TcpClient newClient = new TcpClient(connectEndpoint.AddressFamily);
        newClient.NoDelay = true;
        newClient.Connect(connectEndpoint);
        clients.TryAdd(clientID, newClient);
        return newClient;
    }

    public void Shutdown()
    {
        running = false;
    }
}