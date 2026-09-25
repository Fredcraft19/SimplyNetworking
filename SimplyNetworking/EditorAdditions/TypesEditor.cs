

namespace SimplyNetworking
{
    namespace EditorAdditions
    {
        /// <summary>
        /// Determins if the Identity should be destroyed when its owner leaves or if its ownership should be transfered to the Master Client
        /// </summary>
        [System.Serializable]
        public enum OwnerLeaveAction
        {
            /// <summary>
            /// When the owner of a Network Identity leaves, the ownership of that object will be transfered to the Master Client
            /// </summary>
            TransferToMaster,
            /// <summary>
            /// When the owner of a Network Identity leaves, the object will be destroyed accross all clients
            /// </summary>
            Destroy
        }
    }
}

