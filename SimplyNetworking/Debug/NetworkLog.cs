using SimplyNetworking.Debug;

using UnityEngine;

namespace SimplyNetworking
{
    namespace Debug
    {
        public class NetworkLog : MonoBehaviour
        {
            public LogLevel logLevel = LogLevel.WarningsAndErrors;
            void Awake()
            {
                Log.level = logLevel;
            }
        }
    }
}