using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

public class TunnelClient
{
    public bool running = true;
    NetworkHandler networkHandler;
    public TcpListener listener;
    private IPEndPoint serverEndpoint;
    private byte[] readBuffer = new byte[16 * 1024];
    private Random rand = new Random();
    private ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
    private Thread networkLoop;

    public TunnelClient(NetworkHandler networkHandler, IPEndPoint endpoint, int listenPort)
    {

        this.networkHandler = networkHandler;
        this.serverEndpoint = endpoint;
        networkHandler.SetClient(this);
        networkLoop = new Thread(new ThreadStart(NetworkLoop));
        networkLoop.Start();
        listener = new TcpListener(new IPEndPoint(IPAddress.IPv6Any, listenPort));
        listener.Server.DualMode = true;
        listener.Start();
        listener.BeginAcceptTcpClient(HandleConnect, null);
    }

    private void HandleConnect(IAsyncResult ar)
    {
        try
        {
            int clientID = 0;
            while (clientID == 0)
            {
                clientID = rand.Next();
            }
            Console.WriteLine("Local connection: " + clientID);
            TcpClient client = listener.EndAcceptTcpClient(ar);
            client.NoDelay = true;
            networkHandler.ConnectClient(clientID, client, serverEndpoint);
            clients.TryAdd(clientID, client);
        }
        catch
        {
        }
        if (running)
        {
            listener.BeginAcceptTcpClient(HandleConnect, null);
        }
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
                        Console.WriteLine("Error during TCP read: " + e.Message);
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
                            Console.WriteLine("Error during TCP write: " + e.Message);
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

    public void Shutdown()
    {
        running = false;
        listener.Stop();
    }
}