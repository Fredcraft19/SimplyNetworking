using System;
using System.Collections;
using System.IO;
using SimplyNetworking.Debug;
using SimplyNetworking.Types;
using UnityEngine;


namespace SimplyNetworking
{
    namespace Components
    {
        public enum NetworkTransformType : byte
        {
            Position = 0x01,
            Rotation = 0x02,
            Scale = 0x03
        }

        public class NetworkTransform : NetworkComponent
        {
            [Header("Sync Settings")]
            [Tooltip("Enables the tracking of the objects position")]
            [field: SerializeField]
            private bool SyncPosition = true;
            
            [Tooltip("Enables the tracking of the objects rotation")]
            [field: SerializeField]
            private bool SyncRotation = true;
            [Tooltip("Enables the tracking of the objects scale")]
            [field: SerializeField]
            private bool SyncScale = true;
            [Header("Lerp Settings")]
            [Tooltip("Recomended value 10")]
            [field: SerializeField]

            public float lerpSpeed = 10f;

            [Tooltip("Enables lerping for when the position is changed")]
            [field: SerializeField]

            private bool LerpPosition = true;   

            [Tooltip("Enables lerping for when the rotation is changed")]

            [field: SerializeField]
            private bool LerpRotation = true;
            
            [Tooltip("Enables lerping for when the scale is changed")]
            [field: SerializeField]
            private bool LerpScale = true;

            

            private Vector3 oldPosition;
            private Quaternion oldRotation;
            private Vector3 oldScale;

            private Vector3 targetPosition;
            private Quaternion targetRotation;
            private Vector3 targetScale;

            void Start()
            {
                Initialize(NetworkComponentType.Transform);

                StartCoroutine(TickUpdate());

                targetPosition = transform.position;
                targetRotation = transform.rotation;
                targetScale = transform.localScale;



            }

            void Update()
            {
                if(identity.IsMine) return;

                if(SyncPosition)
                {
                    if (LerpPosition)
                        transform.position = Vector3.Lerp(transform.position, targetPosition,  lerpSpeed * Time.deltaTime);
                    else
                        transform.position = targetPosition;
                }

                if (SyncRotation)
                {
                    if (LerpRotation)
                        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,  lerpSpeed * Time.deltaTime);
                    else
                        transform.rotation = targetRotation;
                }

                if (SyncScale)
                {
                    if (LerpScale)
                        transform.localScale = Vector3.Lerp(transform.localScale, targetScale,  lerpSpeed * Time.deltaTime);
                    else
                        transform.localScale = targetScale;
                }
            }

            public override void HandlePacket(Packet packet)
            {
                NetworkTransformType type = (NetworkTransformType)packet.data[5];

                switch (type)
                {
                    case NetworkTransformType.Position:
                        {
                            Vector3 newPosition = new Vector3(
                                BitConverter.ToSingle(packet.data, 6) ,
                                BitConverter.ToSingle(packet.data, 10),
                                BitConverter.ToSingle(packet.data, 14)
                            );
                            targetPosition = newPosition;
                            break;
                        }
                    case NetworkTransformType.Rotation:
                        {
                            Quaternion newPosition = new Quaternion(
                                BitConverter.ToSingle(packet.data, 6) ,
                                BitConverter.ToSingle(packet.data, 10),
                                BitConverter.ToSingle(packet.data, 14),
                                BitConverter.ToSingle(packet.data, 18)
                            );
                            targetRotation  = newPosition;
                            break;
                        }
                    case NetworkTransformType.Scale:
                        {
                            Vector3 newScale = new Vector3(
                                BitConverter.ToSingle(packet.data, 6) ,
                                BitConverter.ToSingle(packet.data, 10),
                                BitConverter.ToSingle(packet.data, 14)
                            );
                            targetScale = newScale;
                            break;
                        }
                }
            }

            IEnumerator TickUpdate()
            {
                while (true)
                {
                    try
                    {
                        if (NetworkManager.InRoom && identity.IsMine)
                        {
                            if (SyncPosition)
                            {
                                if(oldPosition != transform.position)
                                {
                                    oldPosition = transform.position;
                                    Vector3 pos = transform.position;
                                    byte[] payload;
                                    
                                    using (var stream = new MemoryStream())
                                    using (var writer = new BinaryWriter(stream))
                                    {
                                        writer.Write(identity.ObjectID);
                                        writer.Write((byte)NetworkComponentType.Transform);
                                        writer.Write((byte)NetworkTransformType.Position);
                                        writer.Write(pos.x);
                                        writer.Write(pos.y);
                                        writer.Write(pos.z);
                                        payload = stream.ToArray();
                                    }

                                    NetworkManager.SendPacket(new Packet
                                    {
                                        cmd = Command.FastData,
                                        data = payload  
                                    });
                                }    
                            }

                            if (SyncRotation)
                            {
                                if(oldRotation != transform.rotation)
                                {
                                    oldRotation = transform.rotation;
                                    Quaternion rot = transform.rotation;
                                    byte[] payload;
                                    
                                    using (var stream = new MemoryStream())
                                    using (var writer = new BinaryWriter(stream))
                                    {
                                        writer.Write(identity.ObjectID);
                                        writer.Write((byte)NetworkComponentType.Transform);
                                        writer.Write((byte)NetworkTransformType.Rotation);
                                        writer.Write(rot.x);
                                        writer.Write(rot.y);
                                        writer.Write(rot.z);
                                        writer.Write(rot.w);
                                        payload = stream.ToArray();
                                    }


                                    NetworkManager.SendPacket(new Packet
                                    {
                                        cmd = Command.FastData,
                                        data = payload  
                                    });
                                }    
                            }

                            if (SyncPosition)
                            {
                                if(oldScale != transform.localScale)
                                {
                                    oldScale = transform.localScale;
                                    Vector3 sca = transform.localScale;
                                    byte[] payload;
                                    
                                    using (var stream = new MemoryStream())
                                    using (var writer = new BinaryWriter(stream))
                                    {
                                        writer.Write(identity.ObjectID);
                                        writer.Write((byte)NetworkComponentType.Transform);
                                        writer.Write((byte)NetworkTransformType.Scale);
                                        writer.Write(sca.x);
                                        writer.Write(sca.y);
                                        writer.Write(sca.z);
                                        payload = stream.ToArray();
                                    }

                                    NetworkManager.SendPacket(new Packet
                                    {
                                        cmd = Command.FastData, 
                                        data = payload  
                                    });
                                }    
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        Log.Error(e.Message);
                    }

                    yield return new WaitForSeconds((float)NetworkManager.tickRate / 1000f);
                }
            }
        }        
    }
}