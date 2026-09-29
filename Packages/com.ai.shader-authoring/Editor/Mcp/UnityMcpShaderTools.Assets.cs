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
using UnityEditor.PackageManager;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Rendering;
#endif
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    internal static partial class UnityMcpShaderTools
    {
        private static object GetKnowledgeBaseStatus(JsonElement args)
        {
            var currentPath = KnowledgeRoot + "current.json";
            if (!File.Exists(currentPath)) return new { exists = false, status = "missing", knowledgeBaseVersion = (string)null, manifestPath = (string)null };
            try
            {
#if UNITY_6000_0_OR_NEWER
                using var document = JsonDocument.Parse(File.ReadAllText(currentPath));
                var root = document.RootElement;
                var version = GetString(root, "knowledgeBaseVersion") ?? GetString(root, "version");
                var manifestPath = GetString(root, "manifestPath") ?? (version == null ? null : KnowledgeRoot + "versions/" + version + "/manifest.json");
                if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath)) return new { exists = true, status = "corrupt", knowledgeBaseVersion = version, manifestPath, fingerprintComparison = new { matches = false, changedDomains = new[] { "manifest" } } };
                using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var changedDomains = UnityMcpKnowledgeBaseIntegrity.Compare(manifestDocument.RootElement, UnityMcpKnowledgeBaseIntegrity.Capture());
                var declaredFresh = string.Equals(GetString(manifestDocument.RootElement, "freshnessStatus"), "fresh", StringComparison.OrdinalIgnoreCase);
                var fresh = declaredFresh && changedDomains.Length == 0;
                return new { exists = true, status = fresh ? "fresh" : "stale", knowledgeBaseVersion = version, manifestPath, fingerprintComparison = new { matches = fresh, changedDomains } };
#else
                using (var document = JsonDocument.Parse(File.ReadAllText(currentPath)))
                {
                    var root = document.RootElement;
                    var version = GetString(root, "knowledgeBaseVersion") ?? GetString(root, "version");
                    var manifestPath = GetString(root, "manifestPath") ?? (version == null ? null : KnowledgeRoot + "versions/" + version + "/manifest.json");
                    if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath)) return new { exists = true, status = "corrupt", knowledgeBaseVersion = version, manifestPath, fingerprintComparison = new { matches = false, changedDomains = new[] { "manifest" } } };
                    using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                    {
                        var changedDomains = UnityMcpKnowledgeBaseIntegrity.Compare(manifestDocument.RootElement, UnityMcpKnowledgeBaseIntegrity.Capture());
                        var declaredFresh = string.Equals(GetString(manifestDocument.RootElement, "freshnessStatus"), "fresh", StringComparison.OrdinalIgnoreCase);
                        var fresh = declaredFresh && changedDomains.Length == 0;
                        return new { exists = true, status = fresh ? "fresh" : "stale", knowledgeBaseVersion = version, manifestPath, fingerprintComparison = new { matches = fresh, changedDomains } };
                    }
                }
#endif
            }
            catch (Exception exception) { return new { exists = true, status = "failed", failureReason = exception.Message }; }
        }

        private static object QueryKnowledgeBase(JsonElement args)
        {
            var version = ResolveKnowledgeBaseVersion(args);
            if (string.IsNullOrEmpty(version))
                return new { status = "missing", results = Array.Empty<object>(), warning = "No persisted Shader Knowledge Base is available." };

            var root = KnowledgeRoot + "versions/" + version + "/";
            if (!Directory.Exists(root))
                return new { status = "missing", knowledgeBaseVersion = version, results = Array.Empty<object>(), warning = "The requested Shader Knowledge Base version does not exist." };
            var manifestPath = root + "manifest.json";
            if (!File.Exists(manifestPath)) return new { status = "corrupt", knowledgeBaseVersion = version, results = Array.Empty<object>(), warning = "Knowledge Base manifest is missing." };
            using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                var changedDomains = UnityMcpKnowledgeBaseIntegrity.Compare(manifestDocument.RootElement, UnityMcpKnowledgeBaseIntegrity.Capture());
                if (changedDomains.Length > 0 || !string.Equals(GetString(manifestDocument.RootElement, "freshnessStatus"), "fresh", StringComparison.OrdinalIgnoreCase))
                    return new { status = "stale", knowledgeBaseVersion = version, results = Array.Empty<object>(), changedDomains, warning = "Knowledge Base inputs changed; rebuild before retrieval." };
            }

            var query = GetString(args, "query") ?? string.Empty;
            var terms = TokenizeQuery(query).Concat(GetStringArray(args, "tags")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var requestedTypes = GetStringArray(args, "types");
            var limit = GetInt(args, "limit", 10, 1, 50);
            var partitions = new[]
            {
                new KnowledgePartition("shaderExample", root + "shader-corpus.json"),
                new KnowledgePartition("functionCard", root + "function-cards.json"),
                new KnowledgePartition("convention", root + "project-conventions.json"),
                new KnowledgePartition("capability", root + "capability-catalog.json")
            };
            var matches = new List<KnowledgeQueryMatch>();
            foreach (var partition in partitions)
            {
                if (requestedTypes.Length > 0 && !requestedTypes.Any(type => string.Equals(type, partition.type, StringComparison.OrdinalIgnoreCase)))
                    continue;
                matches.AddRange(QueryKnowledgePartition(partition, terms));
            }

            var results = matches
                .OrderByDescending(match => match.score)
                .ThenBy(match => match.type, StringComparer.Ordinal)
                .ThenBy(match => match.payload, StringComparer.Ordinal)
                .Take(limit)
                .Select(match => (object)new { type = match.type, score = match.score, item = match.payload })
                .ToArray();
            return new
            {
                status = "ok",
                knowledgeBaseVersion = version,
                query,
                tags = GetStringArray(args, "tags"),
                types = requestedTypes,
                limit,
                resultCount = results.Length,
                results,
                missingCoverage = results.Length == 0 ? new[] { "No indexed entry matched the requested query, tags, or types." } : Array.Empty<string>()
            };
        }

        private static string ResolveKnowledgeBaseVersion(JsonElement args)
        {
            var requested = GetString(args, "knowledgeBaseVersion");
            if (!string.IsNullOrEmpty(requested) && !string.Equals(requested, "current", StringComparison.OrdinalIgnoreCase)) return requested;
            var currentPath = KnowledgeRoot + "current.json";
            if (!File.Exists(currentPath)) return null;
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(currentPath)))
                    return GetString(document.RootElement, "knowledgeBaseVersion") ?? GetString(document.RootElement, "version");
            }
            catch { return null; }
        }

        private static IEnumerable<string> TokenizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            return query.Split(new[] { ' ', '\t', '\r', '\n', ',', '，', ';', '；', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => term.Trim())
                .Where(term => term.Length > 0);
        }

        private static IEnumerable<KnowledgeQueryMatch> QueryKnowledgePartition(KnowledgePartition partition, string[] terms)
        {
            if (!File.Exists(partition.path)) return Array.Empty<KnowledgeQueryMatch>();
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(partition.path)))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<KnowledgeQueryMatch>();
                    return document.RootElement.EnumerateArray()
                        .Select(element => element.GetRawText())
                        .Select(payload => new KnowledgeQueryMatch { type = partition.type, payload = payload, score = ScoreKnowledgeEntry(payload, terms) })
                        .Where(match => terms.Length == 0 || match.score > 0)
                        .ToArray();
                }
            }
            catch
            {
                return Array.Empty<KnowledgeQueryMatch>();
            }
        }

        private static int ScoreKnowledgeEntry(string payload, string[] terms)
        {
            if (terms.Length == 0) return 1;
            var score = 0;
            foreach (var term in terms)
            {
                if (payload.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score++;
            }
            return score;
        }

        private static object GetAssetRevision(JsonElement args)
        {
            var paths = GetStringArray(args, "assetPaths");
            return new { assets = paths.Select(path => DescribeAsset(path)).ToArray() };
        }

        private static object DescribeAsset(string path)
        {
            RequireReadPath(path);
            var exists = File.Exists(path) || Directory.Exists(path);
            return new { path, exists, kind = Directory.Exists(path) ? "directory" : "text", revision = exists && File.Exists(path) ? Revision(path) : "absent", contentHash = exists && File.Exists(path) ? Revision(path) : (string)null, writableByStructuredTool = IsGeneratedPath(path) || IsArtifactPath(path) };
        }

        private static object InspectShaderStructure(JsonElement args)
        {
            var path = RequireString(args, "assetPath");
            RequireReadPath(path);
            if (!File.Exists(path))
                return new { asset = new { path, exists = false, revision = "absent" }, parseDiagnostics = new[] { new { severity = "error", message = "Asset does not exist." } } };

            var revision = Revision(path);
            var expectedRevision = GetString(args, "expectedRevision");
            if (!string.IsNullOrEmpty(expectedRevision) && !string.Equals(expectedRevision, revision, StringComparison.Ordinal))
                throw new InvalidOperationException("revision_conflict: expected " + expectedRevision + ", observed " + revision);

            var parsed = UnityMcpShaderStructureAnalysis.Parse(File.ReadAllText(path));
            object comparison = null;
            var referencePath = GetString(args, "referenceAssetPath");
            if (!string.IsNullOrEmpty(referencePath))
            {
                RequireReadPath(referencePath);
                if (!File.Exists(referencePath)) throw new ArgumentException("referenceAssetPath does not exist: " + referencePath);
                var approvedDifferenceKeys = GetStringArray(args, "approvedDifferenceKeys");
                comparison = UnityMcpShaderStructureAnalysis.Compare(
                    UnityMcpShaderStructureAnalysis.Parse(File.ReadAllText(referencePath)),
                    parsed,
                    approvedDifferenceKeys);
            }

            return new
            {
                asset = new { path, exists = true, revision, shaderName = parsed.shaderName },
                structure = UnityMcpShaderStructureAnalysis.Serialize(parsed),
                comparison,
                semanticProof = "unknown: lexical comparison does not prove texture sampling, macro branches, workflow wiring, or SurfaceData connections."
            };
        }

        private static object WriteGeneratedTextAsset(JsonElement args)
        {
            var idempotencyKey = RequireString(args, "idempotencyKey");
            var asset = RequireProperty(args, "asset");
            var path = RequireString(asset, "path");
            var content = RequireString(asset, "contentUtf8");
            var requestHash = Hash(args.GetRawText());
            if (CompletedWrites.TryGetValue(idempotencyKey, out var completed))
            {
                if (completed.requestHash != requestHash) throw new InvalidOperationException("idempotency_conflict: key was already used with different input.");
                return completed.result;
            }
            var baseRevision = GetString(asset, "baseRevision") ?? "absent";
            RequireGeneratedPath(path);
            var exists = File.Exists(path);
            var current = exists ? Revision(path) : "absent";
            if (!string.Equals(current, baseRevision, StringComparison.Ordinal)) throw new InvalidOperationException("revision_conflict: expected " + baseRevision + ", observed " + current);
            RequireAuthorizedPlanForWrite(args, path, current);
            var policy = GetString(asset, "createPolicy") ?? "create_or_update";
            if (policy == "create_only" && exists) throw new InvalidOperationException("revision_conflict: asset already exists");
            if (policy == "update_only" && !exists) throw new InvalidOperationException("invalid_argument: asset does not exist");
            var directory = Path.GetDirectoryName(path) ?? GeneratedRoot;
            Directory.CreateDirectory(directory);
            var before = exists ? File.ReadAllText(path) : string.Empty;
            var transactionId = Guid.NewGuid().ToString("N");
            var transactionRoot = "Library/UnityMcp/transactions/" + transactionId + "/";
            Directory.CreateDirectory(transactionRoot);
            var temporaryPath = transactionRoot + Path.GetFileName(path) + ".tmp";
            var backupPath = path + ".unitymcp.bak";
            var transactionPath = transactionRoot + "transaction.json";
            File.WriteAllText(transactionPath, JsonSerializer.Serialize(new { transactionId, status = "prepared", path, baseRevision, requestHash, createdAtUtc = DateTime.UtcNow.ToString("o") }, JsonOptions));
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            try
            {
                if (exists) File.Replace(temporaryPath, path, backupPath);
                else File.Move(temporaryPath, path);
            }
            catch
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                throw;
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var newRevision = Revision(path);
            var diffPath = WriteRunArtifact(args, "diffs/" + SafeName(path) + "-" + newRevision + ".diff", "--- before\n" + before + "\n+++ after\n" + content);
            var result = new { asset = new { path, previousRevision = current, newRevision, contentHash = newRevision }, changedRanges = new[] { new { startLine = 1, endLine = content.Split('\n').Length } }, importRequired = true, transactionId, artifactDiff = new { path = diffPath, contentHash = Hash(File.ReadAllText(diffPath)) } };
            File.WriteAllText(transactionPath, JsonSerializer.Serialize(new { transactionId, status = "committed", path, baseRevision, newRevision, requestHash, completedAtUtc = DateTime.UtcNow.ToString("o") }, JsonOptions));
            CompletedWrites[idempotencyKey] = new IdempotentWriteRecord { requestHash = requestHash, result = result };
            return result;
        }
    }
}
#endif
