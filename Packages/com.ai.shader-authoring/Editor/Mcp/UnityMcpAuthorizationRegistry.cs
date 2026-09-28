#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Unity-owned authorization store. MCP requests may propose grants, but only local Editor UI may approve them.</summary>
    internal static class UnityMcpAuthorizationRegistry
    {
        internal const string WriteCapability = "WRITE_GENERATED_ASSET";
        internal const string BuildKnowledgeCapability = "BUILD_KNOWLEDGE_BASE";
        internal const string CompileCapability = "COMPILE_GENERATED_ASSET";
        internal const string ConfigureValidationCapability = "CONFIGURE_ISOLATED_VALIDATION";
        internal const string CaptureCapability = "CAPTURE_VALIDATION_EVIDENCE";
        internal const string CheckpointCapability = "CREATE_CHECKPOINT";
        internal const string RestoreCapability = "RESTORE_GENERATED_ASSET";
        private const string StorePath = "Library/UnityMcp/authorization-grants.json";
        private static readonly object Gate = new object();
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

        [Serializable]
        internal sealed class Grant
        {
            public string grantId;
            public string status;
            public string runId;
            public string codePlanId;
            public string[] allowedCapabilities;
            public string[] allowedFiles;
            public AssetRevision[] allowedAssetRevisions;
            public string[] allowedJobTypes;
            public int maxWrites;
            public int writesConsumed;
            public string proposedAtUtc;
            public string approvedAtUtc;
            public string expiresAtUtc;
        }

        [Serializable]
        internal sealed class AssetRevision { public string path; public string revision; }
        [Serializable] private sealed class Store { public List<Grant> grants = new List<Grant>(); }

        internal static Grant Propose(JsonElement args)
        {
            var context = RequireObject(args, "operationContext");
            var grantId = RequireString(context, "authorizationGrantId");
            var runId = RequireString(context, "runId");
            var codePlanId = RequireString(context, "codePlanId");
            var scope = RequireObject(args, "scope");
            var files = ReadStrings(scope, "allowedFiles").Select(NormalizeAssetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var capabilities = ReadStrings(scope, "allowedCapabilities").Distinct(StringComparer.Ordinal).ToArray();
            var knownCapabilities = new[] { WriteCapability, BuildKnowledgeCapability, CompileCapability, ConfigureValidationCapability, CaptureCapability, CheckpointCapability, RestoreCapability };
            if (capabilities.Length == 0 || capabilities.Any(capability => !knownCapabilities.Contains(capability))) throw new UnauthorizedAccessException("Proposal contains no capability or an unsupported capability.");
            if (files.Any(path => !IsAllowedScopePath(path))) throw new UnauthorizedAccessException("Authorization proposal contains a path outside generated assets, artifacts, or the fixed validation fixture.");
            if ((capabilities.Contains(WriteCapability) || capabilities.Contains(CompileCapability) || capabilities.Contains(ConfigureValidationCapability) || capabilities.Contains(CaptureCapability)) && files.Length == 0)
                throw new UnauthorizedAccessException("This capability requires an explicit file scope.");
            var revisions = ReadRevisions(scope);
            var jobTypes = ReadStrings(scope, "allowedJobTypes").Distinct(StringComparer.Ordinal).ToArray();
            var maxWrites = ReadInt(scope, "maxWrites", 1, 1, 100);
            var expiresAt = ReadString(scope, "expiresAtUtc") ?? DateTime.UtcNow.AddMinutes(30).ToString("o");
            if (!DateTime.TryParse(expiresAt, out var expiration) || expiration <= DateTime.UtcNow || expiration > DateTime.UtcNow.AddHours(24))
                throw new ArgumentException("expiresAtUtc must be within the next 24 hours.");

            lock (Gate)
            {
                var store = Load();
                var existing = store.grants.FirstOrDefault(item => item.grantId == grantId);
                if (existing != null) return Clone(existing);
                var grant = new Grant
                {
                    grantId = grantId, status = "proposed", runId = runId, codePlanId = codePlanId,
                    allowedCapabilities = capabilities, allowedFiles = files, allowedAssetRevisions = revisions,
                    allowedJobTypes = jobTypes,
                    maxWrites = maxWrites, writesConsumed = 0, proposedAtUtc = DateTime.UtcNow.ToString("o"), expiresAtUtc = expiration.ToUniversalTime().ToString("o")
                };
                store.grants.Add(grant);
                Save(store);
                return Clone(grant);
            }
        }

        internal static Grant[] Snapshot()
        {
            lock (Gate) return Load().grants.Select(Clone).ToArray();
        }

        internal static bool Approve(string grantId)
        {
            lock (Gate)
            {
                var store = Load();
                var grant = store.grants.FirstOrDefault(item => item.grantId == grantId && item.status == "proposed");
                if (grant == null || IsExpired(grant)) return false;
                grant.status = "approved";
                grant.approvedAtUtc = DateTime.UtcNow.ToString("o");
                Save(store);
                return true;
            }
        }

        internal static bool Revoke(string grantId)
        {
            lock (Gate)
            {
                var store = Load();
                var grant = store.grants.FirstOrDefault(item => item.grantId == grantId);
                if (grant == null) return false;
                grant.status = "revoked";
                Save(store);
                return true;
            }
        }

        internal static Grant AuthorizeAndConsume(JsonElement args, string path, string currentRevision)
        {
            var context = RequireObject(args, "operationContext");
            var grantId = RequireString(context, "authorizationGrantId");
            var runId = RequireString(context, "runId");
            var codePlanId = RequireString(context, "codePlanId");
            path = NormalizeAssetPath(path);
            lock (Gate)
            {
                var store = Load();
                var grant = store.grants.FirstOrDefault(item => item.grantId == grantId);
                if (grant == null || grant.status != "approved") throw new UnauthorizedAccessException("Authorization grant is not approved in Unity.");
                if (IsExpired(grant)) { grant.status = "expired"; Save(store); throw new UnauthorizedAccessException("Authorization grant has expired."); }
                if (grant.runId != runId || grant.codePlanId != codePlanId) throw new UnauthorizedAccessException("Authorization context does not match the approved grant.");
                if (!grant.allowedCapabilities.Contains(WriteCapability)) throw new UnauthorizedAccessException("Grant lacks WRITE_GENERATED_ASSET.");
                if (!grant.allowedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Target path is outside the approved grant.");
                var revision = grant.allowedAssetRevisions.FirstOrDefault(item => string.Equals(item.path, path, StringComparison.OrdinalIgnoreCase));
                if (revision == null || revision.revision != currentRevision) throw new InvalidOperationException("revision_conflict: approved asset revision no longer matches.");
                if (grant.writesConsumed >= grant.maxWrites) throw new UnauthorizedAccessException("Authorization grant write budget is exhausted.");
                grant.writesConsumed++;
                if (grant.writesConsumed >= grant.maxWrites) grant.status = "consumed";
                Save(store);
                return Clone(grant);
            }
        }

        internal static Grant AuthorizeJobAndConsume(JsonElement args, string jobType, string capability, IEnumerable<string> paths)
        {
            var context = RequireObject(args, "operationContext");
            var grantId = RequireString(context, "authorizationGrantId");
            var runId = RequireString(context, "runId");
            var codePlanId = RequireString(context, "codePlanId");
            var normalizedPaths = (paths ?? Array.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)).Select(NormalizeAssetPath).ToArray();
            lock (Gate)
            {
                var store = Load();
                var grant = store.grants.FirstOrDefault(item => item.grantId == grantId);
                if (grant == null || grant.status != "approved") throw new UnauthorizedAccessException("Authorization grant is not approved in Unity.");
                if (IsExpired(grant)) { grant.status = "expired"; Save(store); throw new UnauthorizedAccessException("Authorization grant has expired."); }
                if (grant.runId != runId || grant.codePlanId != codePlanId) throw new UnauthorizedAccessException("Authorization context does not match the approved grant.");
                if (!(grant.allowedCapabilities ?? Array.Empty<string>()).Contains(capability)) throw new UnauthorizedAccessException("Grant lacks capability " + capability + ".");
                if (!(grant.allowedJobTypes ?? Array.Empty<string>()).Contains(jobType)) throw new UnauthorizedAccessException("Job type is outside the approved grant.");
                foreach (var path in normalizedPaths)
                {
                    if (!(grant.allowedFiles ?? Array.Empty<string>()).Contains(path, StringComparer.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Job target path is outside the approved grant: " + path);
                    var revision = (grant.allowedAssetRevisions ?? Array.Empty<AssetRevision>()).FirstOrDefault(item => string.Equals(item.path, path, StringComparison.OrdinalIgnoreCase));
                    var current = File.Exists(path) ? Sha256(File.ReadAllBytes(path)) : "absent";
                    if (revision == null || revision.revision != current) throw new InvalidOperationException("revision_conflict: approved job asset revision no longer matches " + path);
                }
                if (grant.writesConsumed >= grant.maxWrites) throw new UnauthorizedAccessException("Authorization grant operation budget is exhausted.");
                grant.writesConsumed++;
                if (grant.writesConsumed >= grant.maxWrites) grant.status = "consumed";
                Save(store);
                return Clone(grant);
            }
        }

        private static bool IsExpired(Grant grant) => !DateTime.TryParse(grant.expiresAtUtc, out var value) || value.ToUniversalTime() <= DateTime.UtcNow;
        private static Store Load()
        {
            if (!File.Exists(StorePath)) return new Store();
            try { return JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath), Options) ?? new Store(); }
            catch { return new Store(); }
        }
        private static void Save(Store store)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
            var temporary = StorePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(store, Options), new UTF8Encoding(false));
            if (File.Exists(StorePath)) File.Replace(temporary, StorePath, StorePath + ".bak"); else File.Move(temporary, StorePath);
        }
        private static Grant Clone(Grant grant) => JsonSerializer.Deserialize<Grant>(JsonSerializer.Serialize(grant, Options), Options);
        private static string Sha256(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string NormalizeAssetPath(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/').Trim();
            if (Path.IsPathRooted(path) || path.Split('/').Any(part => part == ".." || part == ".")) throw new UnauthorizedAccessException("Invalid project-relative path.");
            return path;
        }
        private static bool IsAllowedScopePath(string path) => path.StartsWith("Assets/AIShader/Generated/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Artifacts/ShaderRuns/", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Packages/com.ai.shader-authoring/Tests/AI Shader Authoring.unity", StringComparison.OrdinalIgnoreCase);
        private static JsonElement RequireObject(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new ArgumentException("Missing object: " + name);
        private static string RequireString(JsonElement element, string name) => ReadString(element, name) ?? throw new ArgumentException("Missing string: " + name);
        private static string ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static string[] ReadStrings(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() : Array.Empty<string>();
        private static int ReadInt(JsonElement element, string name, int fallback, int min, int max) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? Math.Max(min, Math.Min(max, parsed)) : fallback;
        private static AssetRevision[] ReadRevisions(JsonElement scope)
        {
            if (!scope.TryGetProperty("allowedAssetRevisions", out var value) || value.ValueKind != JsonValueKind.Array) return Array.Empty<AssetRevision>();
            return value.EnumerateArray().Select(item => new AssetRevision { path = NormalizeAssetPath(RequireString(item, "path")), revision = RequireString(item, "revision") }).ToArray();
        }
    }
}
#endif
