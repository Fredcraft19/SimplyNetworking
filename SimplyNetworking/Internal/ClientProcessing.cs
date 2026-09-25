using SimplyNetworking.Internal.Socket;
using SimplyNetworking.Types;
using SimplyNetworking.Debug;
using SimplyNetworking.Components;

using System;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace SimplyNetworking
{

        namespace Internal
        {
            // Higher Level Client
            public class HClient
            {
                private uint oldID = 0;
                private string oldRoom = "";
                private bool oldMaster = false;

                public uint ID { get; private set; }
                public string Room { get; private set; }
                public bool Master { get; private set; }

                public Action OnConnected;
                public Action OnJoinedRoom;
                public Action OnLeftRoom;
                public Action OnBecameMaster;

                private Client_ client;
                private uint packetCount = 0;
                private ConcurrentQueue<Packet> pendingPackets = new ConcurrentQueue<Packet>();
                private readonly ConcurrentDictionary<uint, (Packet packet, DateTime sentTime)> reliablePackets = new();
                public HClient(IPAddress ip, int port = 8082)
                {
                    client = new Client_();
                    client.Initialize(ip, port);
                    client.StartMessageLoop();
                    StartPacketLoop();
                    ProcessPackets();
                    Loop();
                }
                private void Loop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                if(ID != oldID)
                                {
                                    if(oldID == 0)
                                    {
                                        OnConnected?.Invoke();
                                        Log.Message("CB: Connected to server");
                                    }
                                    oldID = ID;
                                }
                                if (Room != oldRoom)
                                {
                                    if (oldRoom != "" && oldRoom != null)
                                    {
                                        OnLeftRoom?.Invoke();
                                        Log.Message("CB: left room");
                                    }
                                    if (Room != "" && Room != null)
                                    {
                                        OnJoinedRoom?.Invoke();
                                        Log.Message($"CB: joined room n'{Room}' o'{oldRoom}'");
                                    }
                                    oldRoom = Room;
                                }

                                if(Master != oldMaster )
                                {
                                    if (Master && !oldMaster)
                                    {
                                        OnBecameMaster?.Invoke();
                                        Log.Message("CB: became master");
                                    }
                                    oldMaster = Master;
                                }
                            }
                            catch(Exception e)
                            {
                                Log.Error(e.Message);
                            }
                            await Task.Delay((int)(1000f / (float)NetworkManager.tickRate));
                        }
                    });
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
                                List<(uint packetID, Packet packet, DateTime time)> packetsToOverwrite = new();
                                foreach (var entry in reliablePackets)
                                {
                                    uint packetID = entry.Key;
                                    var (packet, sentTime) = entry.Value;

                                    if ((now - sentTime).TotalMilliseconds > 500)
                                    {
                                        packetsToOverwrite.Add((packetID, packet, now));

                                        ResendPacket(packet);
                                        Log.Message("Sending again. reciept not recieved");
                                    }
                                }
                                foreach (var x in packetsToOverwrite)
                                {
                                    reliablePackets[x.packetID] = (x.packet, x.time);
                                }


                                while (pendingPackets.TryDequeue(out Packet packet))
                                {
                                    //if(packet.cmd != Command.Pong && packet.cmd != Command.Reciept) Log.Message("Sending Packet: " + packet.cmd);
                                    await client.Send(packet.Serialize());

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
                private void ProcessPackets()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                while (client.pendingRecievedPackets.TryDequeue(out Packet packet))
                                {
                                    switch (packet.cmd)
                                    {
                                        case Command.NULL:
                                            {
                                                Log.Warn("Null command recieved. Ignoring it");
                                                break;
                                            }
                                        case Command.Ping:
                                            {
                                                // Send Pong to server
                                                Packet pong = new Packet
                                                {
                                                    cmd = Command.Pong
                                                };
                                                SendPacket(pong);

                                                //Log.Message("Ping recieved, sending Pong to server");
                                                break;
                                            }

                                        case Command.SetID:
                                            {
                                                if (packet.senderID == 0)
                                                {
                                                    uint newID = BitConverter.ToUInt32(packet.data);
                                                    ID = newID;
                                                    Log.Message($"Set ID to: {newID}");
                                                }
                                                else
                                                {
                                                    Log.Warn($"Can't set ID to what another client says (sender: {packet.senderID}), ingoring it");
                                                }

                                                Packet reciept = new Packet
                                                {
                                                    cmd = Command.Reciept,
                                                    data = BitConverter.GetBytes(packet.packetID)
                                                };
                                                SendPacket(reciept);

                                                break;
                                            }
                                        case Command.UpdateState:
                                            {
                                                if (packet.senderID == 0)
                                                {
                                                    //Log.Message("Updating state");
                                                    bool master = packet.data[0] == 1;
                                                    uint newID = BitConverter.ToUInt32(packet.data, 1);
                                                    string room = Encoding.UTF8.GetString(packet.data, 5, packet.data.Length - 5);

                                                    Master = master;
                                                    ID = newID;
                                                    Room = room;
                                                }
                                                else
                                                {
                                                    Log.Warn($"Can't update state to what another client says (sender: {packet.senderID}), ignoring it");
                                                }

                                                Packet reciept = new Packet
                                                {
                                                    cmd = Command.Reciept,
                                                    data = BitConverter.GetBytes(packet.packetID)
                                                };
                                                SendPacket(reciept);

                                                break;
                                            }
                                        case Command.Reciept:
                                            {
                                                if (packet.data != null && packet.data.Length >= 4)
                                                {
                                                    uint packedID = BitConverter.ToUInt32(packet.data, 0);
                                                    reliablePackets.TryRemove(packedID, out _);
                                                }
                                                break;
                                            }
                                        case Command.RPC:
                                            {
                                                RpcTarget rpc_target = (RpcTarget)packet.data[0];
                                                uint objId = BitConverter.ToUInt32(packet.data, 1);
                                                int nameLen = BitConverter.ToInt32(packet.data, 5);
                                                string RpcName = Encoding.UTF8.GetString(packet.data, 9, nameLen);
                                                byte[] parameterData = packet.data[(nameLen + 9)..];

                                                if ((rpc_target == RpcTarget.All || rpc_target == RpcTarget.Others) || (rpc_target == RpcTarget.Master && Master))
                                                {
                                                    PendingRPC rpc = new PendingRPC
                                                    {
                                                        target = rpc_target,
                                                        objectId = objId,
                                                        name = RpcName,
                                                        data = parameterData
                                                    };

                                                    NetworkManager.AddPendingRPC(rpc);
                                                }

                                                Packet reciept = new Packet
                                                {
                                                    cmd = Command.Reciept,
                                                    data = BitConverter.GetBytes(packet.packetID)
                                                };
                                                SendPacket(reciept);

                                                break;
                                            }
                                        case Command.NetworkVariable:
                                            {
                                                if ((NetworkDelivery)packet.data[0] == NetworkDelivery.Reliable)
                                                {
                                                    Packet reciept = new Packet
                                                    {
                                                        cmd = Command.Reciept,
                                                        data = BitConverter.GetBytes(packet.packetID)
                                                    };
                                                    SendPacket(reciept);
                                                }

                                                uint objectId = BitConverter.ToUInt32(packet.data, 1);
                                                int nameLen = BitConverter.ToInt32(packet.data, 5);
                                                string var_name = Encoding.UTF8.GetString(packet.data, 9, nameLen);

                                                NetworkManager.AddPendingNetworkVariableSet(objectId, var_name, packet.data[(9 + nameLen)..]);

                                                break;
                                            }
                                        case Command.FastData:
                                            {
                                                NetworkManager.AddPendingFastData(packet);
                                                break;
                                            }
                                    }
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
                public void SendPacket(Packet packet)
                {
                    if (packetCount > 4200000000)
                        packetCount = 1;

                    packet.packetID = packetCount++;
                    packet.senderID = ID;

                    pendingPackets.Enqueue(packet);
                }

                private void ResendPacket(Packet packet)
                {
                    pendingPackets.Enqueue(packet);
                }

                public void SendReliablePacket(Packet packet)
                {
                    if (packetCount > 4200000000)
                        packetCount = 1;

                    packet.packetID = packetCount++;
                    packet.senderID = ID;

                    pendingPackets.Enqueue(packet);
                    reliablePackets[packet.packetID] = (packet, DateTime.UtcNow);
                }
            }
        }
    
}