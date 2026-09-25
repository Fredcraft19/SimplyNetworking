using System.Net;

using UnityEngine;

using SimplyNetworking.Debug;

namespace SimplyNetworking
{
    namespace Components
    {
        /// <summary>
        /// Adds useful Network callbacks to standard Network Behaviour class
        /// </summary>
        public abstract class NetworkBehaviourCallbacks : NetworkBehaviour
        {

            private void registerEvents()
            {
                Log.Message("JR: Registered Events! ");
                NetworkManager.AddCallbacks(OnServerConnected, OnJoinedRoom, OnLeftRoom, OnBecameMaster);
            }
            public virtual void OnServerConnected() {}
            public virtual void OnJoinedRoom() {}
            public virtual void OnLeftRoom() {}
            public virtual void OnBecameMaster() {}

            /// <summary>
            /// Registers events then runs NetworkStart() like normal Unity MonoBehaviour
            /// </summary>
            protected void Start()
            {
                registerEvents();
                NetworkStart();
            }

            /// <summary>
            /// Runs just like normal Unity Awake() after Callbacks has registered events to the Network Engine.
            /// </summary>
            public virtual void NetworkStart() {}
        }
    }
}