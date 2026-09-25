using SimplyNetworking.Debug;

using UnityEngine;

namespace SimplyNetworking
{
    namespace Components
    {
        /// <summary>
        /// Adds a base Network Identity reference 'identity' to MonoBehaviour
        /// </summary>
        public class NetworkBehaviour : MonoBehaviour
        {
            private NetworkIdentity _identity;
            public NetworkIdentity identity
            {
                get
                {
                    if(_identity == null)
                    {
                        _identity = GetComponent<NetworkIdentity>();
                        if(_identity == null) Log.Error("Network Identity component doesn't exist");
                    }
                    return _identity;
                }
            }
        }
    }
}

