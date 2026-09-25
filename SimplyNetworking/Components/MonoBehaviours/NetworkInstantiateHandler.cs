using UnityEngine;

using SimplyNetworking.Debug;

namespace SimplyNetworking
{
    namespace Components
    {
        public class NetworkInstantiateHandler : NetworkBehaviour
        {
            void Start()
            {
                if(identity == null)
                {
                    Log.Error("Identity was null?!");
                    return;
                }

                identity.BindRPC
                <
                string, uint,
                float, float, float,        // Position
                float, float, float, float  // Rotation
                >
                ("Instantiate", Instantiate);
            }
            void Instantiate(string name, uint ownerID, float px, float py, float pz, float rx, float ry, float rz, float rw)
            {
                Vector3 position = new Vector3(px, py, pz);
                Quaternion rotation = new Quaternion(rx, ry, rz, rw);
                GameObject obj = Resources.Load<GameObject>(name);
                if(obj == null)
                {
                    Log.Error($"GameObject with name '{name}' can't be found to instantiate");
                }
                else
                {
                    NetworkIdentity id = GameObject.Instantiate(obj, position, rotation).GetComponent<NetworkIdentity>();
                    if (id == null)
                    {
                        Log.Error("You can only network instantiate game objects with the Network Identity component.");
                    }
                    else
                    {
                        id.ownerLeaveAction = EditorAdditions.OwnerLeaveAction.Destroy;
                    }
                    
                    if(ownerID == NetworkManager.ID)
                    {
                        id.CheckOwnership(ownerID);
                    }
                }
            }

        }
    }
}

