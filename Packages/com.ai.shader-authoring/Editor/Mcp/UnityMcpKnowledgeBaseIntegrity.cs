#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMcp.Editor
{
    internal static class UnityMcpKnowledgeBaseIntegrity
    {
        internal const string SchemaVersion = "2";
        private static readonly string[] SourceExtensions = { ".shader", ".hlsl", ".cginc", ".shadergraph", ".mat" };

        internal sealed class Snapshot
        {
            public string environmentFingerprint;
            public string projectAssetFingerprint;
            public string sourceFingerprint;
            public SourceEntry[] sources;
            public string[] changedDomains;
        }

        [Serializable] internal sealed class SourceEntry { public string path; public string revision; public string[] includeDependencies; }

        internal static Snapshot Capture()
        {
            var sources = EnumerateSources().OrderBy(item => item.path, StringComparer.OrdinalIgnoreCase).ToArray();
            var environmentPayload = string.Join("|", new[]
            {
                Application.unityVersion,
                GraphicsSettings.currentRenderPipeline == null ? "builtin" : AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),
                GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().AssemblyQualifiedName,
                PlayerSettings.colorSpace.ToString(),
                File.Exists("Packages/packages-lock.json") ? Hash(File.ReadAllBytes("Packages/packages-lock.json")) : "missing"
            });
            return new Snapshot
            {
                environmentFingerprint = Hash(environmentPayload),
                projectAssetFingerprint = Hash(string.Join("\n", sources.Where(item => item.path.StartsWith("Assets/", StringComparison.Ordinal)).Select(item => item.path + ":" + item.revision))),
                sourceFingerprint = Hash(string.Join("\n", sources.Select(item => item.path + ":" + item.revision + ":" + string.Join(",", item.includeDependencies ?? Array.Empty<string>())))),
                sources = sources,
                changedDomains = Array.Empty<string>()
            };
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

        private static IEnumerable<SourceEntry> EnumerateSources()
        {
            foreach (var root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var path = file.Replace('\\', '/');
                    if (!SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(file); } catch { continue; }
                    var text = Path.GetExtension(path).Equals(".mat", StringComparison.OrdinalIgnoreCase) ? string.Empty : Encoding.UTF8.GetString(bytes);
                    yield return new SourceEntry { path = path, revision = Hash(bytes), includeDependencies = ExtractIncludes(text).ToArray() };
                }
            }
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
