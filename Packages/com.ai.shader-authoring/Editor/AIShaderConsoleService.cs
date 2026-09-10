#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEditor;

namespace AIShader.Editor
{
    /// <summary>
    /// Unity Console cache based on the existing dsh Unity Console MCP implementation.
    /// Logs are pulled on demand by MCP and remain in memory only.
    /// </summary>
    [InitializeOnLoad]
    public static class AIShaderConsoleService
    {
        public sealed class Entry
        {
            public string id;
            public string timestampUtc;
            public string level;
            public string message;
            public string stackTrace;
            public int threadId;
        }

        private const int DefaultMaxLogs = 2000;
        private const int MaxStackTraceCharacters = 12000;
        private static readonly object SyncRoot = new object();
        private static readonly List<Entry> Logs = new List<Entry>();

        static AIShaderConsoleService()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.quitting += Shutdown;
        }

        public static int Count
        {
            get { lock (SyncRoot) return Logs.Count; }
        }

        public static int ErrorCount
        {
            get
            {
                lock (SyncRoot)
                {
                    var count = 0;
                    foreach (var entry in Logs)
                        if (IsError(entry.level)) count++;
                    return count;
                }
            }
        }

        public static void Clear()
        {
            lock (SyncRoot) Logs.Clear();
        }

        public static List<Entry> Query(string level = null, string search = null, bool includeStackTrace = false, int limit = 100)
        {
            var result = new List<Entry>();
            limit = Mathf.Clamp(limit, 1, 500);
            lock (SyncRoot)
            {
                for (var i = Logs.Count - 1; i >= 0 && result.Count < limit; i--)
                {
                    var entry = Logs[i];
                    if (!MatchesLevel(entry.level, level) || !MatchesSearch(entry, search)) continue;
                    result.Add(Clone(entry, includeStackTrace));
                }
            }
            return result;
        }

        public static Entry GetById(string id)
        {
            lock (SyncRoot)
            {
                for (var i = 0; i < Logs.Count; i++)
                    if (Logs[i].id == id) return Clone(Logs[i], true);
            }
            return null;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition) && condition.StartsWith("[AI Shader MCP]", StringComparison.Ordinal)) return;
            var entry = new Entry
            {
                id = Guid.NewGuid().ToString("N"),
                timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                level = type.ToString(),
                message = condition ?? string.Empty,
                stackTrace = stackTrace ?? string.Empty,
                threadId = Thread.CurrentThread.ManagedThreadId
            };
            lock (SyncRoot)
            {
                Logs.Add(entry);
                while (Logs.Count > DefaultMaxLogs) Logs.RemoveAt(0);
            }
        }

        private static bool MatchesLevel(string actual, string requested)
        {
            if (string.IsNullOrEmpty(requested) || string.Equals(requested, "all", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(requested, "errors", StringComparison.OrdinalIgnoreCase)) return IsError(actual);
            return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSearch(Entry entry, string search)
        {
            return string.IsNullOrEmpty(search) || entry.message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || entry.stackTrace.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsError(string level) => level == "Error" || level == "Exception" || level == "Assert";

        private static Entry Clone(Entry source, bool includeStackTrace)
        {
            var trace = includeStackTrace ? source.stackTrace : string.Empty;
            if (trace.Length > MaxStackTraceCharacters) trace = trace.Substring(0, MaxStackTraceCharacters) + "\n... [truncated]";
            return new Entry { id = source.id, timestampUtc = source.timestampUtc, level = source.level, message = source.message, stackTrace = trace, threadId = source.threadId };
        }

        private static void Shutdown()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
        }
    }
}
#endif
