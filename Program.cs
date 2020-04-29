using System;
using System.Net;
using System.Net.Sockets;

namespace TCPTunnel
{
    class Program
    {
        private static bool running;

        static int Main(string[] args)
        {
            Settings settings = new Settings();
            settings.isServer = false;
            settings.ipEndpointStr = "minecraft1.52k.de:25560";
            settings.listenPort = 0;

            int serverCount = 0;
            foreach (string arg in args)
            {
                if (serverCount == 2)
                {
                    settings.ipEndpointStr = arg;
                }
                if (serverCount == 1)
                {
                    settings.listenPort = Int32.Parse(arg);
                }
                if (serverCount > 0)
                {
                    serverCount--;
                    continue;
                }
                if (arg == "--server")
                {
                    settings.isServer = true;
                    serverCount = 2;
                }
                if (arg == "--ipv6")
                {
                    settings.ipv4only = false;
                    settings.ipv6only = true;
                }
                if (arg == "--ipv4")
                {
                    settings.ipv4only = true;
                    settings.ipv6only = false;
                }
            }

            //ParseIPEndpoint
            string addPart = settings.ipEndpointStr.Substring(0, settings.ipEndpointStr.LastIndexOf(":"));
            //Trim [] parts;
            if (addPart.Contains("["))
            {
                addPart.Substring(1, addPart.Length - 2);
            }
            if (!IPAddress.TryParse(addPart, out IPAddress ipAddr))
            {
                IPAddress[] addrs = null;
                try
                {
                    addrs = Dns.GetHostAddresses(addPart);
                }
                catch (Exception e)
                {
                    Console.WriteLine("DNS error: " + e.Message);
                    return -1;
                }
                foreach (IPAddress testIP in addrs)
                {
                    if (!settings.ipv4only && !settings.ipv6only)
                    {
                        ipAddr = testIP;
                        break;
                    }
                    if (settings.ipv4only && testIP.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddr = testIP;
                        break;
                    }
                    if (settings.ipv6only && testIP.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ipAddr = testIP;
                        break;
                    }
                }
            }

            if (ipAddr == null)
            {
                Console.WriteLine("DNS returned no usable results");
                return -2;
            }

            string portPart = settings.ipEndpointStr.Substring(settings.ipEndpointStr.LastIndexOf(":") + 1);
            settings.ipEndpoint = new IPEndPoint(ipAddr, Int32.Parse(portPart));

            if (settings.listenPort == 0)
            {
                settings.listenPort = Int32.Parse(portPart);
            }

            //Init
            running = true;
            NetworkHandler networkHandler = new NetworkHandler();
            UDPTunnel udpt = new UDPTunnel(networkHandler, settings);
            if (settings.isServer)
            {
                TunnelServer ts = new TunnelServer(networkHandler, settings);
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
                TunnelClient tc = new TunnelClient(networkHandler, settings);
                Console.WriteLine("Press Ctrl+C to quit");
                Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
                {
                    running = false;
                    tc.Shutdown();
                    udpt.Shutdown();
                };
                DisplayMain(networkHandler);

            }
            return 0;
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
