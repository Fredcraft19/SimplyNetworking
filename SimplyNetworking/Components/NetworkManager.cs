using SimplyNetworking.Internal;
using SimplyNetworking.Types;
using SimplyNetworking.Debug;

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using Unity.VisualScripting;

namespace SimplyNetworking
{

        namespace Components
        {
            /// <summary>
            /// Client-Side -
            /// Manages all network traffic for client.
            /// </summary>
            public static class NetworkManager
            {
                // Unity API variables
                public static int tickRate {get; private set;} = 20;

                public static uint ID
                {
                get
                {
                    if(client == null)
                    {
                        return 0;
                    }
                    else
                    {
                        return client.ID;
                    }
                }
                }
                public static string Room
                {
                get
                {
                    if(client == null)
                    {
                        return "";
                    }
                    else
                    {
                        return client.Room;
                    }
                }
                }
                public static bool MasterClient
                {
                get
                {
                    if(client == null)
                    {
                        return false;
                    }
                    else
                    {
                        return client.Master;
                    }
                }
                }

                public static bool Connected
                {
                    get
                    {
                        if(client == null)
                            return false;
                        else
                        {
                            if(ID != 0)
                                return true;
                            else
                                return false;
                        }
                    }
                }
                public static bool InRoom
                {
                    get
                    {
                        if(client == null)
                            return false;
                        else
                        {
                            if(Room != "")
                                return true;
                            else
                                return false;
                        }
                    }
                }

                // private
                private static bool initialized = false;
                private static bool conected = false;
                private static HClient client;
                private static Dictionary<uint, NetworkIdentity> identitys = new Dictionary<uint, NetworkIdentity>();
                private static ConcurrentQueue<PendingRPC> pendingRPCs = new ConcurrentQueue<PendingRPC>();
                private static ConcurrentQueue<Packet> pendingFastData = new();
                private static NetworkIdentity managerIdentity;
                private static uint newId = 0;

                /// <summary>
                /// Sets the global networking tick rate accross all components
                /// </summary>
                public static void SetTickSpeed(int _tickRate = 20, bool ignoreWarning = false)
                {
                    if(_tickRate < 1)
                    {
                        Log.Warn("Tick rate cannot be less than 1. (Tickrate set to 1)");
                        tickRate = 1;
                        return;
                    }
                    else if(_tickRate > 120)
                    {
                        Log.Warn("Tick rate cannot be more than 120. (Tickrate set to 120)");
                        tickRate = 120;
                        return;
                    }

                    tickRate = _tickRate;

                    if(!ignoreWarning){
                        if(_tickRate > 72)
                        {
                            Log.Warn("Tickrate over 72Hz might make the Networking Engine struggle");
                        }
                        else if (_tickRate < 20)
                        {
                            Log.Warn("Tickrate under 20Hz might make the Networking very slow for movement");
                        }
                    }
                }
                // Callbacks
                public static void AddCallbacks(Action conenct_server, Action joined_room, Action left_room, Action became_master)
                {
                    client.OnConnected += conenct_server;
                    client.OnJoinedRoom += joined_room;
                    client.OnLeftRoom += left_room;
                    client.OnBecameMaster += became_master;
                }

                public static void AddPendingFastData(Packet packet)
                {
                    pendingFastData.Enqueue(packet);
                }

                public static void AddPendingRPC(PendingRPC rpc)
                {
                    pendingRPCs.Enqueue(rpc);
                }
                public static void GetIdentityID(NetworkIdentity i)
                {
                    i.ObjectID = newId++;
                    identitys[i.ObjectID] = i;
                }
                public static void AddPendingNetworkVariableSet(uint objectId, string varName, byte[] value)
                {
                    if (identitys.ContainsKey(objectId))
                    {
                        identitys[objectId].SetNetworkVariable(varName, value);
                    }
                    else
                    {
                        Log.Error($"Identity with ID '{objectId}' doesn't exist. Can't set Network Variables '{varName}' value");
                    }
                }

                private static void Initialize(IPAddress serverIP = null, int serverPort = 8082)
                {
                    if (initialized) return;
                    initialized = true;

                    newId = 0;

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
                                        identitys[rpc.objectId].runLocalRpc(rpc.name, rpc.data);
                                    }
            
                                }
                                else
                                {
                                    Log.Error("Can't  run an RPC on a Network Identity that doesnt exist");
                                }
                            }

                            while (pendingFastData.TryDequeue(out var packet))
                            {
                                uint objId = BitConverter.ToUInt32(packet.data, 0);
                                if (identitys.ContainsKey(objId))
                                {
                                    identitys[objId].AddFastPacket(packet);
                                }
                                else
                                {
                                    Log.Error($"No identity has ID '{objId}', can't send FastData to it.");
                                }
                            }

                            await Task.Delay(5);
                        }
                    });
                }
                /// <summary>
                /// Connect your client to the server
                /// </summary>
                public static void Connect(IPAddress serverIp, int serverPort = 8082)
                {
                    if (conected) return;
                    conected = true;

                    Initialize(serverIp, serverPort);

                    managerIdentity = new GameObject("Network Manager").AddComponent<NetworkIdentity>();
                    managerIdentity.AddComponent<NetworkInstantiateHandler>();

                    Packet connect = new Packet
                    {
                        cmd = Command.Connect
                    };
                    client.SendReliablePacket(connect);
                }
                /// <summary>
                /// Join room
                /// </summary>
                /// <param name="roomName">Name of the room you want to join</param>
                public static void JoinRoom(string roomName)
                {
                    if (ID == 0)
                    {
                        Log.Warn("Refusing to join a room without being connected to the server");
                        return;
                    }
            
                    Packet joinRoom = new Packet
                    {
                        cmd = Command.SetRoom
                    };
            
                    if (roomName == null)
                    {
                        Log.Warn("Can't join a room with a null string for the name. Leaving room instead");
                        joinRoom.data = Encoding.UTF8.GetBytes("");
                    }
                    else
                    {
                        joinRoom.data = Encoding.UTF8.GetBytes(roomName);
                    }
                    client.SendReliablePacket(joinRoom);
                }
                /// <summary>
                /// Leave current room
                /// </summary>
                public static void LeaveRoom()
                {
                    Packet joinRoom = new Packet
                    {
                        cmd = Command.SetRoom,
                        data = Encoding.UTF8.GetBytes("")
                    };
                    client.SendReliablePacket(joinRoom);
                }

                /// <summary>
                /// Instatiate a GameObject accross all clients
                /// </summary>
                /// <param name="objectName">The name of the game object in Resources folder that you want to Instantiate</param>
                /// <param name="position">Position to instatiate the game object</param>
                /// <param name="rotation">Rotation to instatiate the game object</param>
                /// <returns></returns>
                public static GameObject Instantiate(string objectName, Vector3 position, Quaternion rotation)
                {
                    if(Room != "")
                    {
                        managerIdentity.RPC("Instantiate", RpcTarget.All, objectName, ID, position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w);
                    }
                    else
                    {
                        Log.Error("Can't network instantiate when not in a room!");
                    }
                    return null;
                }            
            
                /// <summary>
                /// Don't use this please, unless you know what your doing.
                /// </summary>
                public static void SendPacket(Packet packet)
                {
                    client.SendPacket(packet);
                }
                /// <summary>
                /// Don't use this please, unless you know what your doing.
                /// </summary>
                public static void SendReliablePacket(Packet packet)
                {
                    client.SendReliablePacket(packet);
                }
                ///// <summary>
                ///// Don't use this please, unless you know what your doing.
                ///// </summary>
                //public static void AddFastDataPacket(uint objectId, Packet packet)
                //{
                //    if (identitys.ContainsKey(objectId))
                //    {
                //        identitys[objectId].AddFastPacket(packet);
                //    }
                //    else
                //    {
                //        Log.Error("identity with id '{objectId}' does not exist, can't add fast data packet to it!");
                //    }
                //}
            
            }
        }
    
}