using System;
using System.Net;
using System.Net.Sockets;

namespace TCPTunnel
{
    class Program
    {
        private static bool running;

        static void Main(string[] args)
        {
            Settings s = new Settings();
            s.isServer = false;
            s.ipEndpoint = "godarklight.info.tm:25560";
            int listenPort = 0;

            if (args.Length == 1 && args[0] != "--server")
            {
                s.isServer = false;
                s.ipEndpoint = args[0];
            }

            if (args.Length == 3 && args[0] == "--server")
            {
                s.isServer = true;
                s.ipEndpoint = args[1];
                listenPort = Int32.Parse(args[2]);
            }

            //ParseIPEndpoint
            string addPart = s.ipEndpoint.Substring(0, s.ipEndpoint.LastIndexOf(":"));
            //Trim [] parts;
            if (addPart.Contains("["))
            {
                addPart.Substring(1, addPart.Length - 2);
            }
            if (!IPAddress.TryParse(addPart, out IPAddress ipAddr))
            {
                IPAddress[] addrs = Dns.GetHostAddresses(addPart);
                ipAddr = addrs[0];
                foreach (IPAddress testIP in addrs)
                {
                    if (testIP.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddr = testIP;
                        break;
                    }
                }
            }
            string portPart = s.ipEndpoint.Substring(s.ipEndpoint.LastIndexOf(":") + 1);
            IPEndPoint endPoint = new IPEndPoint(ipAddr, Int32.Parse(portPart));

            //Init
            running = true;
            NetworkHandler networkHandler = new NetworkHandler();
            UDPTunnel udpt = new UDPTunnel(networkHandler, listenPort);
            if (s.isServer)
            {
                TunnelServer ts = new TunnelServer(networkHandler, endPoint);
                Console.WriteLine("Press Ctrl+C key to quit");
                Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
                {
                    running = false;
                    ts.Shutdown();
                    udpt.Shutdown();
                };
                DisplayMain(networkHandler);
            }
            else
            {
                TunnelClient tc = new TunnelClient(networkHandler, endPoint, Int32.Parse(portPart) + 1);
                Console.WriteLine("Press Ctrl+C to quit");
                Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
                {
                    running = false;
                    tc.Shutdown();
                    udpt.Shutdown();
                };
                DisplayMain(networkHandler);

            }
        }

        private static void DisplayMain(NetworkHandler networkHandler)
        {
            int timeToSleep = 5;
            while (running)
            {
                networkHandler.PrintStats(timeToSleep);
            }
        }
    }
}
