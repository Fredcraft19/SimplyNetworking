using SimplyNetworking.Debug;
using SimplyNetworking.Components;

using System;
using System.IO;
using System.Text;
using System.Net;
using System.Collections.Generic;
using System.Linq;

namespace SimplyNetworking
{

        namespace Types
        {
            
            
            public static class ByteHelper
            {
                public static byte[] SerializeValue<T>(T _value)
                {
                    using var ms = new MemoryStream();
                    using var writer = new BinaryWriter(ms);

                    switch (_value)
                    {
                        case int v: writer.Write(v); break;
                        case float v: writer.Write(v); break;
                        case double v: writer.Write(v); break;
                        case bool v: writer.Write(v); break;
                        case string v: writer.Write(v ?? string.Empty); break;
                        case byte[] v: writer.Write(v); break;
                        case ushort v: writer.Write(v); break;
                        case uint v: writer.Write(v); break;
                        case long v: writer.Write(v); break;

                        default:
                            throw new NotSupportedException($"Type {typeof(T).Name} is not supported for NetworkVariable serialization.");
                    }

                    return ms.ToArray();
                }
                public static T DeserializeValue<T>(byte[] data)
                {
                    using var ms = new MemoryStream(data);
                    using var reader = new BinaryReader(ms);
                    Type type = typeof(T);

                    object result = type switch
                    {
                        _ when type == typeof(int) => reader.ReadInt32(),
                        _ when type == typeof(float) => reader.ReadSingle(),
                        _ when type == typeof(double) => reader.ReadDouble(),
                        _ when type == typeof(bool) => reader.ReadBoolean(),
                        _ when type == typeof(string) => reader.ReadString(),
                        _ when type == typeof(byte[]) => reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)),
                        _ when type == typeof(ushort) => reader.ReadUInt16(),
                        _ when type == typeof(uint) => reader.ReadUInt32(),
                        _ when type == typeof(long) => reader.ReadInt64(),

                        _ => throw new NotSupportedException($"Type {type.Name} is not supported for NetworkVariable deserialization.")
                    };

                    return (T)result;
                }
            }
            public abstract class NetworkVariableBase
            {
                public string Name { get; set; }
                public abstract object RawValue { get; set; }
                public abstract bool IsDirty { get; internal set; }
                public abstract void DeserializeValue(byte[] data);
                internal uint objectId;

                /// <summary>
                /// Dont use this unless you know what your doing please.
                /// </summary>
                /// <param name="id"></param>
                public void NetworkIdentityGetID(NetworkIdentity id)
                {
                    objectId = id.ObjectID;
                }
            }

            public class NetworkVariable<T> : NetworkVariableBase
            {

                public NetworkDelivery delivery;

                public NetworkVariable(string name, T defaultValue = default, NetworkDelivery _delivery = NetworkDelivery.Reliable)
                {
                    Name = name;
                    _value = defaultValue;
                    delivery = _delivery;
                }


                private T _value;
                public T Value
                {
                    get => _value;
                    set
                    {
                        if (!EqualityComparer<T>.Default.Equals(_value, value))
                        {
                            _value = value;
                            IsDirty = true;

                            // Sync accross network

                            Log.Message($"Syncing Value of '{Name}' to '{_value.ToString()}'");

                            byte[] nameBytes = Encoding.UTF8.GetBytes(Name);
                            byte[] valueBytes = ByteHelper.SerializeValue<T>(_value);

                            byte[] dataPayload;
                            using (MemoryStream ms = new MemoryStream())
                            using (BinaryWriter writer = new BinaryWriter(ms))
                            {
                                writer.Write((byte)delivery);
                                writer.Write(objectId);
                                writer.Write(nameBytes.Length);
                                writer.Write(nameBytes);
                                writer.Write(valueBytes);
                                dataPayload = ms.ToArray();
                            }

                            Packet sync = new Packet
                            {
                                cmd = Command.NetworkVariable,
                                data = dataPayload
                            };

                            if (delivery == NetworkDelivery.Reliable)
                            {
                                NetworkManager.SendReliablePacket(sync);
                            }
                            else
                            {
                                NetworkManager.SendPacket(sync);
                            }
                        }
                    }
                }

                public override object RawValue
                {
                    get => Value;
                    set => Value = (T)value;
                }

                public override bool IsDirty { get; internal set; }


                public override void DeserializeValue(byte[] data)
                {
                    _value = ByteHelper.DeserializeValue<T>(data);
                    Log.Message($"Set value of '{Name}' to '{_value.ToString()}'");
                }
            }
            /// <summary>
            /// Network Delivery types for Network Variables
            /// </summary>
            public enum NetworkDelivery
            {
                /// <summary>
                /// For high frequency updates
                /// </summary>
                Unreliable,
                /// <summary>
                /// For occasional updates
                /// </summary>
                Reliable
            }
            public enum NetworkComponentType : byte
            {
                Null = 0x00,
                Identity = 0x01,
                Transform = 0x02
            }

            public struct RpcData
            {
                public object[] args;
                public Delegate method;
            }

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
                ClientUpdate = 0x08,    // another client joined/left the room

                // Safe, reliable
                RPC = 0x09,
                NetworkVariable = 0x0A,

                // Unsafe, Unreliable
                FastData = 0x0B
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

            public enum RpcTarget : byte
            {
                All = 0x00,
                Others = 0x01,
                Master = 0x02
            }
            
            public class PendingRPC
            {
                public RpcTarget target;
                public uint objectId;
                public string name;
                public byte[] data;
            }

        }
    
}
