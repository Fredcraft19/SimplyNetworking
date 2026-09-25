using SimplyNetworking.Types;
using SimplyNetworking.Debug;
using SimplyNetworking.Internal;

using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace SimplyNetworking 
{

        namespace Components
        {
            // Server-Side
            // Manages all network traffic for server
            public class RelayServer
            {
                private HServer server;
                public Dictionary<uint, ServerClient> clients = new Dictionary<uint, ServerClient>(); // uint is ID
                public Dictionary<string, ServerRoom> rooms = new Dictionary<string, ServerRoom>(); // for tracking who's the host client

                // Tracks which endpoints have already been assigned an ID, so a stray/duplicate
                // Connect packet (still carrying senderID 0, since the client hasn't been told
                // its ID yet) doesn't get treated as a brand new client every time it arrives.
                private Dictionary<EndPoint, uint> endpointToId = new Dictionary<EndPoint, uint>();
            
                private uint clientIDCounter = 0;
                public int pingInterval = 1000; // 1000ms (1s)
                public RelayServer()
                {
                    server = new HServer();
            
                    clients.Clear(); // just in case i guess
            
                    ProcessPackets();
                    ServerLoop();
                }
                private void ServerLoop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            PingClients();
            
                            await Task.Delay(pingInterval);
                        }
                    });
                }
                private void PingClients()
                {
                    foreach (var client in clients)
                    {
                        ServerClient sc = client.Value;
                        sc.ID = client.Key;
                        if ((DateTime.UtcNow - sc.lastPing).TotalSeconds >= 5)
                        {
                            Log.Message($"Kicking client: {sc.ID} for not responding to pings");

                            byte[] payload = BitConverter.GetBytes(sc.ID);
                            payload.Append((byte)0);

                            Packet clientLeft = new Packet
                            {
                                cmd = Command.ClientUpdate,
                                data = payload
                            };
                            RelayPacket(clientLeft, sc.ID, clients[client.Key].Room);

                            clients[client.Key].MasterClient = false;
                            rooms[clients[client.Key].Room].ClientIDs.Remove(client.Key);
            
                            if (rooms[clients[client.Key].Room].ClientIDs.Count == 0)
                            {
                                if (rooms.Remove(clients[client.Key].Room, out var _)) { }
                            }
                            else
                            {
                                uint newMasterID = rooms[clients[client.Key].Room].ClientIDs.First();
                                clients[newMasterID].MasterClient = true;
                                UpdateClientState(newMasterID);
                            }
            
                            endpointToId.Remove(sc.ep);
                            clients.Remove(client.Key);
                        }
                        else
                        {
                            EndPointPacket ping = new EndPointPacket
                            {
                                endPoint = sc.ep,
                                packet = new Packet { cmd = Command.Ping }
                            };
            
                            server.SendPacket(ping);
                        }
                    }
                }
            
                private void ProcessPackets()
                {
                    Log.Message("Relay Server Started");
                    Task.Run(() =>
                    {
                        while (true)
                        {
                            try
                            {
                            
                                while (server.server.pendingRecievedPackets.TryDequeue(out EndPointPacket epp))
                                {
                                    EndPoint endPoint = epp.endPoint;
                                    Packet packet = epp.packet;
            
                                    if (packet.senderID == 0)
                                    {
                                        if (endpointToId.TryGetValue(endPoint, out uint existingId))
                                        {
                                            Log.Warn($"Recieved a duplicate connect from an already-registered endpoint (ID {existingId}). Re-sending existing ID instead of creating a new client.");

                                            Packet resendSetID = new Packet
                                            {
                                                cmd = Command.SetID,
                                                data = BitConverter.GetBytes(existingId)
                                            };
                                            server.SendReliablePacket(new EndPointPacket(endPoint, resendSetID));

                                            EndPointPacket dupReciept = new EndPointPacket
                                            {
                                                endPoint = endPoint,
                                                packet = new Packet
                                                {
                                                    cmd = Command.Reciept,
                                                    data = BitConverter.GetBytes(packet.packetID)
                                                }
                                            };
                                            server.SendPacket(dupReciept);
                                        }
                                        else
                                        {
                                            Packet setID = new Packet
                                            {
                                                cmd = Command.SetID,
                                                data = BitConverter.GetBytes(++clientIDCounter)
                                            };

                                            if (!clients.ContainsKey(clientIDCounter))
                                            {
                                                Log.Message($"Server assigned a client a new ID: {clientIDCounter}");
                                                if (endPoint == null) Log.Error("EndPoint is null");
                                                server.SendReliablePacket(new EndPointPacket(endPoint, setID));
                                                clients[clientIDCounter] = new ServerClient(endPoint);
                                                endpointToId[endPoint] = clientIDCounter;

                                                EndPointPacket reciept = new EndPointPacket
                                                {
                                                    endPoint = endPoint,
                                                    packet = new Packet
                                                    {
                                                        cmd = Command.Reciept,
                                                        data = BitConverter.GetBytes(packet.packetID)
                                                    }
                                                };

                                                server.SendPacket(reciept);
                                            }
                                            else
                                            {
                                                Log.Error("Server tried to assigned a client with an ID thats already in use");
                                            }
                                        }
                                    }
                                    else
                                    {
                                    
                                        clients[packet.senderID].lastPing = DateTime.UtcNow;
            
                                        switch (packet.cmd)
                                        {
                                            case Command.NULL:
                                                {
                                                    Log.Error("Server recieved null command from client, " + packet.senderID);
                                                    break;
                                                }
                                            case Command.Ping:
                                                {
                                                    Log.Warn("Ping recieved from a client (Clients should send Pongs)");
                                                    break;
                                                }
                                            case Command.Pong:
                                                {
                                                    //Log.Message("Pong recieved from client: " + packet.senderID);
                                                    clients[packet.senderID].lastPing = DateTime.UtcNow;
            
                                                    if (clients[packet.senderID].Room != null || clients[packet.senderID].Room != "")
                                                    {
                                                        UpdateClientState(packet.senderID);
                                                    }
                                                    break;
                                                }
                                            case Command.SetID:
                                                {
                                                    Log.Warn($"Server Recieved a SetID command from Client: {packet.senderID}. Ignoring it.");
                                                    break;
                                                }
                                            case Command.Connect:
                                                {
                                                    Log.Message($"Client '{packet.senderID}' tried to connect to server when they already have. Ignoring it.");
                                                    break;
                                                }
                                            case Command.SetRoom:
                                                {
                                                    string roomName = Encoding.UTF8.GetString(packet.data);
                                                    Log.Message($"Client '{packet.senderID}' joined room '{roomName}'");
            
            
                                                    if (roomName == "")
                                                    {
                                                        clients[packet.senderID].MasterClient = false;
                                                        
                                                        byte[] payload = BitConverter.GetBytes(packet.senderID);
                                                        payload.Append((byte)0);

                                                        Packet clientLeft = new Packet
                                                        {
                                                            cmd = Command.ClientUpdate,
                                                            data = payload
                                                        };
                                                        RelayPacket(clientLeft, packet.senderID, clients[packet.senderID].Room);

                                                        rooms[clients[packet.senderID].Room].ClientIDs.Remove(packet.senderID);
            
                                                        if (rooms[clients[packet.senderID].Room].ClientIDs.Count == 0)
                                                        {
                                                            if (rooms.Remove(clients[packet.senderID].Room, out var _)) { }
                                                        }
                                                        else
                                                        {
                                                            uint newMasterID = rooms[clients[packet.senderID].Room].ClientIDs.First();
                                                            clients[newMasterID].MasterClient = true;
                                                            UpdateClientState(newMasterID);
                                                        }
                                                        clients[packet.senderID].Room = roomName;
                                                    }
                                                    else
                                                    {
                                                        if(clients[packet.senderID].Room != "") // Client is joining from another room. clean up old master and new master
                                                        {
                                                            clients[packet.senderID].MasterClient = false;
                                                        
                                                            byte[] idPayload = BitConverter.GetBytes(packet.senderID);
                                                            idPayload.Append((byte)0);

                                                            Packet clientLeft = new Packet
                                                            {
                                                                cmd = Command.ClientUpdate,
                                                                data = idPayload
                                                            };
                                                            RelayPacket(clientLeft, packet.senderID, clients[packet.senderID].Room);

                                                            rooms[clients[packet.senderID].Room].ClientIDs.Remove(packet.senderID);

                                                            if (rooms[clients[packet.senderID].Room].ClientIDs.Count == 0)
                                                            {
                                                                if (rooms.Remove(clients[packet.senderID].Room, out var _)) { }
                                                            }
                                                            else
                                                            {
                                                                uint newMasterID = rooms[clients[packet.senderID].Room].ClientIDs.First();
                                                                clients[newMasterID].MasterClient = true;
                                                                UpdateClientState(newMasterID);
                                                            }
                                                        }
                                                        clients[packet.senderID].Room = roomName;
                                                        if (!rooms.ContainsKey(roomName))
                                                        {
                                                            ServerRoom newRoom = new ServerRoom
                                                            {
                                                                HostID = packet.senderID
                                                            };
                                                            rooms[roomName] = newRoom;
                                                            clients[packet.senderID].MasterClient = true;
                                                        }
                                                        else
                                                        {
                                                            clients[packet.senderID].MasterClient = false;
                                                        }
                                                        rooms[roomName].ClientIDs.Add(packet.senderID);


                                                        byte[] payload = BitConverter.GetBytes(packet.senderID);
                                                        payload.Append((byte)1);

                                                        Packet clientJoined = new Packet
                                                        {
                                                            cmd = Command.ClientUpdate,
                                                            data = payload
                                                        };
                                                        RelayPacket(clientJoined, packet.senderID, clients[packet.senderID].Room);
                                                    }
            
                                                    EndPointPacket reciept = new EndPointPacket
                                                    {
                                                        endPoint = clients[packet.senderID].ep,
                                                        packet = new Packet
                                                        {
                                                            cmd = Command.Reciept,
                                                            data = BitConverter.GetBytes(packet.packetID)
                                                        }
                                                    };
                                                    server.SendPacket(reciept);
            
                                                    UpdateClientState(packet.senderID);
                                                    break;
                                                }
                                            case Command.Reciept:
                                                {
                                                    if (packet.data != null && packet.data.Length >= 4)
                                                    {
                                                        uint packedID = BitConverter.ToUInt32(packet.data, 0);
                                                        server.reliablePackets.TryRemove(packedID, out _);
                                                    }
                                                    break;
                                                }
                                            case Command.RPC: // sent via reliable
                                                {
                                                    // Send Reciept
                                                    EndPointPacket reciept = new EndPointPacket
                                                    {
                                                        packet = new Packet
                                                        {
                                                            cmd = Command.Reciept,
                                                            data = BitConverter.GetBytes(packet.packetID)
                                                        },
                                                        endPoint = clients[packet.senderID].ep
                                                    };
            
                                                    server.SendPacket(reciept);
            
                                                    // Relay
                                                    RelayPacket(packet, packet.senderID, clients[packet.senderID].Room);
            
            
                                                    break;
                                                }
                                            case Command.NetworkVariable:
                                                {
                                                    NetworkDelivery delivery = (NetworkDelivery)packet.data[0];
            
                                                    if (delivery == NetworkDelivery.Reliable)
                                                    {
                                                        EndPointPacket reciept = new EndPointPacket
                                                        {
                                                            packet = new Packet
                                                            {
                                                                cmd = Command.Reciept,
                                                                data = BitConverter.GetBytes(packet.packetID)
                                                            },
                                                            endPoint = clients[packet.senderID].ep
                                                        };
                                                        server.SendPacket(reciept);
                                                    }
            
                                                    // Relay
                                                    RelayPacket(packet, packet.senderID, clients[packet.senderID].Room);
            
            
                                                    break;
                                                }
                                            default:    // relay
                                                {
                                                    RelayPacket(packet, packet.senderID, clients[packet.senderID].Room);
                                                    break;
                                                }
            
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error($"Error processing packets: {e.Message}");
                            }
                        }
                    });
                }
                private void RelayPacket(Packet p, uint senderID, string roomName)
                {
                    if (!rooms.TryGetValue(roomName, out var room))
                        return;
            
                    EndPointPacket epPacket = new EndPointPacket { packet = p };
            
                    foreach (uint targetID in room.ClientIDs)
                    {
                        if (targetID == senderID)
                            continue;
            
                        if (clients.TryGetValue(targetID, out var client))
                        {
                            epPacket.endPoint = client.ep;
                            server.SendPacket(epPacket);
                        }
                    }
                }
            
                private void UpdateClientState(uint id)
                {
                
                    if (clients.ContainsKey(id))
                    {
                        ServerClient client = clients[id];

                        byte[] roomBytes = Encoding.UTF8.GetBytes(client.Room);
                        byte[] idBytes = BitConverter.GetBytes(client.ID);
                        byte[] payload = new byte[1 + idBytes.Length + roomBytes.Length];
                        int offset = 0;
                        payload[offset++] = (byte)(client.MasterClient ? 1 : 0);
                        Buffer.BlockCopy(idBytes, 0, payload, offset, idBytes.Length);
                        offset += idBytes.Length;
                        Buffer.BlockCopy(roomBytes, 0, payload, offset, roomBytes.Length);

                        EndPointPacket state = new EndPointPacket
                        {
                            endPoint = client.ep,
                            packet = new Packet
                            {
                                cmd = Command.UpdateState,
                                data = payload
                            }
                        };
            
                        server.SendReliablePacket(state);
                        //Log.Message($"Updating client '{id}' state");
                    }
                    else
                    {
                        Log.Error($"ID: '{id}' doesnt exist in the server.");
                    }
                }
            }
        }
    
}