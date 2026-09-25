using SimplyNetworking.Components;

using UnityEditor;
using UnityEngine;

namespace SimplyNetworking
{
    namespace EditorAdditions
    {
        #if UNITY_EDITOR
        

        [CustomEditor(typeof(NetworkIdentity))]
        public class NetworkIdentityEditor : Editor
        {
            private void OnEnable()
            {
                NetworkIdentity identity = (NetworkIdentity)target;

                if (identity.ObjectID == 0 || IsDuplicateID(identity))
                {
                    AssignNextUniqueID(identity);
                }
            }

            public override void OnInspectorGUI()
            {
                NetworkIdentity identity = (NetworkIdentity)target;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LongField("Object ID", identity.ObjectID);
                EditorGUI.EndDisabledGroup();
            }

            private static void AssignNextUniqueID(NetworkIdentity current)
            {
                NetworkIdentity[] allIdentities = FindObjectsOfType<NetworkIdentity>(true);

                uint maxId = 0;
                foreach (var netId in allIdentities)
                {
                    if (netId != current && netId.ObjectID > maxId)
                    {
                        maxId = netId.ObjectID;
                    }
                }

                Undo.RecordObject(current, "Assign Network ObjectID");
                current.EditorSetObjectID(maxId + 1);
                EditorUtility.SetDirty(current);
            }

            private static bool IsDuplicateID(NetworkIdentity current)
            {
                NetworkIdentity[] allIdentities = FindObjectsOfType<NetworkIdentity>(true);
                foreach (var netId in allIdentities)
                {
                    if (netId != current && netId.ObjectID == current.ObjectID)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        #endif
    }
}

