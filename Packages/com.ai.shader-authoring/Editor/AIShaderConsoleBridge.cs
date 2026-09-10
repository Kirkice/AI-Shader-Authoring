#if UNITY_EDITOR
using System.Collections.Generic;

namespace AIShader.Editor
{
    /// <summary>Compatibility facade backed by AIShaderConsoleService.</summary>
    public static class AIShaderConsoleBridge
    {
        public sealed class Entry
        {
            public string id;
            public string level;
            public string message;
            public string stackTrace;
            public string timestampUtc;
            public int threadId;
        }

        public static void Clear() => AIShaderConsoleService.Clear();
        public static int ErrorCount => AIShaderConsoleService.ErrorCount;

        public static List<Entry> Get(string level = null, int limit = 100)
        {
            var source = AIShaderConsoleService.Query(level, null, true, limit);
            var result = new List<Entry>(source.Count);
            foreach (var item in source)
                result.Add(new Entry { id = item.id, level = item.level, message = item.message, stackTrace = item.stackTrace, timestampUtc = item.timestampUtc, threadId = item.threadId });
            return result;
        }
    }
}
#endif
