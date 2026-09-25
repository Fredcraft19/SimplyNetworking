using SimplyNetworking.Types;
using SimplyNetworking.Debug;

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace SimplyNetworking
{
    
        namespace Internal
        {
        namespace Socket
        {
            public class Server_
            {
            
                public ConcurrentQueue<EndPointPacket> pendingRecievedPackets = new ConcurrentQueue<EndPointPacket>();
                public const int PORT = 8082;

                private System.Net.Sockets.Socket _socket;
                private EndPoint _ep;
                private EndPoint _blankEP;


                private byte[] _buffer_recv;
                private ArraySegment<byte> _buffer_recv_segment;

                public void Initialize()
                {
                    _buffer_recv = new byte[4096];
                    _buffer_recv_segment = new(_buffer_recv);

                    _ep = new IPEndPoint(IPAddress.Any, PORT);
                    _blankEP = new IPEndPoint(IPAddress.Any, 0);

                    _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);

                    _socket.Bind(_ep);
                }

                public void StartMessageLoop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                var res = await _socket.ReceiveMessageFromAsync(_buffer_recv_segment, SocketFlags.None, _blankEP);

                                byte[] packetData = new byte[res.ReceivedBytes];
                                Buffer.BlockCopy(_buffer_recv, 0, packetData, 0, res.ReceivedBytes);

                                EndPointPacket epp = new EndPointPacket();
                                epp.packet.Deserialize(packetData, res.ReceivedBytes);
                                epp.endPoint = res.RemoteEndPoint;
                                pendingRecievedPackets.Enqueue(epp);

                            }
                            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                            {
                                Log.Warn("Failed to connect to a client");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Server Loop Error: {ex.Message}");
                            }
                        }
                    });
                }

                public async Task SendTo(EndPoint recipient, byte[] data)
                {
                    var s = new ArraySegment<byte>(data);
                    await _socket.SendToAsync(s, SocketFlags.None, recipient);
                }
            }
        }
    }
}