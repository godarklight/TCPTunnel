using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;


public class UDPTunnel
{
    public bool running = true;
    public Socket udpc;
    public byte[] readBuffer = new byte[16 * 1024];
    public Thread readThread;
    public Thread writeThread;
    public NetworkHandler networkHandler;
    public long tokenTime;
    public long tokens;
    //512kB/s
    public long tokensPerSecond = 512 * 1024;
    //1MB token buffer
    public long tokensMax = 1024 * 1024;

    public UDPTunnel(NetworkHandler networkHandler, int listenPort)
    {
        this.networkHandler = networkHandler;
        udpc = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        udpc.DualMode = true;
        udpc.Bind(new IPEndPoint(IPAddress.IPv6Any, listenPort));
        readThread = new Thread(new ThreadStart(ReadLoop));
        readThread.Start();
        writeThread = new Thread(new ThreadStart(WriteLoop));
        writeThread.Start();
    }

    public void ReadLoop()
    {
        IPEndPoint anyAddr = new IPEndPoint(IPAddress.IPv6Any, 25560);
        while (running)
        {
            bool activity = false;
            if (udpc.Poll(10000, SelectMode.SelectRead))
            {
                EndPoint recvAddr = anyAddr;
                int bytesRead = udpc.ReceiveFrom(readBuffer, 0, readBuffer.Length, SocketFlags.None, ref recvAddr);
                if (bytesRead > 0)
                {
                    activity = true;
                    IPEndPoint recvIPAddr = recvAddr as IPEndPoint;
                    networkHandler.HandleUDPMessage(readBuffer, bytesRead, recvIPAddr);
                }
            }
            if (!activity)
            {
                Thread.Sleep(5);
            }
        }
    }

    public void WriteLoop()
    {
        tokens = tokensMax;
        while (running)
        {
            bool sent = false;
            long currentTime = DateTime.UtcNow.Ticks;
            long timeElapsed = currentTime - tokenTime;
            tokenTime = currentTime;
            long newTokens = (timeElapsed * tokensPerSecond) / TimeSpan.TicksPerSecond;
            tokens += newTokens;
            if (tokens > tokensMax)
            {
                tokens = tokensMax;
            }
            if (tokens > 1000)
            {
                if (networkHandler.GetUDPMessage(out byte[] sendMessage, out int sendLength, out IPEndPoint endPoint))
                {
                    tokens -= sendLength;
                    sent = true;
                    udpc.SendTo(sendMessage, 0, sendLength, SocketFlags.None, endPoint);
                }
            }
            if (!sent)
            {
                Thread.Sleep(5);
            }
        }
    }

    public void Shutdown()
    {
        running = false;
        readThread.Join();
        writeThread.Join();
        readThread = null;
        writeThread = null;
    }
}