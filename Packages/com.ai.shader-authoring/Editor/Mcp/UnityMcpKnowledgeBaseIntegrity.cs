#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMcp.Editor
{
    [InitializeOnLoad]
    internal static class UnityMcpKnowledgeBaseIntegrity
    {
        internal const string SchemaVersion = "2";
        private static readonly string[] SourceExtensions = { ".shader", ".hlsl", ".cginc", ".shadergraph", ".mat" };
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedSource> SourceCache = new Dictionary<string, CachedSource>(StringComparer.OrdinalIgnoreCase);
        private static Snapshot cachedSnapshot;
        private static string cachedEnvironmentPayload;
        private static Task<Snapshot> inFlightCapture;
        private static int invalidationVersion;
        private static int capturedInvalidationVersion = -1;

        static UnityMcpKnowledgeBaseIntegrity()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        internal sealed class Snapshot
        {
            public string environmentFingerprint;
            public string projectAssetFingerprint;
            public string sourceFingerprint;
            public SourceEntry[] sources;
            public string[] changedDomains;
        }

        [Serializable] internal sealed class SourceEntry { public string path; public string revision; public string[] includeDependencies; }
        private sealed class CachedSource { public long length; public long lastWriteUtcTicks; public SourceEntry source; }

        internal static Snapshot Capture()
        {
            return CaptureCore(CancellationToken.None);
        }

        internal static Task<Snapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            var environmentPayload = BuildEnvironmentPayload();
            lock (CacheLock)
            {
                if (TryGetCachedSnapshot(environmentPayload, out var snapshot)) return Task.FromResult(snapshot);
                if (inFlightCapture != null && !inFlightCapture.IsCompleted) return inFlightCapture;
                var captureVersion = invalidationVersion;
                inFlightCapture = Task.Run(() => CaptureCore(environmentPayload, captureVersion, cancellationToken), cancellationToken);
                return inFlightCapture;
            }
        }

        private static Snapshot CaptureCore(CancellationToken cancellationToken)
        {
            var environmentPayload = BuildEnvironmentPayload();
            int captureVersion;
            lock (CacheLock)
            {
                if (TryGetCachedSnapshot(environmentPayload, out var cached)) return cached;
                captureVersion = invalidationVersion;
            }
            return CaptureCore(environmentPayload, captureVersion, cancellationToken);
        }

        private static Snapshot CaptureCore(string environmentPayload, int captureVersion, CancellationToken cancellationToken)
        {
            var sources = EnumerateSources(cancellationToken).OrderBy(item => item.path, StringComparer.OrdinalIgnoreCase).ToArray();
            var snapshot = new Snapshot
            {
                environmentFingerprint = Hash(environmentPayload),
                projectAssetFingerprint = Hash(string.Join("\n", sources.Where(item => item.path.StartsWith("Assets/", StringComparison.Ordinal)).Select(item => item.path + ":" + item.revision))),
                sourceFingerprint = Hash(string.Join("\n", sources.Select(item => item.path + ":" + item.revision + ":" + string.Join(",", item.includeDependencies ?? Array.Empty<string>())))),
                sources = sources,
                changedDomains = Array.Empty<string>()
            };
            lock (CacheLock)
            {
                if (captureVersion == invalidationVersion)
                {
                    cachedSnapshot = snapshot;
                    cachedEnvironmentPayload = environmentPayload;
                    capturedInvalidationVersion = captureVersion;
                }
            }
            return snapshot;
        }

        private static bool TryGetCachedSnapshot(string environmentPayload, out Snapshot snapshot)
        {
            snapshot = cachedSnapshot;
            return snapshot != null
                && capturedInvalidationVersion == invalidationVersion
                && string.Equals(cachedEnvironmentPayload, environmentPayload, StringComparison.Ordinal);
        }

        internal static void Invalidate()
        {
            lock (CacheLock)
            {
                invalidationVersion++;
                cachedSnapshot = null;
                cachedEnvironmentPayload = null;
                inFlightCapture = null;
            }
        }

        private static string BuildEnvironmentPayload()
        {
            const string packageLockPath = "Packages/packages-lock.json";
            var packageLockStamp = File.Exists(packageLockPath)
                ? File.GetLastWriteTimeUtc(packageLockPath).Ticks + ":" + new FileInfo(packageLockPath).Length
                : "missing";
            return string.Join("|", new[]
            {
                Application.unityVersion,
                GraphicsSettings.currentRenderPipeline == null ? "builtin" : AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),
                GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().AssemblyQualifiedName,
                PlayerSettings.colorSpace.ToString(),
                packageLockStamp
            });
        }

        internal static string[] Compare(JsonElement manifest, Snapshot current)
        {
            var changed = new List<string>();
            if (ReadString(manifest, "schemaVersion") != SchemaVersion) changed.Add("schema");
            if (ReadString(manifest, "environmentFingerprint") != current.environmentFingerprint) changed.Add("environment");
            if (ReadString(manifest, "projectAssetFingerprint") != current.projectAssetFingerprint) changed.Add("project_assets");
            if (ReadString(manifest, "sourceFingerprint") != current.sourceFingerprint) changed.Add("source_dependencies");
            return changed.ToArray();
        }

        internal static void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
        }

        private static IEnumerable<SourceEntry> EnumerateSources(CancellationToken cancellationToken)
        {
            foreach (var root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = file.Replace('\\', '/');
                    if (!SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                    SourceEntry source;
                    try { source = ReadSourceIncrementally(file, path); } catch { continue; }
                    yield return source;
                }
            }
        }

        private static SourceEntry ReadSourceIncrementally(string file, string path)
        {
            var info = new FileInfo(file);
            var length = info.Length;
            var lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
            lock (CacheLock)
            {
                if (SourceCache.TryGetValue(path, out var cached)
                    && cached.length == length
                    && cached.lastWriteUtcTicks == lastWriteUtcTicks)
                    return cached.source;
            }

            var bytes = File.ReadAllBytes(file);
            var text = Path.GetExtension(path).Equals(".mat", StringComparison.OrdinalIgnoreCase) ? string.Empty : Encoding.UTF8.GetString(bytes);
            var source = new SourceEntry { path = path, revision = Hash(bytes), includeDependencies = ExtractIncludes(text).ToArray() };
            lock (CacheLock)
            {
                SourceCache[path] = new CachedSource { length = length, lastWriteUtcTicks = lastWriteUtcTicks, source = source };
            }
            return source;
        }

        private static IEnumerable<string> ExtractIncludes(string text)
        {
            using (var reader = new StringReader(text ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("#include", StringComparison.Ordinal)) continue;
                    var first = trimmed.IndexOf('"');
                    var last = trimmed.LastIndexOf('"');
                    if (first >= 0 && last > first) yield return trimmed.Substring(first + 1, last - first - 1).Replace('\\', '/');
                }
            }
        }

        private static string ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
