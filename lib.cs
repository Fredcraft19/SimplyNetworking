using SimplyNetworking.API.Internal;
using SimplyNetworking.API.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace SimplyNetworking
{
    namespace API
    {
        public enum LogLevel
        {
            All,
            WarningsAndErrors,
            Errors,
            None
        }
        public static class Log
        {
            public static LogLevel level = LogLevel.All;
            public static void Message(string msg)
            {
                if (level == LogLevel.All) Console.WriteLine($"[  LOG  ] {msg}");
            }
            public static void Error(string err_msg)
            {
                if (level == LogLevel.Errors || level == LogLevel.All || level == LogLevel.WarningsAndErrors)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ ERROR ] {err_msg}");
                    Console.ResetColor();
                }
            }
            public static void Warn(string wrn_msg)
            {
                if (level == LogLevel.All || level == LogLevel.WarningsAndErrors)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[WARNING] {wrn_msg}");
                    Console.ResetColor();
                }
            }
        }
        public class PendingRPC
        {
            public RpcTarget target;
            public uint objectId;
            public string name;
            public byte[] data;
        }

        // Client-Side
        // Manages all network traffic for client.
        public static class NetworkManager
        {
            private static HClient client;
            private static Dictionary<uint, NetworkIdentity> identitys = new Dictionary<uint, NetworkIdentity>();
            private static ConcurrentQueue<PendingRPC> pendingRPCs = new ConcurrentQueue<PendingRPC>();

            public static void AddPendingRPC(PendingRPC rpc)
            {
                pendingRPCs.Enqueue(rpc);
            }
            public static void AddIdentity(NetworkIdentity i)
            {
                identitys[i.ObjectID] = i;
            }

            public static void Initialize(IPAddress serverIP = null, int serverPort = 8082)
            {
                if (serverIP == null)
                {
                    client = new HClient(IPAddress.Loopback, serverPort);
                    Log.Warn("Server IP is set to LocalHost due to it being null.");
                }
                else
                {
                    client = new HClient(serverIP, serverPort);
                    Log.Message("Connecting to server ip..");
                }
                ManagerLoop();
            }

            private static void ManagerLoop()
            {
                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        while (pendingRPCs.TryDequeue(out var rpc))
                        {
                            if (identitys.ContainsKey(rpc.objectId))
                            {
                                if ((rpc.target == RpcTarget.Master && MasterClient) || (rpc.target != RpcTarget.Master))
                                {
                                    identitys[rpc.objectId].RunRPC(rpc.name, rpc.data);
                                }

                            }
                            else
                            {
                                Log.Error("Can't run an RPC on a Network Identity that doesnt exist");
                            }
                        }
                        await Task.Delay(5);
                    }
                });
            }

            public static uint ID
            {
                get
                {
                    return client.ID;
                }
            }
            public static string Room
            {
                get
                {
                    return client.Room;
                }
            }
            public static bool MasterClient
            {
                get
                {
                    return client.Master;
                }
            }


            public static void Connect()
            {
                Packet connect = new Packet
                {
                    cmd = Command.Connect
                };
                client.SendPacket(connect);
            }
            public static void JoinRoom(string roomName)
            {
                if (ID == 0)
                {
                    Log.Warn("Refusing to join a room without an ID");
                    return;
                }

                Packet joinRoom = new Packet
                {
                    cmd = Command.SetRoom
                };

                if (roomName == null)
                {
                    Log.Warn("Can't join a room with a null string for the name. Leaving room instead");
                    joinRoom.data = System.Text.Encoding.UTF8.GetBytes("");
                }
                else
                {
                    joinRoom.data = System.Text.Encoding.UTF8.GetBytes(roomName);
                }
                client.SendPacket(joinRoom);
            }

            /// <summary>
            /// Don't use this please, unless you know what your doing.
            /// </summary>
            public static void SendPacket(Packet packet)
            {
                client.SendPacket(packet);
            }

        }

        public class NetworkIdentity
        {
            public uint ObjectID { get; private set; }

            private readonly Dictionary<string, Delegate> bindedRpcs = new Dictionary<string, Delegate>();

            public NetworkIdentity(uint id)
            {
                ObjectID = id;
                NetworkManager.AddIdentity(this);
            }

            /// <summary>
            /// Calls an RPC
            /// </summary>
            public void RPC(string rpcName, RpcTarget target, params object[] args)
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    foreach (var arg in args)
                    {
                        switch (arg)
                        {
                            case int v: writer.Write(v); break;
                            case float v: writer.Write(v); break;
                            case string v: writer.Write(v); break;
                            case bool v: writer.Write(v); break;
                            case byte[] v: writer.Write(v.Length); writer.Write(v); break;

                            default: throw new NotSupportedException($"Type {arg.GetType()} not supported");
                        }
                    }
                    byte[] paramBytes = stream.ToArray();

                    Packet packet = new Packet
                    {
                        cmd = Command.RPC,
                        data = [(byte)target, .. BitConverter.GetBytes(ObjectID), .. BitConverter.GetBytes(Encoding.UTF8.GetByteCount(rpcName)), .. Encoding.UTF8.GetBytes(rpcName), .. paramBytes]
                    };
                    NetworkManager.SendPacket(packet);

                    if (RpcTarget.All == target)
                        RunRPC(rpcName, paramBytes);
                }
            }

            /// <summary>
            /// When binding an RPC, the method must be return type void.
            /// </summary>
            public void BindRPC(string rpcName, Delegate method)
            {
                Log.Message($"Bound RPC '{rpcName}'");
                if (rpcName == null || rpcName == " ")
                {
                    Log.Error("Actual RPC name needed!");
                    return;
                }
                if (!bindedRpcs.ContainsKey(rpcName))
                {
                    bindedRpcs[rpcName] = method;
                }
                else
                {
                    Log.Error($"RPC already exists with name {rpcName}!");
                }
            }
            public void BindRPC(string rpcName, Action m) => BindRPC(rpcName, (Delegate)m);
            public void BindRPC<T>(string rpcName, Action<T> m) => BindRPC(rpcName, (Delegate)m);
            public void BindRPC<T1, T2>(string rpcName, Action<T1, T2> m) => BindRPC(rpcName, (Delegate)m);
            public void BindRPC<T1, T2, T3>(string rpcName, Action<T1, T2, T3> m) => BindRPC(rpcName, (Delegate)m);
            public void BindRPC<T1, T2, T3, T4>(string rpcName, Action<T1, T2, T3, T4> m) => BindRPC(rpcName, (Delegate)m);
            public void BindRPC<T1, T2, T3, T4, T5>(string rpcName, System.Action<T1, T2, T3, T4, T5> m) => BindRPC(rpcName, (Delegate)m);


            private object[] ReadParams(byte[] data, System.Reflection.ParameterInfo[] methodParams)
            {
                var args = new object[methodParams.Length];
                using (var stream = new MemoryStream(data))
                using (var reader = new BinaryReader(stream))
                {
                    for (int i = 0; i < methodParams.Length; i++)
                    {
                        var type = methodParams[i].ParameterType;
                        if (type == typeof(int)) args[i] = reader.ReadInt32();
                        else if (type == typeof(float)) args[i] = reader.ReadSingle();
                        else if (type == typeof(string)) args[i] = reader.ReadString();
                        else if (type == typeof(bool)) args[i] = reader.ReadBoolean();
                        else if (type == typeof(byte[])) args[i] = reader.ReadBytes(reader.ReadInt32());
                    }
                }
                return args;
            }

            /// <summary>
            /// Locally run an binded RPC method (dont use this)
            /// </summary>
            /// <param name="rpcName"></param>
            /// <param name="args"></param>
            public void RunRPC(string rpcName, byte[] paramData)
            {
                if (bindedRpcs.TryGetValue(rpcName, out var method))
                {
                    try
                    {
                        var methodParams = method.Method.GetParameters();
                        object[] convertedArgs = ReadParams(paramData, methodParams);

                        method.DynamicInvoke(convertedArgs);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Error trying to execute RPC: '{rpcName}', Error: {e}");
                    }
                }
                else
                {
                    Log.Warn($"RPC Command '{rpcName}' recieved but no RPC was bound, use BindRPC('{rpcName}', Delegate) to do so");
                }
            }
        }
        public enum RpcTarget : byte
        {
            All = 0x00,
            Others = 0x01,
            Master = 0x02
        }
        // Server-Side
        // Manages all network traffic for server
        public class RelayServer
        {
            private HServer server;
            public Dictionary<uint, ServerClient> clients = new Dictionary<uint, ServerClient>(); // uint is ID
            public Dictionary<string, ServerRoom> rooms = new Dictionary<string, ServerRoom>(); // for tracking who's the host client

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
                                    Packet setID = new Packet
                                    {
                                        cmd = Command.SetID,
                                        data = BitConverter.GetBytes(++clientIDCounter)
                                    };

                                    if (!clients.ContainsKey(clientIDCounter))
                                    {
                                        Log.Message($"Server assigned a client a new ID: {clientIDCounter}");
                                        if (endPoint == null) Log.Error("EndPoint is null");
                                        server.SendPacket(new EndPointPacket(endPoint, setID));
                                        clients[clientIDCounter] = new ServerClient(endPoint);
                                    }
                                    else
                                    {
                                        Log.Error("Server tried to assigned a client with an ID thats already in use");
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
                                        case Command.Reciept:
                                            {
                                                Log.Message($"Reciept recived for my previous command: {packet.cmd} : ID {packet.packetID}");
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
                                                }

                                                UpdateClientState(packet.senderID);
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
                    EndPointPacket state = new EndPointPacket
                    {
                        endPoint = client.ep,
                        packet = new Packet
                        {
                            cmd = Command.UpdateState,
                            data = [(byte)(client.MasterClient ? 1 : 0), .. BitConverter.GetBytes(client.ID), .. Encoding.UTF8.GetBytes(client.Room)]
                        }
                    };

                    server.SendPacket(state);
                    //Log.Message($"Updating client '{id}' state");
                }
                else
                {
                    Log.Error($"ID: '{id}' doesnt exist in the server.");
                }
            }
        }
        namespace Types
        {

            public struct Packet
            {
                public uint senderID;
                public uint packetID;
                public Command cmd;
                public byte[] data;

                public byte[] Serialize()
                {
                    byte[] raw = new byte[9 + (data?.Length ?? 0)];

                    BitConverter.TryWriteBytes(raw.AsSpan(0, 4), senderID);
                    BitConverter.TryWriteBytes(raw.AsSpan(4, 4), packetID);
                    raw[8] = (byte)cmd;

                    if (data != null && data.Length > 0)
                    {
                        Buffer.BlockCopy(data, 0, raw, 9, data.Length);
                    }

                    return raw;
                }

                public void Deserialize(byte[] rawData, int receivedBytes)
                {
                    senderID = BitConverter.ToUInt32(rawData, 0);
                    packetID = BitConverter.ToUInt32(rawData, 4);
                    cmd = (Command)rawData[8];
                    data = rawData[9..receivedBytes];
                }
            }
            public struct EndPointPacket
            {
                public EndPoint endPoint;
                public Packet packet;
                public EndPointPacket(EndPoint ep, Packet _packet)
                {
                    endPoint = ep;
                    packet = _packet;
                }
            }
            public enum Command : Byte
            {
                // Null Command
                NULL = 0x00,
                // Utility
                Ping = 0x01,    // Server Sent to clients
                Pong = 0x02,    // Client Sent to relay
                Connect = 0x03,

                // For RPC's to prove they were sent and recieved
                Reciept = 0x04,

                // Client State Control
                UpdateState = 0x05,
                SetID = 0x06,
                SetRoom = 0x07,

                // Safe, reliable
                RPC = 0x08,
                NetworkVariable = 0x09,

                // Unsafe, Unreliable
                FastData = 0x0A
            }
            public class ServerClient
            {
                public DateTime lastPing;
                public EndPoint ep;
                public uint ID = 0;
                public string Room = "";
                public bool MasterClient = false;
                public ServerClient(EndPoint endPoint)
                {
                    ep = endPoint;
                    lastPing = DateTime.UtcNow;
                }
            }
            public class ServerRoom
            {
                public string Name = "";
                public uint HostID = 0;
                public HashSet<uint> ClientIDs = new HashSet<uint>();

            }
        }
        namespace Internal
        {
            // Higher Level Client
            public class HClient
            {
                public uint ID { get; private set; }
                public string Room { get; private set; }
                public bool Master { get; private set; }

                private Client_ client;
                private uint packetCount = 0;
                private ConcurrentQueue<Packet> pendingPackets = new ConcurrentQueue<Packet>();
                public HClient(IPAddress ip, int port = 8082)
                {
                    client = new Client_();
                    client.Initialize(ip, port);
                    client.StartMessageLoop();
                    StartPacketLoop();
                    ProcessPackets();
                }

                private void StartPacketLoop()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                while (pendingPackets.TryDequeue(out Packet packet))
                                {
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
            }
            public class HServer
            {
                public Server_ server;
                private uint packetCount = 0;
                private ConcurrentQueue<EndPointPacket> pendingPackets = new ConcurrentQueue<EndPointPacket>();
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
                                while (pendingPackets.TryDequeue(out EndPointPacket epp))
                                {
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
            }

            public class Server_
            {

                public ConcurrentQueue<EndPointPacket> pendingRecievedPackets = new ConcurrentQueue<EndPointPacket>();
                public const int PORT = 8082;

                private Socket _socket;
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

            public class Client_
            {
                public ConcurrentQueue<Packet> pendingRecievedPackets = new ConcurrentQueue<Packet>();
                private Socket _socket;
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
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);

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
                                var res = await _socket.ReceiveMessageFromAsync(_buffer_recv_segment, SocketFlags.None, _blankEP);
                                byte[] rawBytes = _buffer_recv;

                                Packet packet = new Packet();
                                packet.Deserialize(rawBytes, res.ReceivedBytes);

                                //Log.Message($"Recived Packet with command: {packet.cmd}");
                                pendingRecievedPackets.Enqueue(packet);
                            }
                            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                            {
                                Log.Warn("Lost connection to server: " + ex.Message);
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
