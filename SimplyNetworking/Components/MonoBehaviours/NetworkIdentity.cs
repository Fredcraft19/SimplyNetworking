using SimplyNetworking.Types;
using SimplyNetworking.Debug;
using SimplyNetworking.EditorAdditions;

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using UnityEngine;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections;

namespace SimplyNetworking  
{
        namespace Components
        {
            public class NetworkIdentity : MonoBehaviour
            {   
                [Tooltip("The client ID of the owner of this object")]
                public uint OwnerID {get; set;}
                
                [Tooltip("Unique ID automaticalled assigned by the Editor.")]
                public uint ObjectID { get; set; }

                [Tooltip("If the object is owned by the Master Client of the room")]
                public bool IsMasterClients { get; private set; } = true;

                [Tooltip("If the object is owned by this client")]
                public bool IsMine { get; private set; } = false;

                [Tooltip("What the object should do when its owner leaves")]
                public OwnerLeaveAction ownerLeaveAction = OwnerLeaveAction.TransferToMaster;

                private Dictionary<NetworkComponentType, NetworkComponent> networkComponents = new();
            
                public void EditorSetObjectID(uint id)
                {
                    ObjectID = id;
                }

                public void CheckOwnership(uint ownerID)
                {
                    IsMine = ownerID == NetworkManager.ID;
                }

                public void AddNetworkComponent(NetworkComponent component, NetworkComponentType type)
                {
                    networkComponents[type] = component;
                }
                
                private Dictionary<string, Delegate> bindedRpcs = new Dictionary<string, Delegate>();
            
                /// <summary>
                /// Leave unless you know what your doing please!
                /// </summary>
                private readonly Dictionary<string, NetworkVariableBase> networkVariables = new();

                private ConcurrentQueue<Packet> fastPackets = new();

                private ConcurrentQueue<RpcData> queuedRpcs = new();
            
                public void Start()
                {
                    NetworkManager.GetIdentityID(this);
                    StartCoroutine(RpcDataLoop());
                    ProcessPackets();
                }

                public IEnumerator RpcDataLoop()
                {
                    while (true)
                    {
                        try
                        {
                            while(queuedRpcs.TryDequeue(out var data))
                            {
                                data.method.DynamicInvoke(data.args);
                            }
                        }
                        catch(Exception e)
                        {
                            Log.Error("Error in RpcData SlowUpdate(): " + e.Message);
                        }
                        yield return new WaitForSeconds(0.01f); // every 10ms
                    }
                }
            
                public void AddNetworkVaribale(NetworkVariableBase networkVariable)
                {
                    networkVariable.NetworkIdentityGetID(this);
                    networkVariables[networkVariable.Name] = networkVariable;
                }                
            
                public void SetNetworkVariable(string name, byte[] value)
                {
                    if (networkVariables.ContainsKey(name))
                    {
                        networkVariables[name].DeserializeValue(value);
                    }
                    else
                    {
                        Log.Error($"Network Variable '{name}' does not exist, can't set its value");
                    }
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
                                case uint v: writer.Write(v); break;
                                case float v: writer.Write(v); break;
                                case string v: writer.Write(v); break;
                                case bool v: writer.Write(v); break;
                                case byte[] v: writer.Write(v.Length); writer.Write(v); break;
            
                                default: throw new NotSupportedException($"Type {arg.GetType()} not supported");
                            }
                        }
                        byte[] paramBytes = stream.ToArray();
            
                        byte[] nameBytes = Encoding.UTF8.GetBytes(rpcName);
                        byte[] objectIdBytes = BitConverter.GetBytes(ObjectID);
                        byte[] nameLenBytes = BitConverter.GetBytes(nameBytes.Length);

                        int totalSize = 1 + objectIdBytes.Length + nameLenBytes.Length + nameBytes.Length + (paramBytes?.Length ?? 0);
                        byte[] payload = new byte[totalSize];

                        int offset = 0;
                        payload[offset++] = (byte)target;

                        Buffer.BlockCopy(objectIdBytes, 0, payload, offset, objectIdBytes.Length);
                        offset += objectIdBytes.Length;

                        Buffer.BlockCopy(nameLenBytes, 0, payload, offset, nameLenBytes.Length);
                        offset += nameLenBytes.Length;

                        Buffer.BlockCopy(nameBytes, 0, payload, offset, nameBytes.Length);
                        offset += nameBytes.Length;

                        if (paramBytes != null && paramBytes.Length > 0)
                        {
                            Buffer.BlockCopy(paramBytes, 0, payload, offset, paramBytes.Length);
                        }

                        Packet packet = new Packet
                        {
                            cmd = Command.RPC,
                            data = payload
                        };
                        
                        NetworkManager.SendReliablePacket(packet);
            
                        if (RpcTarget.All == target)
                            runLocalRpc(rpcName, paramBytes);
                    }
                }
            
                /// <summary>
                /// When binding an RPC, the method must be return type void.
                /// </summary>
                public void BindRPC(string rpcName, Delegate method)
                {
                    if (rpcName == null || rpcName == " ")
                    {
                        Log.Error("Actual RPC name needed!");
                        return;
                    }
                    if(method == null)
                    {
                        Log.Error("Method to be bound was null!");
                        return;
                    }
                    if(bindedRpcs == null)
                    {
                        Log.Warn("bindedRpcs was null. Making new bindedRpcs..");
                        bindedRpcs = new Dictionary<string, Delegate>();
                    }

                    if (!bindedRpcs.ContainsKey(rpcName))
                    {
                        bindedRpcs[rpcName] = method;
                        Log.Message($"Bound RPC '{rpcName}'");
                    }
                    else
                    {
                        Log.Error($"RPC already exists with name {rpcName}!");
                    }
                }
                public void BindRPC(string rpcName, Action m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1>(string rpcName, Action<T1> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2>(string rpcName, Action<T1, T2> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3>(string rpcName, Action<T1, T2, T3> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4>(string rpcName, Action<T1, T2, T3, T4> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5>(string rpcName, Action<T1, T2, T3, T4, T5> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5, T6>(string rpcName, Action<T1, T2, T3, T4, T5, T6> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5, T6, T7>(string rpcName, Action<T1, T2, T3, T4, T5, T6, T7> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5, T6, T7, T8>(string rpcName, Action<T1, T2, T3, T4, T5, T6, T7, T8> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string rpcName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> m) => BindRPC(rpcName, (Delegate)m);
                public void BindRPC<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string rpcName, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> m) => BindRPC(rpcName, (Delegate)m);
            
            
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
                            else if (type == typeof(uint)) args[i] = reader.ReadUInt32();
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
                public void runLocalRpc(string rpcName, byte[] paramData)
                {
                    if (bindedRpcs.TryGetValue(rpcName, out var _method))
                    {
                        try
                        {
                            var methodParams = _method.Method.GetParameters();
                            object[] convertedArgs = ReadParams(paramData, methodParams);

                            queuedRpcs.Enqueue(new RpcData
                                {
                                    args = convertedArgs,
                                    method = _method
                                });
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

                public void AddFastPacket(Packet fastPacket)
                {
                    fastPackets.Enqueue(fastPacket);
                }

                private void ProcessPackets()
                {
                    _ = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                while(fastPackets.TryDequeue(out Packet packet))
                                {
                                    NetworkComponentType targetType = (NetworkComponentType)packet.data[4];
                                    if (networkComponents.ContainsKey(targetType))
                                    {
                                        networkComponents[targetType].AddPacket(packet);
                                    }
                                    else
                                    {
                                        Log.Error($"Identity '{ObjectID}' doesnt have component of type: '{targetType}'");
                                    }
                                }
                            }
                            catch(Exception e)
                            {
                                Log.Error(e.Message);
                            }
                            await Task.Delay(5);
                        }
                    });
                }
            }
        }
    
}