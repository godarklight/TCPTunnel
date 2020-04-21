# TCPTunnel
By default with no arguments, it runs in client mode, connects to UDP godarklight.info.tm:25560 and listens on TCP 25561.  
  
This program uses a token bucket (512kb/s, 1MB max) to limit the send rate, and retransmits packets after 100ms.  
This is a workaround for minecraft's TCP connection. TCP struggles on high-latency lossy links.  
  
### Client mode (Listens for incoming TCP connections and forwards to a UDP server)
To change the UDP endpoint, create a shortcut to it and change the UDP endpoint "TCPTunnel.exe godarklight.info.tm:25560"  
TCPTunnel only takes one argument in this mode, the endpoint.  
The client will listen for TCP connections on __the servers endpoint port + 1__  
  
### Server mode (Listens for incoming UDP connections and connects to a TCP endpoint)
To change the TCP endpoint and port, create a shortcut to it and specify the endpoint "TCPTunnel.exe --server 127.0.0.1:25565 25560"  
That is, it listens for UDP connections on port 25560 and forwards them to 127.0.0.1:25565 (a minecraft server running on the same machine)  
