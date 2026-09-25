using SimplyNetworking.Internal.Socket;
using SimplyNetworking.Types;
using SimplyNetworking.Debug;

using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;



namespace SimplyNetworking
{

        namespace Internal
        {
            public class HServer
            {
                public Server_ server;
                private uint packetCount = 0;
                private ConcurrentQueue<EndPointPacket> pendingPackets = new ConcurrentQueue<EndPointPacket>();
                public readonly ConcurrentDictionary<uint, (EndPointPacket packet, DateTime sentTime)> reliablePackets = new();
                public HServer()
                {
                    server = new Server_();
                    server.Initialize();
                    server.StartMessageLoop();
                    StartPacketLoop();
                }
                private void StartPacketLoop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                DateTime now = DateTime.UtcNow;
                                foreach (var entry in reliablePackets)
                                {
                                    uint packetID = entry.Key;
                                    var (packet, sentTime) = entry.Value;

                                    if ((now - sentTime).TotalMilliseconds > 500)
                                    {
                                        reliablePackets[packetID] = (packet, now);

                                        ResendPacket(packet);
                                        Log.Message("Sending again. reciept not recieved");
                                    }
                                }
                                while (pendingPackets.TryDequeue(out EndPointPacket epp))
                                {
                                    //Log.Message("Sending Packet: " + epp.packet.cmd);
                                    await server.SendTo(epp.endPoint, epp.packet.Serialize());
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e.Message);
                            }

                            await Task.Delay(5);
                        }
                    });
                }
                public void SendPacket(EndPointPacket epp)
                {
                    if (packetCount > 4200000000)
                        packetCount = 1;
                    epp.packet.packetID = packetCount++;
                    epp.packet.senderID = 0;
                    pendingPackets.Enqueue(epp);
                }

                private void ResendPacket(EndPointPacket epp)
                {
                    pendingPackets.Enqueue(epp);
                }
                public void SendReliablePacket(EndPointPacket epp)
                {
                    if (packetCount > 4200000000)
                        packetCount = 1;
                    epp.packet.packetID = packetCount++;
                    epp.packet.senderID = 0;
                    pendingPackets.Enqueue(epp);
                    reliablePackets[epp.packet.packetID] = (epp, DateTime.UtcNow);
                }
            }
        }
    
}