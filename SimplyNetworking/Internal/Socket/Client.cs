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


            public class Client_
            {
                public ConcurrentQueue<Packet> pendingRecievedPackets = new ConcurrentQueue<Packet>();
                private System.Net.Sockets.Socket _socket;
                private EndPoint _serverEP;
                private EndPoint _blankEP;
            
                private byte[] _buffer_recv;
                private ArraySegment<byte> _buffer_recv_segment;
            
                public void Initialize(IPAddress address, int port)
                {
                    _buffer_recv = new byte[4096];
                    _buffer_recv_segment = new(_buffer_recv);
            
                    _serverEP = new IPEndPoint(address, port);
                    _blankEP = new IPEndPoint(IPAddress.Any, 0);
            
                    _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    //_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            
                    _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
            
                public void StartMessageLoop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                var res = await _socket.ReceiveFromAsync(_buffer_recv_segment, SocketFlags.None, _blankEP);
                                byte[] rawBytes = _buffer_recv;
            
                                Packet packet = new Packet();
                                packet.Deserialize(rawBytes, res.ReceivedBytes);
                                //if(packet.cmd != Command.Ping && packet.cmd != Command.Reciept && packet.cmd != Command.UpdateState) Log.Message("Recieved Packet: " + packet.cmd);
                                pendingRecievedPackets.Enqueue(packet);
                            }
                            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                            {
                                Log.Warn("Lost connection to server: " + ex.Message);
                                return;
                            }
                            catch (Exception ex)
                            { 
                                Log.Error($"Client Loop Error: {ex.Message}");
                            }
                        }
                    });
                }
            
                public async Task Send(byte[] data)
                {
                    var s = new ArraySegment<byte>(data);
                    await _socket.SendToAsync(s, SocketFlags.None, _serverEP);
                }
            }

        }
    }
}
