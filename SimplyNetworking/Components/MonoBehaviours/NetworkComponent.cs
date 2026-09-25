using System;
using System.Collections;
using System.Collections.Concurrent;
using SimplyNetworking.Debug;
using SimplyNetworking.Types;

using UnityEngine;

namespace SimplyNetworking
{
    namespace Components
    {
        public class NetworkComponent : NetworkBehaviour
        {
            internal void Initialize(NetworkComponentType type)
            {
                identity.AddNetworkComponent(this, type);
                StartCoroutine(HandlePackets());
            }


            private ConcurrentQueue<Packet> packets = new();
            public void AddPacket(Packet packet) => packets.Enqueue(packet);
            private IEnumerator HandlePackets()
            {
                while (true)
                {
                    try
                    {
                        while(packets.TryDequeue(out var packet)) HandlePacket(packet);
                    }
                    catch(Exception e)
                    {
                        Log.Error("NetworkTransform.HandlePackets()  Error: " + e.Message);
                    }

                    yield return new WaitForSeconds(0.002f);
                }
            }

            
            public virtual void HandlePacket(Packet packet) {}
        }
    }
}