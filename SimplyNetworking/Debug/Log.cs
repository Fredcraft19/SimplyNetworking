using UnityEngine;

namespace SimplyNetworking
{
    namespace Debug
    {
        public enum LogLevel
        {
            All,
            ErrorsAndLogs,
            WarningsAndErrors,
            Errors,
            None
        }
        
        public static class Log
        {
            public static LogLevel level = LogLevel.All;
            public static void Message(string msg)
            {
                if (level == LogLevel.All || level == LogLevel.ErrorsAndLogs) UnityEngine.Debug.Log($"[Network Log] {msg}");
            }
            public static void Error(string err_msg)
            {
                if (level == LogLevel.Errors || level == LogLevel.All || level == LogLevel.WarningsAndErrors)
                {
                    UnityEngine.Debug.LogError($"[Network Error] {err_msg}");
                }
            }
            public static void Warn(string wrn_msg)
            {
                if (level == LogLevel.All || level == LogLevel.WarningsAndErrors || level == LogLevel.ErrorsAndLogs)
                {
                    UnityEngine.Debug.LogWarning($"[Network Warning] {wrn_msg}");
                }
            }
        }
    }
}