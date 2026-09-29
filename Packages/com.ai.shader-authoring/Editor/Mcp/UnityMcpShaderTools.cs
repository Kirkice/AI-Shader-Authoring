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
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Rendering;
#endif
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    /// <summary>
    /// Structured, allow-listed Shader authoring operations. All file mutations are limited to
    /// generated assets and artifacts; arbitrary editor commands must not be used by this host.
    /// </summary>
    internal static class UnityMcpShaderTools
    {
        private const string GeneratedRoot = "Assets/AIShader/Generated/";
        private const string KnowledgeRoot = "Artifacts/ShaderKnowledgeBase/";
        private const string RunsRoot = "Artifacts/ShaderRuns/";
        // Shader 验收必须使用受版本控制的固定测试夹具，禁止由调用方以层级路径选择任意场景对象。
        private const string FixedValidationScenePath = "Packages/com.ai.shader-authoring/Tests/AI Shader Authoring.unity";
        // Trunk 固定场景中的真实对象名为 Sphere；生成材质必须作为真实资产绑定到该夹具目标，不能通过复制夹具材质后只替换 Shader 伪造验收。
        private const string FixedValidationRendererName = "Sphere";
        private static readonly Color ValidationBackgroundColor = new Color(8f / 255f, 11f / 255f, 16f / 255f, 1f);
        private static readonly Dictionary<string, JobRecord> Jobs = new Dictionary<string, JobRecord>();
        private static readonly Dictionary<string, string> JobByIdempotencyKey = new Dictionary<string, string>();
        private const string JobsRoot = "Library/UnityMcp/jobs/";
        private const int MaxRetainedJobs = 200;
        private const int MaxActiveJobs = 8;
        private static readonly Dictionary<string, ValidationSessionRecord> ValidationSessions = new Dictionary<string, ValidationSessionRecord>();
        private static readonly Dictionary<string, IdempotentWriteRecord> CompletedWrites = new Dictionary<string, IdempotentWriteRecord>();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true, WriteIndented = true };

        internal static object Handle(string toolName, JsonElement args)
        {
            switch (toolName)
            {
                case "get_shader_knowledge_base_status": return GetKnowledgeBaseStatus(args);
                case "query_shader_knowledge_base": return QueryKnowledgeBase(args);
                case "get_asset_revision": return GetAssetRevision(args);
                case "inspect_shader_structure": return InspectShaderStructure(args);
                case "propose_authorization_grant": return UnityMcpAuthorizationRegistry.Propose(args);
                case "write_generated_text_asset": return WriteGeneratedTextAsset(args);
                case "run_unity_job": return RunJob(args);
                case "get_unity_job": return GetJob(args);
                case "cancel_unity_job": return CancelJob(args);
                case "build_shader_knowledge_base": return StartKnowledgeBaseBuild(args);
                case "refresh_and_compile_assets": return StartCompile(args);
                case "export_compiled_gles_variants": return StartCompiledGlesExport(args);
                case "analyze_shader_performance": return StartPerformanceAnalysis(args);
                case "ensure_validation_scene": return StartValidationScene(args);
                case "capture_validation": return StartCapture(args);
                case "create_shader_checkpoint": return StartShaderCheckpoint(args);
                case "get_console_diagnostics": return GetConsoleDiagnostics(args);
                default: throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unsupported structured Unity MCP tool.");
            }
        }

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

        private static object RunJob(JsonElement args)
        {
            var type = RequireString(args, "jobType");
            switch (type)
            {
                case "build_shader_knowledge_base":
                case "refresh_and_compile_assets":
                case "export_compiled_gles_variants":
                case "analyze_shader_performance":
                case "ensure_validation_scene":
                case "capture_validation":
                case "create_shader_checkpoint":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported structured Unity job type.");
            }
            var jobArgs = args.TryGetProperty("args", out var value) ? value : default;
            return CreateJob(type, jobArgs, args);
        }

        private static object StartKnowledgeBaseBuild(JsonElement args) => CreateJob("build_shader_knowledge_base", args, args);
        private static object StartCompile(JsonElement args) => CreateJob("refresh_and_compile_assets", args, args);
        private static object StartCompiledGlesExport(JsonElement args) => CreateJob("export_compiled_gles_variants", args, args);
        private static object StartPerformanceAnalysis(JsonElement args) => CreateJob("analyze_shader_performance", args, args);
        private static object StartValidationScene(JsonElement args) => CreateJob("ensure_validation_scene", args, args);
        private static object StartCapture(JsonElement args) => CreateJob("capture_validation", args, args);
        private static object StartShaderCheckpoint(JsonElement args) => CreateJob("create_shader_checkpoint", args, args);

        /// <summary>
        /// Reads Unity console diagnostics, optionally scoped to specific assets, and augments Shader
        /// assets with authoritative compiler error state. This is the Console-error gate for Step 7.
        /// </summary>
        private static object GetConsoleDiagnostics(JsonElement args)
        {
            var paths = GetStringArray(args, "assetPaths");
            var includeWarnings = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("includeWarnings", out var includeWarningsElement) && includeWarningsElement.ValueKind == JsonValueKind.True;
            // The cursor only suppresses *stale console noise*, so an already-fixed Shader does not stay
            // "dirty" forever. Authoritative live Shader compile state is never cursored: a broken Shader
            // whose error was logged before the cursor must still fail the gate.
            var since = GetString(args, "since");
            var logs = ReadConsoleLogEntries()
                .Where(log => IsAfterCursor(ReadString(log, "timestamp"), since))
                .ToArray();
            var diagnostics = new List<DiagnosticEntry>();
            var scannedShaders = new List<object>();
            var shaderErrorCount = 0;
            if (paths.Length == 0)
            {
                diagnostics.AddRange(ExtractDiagnostics(logs, null, includeWarnings));
            }
            else
            {
                foreach (var path in paths)
                {
                    RequireReadPath(path);
                    diagnostics.AddRange(ExtractDiagnostics(logs, path, includeWarnings));
                    if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                    var scan = DescribeShaderCompilation(path, includeWarnings);
                    scannedShaders.Add(scan.payload);
                    if (scan.hasErrors) shaderErrorCount++;
                    // Live compiler findings are folded into errorCount so the gate can never report
                    // "clean" while Unity itself flags the Shader as broken.
                    diagnostics.AddRange(scan.diagnostics);
                }
            }
            var errorCount = diagnostics.Count(item => item.severity == "error");
            var warningCount = diagnostics.Count(item => item.severity == "warning");
            return new
            {
                status = errorCount > 0 ? "failed" : "clean",
                errorCount,
                warningCount,
                shaderErrorCount,
                since = since ?? "epoch",
                sinceScope = "console-noise-only",
                scannedAssetPaths = paths,
                scannedShaders = scannedShaders.ToArray(),
                diagnostics = diagnostics.ToArray(),
                suggestions = errorCount > 0
                    ? new[] { "Fix the reported Shader or asset errors, then call refresh_and_compile_assets and re-read diagnostics before any visual validation." }
                    : Array.Empty<string>()
            };
        }

        private static bool IsAfterCursor(string timestamp, string since)
        {
            if (string.IsNullOrEmpty(since)) return true;
            if (string.IsNullOrEmpty(timestamp)) return true;
            return DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                && DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cursor)
                && parsed > cursor;
        }

        /// <summary>
        /// Collects Unity's authoritative shader-compiler findings for every supported platform.
        /// ShaderUtil.ShaderHasError is the definitive live flag; ShaderUtil.GetShaderMessages adds
        /// compiler detail and is merged into the returned diagnostics so it drives the gate.
        /// </summary>
        private static ShaderScanResult DescribeShaderCompilation(string assetPath, bool includeWarnings)
        {
            var result = new ShaderScanResult();
            var observedAtUtc = DateTime.UtcNow.ToString("o");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                result.hasErrors = true;
                result.payload = new { assetPath, shaderName = (string)null, hasErrors = true, hasWarnings = false, messageCount = 0, platforms = Array.Empty<object>() };
                result.diagnostics.Add(new DiagnosticEntry
                {
                    severity = "error",
                    source = "shader-compiler",
                    message = "The Shader asset could not be loaded; it may be missing or failed to import.",
                    assetPath = assetPath,
                    timestamp = observedAtUtc
                });
                return result;
            }
            var platformResults = new List<object>();
            var messageCount = 0;
#if UNITY_6000_0_OR_NEWER
            // Unity 6 exposes a platform-specific overload, so preserve per-platform data.
            foreach (var platform in Enum.GetValues(typeof(ShaderCompilerPlatform)).Cast<ShaderCompilerPlatform>())
            {
                ShaderMessage[] messages;
                try
                {
                    messages = ShaderUtil.GetShaderMessages(shader, platform);
                }
                catch
                {
                    continue;
                }
                if (messages == null || messages.Length == 0) continue;
                AddShaderMessages(messages, platform.ToString(), assetPath, observedAtUtc, includeWarnings, result, platformResults, ref messageCount);
            }
#else
            // Unity 2019.4 provides only the single-argument overload. Each message retains its platform.
            ShaderMessage[] messages;
            try
            {
                messages = ShaderUtil.GetShaderMessages(shader);
            }
            catch
            {
                messages = Array.Empty<ShaderMessage>();
            }
            if (messages != null && messages.Length > 0)
                AddShaderMessages(messages, "all", assetPath, observedAtUtc, includeWarnings, result, platformResults, ref messageCount);
#endif
            // The per-platform message list can be empty even for a broken Shader (for example when the
            // platform has not been compiled yet). ShaderHasError is the definitive live signal.
            if (!result.hasErrors && ShaderHasError(shader))
            {
                result.hasErrors = true;
                result.diagnostics.Add(new DiagnosticEntry
                {
                    severity = "error",
                    source = "shader-compiler",
                    message = "ShaderUtil reports this Shader as having compile errors, but no per-platform message was exposed. Inspect the failing pass in the Shader Inspector.",
                    assetPath = assetPath,
                    timestamp = observedAtUtc
                });
            }
            result.payload = new { assetPath, shaderName = shader.name, hasErrors = result.hasErrors, hasWarnings = result.hasWarnings, messageCount, platforms = platformResults.ToArray() };
            return result;
        }

        /// <summary>Converts compiler messages into MCP diagnostics while preserving their platform metadata.</summary>
        private static void AddShaderMessages(ShaderMessage[] messages, string platform, string assetPath, string observedAtUtc, bool includeWarnings, ShaderScanResult result, List<object> platformResults, ref int messageCount)
        {
            var serialized = messages.Select(message => new
            {
                message.severity,
                message.message,
                message.platform,
                message.line,
                file = string.IsNullOrEmpty(message.file) ? ResolveShaderSourceFile(assetPath, message.message) : message.file
            }).ToArray();
            foreach (var message in messages)
            {
                messageCount++;
                string severity = null;
#if UNITY_6000_0_OR_NEWER
                if (message.severity == ShaderCompilerMessageSeverity.Error)
                {
                    result.hasErrors = true;
                    severity = "error";
                }
                else if (message.severity == ShaderCompilerMessageSeverity.Warning)
                {
                    result.hasWarnings = true;
                    severity = "warning";
                }
#else
                // Unity 2019.4 does not expose ShaderCompilerMessageSeverity. The legacy
                // ShaderMessage.severity value still reports its symbolic severity name.
                var legacySeverity = message.severity.ToString();
                if (string.Equals(legacySeverity, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    result.hasErrors = true;
                    severity = "error";
                }
                else if (string.Equals(legacySeverity, "Warning", StringComparison.OrdinalIgnoreCase))
                {
                    result.hasWarnings = true;
                    severity = "warning";
                }
#endif
                if (severity == null || (severity == "warning" && !includeWarnings)) continue;
                result.diagnostics.Add(new DiagnosticEntry { severity = severity, source = "shader-compiler", message = message.message, assetPath = assetPath, timestamp = observedAtUtc });
            }
            platformResults.Add(new { platform = platform, messages = serialized });
        }

        private static bool ShaderHasError(Shader shader)
        {
#if UNITY_6000_0_OR_NEWER
            // Retain the Unity 6-era direct API call for Unity 6 Editors.
            try
            {
                return ShaderUtil.ShaderHasError(shader);
            }
            catch
            {
                return false;
            }
#else
            // Keep compilation valid on 2019.4 even if this internal Editor API is absent there.
            try
            {
                var method = typeof(ShaderUtil).GetMethod("ShaderHasError", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(Shader) }, null);
                return method != null && (bool)method.Invoke(null, new object[] { shader });
            }
            catch
            {
                return false;
            }
#endif
        }

        private static string ResolveShaderSourceFile(string assetPath, string message)
        {
            if (string.IsNullOrEmpty(message)) return assetPath;
            var match = System.Text.RegularExpressions.Regex.Match(message, @"(Assets/[^\s\(]+\.(?:shader|hlsl|cginc))");
            return match.Success ? match.Value : assetPath;
        }

        private static JsonElement[] ReadConsoleLogEntries()
        {
            var snapshotJson = UnityMcpConnection.GetRecentLogSnapshotJson();
            if (string.IsNullOrEmpty(snapshotJson)) return Array.Empty<JsonElement>();
            try
            {
#if UNITY_6000_0_OR_NEWER
                using var document = JsonDocument.Parse(snapshotJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<JsonElement>();
                return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
#else
                using (var document = JsonDocument.Parse(snapshotJson))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<JsonElement>();
                    return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
                }
#endif
            }
            catch
            {
                return Array.Empty<JsonElement>();
            }
        }

        private static List<DiagnosticEntry> ExtractDiagnostics(JsonElement[] logs, string assetPath, bool includeWarnings)
        {
            var results = new List<DiagnosticEntry>();
            var normalized = string.IsNullOrEmpty(assetPath) ? null : assetPath.Replace('\\', '/');
            var shaderName = normalized != null && normalized.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ? AssetDatabase.LoadAssetAtPath<Shader>(assetPath)?.name : null;
            foreach (var log in logs)
            {
                var severity = MapSeverity(ReadString(log, "logType"));
                if (severity == null) continue;
                if (severity == "warning" && !includeWarnings) continue;
                var message = ReadString(log, "message");
                if (string.IsNullOrEmpty(message)) continue;
                if (normalized != null && !IsRelatedToAsset(message, normalized, shaderName)) continue;
                results.Add(new DiagnosticEntry
                {
                    severity = severity,
                    source = "console",
                    message = message,
                    stackTrace = ReadString(log, "stackTrace"),
                    timestamp = ReadString(log, "timestamp"),
                    assetPath = assetPath
                });
            }
            if (normalized != null && normalized.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAssetAtPath<Shader>(assetPath) == null)
            {
                results.Add(new DiagnosticEntry { severity = "error", source = "asset", message = "The Shader asset could not be loaded; it may be missing or failed to import.", assetPath = assetPath });
            }
            return results;
        }

        private static string MapSeverity(string logType)
        {
            if (string.IsNullOrEmpty(logType)) return null;
            if (string.Equals(logType, "Error", StringComparison.OrdinalIgnoreCase)) return "error";
            if (string.Equals(logType, "Exception", StringComparison.OrdinalIgnoreCase)) return "error";
            if (string.Equals(logType, "Assert", StringComparison.OrdinalIgnoreCase)) return "error";
            if (string.Equals(logType, "Warning", StringComparison.OrdinalIgnoreCase)) return "warning";
            return null;
        }

        private static bool IsRelatedToAsset(string message, string normalizedAssetPath, string shaderName)
        {
            var lower = message.ToLowerInvariant();
            if (lower.Contains(normalizedAssetPath.ToLowerInvariant())) return true;
            var fileName = Path.GetFileName(normalizedAssetPath);
            if (!string.IsNullOrEmpty(fileName) && lower.Contains(fileName.ToLowerInvariant())) return true;
            if (string.IsNullOrEmpty(shaderName)) return false;
            // Unity reports shader compile errors as: Shader error in 'AIShader/Generated/Xyz': ...
            var lowerShaderName = shaderName.ToLowerInvariant();
            if (lower.Contains("'" + lowerShaderName + "'")) return true;
            // Also match the last path segment of the shader name for tolerance.
            var lastSegment = lowerShaderName.Split('/').LastOrDefault();
            return !string.IsNullOrEmpty(lastSegment) && lower.Contains("'" + lastSegment + "'");
        }

        private static string ReadString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static object CreateJob(string type, JsonElement jobArgs, JsonElement context)
        {
            RecoverPersistedJobs();
            var idempotencyKey = GetString(context, "idempotencyKey");
            var inputHash = Hash(type + "\n" + (jobArgs.ValueKind == JsonValueKind.Undefined ? "{}" : jobArgs.GetRawText()));
            if (!string.IsNullOrEmpty(idempotencyKey) && JobByIdempotencyKey.TryGetValue(idempotencyKey, out var existingId) && Jobs.TryGetValue(existingId, out var existing))
            {
                if (!string.Equals(existing.inputHash, inputHash, StringComparison.Ordinal)) throw new InvalidOperationException("idempotency_conflict: key was already used with different job input.");
                return new { jobId = existing.jobId, status = existing.status, acceptedJobType = existing.jobType, acceptedAtUtc = existing.createdAtUtc, completedAtUtc = existing.completedAtUtc, reused = true };
            }
            if (Jobs.Values.Count(job => job.status == "queued" || job.status == "running" || job.status == "cancelling") >= MaxActiveJobs) throw new InvalidOperationException("job_queue_full: active job limit reached.");
            AuthorizeJob(type, jobArgs, context);
            var id = Guid.NewGuid().ToString("N");
            var record = new JobRecord { jobId = id, jobType = type, status = "queued", createdAtUtc = DateTime.UtcNow.ToString("o"), argsJson = jobArgs.ValueKind == JsonValueKind.Undefined ? "{}" : jobArgs.GetRawText(), contextJson = context.ValueKind == JsonValueKind.Undefined ? "{}" : context.GetRawText(), idempotencyKey = idempotencyKey, inputHash = inputHash, cancellation = new CancellationTokenSource() };
            Jobs[id] = record;
            if (!string.IsNullOrEmpty(idempotencyKey)) JobByIdempotencyKey[idempotencyKey] = id;
            PersistJob(record);
            TrimRetainedJobs();
            EditorApplication.delayCall += () => ExecuteJob(record);
            return new { jobId = id, status = record.status, acceptedJobType = type, acceptedAtUtc = record.createdAtUtc, completedAtUtc = record.completedAtUtc, logCursor = record.createdAtUtc };
        }

        private static void AuthorizeJob(string type, JsonElement jobArgs, JsonElement context)
        {
            string capability;
            var paths = new List<string>();
            switch (type)
            {
                case "build_shader_knowledge_base": capability = UnityMcpAuthorizationRegistry.BuildKnowledgeCapability; break;
                case "refresh_and_compile_assets":
                    capability = UnityMcpAuthorizationRegistry.CompileCapability;
                    paths.AddRange(GetStringArray(jobArgs, "assetPaths"));
                    break;
                case "export_compiled_gles_variants":
                case "analyze_shader_performance":
                    capability = UnityMcpAuthorizationRegistry.CompileCapability;
                    var shaderPath = GetString(jobArgs, "shaderPath");
                    if (!string.IsNullOrEmpty(shaderPath)) paths.Add(shaderPath);
                    break;
                case "ensure_validation_scene":
                    capability = UnityMcpAuthorizationRegistry.ConfigureValidationCapability;
                    if (jobArgs.TryGetProperty("target", out var target))
                    {
                        paths.Add(GetString(target, "shaderPath"));
                        paths.Add(GetString(target, "materialAssetPath"));
                    }
                    paths.Add(FixedValidationScenePath);
                    break;
                case "capture_validation": capability = UnityMcpAuthorizationRegistry.CaptureCapability; break;
                case "create_shader_checkpoint": capability = UnityMcpAuthorizationRegistry.CheckpointCapability; break;
                default: throw new UnauthorizedAccessException("No authorization capability is mapped for job type " + type);
            }
            UnityMcpAuthorizationRegistry.AuthorizeJobAndConsume(context, type, capability, paths.Where(path => !string.IsNullOrEmpty(path)));
        }

        private static Task ExecuteJob(JobRecord record)
        {
            if (record.status == "cancelled") return Task.CompletedTask;
            record.status = "running";
            PersistJob(record);
            try
            {
                using (var args = JsonDocument.Parse(record.argsJson))
                {
                    object result;
                    switch (record.jobType)
                    {
                        case "build_shader_knowledge_base": result = BuildKnowledgeBase(args.RootElement, record); break;
                        case "refresh_and_compile_assets": result = RunCompile(args.RootElement, record); break;
                        case "export_compiled_gles_variants": result = CompiledGlesVariantExporter.Export(args.RootElement, record.cancellation.Token); break;
                        case "analyze_shader_performance": result = ShaderPerformanceAnalyzer.Analyze(args.RootElement, record.cancellation.Token); break;
                        case "ensure_validation_scene": result = EnsureValidationScene(args.RootElement, record); break;
                        case "capture_validation": result = CaptureValidation(args.RootElement, record); break;
                        case "create_shader_checkpoint": result = CreateShaderCheckpoint(args.RootElement, record); break;
                        default: throw new InvalidOperationException("Unsupported job type: " + record.jobType);
                    }
                    ThrowIfJobCancellationRequested(record);
                    record.resultJson = JsonSerializer.Serialize(result, JsonOptions);
                    record.status = "succeeded";
                }
            }
            catch (OperationCanceledException)
            {
                record.status = "cancelled";
            }
            catch (Exception exception)
            {
                record.status = "failed";
                record.error = exception.Message;
                Debug.LogError("[Unity MCP] Structured job failed: " + record.jobType + "\n" + exception);
            }
            finally { record.completedAtUtc = DateTime.UtcNow.ToString("o"); PersistJob(record); }
            return Task.CompletedTask;
        }

        private static object GetJob(JsonElement args)
        {
            var id = RequireString(args, "jobId");
            RecoverPersistedJobs();
            if (!Jobs.TryGetValue(id, out var record)) throw new ArgumentException("Unknown jobId: " + id);
            var progress = record.status == "succeeded" ? 1f : record.status == "running" || record.status == "cancelling" ? 0.5f : 0f;
            return new { record.jobId, status = record.status, phase = record.jobType, progress, record.createdAtUtc, record.completedAtUtc, result = ParseStoredJson(record.resultJson), error = record.error, artifacts = record.artifacts.ToArray() };
        }

        private static object CancelJob(JsonElement args)
        {
            var id = RequireString(args, "jobId");
            RecoverPersistedJobs();
            if (!Jobs.TryGetValue(id, out var record)) throw new ArgumentException("Unknown jobId: " + id);
            var previous = record.status;
            var cancellationAccepted = false;
            if (record.status == "queued")
            {
                record.cancellation.Cancel();
                record.status = "cancelled";
                record.completedAtUtc = DateTime.UtcNow.ToString("o");
                cancellationAccepted = true;
            }
            else if (record.status == "running" && IsCooperativelyCancellable(record.jobType))
            {
                record.cancellation.Cancel();
                record.status = "cancelling";
                cancellationAccepted = true;
            }
            PersistJob(record);
            return new
            {
                jobId = id,
                previousStatus = previous,
                status = record.status,
                cancellationAccepted,
                preservedArtifacts = record.artifacts.ToArray()
            };
        }

        private static void ThrowIfJobCancellationRequested(JobRecord record)
        {
            if (record != null) record.cancellation.Token.ThrowIfCancellationRequested();
        }

        private static bool IsCooperativelyCancellable(string jobType)
        {
            return string.Equals(jobType, "build_shader_knowledge_base", StringComparison.Ordinal)
                || string.Equals(jobType, "refresh_and_compile_assets", StringComparison.Ordinal)
                || string.Equals(jobType, "export_compiled_gles_variants", StringComparison.Ordinal)
                || string.Equals(jobType, "analyze_shader_performance", StringComparison.Ordinal)
                || string.Equals(jobType, "capture_validation", StringComparison.Ordinal);
        }

        private static void PersistJob(JobRecord record)
        {
            Directory.CreateDirectory(JobsRoot);
            UnityMcpKnowledgeBaseIntegrity.AtomicWrite(JobsRoot + record.jobId + ".json", JsonSerializer.Serialize(record, JsonOptions));
        }

        private static void RecoverPersistedJobs()
        {
            if (!Directory.Exists(JobsRoot)) return;
            foreach (var path in Directory.GetFiles(JobsRoot, "*.json"))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(path), JsonOptions);
                    if (record == null || string.IsNullOrEmpty(record.jobId) || Jobs.ContainsKey(record.jobId)) continue;
                    record.cancellation = new CancellationTokenSource();
                    if (record.status == "queued" || record.status == "running" || record.status == "cancelling")
                    {
                        record.status = "interrupted";
                        record.completedAtUtc = DateTime.UtcNow.ToString("o");
                        record.error = "Unity domain reload or process restart interrupted the job; non-idempotent jobs are not replayed automatically.";
                        PersistJob(record);
                    }
                    Jobs[record.jobId] = record;
                    if (!string.IsNullOrEmpty(record.idempotencyKey)) JobByIdempotencyKey[record.idempotencyKey] = record.jobId;
                }
                catch { }
            }
        }

        private static void TrimRetainedJobs()
        {
            if (!Directory.Exists(JobsRoot)) return;
            var files = new DirectoryInfo(JobsRoot).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(MaxRetainedJobs).ToArray();
            foreach (var file in files) try { file.Delete(); } catch { }
        }

        private static object BuildKnowledgeBase(JsonElement args, JobRecord record)
        {
            var mode = GetString(args, "mode") ?? "full";
            var pipelineAssetPath = GraphicsSettings.currentRenderPipeline == null ? "builtin" : AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline);
            var packageLockPath = "Packages/packages-lock.json";
            var packageLockFingerprint = File.Exists(packageLockPath) ? Revision(packageLockPath) : "no-package-lock";
            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Hash(Application.unityVersion + pipelineAssetPath + packageLockFingerprint).Substring(0, 8);
            var root = KnowledgeRoot + "versions/" + version + "/";
            Directory.CreateDirectory(root);

            // The corpus must cover package-provided shaders (URP/HDRP/custom SRP) as well as project assets.
            var shaderPaths = AssetDatabase.FindAssets("t:Shader")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)))
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var corpus = shaderPaths.Select(path => new
            {
                path,
                sourceRevision = RevisionOfAsset(path),
                shaderName = AssetDatabase.LoadAssetAtPath<Shader>(path)?.name,
                origin = path.StartsWith("Packages/", StringComparison.Ordinal) ? "package" : "project"
            }).ToArray();

            var environment = new { unityVersion = Application.unityVersion, renderPipeline = GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name, colorSpace = PlayerSettings.colorSpace.ToString(), graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), packageLockFingerprint };
            File.WriteAllText(root + "environment.json", JsonSerializer.Serialize(environment, JsonOptions));
            File.WriteAllText(root + "shader-corpus.json", JsonSerializer.Serialize(corpus, JsonOptions));

            var libraryIndex = BuildLibraryIndex();
            File.WriteAllText(root + "library-index.json", JsonSerializer.Serialize(libraryIndex, JsonOptions));

            var libraryDeclarations = ExtractLibraryDeclarations();
            var functionCards = BuildFunctionCards(libraryDeclarations);
            File.WriteAllText(root + "function-cards.json", JsonSerializer.Serialize(functionCards, JsonOptions));

            var capabilityCatalog = BuildCapabilityCatalog(libraryDeclarations);
            File.WriteAllText(root + "capability-catalog.json", JsonSerializer.Serialize(capabilityCatalog, JsonOptions));

            // Project-local conventions stay a separate partition so library facts are never mistaken for house style.
            File.WriteAllText(root + "project-conventions.json", "[]");

            var retrievalIndex = functionCards.Select(card => new { term = card.function, partition = "library", target = card.include })
                .Concat(capabilityCatalog.Select(item => new { term = item.capability, partition = "capabilities", target = item.include }))
                .ToArray();
            File.WriteAllText(root + "retrieval-index.json", JsonSerializer.Serialize(retrievalIndex, JsonOptions));

            var supportedCapabilityCount = capabilityCatalog.Count(item => item.status == "supported");
            var integrity = UnityMcpKnowledgeBaseIntegrity.Capture();
            var manifest = new { schemaVersion = UnityMcpKnowledgeBaseIntegrity.SchemaVersion, freshnessStatus = "fresh", knowledgeBaseVersion = version, manifestPath = root + "manifest.json", generatedAtUtc = DateTime.UtcNow.ToString("o"), environmentFingerprint = integrity.environmentFingerprint, projectAssetFingerprint = integrity.projectAssetFingerprint, sourceFingerprint = integrity.sourceFingerprint, sourceRevisions = integrity.sources, environment, shaderCount = shaderPaths.Length, libraryIncludeCount = libraryIndex.Length, functionCardCount = functionCards.Length, capabilityCount = supportedCapabilityCount };
            UnityMcpKnowledgeBaseIntegrity.AtomicWrite(root + "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
            UnityMcpKnowledgeBaseIntegrity.AtomicWrite(KnowledgeRoot + "current.json", JsonSerializer.Serialize(manifest, JsonOptions));
            record.artifacts.Add(root + "manifest.json");
            return new
            {
                status = "fresh",
                knowledgeBaseVersion = version,
                buildMode = mode,
                manifestPath = root + "manifest.json",
                refreshedPartitions = new[] { "environment", "shader_corpus", "library", "conventions", "capabilities", "retrieval" },
                coverageReport = new
                {
                    shaderCount = shaderPaths.Length,
                    projectShaderCount = corpus.Count(item => item.origin == "project"),
                    packageShaderCount = corpus.Count(item => item.origin == "package"),
                    libraryIncludeCount = libraryIndex.Length,
                    functionCardCount = functionCards.Length,
                    capabilityCount = supportedCapabilityCount
                },
                unresolvedItems = Array.Empty<string>(),
                recommendedNextAction = "Knowledge base is ready."
            };
        }

        /// <summary>Universal Render Pipeline Shader Library root; its includes are the project's real PBR interface surface.</summary>
        private const string UniversalLibraryRoot = "Packages/com.unity.render-pipelines.universal/ShaderLibrary/";

        /// <summary>
        /// Every package Shader Library root that participates in authoring. URP provides the material interfaces,
        /// while the core package provides the shared transform and lighting helpers that URP re-exports.
        /// </summary>
        private static readonly string[] LibraryRoots = new[]
        {
            "Packages/com.unity.render-pipelines.core/ShaderLibrary/",
            "Packages/com.unity.render-pipelines.universal/ShaderLibrary/"
        };

        /// <summary>Derives the owning package name from a logical "Packages/<name>/..." include path.</summary>
        private static string PackageNameFromLogicalPath(string logicalPath)
        {
            if (string.IsNullOrEmpty(logicalPath) || !logicalPath.StartsWith("Packages/", StringComparison.Ordinal)) return "project";
            var remainder = logicalPath.Substring("Packages/".Length);
            var separator = remainder.IndexOf('/');
            return separator < 0 ? remainder : remainder.Substring(0, separator);
        }

        /// <summary>
        /// Maps a Unity asset path to a physical file path. Registry packages live under Library/PackageCache, so
        /// their "Packages/..." asset paths are not readable through System.IO without this resolution.
        /// </summary>
        private static string ResolvePhysicalPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Packages/", StringComparison.Ordinal)) return normalized;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(normalized);
            if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;
            var resolved = info.resolvedPath.Replace('\\', '/');
            var prefix = "Packages/" + info.name;
            if (string.Equals(normalized, prefix, StringComparison.Ordinal)) return resolved;
            return normalized.StartsWith(prefix + "/", StringComparison.Ordinal) ? resolved + normalized.Substring(prefix.Length) : resolved;
        }

        private static string RevisionOfAsset(string assetPath)
        {
            var physical = ResolvePhysicalPath(assetPath);
            return !string.IsNullOrEmpty(physical) && File.Exists(physical) ? Revision(physical) : "absent";
        }

        /// <summary>Indexes every HLSL/CGINC include the package Shader Libraries expose, with revision evidence.</summary>
        private static LibraryIncludeEntry[] BuildLibraryIndex()
        {
            var entries = new List<LibraryIncludeEntry>();
            foreach (var source in EnumerateLibrarySourceFiles())
            {
                entries.Add(new LibraryIncludeEntry
                {
                    path = source.logicalPath,
                    sourceRevision = Revision(source.physicalPath),
                    kind = Path.GetExtension(source.physicalPath).TrimStart('.').ToLowerInvariant(),
                    package = PackageNameFromLogicalPath(source.logicalPath)
                });
            }
            return entries.ToArray();
        }

        /// <summary>URP Shader Library function tokens indexed by the knowledge base. Each one is resolved against real source.</summary>
        private static readonly string[] FunctionTokens = new[]
        {
            "GetVertexPositionInputs", "GetVertexNormalInputs", "TransformObjectToWorld", "TransformObjectToWorldNormal", "TransformObjectToWorldDir",
            "TransformWorldToObjectDir", "TransformWorldToView", "TransformWorldToHClip", "TransformObjectToHClip", "GetWorldSpaceViewDir",
            "GetWorldSpaceNormalizeViewDir", "SafeNormalize", "SampleSH", "SampleSHVertex", "SampleSHPixel", "SampleSH9", "ComputeFogFactor",
            "GetCameraPositionWS", "GetScaledScreenParams", "InitializeInputData", "GetMainLight", "GetAdditionalLightsCount", "GetAdditionalLight",
            "GetAdditionalLights", "LightingLambert", "LightingSpecular", "LightingPhysicallyBased", "GlossyEnvironmentReflection", "InitializeBRDFData",
            "DirectBRDF", "DirectBRDFSpecular", "SpecularStrength", "ReflectivitySpecular", "OneMinusReflectivityMetallic", "MinimalCookTorranceNoF0",
            "SampleAlbedoAlpha", "AlphaDiscard", "SampleMetallicSpecGloss", "SampleNormal", "SampleEmission", "GetMainLightShadowCoord",
            "MainLightRealtimeShadow", "GetMainLightShadowParams"
        };

        private sealed class LibrarySourceFile
        {
            public string logicalPath;
            public string physicalPath;
            public string fileName;
        }

        private sealed class DeclarationLocation
        {
            public string logicalPath;
            public string fileName;
            public int startLine;
            public int endLine;
            public string signature;
            public string sourceRevision;
        }

        /// <summary>
        /// Enumerates every package Shader Library source once, in a stable order, translating registry ("Packages/...")
        /// asset paths to their physical PackageCache locations so their contents are actually readable.
        /// </summary>
        private static LibrarySourceFile[] EnumerateLibrarySourceFiles()
        {
            var results = new List<LibrarySourceFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in LibraryRoots)
            {
                var logicalRoot = root.TrimEnd('/');
                // Resolve the folder itself: probing for a sentinel header is unsafe because each package
                // exposes a different set (core has no Core.hlsl), which silently skipped whole roots.
                var physicalRoot = ResolvePhysicalPath(logicalRoot);
                if (string.IsNullOrEmpty(physicalRoot) || !Directory.Exists(physicalRoot)) continue;
                var files = Directory.GetFiles(physicalRoot, "*.*", SearchOption.AllDirectories)
                    .Where(file => file.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    var normalized = file.Replace('\\', '/');
                    if (!normalized.StartsWith(physicalRoot, StringComparison.Ordinal)) continue;
                    var logicalPath = logicalRoot + normalized.Substring(physicalRoot.Length);
                    if (!seen.Add(logicalPath)) continue;
                    results.Add(new LibrarySourceFile { logicalPath = logicalPath, physicalPath = normalized, fileName = Path.GetFileName(normalized) });
                }
            }
            return results.OrderBy(source => source.logicalPath, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Preferred declaring include for tokens that would otherwise be ambiguous, because the same helper is either
        /// re-declared across libraries or shadowed by an unrelated overload in another header.
        /// </summary>
        private static readonly Dictionary<string, string> PreferredTokenIncludes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "TransformObjectToWorld", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformObjectToWorldNormal", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformObjectToWorldDir", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformWorldToObjectDir", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformWorldToView", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformWorldToHClip", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "TransformObjectToHClip", "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl" },
            { "SampleSH", "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl" },
            { "SampleSH9", "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl" },
            { "GetWorldSpaceViewDir", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "GetWorldSpaceNormalizeViewDir", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "GetCameraPositionWS", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "ComputeFogFactor", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "AlphaDiscard", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "SampleNormal", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl" },
            { "SampleAlbedoAlpha", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl" },
            { "SampleMetallicSpecGloss", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl" },
            { "SampleEmission", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl" },
            { "InitializeBRDFData", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl" },
            { "GlossyEnvironmentReflection", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl" },
            { "MainLightRealtimeShadow", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl" },
            { "GetMainLight", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl" },
            { "GetAdditionalLightsCount", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl" },
            { "GetAdditionalLight", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl" },
            { "GetVertexPositionInputs", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" },
            { "GetVertexNormalInputs", "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl" }
        };

        /// <summary>
        /// Resolves every function and capability token against all package Shader Libraries. Each token is attributed to
        /// the file that truly declares it, preferring the include the pipeline declares and, when that header does not
        /// declare the token, falling back to the whole library rather than dropping the capability to "unknown".
        /// </summary>
        private static Dictionary<string, DeclarationLocation> ExtractLibraryDeclarations()
        {
            var wanted = new HashSet<string>(FunctionTokens, StringComparer.Ordinal);
            foreach (var requirement in CapabilityRequirements) wanted.Add(requirement.function);
            var sources = EnumerateLibrarySourceFiles();
            var results = new Dictionary<string, DeclarationLocation>(StringComparer.Ordinal);
            foreach (var token in wanted)
            {
                var preferred = PreferredIncludeForToken(token);
                var location = LocateBestDeclaration(sources, token, preferred) ?? (preferred == null ? null : LocateBestDeclaration(sources, token, null));
                if (location != null) results[token] = location;
            }
            return results;
        }

        /// <summary>Capability requirements are authoritative; the static table covers the remaining library tokens.</summary>
        private static string PreferredIncludeForToken(string token)
        {
            foreach (var requirement in CapabilityRequirements)
            {
                if (string.Equals(requirement.function, token, StringComparison.Ordinal)) return requirement.include;
            }
            return PreferredTokenIncludes.TryGetValue(token, out var include) ? include : null;
        }

        private static DeclarationLocation LocateBestDeclaration(LibrarySourceFile[] sources, string token, string preferredInclude)
        {
            var preferredFileName = string.IsNullOrEmpty(preferredInclude) ? null : preferredInclude.Substring(preferredInclude.LastIndexOf('/') + 1);
            DeclarationLocation best = null;
            var bestScore = int.MaxValue;
            foreach (var source in sources)
            {
                if (preferredFileName != null && !string.Equals(source.fileName, preferredFileName, StringComparison.OrdinalIgnoreCase)) continue;
                var codeLines = BuildCodeLines(File.ReadAllLines(source.physicalPath));
                if (!TryLocateDeclaration(codeLines, source.fileName, token, out var startLine, out var endLine, out var signature)) continue;
                var score = CandidateScore(codeLines, source.fileName, startLine - 1, token);
                if (score >= bestScore) continue;
                bestScore = score;
                best = new DeclarationLocation { logicalPath = source.logicalPath, fileName = source.fileName, startLine = startLine, endLine = endLine, signature = signature, sourceRevision = Revision(source.physicalPath) };
            }
            return best;
        }

        private static FunctionCard[] BuildFunctionCards(Dictionary<string, DeclarationLocation> declarations)
        {
            var cards = new List<FunctionCard>();
            foreach (var token in FunctionTokens)
            {
                if (!declarations.TryGetValue(token, out var location)) continue;
                cards.Add(new FunctionCard
                {
                    id = location.fileName + ":" + token,
                    function = token,
                    category = FunctionCategory(token),
                    include = location.logicalPath,
                    sourceFile = location.fileName,
                    signature = location.signature,
                    startLine = location.startLine,
                    endLine = location.endLine,
                    sourceRevision = location.sourceRevision,
                    evidence = "Extracted from " + location.logicalPath + " line " + location.startLine + "."
                });
            }
            return cards.ToArray();
        }

        /// <summary>
        /// Removes line and block comments while preserving the line count, so declarations can be located by their real
        /// line number without ever matching documentation prose or commented-out code.
        /// </summary>
        private static string[] BuildCodeLines(string[] lines)
        {
            var result = new string[lines.Length];
            var inBlockComment = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var builder = new StringBuilder(line.Length);
                for (var cursor = 0; cursor < line.Length; cursor++)
                {
                    if (inBlockComment)
                    {
                        if (cursor + 1 < line.Length && line[cursor] == '*' && line[cursor + 1] == '/') { inBlockComment = false; cursor++; }
                        continue;
                    }
                    if (cursor + 1 < line.Length && line[cursor] == '/' && line[cursor + 1] == '*') { inBlockComment = true; cursor++; continue; }
                    if (cursor + 1 < line.Length && line[cursor] == '/' && line[cursor + 1] == '/') break;
                    builder.Append(line[cursor]);
                }
                result[index] = builder.ToString();
            }
            return result;
        }

        /// <summary>Statement keywords that can never introduce a function declaration.</summary>
        private static readonly string[] StatementKeywords = new[] { "return", "if", "else", "while", "for", "switch", "case", "do", "break", "continue", "using", "sizeof", "assert", "static_assert", "throw" };

        /// <summary>
        /// Finds the strongest declaration line for a token. Call sites, argument lists inside multi-line calls,
        /// member accesses, forward declarations and preprocessor lines are all rejected so a capability is only ever
        /// attributed to the file and line that truly declares it.
        /// </summary>
        private static bool TryLocateDeclaration(string[] codeLines, string fileName, string token, out int startLine, out int endLine, out string signature)
        {
            startLine = 0;
            endLine = 0;
            signature = null;
            var bestIndex = -1;
            var bestScore = int.MaxValue;
            for (var index = 0; index < codeLines.Length; index++)
            {
                if (!IsDeclarationCandidate(codeLines, index, token)) continue;
                var score = CandidateScore(codeLines, fileName, index, token);
                if (score >= bestScore) continue;
                bestScore = score;
                bestIndex = index;
            }
            if (bestIndex < 0) return false;
            startLine = bestIndex + 1;
            signature = BuildSignature(codeLines, bestIndex, out endLine);
            return true;
        }

        /// <summary>
        /// A line can only introduce a declaration when the token stands alone and is preceded by a pure type prefix.
        /// Expression syntax in the prefix ("if (dot(", "return half3(", "a = ") disqualifies the line, which is what
        /// previously attributed capabilities to call sites.
        /// </summary>
        private static bool IsDeclarationCandidate(string[] codeLines, int index, string token)
        {
            var line = codeLines[index];
            var tokenIndex = line.IndexOf(token, StringComparison.Ordinal);
            if (tokenIndex < 0) return false;
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) return false;

            // A character glued to the token means the token is part of a longer name or a member access ("Foo.Bar(").
            if (tokenIndex > 0)
            {
                var glued = line[tokenIndex - 1];
                if (glued == '.' || glued == '>' || glued == '_' || char.IsLetterOrDigit(glued)) return false;
            }

            var after = tokenIndex + token.Length;
            while (after < line.Length && char.IsWhiteSpace(line[after])) after++;
            if (after >= line.Length || line[after] != '(') return false;

            var prefix = line.Substring(0, tokenIndex);
            if (prefix.IndexOf('(') >= 0 || prefix.IndexOf('=') >= 0 || prefix.IndexOf(';') >= 0 || prefix.IndexOf(',') >= 0 || prefix.IndexOf('"') >= 0 || prefix.IndexOf('.') >= 0) return false;
            var hasIdentifier = false;
            foreach (var character in prefix)
            {
                if (char.IsLetter(character) || character == '_') { hasIdentifier = true; continue; }
                if (char.IsWhiteSpace(character) || char.IsDigit(character) || character == '*' || character == '&' || character == ':') continue;
                return false;
            }
            if (!hasIdentifier) return false;
            foreach (var keyword in StatementKeywords)
            {
                if (ContainsWord(prefix, keyword)) return false;
            }
            return true;
        }

        /// <summary>Word-boundary containment, so "returns" is never mistaken for the keyword "return".</summary>
        private static bool ContainsWord(string text, string word)
        {
            var searchFrom = 0;
            while (true)
            {
                var found = text.IndexOf(word, searchFrom, StringComparison.Ordinal);
                if (found < 0) return false;
                var beforeOk = found == 0 || !(char.IsLetterOrDigit(text[found - 1]) || text[found - 1] == '_');
                var end = found + word.Length;
                var afterOk = end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_');
                if (beforeOk && afterOk) return true;
                searchFrom = found + 1;
            }
        }

        /// <summary>
        /// Ranks candidates so a real definition (a body that calls itself) beats a bare declaration, and a
        /// non-deprecated header beats a compatibility shim.
        /// </summary>
        private static int CandidateScore(string[] codeLines, string fileName, int index, string token)
        {
            var score = 1;
            for (var probe = index; probe < codeLines.Length && probe - index < 8; probe++)
            {
                if (codeLines[probe].IndexOf('{') >= 0)
                {
                    var body = new StringBuilder();
                    for (var inner = probe; inner < codeLines.Length && inner - probe < 24; inner++) body.Append(codeLines[inner]);
                    if (body.ToString().IndexOf(token, StringComparison.Ordinal) >= 0) score = 0;
                    break;
                }
                if (codeLines[probe].IndexOf(';') >= 0) break;
            }
            if (fileName.IndexOf("deprecated", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
            return score;
        }

        private static string BuildSignature(string[] codeLines, int index, out int endLine)
        {
            var builder = new StringBuilder(codeLines[index].Trim());
            var last = index;
            while (builder.ToString().IndexOf(')') < 0 && last + 1 < codeLines.Length && last - index < 8)
            {
                last++;
                builder.Append(' ').Append(codeLines[last].Trim());
            }
            endLine = last + 1;
            var text = builder.ToString();
            var braceIndex = text.IndexOf('{');
            return braceIndex >= 0 ? text.Substring(0, braceIndex).TrimEnd() : text;
        }

        private static string FunctionCategory(string token)
        {
            if (token.IndexOf("BRDF", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("Specular", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("Reflectivity", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("CookTorrance", StringComparison.OrdinalIgnoreCase) >= 0) return "brdf";
            if (token.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0) return "shadows";
            if (token.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0) return "lighting";
            if (token.IndexOf("Transform", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("ViewDir", StringComparison.OrdinalIgnoreCase) >= 0) return "transform";
            if (token.IndexOf("SampleSH", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("GlossyEnvironment", StringComparison.OrdinalIgnoreCase) >= 0) return "indirect_lighting";
            if (token.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0) return "alpha";
            if (token.StartsWith("Sample", StringComparison.OrdinalIgnoreCase)) return "surface_sampling";
            return "core";
        }

        /// <summary>Capabilities required by the material pipeline, each proven only by real library evidence.</summary>
        private static readonly CapabilityRequirement[] CapabilityRequirements = new[]
        {
            new CapabilityRequirement { capability = "surface_parameters_metallic_roughness", include = "BRDF.hlsl", function = "InitializeBRDFData", note = "Metallic-Roughness SurfaceParameters 构建入口。" },
            new CapabilityRequirement { capability = "direct_lighting_main", include = "RealtimeLights.hlsl", function = "GetMainLight", note = "主方向光结构体接口。" },
            new CapabilityRequirement { capability = "additional_lights_count", include = "RealtimeLights.hlsl", function = "GetAdditionalLightsCount", note = "附加光数量接口。" },
            new CapabilityRequirement { capability = "additional_light_fetch", include = "RealtimeLights.hlsl", function = "GetAdditionalLight", note = "附加光逐个获取接口。" },
            new CapabilityRequirement { capability = "indirect_diffuse_sh", include = "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl", function = "SampleSH", note = "球谐环境漫反射；声明位于 core 包，URP 通过 include 链转发。" },
            new CapabilityRequirement { capability = "indirect_specular_reflection", include = "GlobalIllumination.hlsl", function = "GlossyEnvironmentReflection", note = "反射探针/IBL 镜面环境光；实际效果仍取决于场景探针。" },
            new CapabilityRequirement { capability = "world_normal_transform", include = "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl", function = "TransformObjectToWorldNormal", note = "世界空间法线；该接口位于 core 包而非 URP Shader Library，扫描范围外时保持 unknown 而不是臆断。" },
            new CapabilityRequirement { capability = "view_direction_world_space", include = "ShaderVariablesFunctions.hlsl", function = "GetWorldSpaceNormalizeViewDir", note = "世界空间归一化视线方向。" },
            new CapabilityRequirement { capability = "vertex_position_inputs", include = "ShaderVariablesFunctions.hlsl", function = "GetVertexPositionInputs", note = "顶点位置多空间变换。" },
            new CapabilityRequirement { capability = "fresnel_rim_edge", include = "ShaderVariablesFunctions.hlsl", function = "GetWorldSpaceNormalizeViewDir", note = "菲涅尔边缘光依赖世界法线与视线方向；边缘因子为艺术化叠加项，不替代 BRDF 中的物理 Fresnel。" },
            new CapabilityRequirement { capability = "alpha_clip_discard", include = "SurfaceInput.hlsl", function = "AlphaDiscard", note = "Alpha Clip 片元裁剪。" },
            new CapabilityRequirement { capability = "surface_normal_sampling", include = "SurfaceInput.hlsl", function = "SampleNormal", note = "法线贴图采样。" },
            new CapabilityRequirement { capability = "shadow_sampling", include = "RealtimeLights.hlsl", function = "MainLightRealtimeShadow", note = "主光实时阴影采样。" }
        };

        private static CapabilityCatalogEntry[] BuildCapabilityCatalog(Dictionary<string, DeclarationLocation> declarations)
        {
            var results = new List<CapabilityCatalogEntry>();
            foreach (var requirement in CapabilityRequirements)
            {
                var found = declarations.TryGetValue(requirement.function, out var location);
                results.Add(new CapabilityCatalogEntry
                {
                    capability = requirement.capability,
                    status = found ? "supported" : "unknown",
                    include = found ? location.logicalPath : (requirement.include.StartsWith("Packages/", StringComparison.Ordinal) ? requirement.include : UniversalLibraryRoot + requirement.include),
                    function = requirement.function,
                    sourceLocation = found ? location.startLine + "-" + location.endLine : null,
                    signature = found ? location.signature : null,
                    confidence = found ? "high" : "unknown",
                    evidence = found ? "Resolved from " + location.logicalPath + " (" + requirement.function + ", line " + location.startLine + ")." : "Not found in any scanned package Shader Library (" + requirement.function + "); the capability stays unknown rather than assumed.",
                    note = requirement.note
                });
            }
            return results.ToArray();
        }

        private sealed class LibraryIncludeEntry
        {
            public string path { get; set; }
            public string sourceRevision { get; set; }
            public string kind { get; set; }
            public string package { get; set; }
        }

        private sealed class FunctionCard
        {
            public string id { get; set; }
            public string function { get; set; }
            public string category { get; set; }
            public string include { get; set; }
            public string sourceFile { get; set; }
            public string signature { get; set; }
            public int startLine { get; set; }
            public int endLine { get; set; }
            public string sourceRevision { get; set; }
            public string evidence { get; set; }
        }

        private sealed class CapabilityCatalogEntry
        {
            public string capability { get; set; }
            public string status { get; set; }
            public string include { get; set; }
            public string function { get; set; }
            public string sourceLocation { get; set; }
            public string signature { get; set; }
            public string confidence { get; set; }
            public string evidence { get; set; }
            public string note { get; set; }
        }

        private sealed class CapabilityRequirement
        {
            public string capability;
            public string include;
            public string function;
            public string note;
        }

        /// <summary>
        /// 在 Unity 主线程中同步导入资产并采集编译证据，避免旧版 Unity 丢失 delayCall
        /// 或后台 continuation 无法回写 Job 状态。
        /// </summary>
        private static object RunCompile(JsonElement args, JobRecord record)
        {
            var paths = GetStringArray(args, "assetPaths").Distinct(StringComparer.Ordinal).ToArray();
            if (paths.Length == 0) throw new ArgumentException("assetPaths must contain at least one asset.");
            var materialPaths = GetStringArray(args, "materialAssetPaths").Distinct(StringComparer.Ordinal).ToArray();
            var requestedAtUtc = DateTime.UtcNow.ToString("o");
            var before = paths.ToDictionary(path => path, path => File.Exists(path) ? Revision(path) : "absent", StringComparer.Ordinal);
            ThrowIfJobCancellationRequested(record);
            foreach (var path in paths)
            {
                ThrowIfJobCancellationRequested(record);
                RequireReadPath(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.Refresh();
            // ImportAsset is synchronous on the Editor main thread. Read twice after Refresh so a concurrent
            // change cannot be represented as evidence for a source revision that did not stay stable.
            var firstObserved = paths.ToDictionary(path => path, path => File.Exists(path) ? Revision(path) : "absent", StringComparer.Ordinal);
            var compiledAssets = paths.Select(path =>
            {
                var finalObserved = File.Exists(path) ? Revision(path) : "absent";
                return new { path, observedRevision = finalObserved, inputRevision = before[path], stable = firstObserved[path] == finalObserved, importStatus = "imported" };
            }).ToArray();
            var materials = materialPaths.Select(path =>
            {
                RequireReadPath(path);
                return new { path, revision = File.Exists(path) ? Revision(path) : "absent" };
            }).ToArray();
            var logs = ReadConsoleLogEntries();
            var diagnostics = new List<DiagnosticEntry>();
            var scannedShaders = new List<object>();
            var dependencies = new List<object>();
            foreach (var path in paths)
            {
                ThrowIfJobCancellationRequested(record);
                diagnostics.AddRange(ExtractDiagnostics(logs, path, true));
                if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                var scan = DescribeShaderCompilation(path, true);
                scannedShaders.Add(scan.payload);
                diagnostics.AddRange(scan.diagnostics);
                dependencies.Add(new { shaderPath = path, shaderRevision = File.Exists(path) ? Revision(path) : "absent", includes = CollectIncludeRevisions(path).ToArray() });
            }
            var errorCount = diagnostics.Count(item => item.severity == "error");
            var warningCount = diagnostics.Count(item => item.severity == "warning");
            var evidence = new
            {
                schemaVersion = "1",
                requestedAtUtc,
                observedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                pipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().FullName : "built-in",
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                compiledAssets,
                materialAssets = materials,
                shaderDependencies = dependencies.ToArray(),
                scannedShaders = scannedShaders.ToArray(),
                errorCount,
                warningCount,
                validity = errorCount == 0 && compiledAssets.All(item => item.stable) ? "valid" : "invalid"
            };
            var evidencePath = WriteRunArtifact(args, "compile/" + Guid.NewGuid().ToString("N") + "/compile-evidence.json", JsonSerializer.Serialize(evidence, JsonOptions));
            record.artifacts.Add(evidencePath);
            return new
            {
                status = errorCount > 0 ? "failed" : "passed",
                compiledAssets,
                materialAssets = materials,
                shaderDependencies = dependencies.ToArray(),
                scannedShaders = scannedShaders.ToArray(),
                diagnostics = diagnostics.ToArray(),
                errorCount,
                warningCount,
                evidence = new { path = evidencePath, contentHash = Hash(File.ReadAllText(evidencePath)), validity = errorCount == 0 && compiledAssets.All(item => item.stable) ? "valid" : "invalid" },
                compiledAtUtc = DateTime.UtcNow.ToString("o")
            };
        }

        private static object EnsureValidationScene(JsonElement args, JobRecord record)
        {
            var profile = RequireProperty(args, "validationProfile");
            var target = RequireProperty(args, "target");
            var shaderPath = RequireString(target, "shaderPath");
            var materialPath = RequireString(target, "materialAssetPath");
            var compileEvidencePath = RequireString(args, "compileEvidencePath");
            ValidateCurrentCompileEvidence(compileEvidencePath, shaderPath, materialPath);
            if (!materialPath.StartsWith(GeneratedRoot, StringComparison.Ordinal))
                throw new ArgumentException("target.materialAssetPath must be a generated Material asset under " + GeneratedRoot);
            RequireReadPath(FixedValidationScenePath);
            RequireReadPath(shaderPath);
            RequireReadPath(materialPath);
            if (!File.Exists(FixedValidationScenePath)) throw new ArgumentException("Fixed validation scene is missing: " + FixedValidationScenePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath) ?? throw new ArgumentException("target.materialAssetPath must reference a loadable generated Material asset.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) ?? throw new ArgumentException("target.shaderPath must reference a loadable generated Shader asset.");
            if (material.shader != shader) throw new ArgumentException("target.materialAssetPath must use target.shaderPath before validation.");

            ValidateFixedValidationFixture();
            var sessionId = Guid.NewGuid().ToString("N");
            var session = new ValidationSessionRecord
            {
                sessionId = sessionId,
                runId = TryGetRunId(args) ?? "unscoped",
                scenePath = FixedValidationScenePath,
                materialPath = materialPath,
                shaderPath = shaderPath,
                materialRevision = Revision(materialPath),
                shaderRevision = Revision(shaderPath),
                compileEvidencePath = compileEvidencePath,
                compileEvidenceRevision = Revision(compileEvidencePath),
                materialSlot = GetInt(target, "materialSlot", 0, 0, int.MaxValue),
                width = GetInt(profile, "width", 1024, 64, 4096),
                height = GetInt(profile, "height", 1024, 64, 4096),
                minAverageLuminance = GetFloat(profile, "minAverageLuminance", 0f, 0f, 1f),
                minNonBackgroundRatio = GetFloat(profile, "minNonBackgroundRatio", 0f, 0f, 1f),
                minProjectedBoundsRatio = GetFloat(profile, "minProjectedBoundsRatio", 0.001f, 0.0001f, 1f),
                minTargetRegionNonBackgroundRatio = GetFloat(profile, "minTargetRegionNonBackgroundRatio", 0.01f, 0.0001f, 1f),
                minTargetRegionDifferenceRatio = GetFloat(profile, "minTargetRegionDifferenceRatio", 0.002f, 0.0001f, 1f),
                createdAtUtc = DateTime.UtcNow.ToString("o")
            };
            ValidationSessions[sessionId] = session;
            var statePath = WriteRunArtifact(args, "validation/" + sessionId + "/session-state.json", JsonSerializer.Serialize(session, JsonOptions));
            var path = WriteRunArtifact(args, "validation/" + sessionId + "/session-manifest.json", JsonSerializer.Serialize(new
            {
                sessionId,
                createdAtUtc = session.createdAtUtc,
                validationScene = new { scenePath = session.scenePath, camera = "unique Camera component", width = session.width, height = session.height },
                target = new { rendererName = FixedValidationRendererName, materialAssetPath = session.materialPath, session.materialRevision, materialSlot = session.materialSlot, shaderPath = session.shaderPath, session.shaderRevision, compileEvidencePath = session.compileEvidencePath, session.compileEvidenceRevision },
                criteria = new { session.minAverageLuminance, session.minNonBackgroundRatio, session.minProjectedBoundsRatio, session.minTargetRegionNonBackgroundRatio, session.minTargetRegionDifferenceRatio },
                deterministicFixture = new { clearFlags = "SolidColor", backgroundColor = "#080B10", timeSeconds = 0f },
                statePath,
                sceneMutation = "The fixed scene is opened additively. The real generated Material asset is bound transactionally to Sphere for captures, then the original Renderer material slots, active scene, selection, camera clear state, and global time vectors are restored. Neither the scene nor the generated material is modified or saved."
            }, JsonOptions));
            record.artifacts.Add(statePath);
            record.artifacts.Add(path);
            return new { validationSessionId = sessionId, manifest = new { path, contentHash = Hash(File.ReadAllText(path)) }, state = new { path = statePath, contentHash = Hash(File.ReadAllText(statePath)) }, status = "ready", validationScene = new { scenePath = session.scenePath, camera = "unique" }, target = new { rendererName = FixedValidationRendererName, materialAssetPath = session.materialPath, session.materialRevision, materialSlot = session.materialSlot, shaderPath = session.shaderPath, session.shaderRevision } };
        }

        private static object CaptureValidation(JsonElement args, JobRecord record)
        {
            var sessionId = RequireString(args, "validationSessionId");
            var session = ResolveValidationSession(sessionId, TryGetRunId(args));
            var captures = RequireCaptureRequests(args);
            var previousActiveScene = EditorSceneManager.GetActiveScene();
            var previousSelection = Selection.objects;
            var scene = EditorSceneManager.OpenScene(session.scenePath, OpenSceneMode.Additive);
            try
            {
                var bindings = ResolveFixedValidationBindings(scene);
                var renderer = bindings.renderer;
                var camera = bindings.camera;
                var generatedMaterial = AssetDatabase.LoadAssetAtPath<Material>(session.materialPath) ?? throw new ArgumentException("Generated validation material could not be loaded: " + session.materialPath);
                var targetShader = AssetDatabase.LoadAssetAtPath<Shader>(session.shaderPath) ?? throw new ArgumentException("Validation Shader could not be loaded: " + session.shaderPath);
                if (Revision(session.materialPath) != session.materialRevision || Revision(session.shaderPath) != session.shaderRevision)
                    throw new InvalidOperationException("Generated Material or Shader changed after validation-session setup; create a new validation session so binding evidence uses matching revisions.");
                if (!File.Exists(session.compileEvidencePath) || Revision(session.compileEvidencePath) != session.compileEvidenceRevision)
                    throw new InvalidOperationException("Compile evidence changed or is missing; create a new validation session after a valid compile.");
                ValidateCurrentCompileEvidence(session.compileEvidencePath, session.shaderPath, session.materialPath);
                if (generatedMaterial.shader != targetShader) throw new ArgumentException("Generated Material Shader no longer matches the validation session target Shader.");
                var originalMaterials = renderer.sharedMaterials;
                if (session.materialSlot >= originalMaterials.Length) throw new ArgumentException("target.materialSlot exceeds the fixed Renderer material slot count.");
                var results = new List<object>();
                var allPassed = true;
                string referenceContentHash = null;
                string generatedContentHash = null;
                CaptureRenderData referenceCapture = null;
                CaptureRenderData generatedCapture = null;
                foreach (var capture in captures)
                {
                    var bindingMode = GetString(capture, "bindingMode") ?? "generated_material";
                    if (bindingMode != "generated_material" && bindingMode != "preserve_original") throw new ArgumentException("captures[].bindingMode must be generated_material or preserve_original.");
                    var captureName = SafeCaptureName(GetString(capture, "captureName") ?? bindingMode);
                    var boundGeneratedMaterial = bindingMode == "generated_material";
                    if (boundGeneratedMaterial && capture.TryGetProperty("propertyOverrides", out _)) throw new ArgumentException("generated_material captures must use the persisted generated Material values; create a distinct generated Material asset for parameter variants.");
                    try
                    {
                        if (boundGeneratedMaterial)
                        {
                            var boundMaterials = (Material[])originalMaterials.Clone();
                            boundMaterials[session.materialSlot] = generatedMaterial;
                            renderer.sharedMaterials = boundMaterials;
                            var actualMaterial = renderer.sharedMaterials[session.materialSlot];
                            if (actualMaterial != generatedMaterial || AssetDatabase.GetAssetPath(actualMaterial) != session.materialPath || actualMaterial.shader != targetShader)
                                throw new InvalidOperationException("Generated Material binding readback failed; capture was not performed.");
                        }
                        EditorSceneManager.SetActiveScene(scene);
                        var projectedBoundsRatio = CalculateProjectedBoundsRatio(camera, renderer.bounds, session.width, session.height);
                        var captureResult = RenderValidationCapture(camera, renderer, scene, session, sessionId, captureName, args, out var statistics);
                        var screenshot = captureResult.screenshot;
                        var decision = projectedBoundsRatio >= session.minProjectedBoundsRatio
                            && statistics.nonBackgroundRatio >= session.minNonBackgroundRatio
                            && statistics.targetRegionNonBackgroundRatio >= session.minTargetRegionNonBackgroundRatio
                            && statistics.averageLuminance >= session.minAverageLuminance ? "pass" : "revise";
                        allPassed &= decision == "pass";
                        if (bindingMode == "preserve_original")
                        {
                            referenceContentHash = screenshot.contentHash;
                            referenceCapture = captureResult;
                        }
                        if (bindingMode == "generated_material")
                        {
                            generatedContentHash = screenshot.contentHash;
                            generatedCapture = captureResult;
                        }
                        var boundMaterial = renderer.sharedMaterials.Length > session.materialSlot ? renderer.sharedMaterials[session.materialSlot] : null;
                        results.Add(new {
                            captureName,
                            bindingMode,
                            targetHierarchyPath = GetHierarchyPath(renderer.transform),
                            materialSlot = session.materialSlot,
                            materialBinding = new {
                                expectedAssetPath = boundGeneratedMaterial ? session.materialPath : AssetDatabase.GetAssetPath(originalMaterials[session.materialSlot]),
                                actualAssetPath = AssetDatabase.GetAssetPath(boundMaterial),
                                instanceId = boundMaterial != null ? boundMaterial.GetInstanceID() : 0,
                                shaderName = boundMaterial != null && boundMaterial.shader != null ? boundMaterial.shader.name : null,
                                materialRevision = boundGeneratedMaterial ? session.materialRevision : null,
                                shaderRevision = boundGeneratedMaterial ? session.shaderRevision : null
                            },
                            originalMaterialPaths = originalMaterials.Select(AssetDatabase.GetAssetPath).ToArray(),
                            decision,
                            screenshot,
                            projectedBoundsRatio,
                            statistics
                        });
                    }
                    finally
                    {
                        renderer.sharedMaterials = originalMaterials;
                    }
                }
                var hasReferenceAndGenerated = referenceCapture != null && generatedCapture != null;
                var materialResponse = hasReferenceAndGenerated && !string.Equals(referenceContentHash, generatedContentHash, StringComparison.Ordinal);
                var targetRegionDifferenceRatio = hasReferenceAndGenerated
                    ? CalculateMaskedDifferenceRatio(referenceCapture.pixels, generatedCapture.pixels, UnionMasks(referenceCapture.targetMask, generatedCapture.targetMask))
                    : 0f;
                var targetMaskOverlapRatio = hasReferenceAndGenerated
                    ? CalculateMaskOverlapRatio(referenceCapture.targetMask, generatedCapture.targetMask)
                    : 0f;
                var targetRegionResponse = targetRegionDifferenceRatio >= session.minTargetRegionDifferenceRatio;
                allPassed &= hasReferenceAndGenerated && materialResponse && targetRegionResponse;
                var reportPath = WriteRunArtifact(args, "validation/" + sessionId + "/captures/validation-report.json", JsonSerializer.Serialize(new
                {
                    capturedAtUtc = DateTime.UtcNow.ToString("o"),
                    validationScene = new { session.scenePath, camera = "unique Camera component" },
                    target = new { rendererName = FixedValidationRendererName, materialAssetPath = session.materialPath, session.materialRevision, session.materialSlot, shaderPath = session.shaderPath, session.shaderRevision, session.compileEvidencePath, session.compileEvidenceRevision },
                    captures = results,
                    criteria = new { session.minAverageLuminance, session.minNonBackgroundRatio, session.minProjectedBoundsRatio, session.minTargetRegionNonBackgroundRatio, session.minTargetRegionDifferenceRatio },
                    deterministicFixture = new { clearFlags = "SolidColor", backgroundColor = "#080B10", timeSeconds = 0f },
                    referenceComparison = new { required = true, hasReferenceAndGenerated, referenceContentHash, generatedContentHash, materialResponse, targetRegionDifferenceRatio, targetMaskOverlapRatio, targetRegionResponse },
                    automaticDecisionScope = "Automatic checks establish only evidence validity: current compile binding, target material read-back, projected target bounds, a controlled background, an isolated target silhouette mask, non-empty target pixels, and a reference-versus-generated target-region response. They do not prove the artistic goal; compare deliberately different persisted material assets before final PASS."
                }, JsonOptions));
                record.artifacts.Add(reportPath);
                return new
                {
                    status = allPassed ? "passed" : "revise",
                    decision = allPassed ? "pass" : "revise",
                    validationSessionId = sessionId,
                    captures = results.ToArray(),
                    referenceComparison = new
                    {
                        required = true,
                        hasReferenceAndGenerated,
                        referenceContentHash,
                        generatedContentHash,
                        materialResponse,
                        targetRegionDifferenceRatio,
                        targetMaskOverlapRatio,
                        targetRegionResponse
                    },
                    report = new { path = reportPath, contentHash = Hash(File.ReadAllText(reportPath)) }
                };
            }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded) EditorSceneManager.SetActiveScene(previousActiveScene);
                Selection.objects = previousSelection;
            }
        }

        private static object CreateShaderCheckpoint(JsonElement args, JobRecord record)
        {
            var decision = RequireString(args, "decision");
            if (decision != "pass" && decision != "revise" && decision != "blocked")
                throw new ArgumentException("decision must be pass, revise, or blocked.");
            if (!args.TryGetProperty("assetRevisions", out var revisions)
                || revisions.ValueKind != JsonValueKind.Array
                || !revisions.EnumerateArray().Any())
                throw new ArgumentException("assetRevisions must be a non-empty array.");

            var checkpointId = Guid.NewGuid().ToString("N");
            var manifestPath = WriteRunArtifact(args, "checkpoints/" + checkpointId + "/manifest.json", JsonSerializer.Serialize(new
            {
                checkpointId,
                decision,
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                assetRevisions = revisions.EnumerateArray().Select(value => new
                {
                    path = RequireString(value, "path"),
                    revision = RequireString(value, "revision")
                }).ToArray()
            }, JsonOptions));
            record.artifacts.Add(manifestPath);
            return new { checkpointId, decision, manifest = new { path = manifestPath, contentHash = Hash(File.ReadAllText(manifestPath)) } };
        }

        private static ValidationSessionRecord ResolveValidationSession(string sessionId, string requestedRunId)
        {
            if (ValidationSessions.TryGetValue(sessionId, out var session)) return session;
            var runId = requestedRunId ?? "unscoped";
            var statePath = RunsRoot + runId + "/validation/" + sessionId + "/session-state.json";
            if (!File.Exists(statePath)) throw new ArgumentException("Unknown validationSessionId. The session is absent from memory and no matching persisted session-state artifact was found for this run.");
            using (var document = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var state = document.RootElement;
                session = new ValidationSessionRecord
                {
                    sessionId = RequireString(state, "sessionId"),
                    runId = GetString(state, "runId") ?? runId,
                    scenePath = RequireString(state, "scenePath"),
                    materialPath = RequireString(state, "materialPath"),
                    shaderPath = RequireString(state, "shaderPath"),
                    materialRevision = GetString(state, "materialRevision"),
                    shaderRevision = GetString(state, "shaderRevision"),
                    compileEvidencePath = GetString(state, "compileEvidencePath"),
                    compileEvidenceRevision = GetString(state, "compileEvidenceRevision"),
                    materialSlot = GetInt(state, "materialSlot", 0, 0, int.MaxValue),
                    width = GetInt(state, "width", 1024, 64, 4096),
                    height = GetInt(state, "height", 1024, 64, 4096),
                    minAverageLuminance = GetFloat(state, "minAverageLuminance", 0f, 0f, 1f),
                    minNonBackgroundRatio = GetFloat(state, "minNonBackgroundRatio", 0f, 0f, 1f),
                    minProjectedBoundsRatio = GetFloat(state, "minProjectedBoundsRatio", 0.001f, 0.0001f, 1f),
                    minTargetRegionNonBackgroundRatio = GetFloat(state, "minTargetRegionNonBackgroundRatio", 0.01f, 0.0001f, 1f),
                    minTargetRegionDifferenceRatio = GetFloat(state, "minTargetRegionDifferenceRatio", 0.002f, 0.0001f, 1f),
                    createdAtUtc = GetString(state, "createdAtUtc")
                };
            }
            if (session == null || session.sessionId != sessionId) throw new ArgumentException("Persisted validation session state is invalid.");
            ValidationSessions[sessionId] = session;
            return session;
        }

        private static void ValidateCurrentCompileEvidence(string evidencePath, string shaderPath, string materialPath)
        {
            RequireReadPath(evidencePath);
            RequireReadPath(shaderPath);
            RequireReadPath(materialPath);
            if (!File.Exists(evidencePath)) throw new ArgumentException("compileEvidencePath does not exist: " + evidencePath);
            if (!File.Exists(shaderPath) || !File.Exists(materialPath))
                throw new InvalidOperationException("Current Shader or Material is missing; compile evidence cannot be reused.");

            using (var document = JsonDocument.Parse(File.ReadAllText(evidencePath)))
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !string.Equals(GetString(root, "schemaVersion"), "1", StringComparison.Ordinal)
                    || !string.Equals(GetString(root, "validity"), "valid", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Compile evidence is malformed, incompatible, or invalid and cannot be used for visual validation.");
                }

                if (!root.TryGetProperty("compiledAssets", out var compiled)
                    || compiled.ValueKind != JsonValueKind.Array
                    || !compiled.EnumerateArray().Any(item =>
                        GetString(item, "path") == shaderPath
                        && GetString(item, "observedRevision") == Revision(shaderPath)
                        && item.TryGetProperty("stable", out var stable)
                        && stable.ValueKind == JsonValueKind.True))
                {
                    throw new InvalidOperationException("Compile evidence does not bind a stable current Shader revision.");
                }

                if (!root.TryGetProperty("materialAssets", out var materials)
                    || materials.ValueKind != JsonValueKind.Array
                    || !materials.EnumerateArray().Any(item =>
                        GetString(item, "path") == materialPath
                        && GetString(item, "revision") == Revision(materialPath)))
                {
                    throw new InvalidOperationException("Compile evidence does not bind the current Material revision.");
                }

                if (!root.TryGetProperty("shaderDependencies", out var dependencies)
                    || dependencies.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Compile evidence has no Shader dependency summary.");
                }

                var dependencyEntries = dependencies.EnumerateArray().Where(item =>
                    GetString(item, "shaderPath") == shaderPath
                    && GetString(item, "shaderRevision") == Revision(shaderPath)).ToArray();
                if (dependencyEntries.Length != 1)
                    throw new InvalidOperationException("Compile evidence does not bind dependencies to the current Shader revision.");

                if (!dependencyEntries[0].TryGetProperty("includes", out var includes)
                    || includes.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Compile evidence Shader dependency summary is malformed.");
                }

                foreach (var include in includes.EnumerateArray())
                {
                    var path = GetString(include, "resolvedPath");
                    var revision = GetString(include, "revision");
                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(revision) || revision == "unresolved")
                        throw new InvalidOperationException("Compile evidence contains an unresolved Shader include dependency.");
                    if (!File.Exists(path) || Revision(path) != revision)
                        throw new InvalidOperationException("Compile evidence include dependency is stale: " + path);
                }
            }
        }

        private static IEnumerable<object> CollectIncludeRevisions(string shaderPath)
        {
            var source = File.Exists(shaderPath) ? File.ReadAllText(shaderPath) : string.Empty;
            foreach (var include in System.Text.RegularExpressions.Regex.Matches(source, "#include\\s+[\\\"<]([^\\\">]+)[\\\">]").Cast<System.Text.RegularExpressions.Match>())
            {
                var raw = include.Groups[1].Value.Replace('\\', '/');
                var candidate = raw.StartsWith("Assets/", StringComparison.Ordinal) || raw.StartsWith("Packages/", StringComparison.Ordinal)
                    ? raw
                    : Path.GetDirectoryName(shaderPath).Replace('\\', '/') + "/" + raw;
                yield return new { include = raw, resolvedPath = candidate, revision = File.Exists(candidate) ? Revision(candidate) : "unresolved" };
            }
        }

        private static JsonElement[] RequireCaptureRequests(JsonElement args)
        {
            if (!args.TryGetProperty("captures", out var value) || value.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("captures must be an array containing exactly one preserve_original and one generated_material capture.");

            var captures = value.EnumerateArray().ToArray();
            if (captures.Length != 2)
                throw new ArgumentException("captures must contain exactly one preserve_original and one generated_material capture.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            var bindingModes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capture in captures)
            {
                var captureName = SafeCaptureName(RequireString(capture, "captureName"));
                var bindingMode = RequireString(capture, "bindingMode");
                if (bindingMode != "generated_material" && bindingMode != "preserve_original")
                    throw new ArgumentException("captures[].bindingMode must be generated_material or preserve_original.");
                if (!names.Add(captureName))
                    throw new ArgumentException("captures[].captureName values must be unique after filename sanitization.");
                if (!bindingModes.Add(bindingMode))
                    throw new ArgumentException("captures must contain each bindingMode exactly once.");
            }

            if (!bindingModes.SetEquals(new[] { "preserve_original", "generated_material" }))
                throw new ArgumentException("captures must contain exactly one preserve_original and one generated_material capture.");
            return captures;
        }

        private static CaptureRenderData RenderValidationCapture(Camera camera, Renderer renderer, Scene scene, ValidationSessionRecord session, string sessionId, string captureName, JsonElement args, out ImageStatistics statistics)
        {
            RenderTexture renderTexture = null;
            Texture2D image = null;
            var originalClearFlags = camera.clearFlags;
            var originalBackground = camera.backgroundColor;
            var originalTime = Shader.GetGlobalVector("_Time");
            var originalSinTime = Shader.GetGlobalVector("_SinTime");
            var originalCosTime = Shader.GetGlobalVector("_CosTime");
            try
            {
                // 固定清屏色和时间向量，避免天空盒、编辑器时间和左上像素推断污染像素证据。
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ValidationBackgroundColor;
                SetValidationTime(0f);
                renderTexture = RenderTexture.GetTemporary(session.width, session.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var originalTarget = camera.targetTexture;
                var originalActive = RenderTexture.active;
                try
                {
                    camera.targetTexture = renderTexture;
                    camera.Render();
                    RenderTexture.active = renderTexture;
                    image = new Texture2D(session.width, session.height, TextureFormat.RGBA32, false, false);
                    image.ReadPixels(new Rect(0, 0, session.width, session.height), 0, 0, false);
                    image.Apply(false, false);
                }
                finally { camera.targetTexture = originalTarget; RenderTexture.active = originalActive; }
                var pixels = image.GetPixels32();
                var targetMask = RenderTargetSilhouetteMask(camera, renderer, scene, session.width, session.height);
                statistics = CalculateImageStatistics(pixels, session.width, session.height, ValidationBackgroundColor);
                var targetPixelRect = CalculateMaskPixelRect(targetMask, session.width, session.height);
                statistics.targetPixelRect = new[] { targetPixelRect.x, targetPixelRect.y, targetPixelRect.width, targetPixelRect.height };
                statistics.targetRegionNonBackgroundRatio = CalculateMaskedNonBackgroundRatio(pixels, targetMask, ValidationBackgroundColor);
                statistics.targetMaskPixelCount = targetMask.Count(value => value);
                statistics.targetMaskRatio = statistics.targetMaskPixelCount / (float)Math.Max(1, targetMask.Length);
                var pngPath = RunsRoot + (TryGetRunId(args) ?? session.runId ?? "unscoped") + "/validation/" + sessionId + "/captures/" + captureName + ".png";
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath) ?? RunsRoot);
                File.WriteAllBytes(pngPath, image.EncodeToPNG());
                return new CaptureRenderData
                {
                    screenshot = new CaptureScreenshot { path = pngPath, contentHash = Hash(File.ReadAllBytes(pngPath)), width = session.width, height = session.height },
                    pixels = pixels,
                    targetMask = targetMask
                };
            }
            finally
            {
                Shader.SetGlobalVector("_Time", originalTime);
                Shader.SetGlobalVector("_SinTime", originalSinTime);
                Shader.SetGlobalVector("_CosTime", originalCosTime);
                camera.clearFlags = originalClearFlags;
                camera.backgroundColor = originalBackground;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (renderTexture != null) RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static string SafeCaptureName(string value)
        {
            var name = string.IsNullOrWhiteSpace(value) ? "capture" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name.Replace('/', '_').Replace('\\', '_');
        }

        private static void ValidateFixedValidationFixture()
        {
            var scene = EditorSceneManager.OpenScene(FixedValidationScenePath, OpenSceneMode.Additive);
            try { ResolveFixedValidationBindings(scene); }
            finally { if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }

        private static FixedValidationBindings ResolveFixedValidationBindings(Scene scene)
        {
            var cameras = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            if (cameras.Length != 1) throw new ArgumentException("Fixed validation scene must contain exactly one Camera; found " + cameras.Length + ".");
            var renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(item => string.Equals(item.gameObject.name, FixedValidationRendererName, StringComparison.Ordinal))
                .ToArray();
            if (renderers.Length != 1)
            {
                var candidates = string.Join(", ", scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).Select(item => GetHierarchyPath(item.transform)).ToArray());
                throw new ArgumentException("Fixed validation scene must contain exactly one Renderer named '" + FixedValidationRendererName + "'; found " + renderers.Length + ". Renderer candidates: " + candidates);
            }
            return new FixedValidationBindings { camera = cameras[0], renderer = renderers[0] };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static float CalculateProjectedBoundsRatio(Camera camera, Bounds bounds, int width, int height)
        {
            var rect = CalculateProjectedPixelRect(camera, bounds, width, height);
            return rect.width * rect.height / (float)(width * height);
        }

        private static RectInt CalculateProjectedPixelRect(Camera camera, Bounds bounds, int width, int height)
        {
            var corners = new List<Vector3>();
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
                corners.Add(camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
            var visible = corners.Where(point => point.z > camera.nearClipPlane).ToArray();
            if (visible.Length == 0) return new RectInt();
            var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(visible.Min(point => point.x)) * width), 0, width);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(visible.Max(point => point.x)) * width), 0, width);
            var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(visible.Min(point => point.y)) * height), 0, height);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(visible.Max(point => point.y)) * height), 0, height);
            return new RectInt(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static void SetValidationTime(float seconds)
        {
            Shader.SetGlobalVector("_Time", new Vector4(seconds / 20f, seconds, seconds * 2f, seconds * 3f));
            Shader.SetGlobalVector("_SinTime", new Vector4(Mathf.Sin(seconds / 8f), Mathf.Sin(seconds / 4f), Mathf.Sin(seconds / 2f), Mathf.Sin(seconds)));
            Shader.SetGlobalVector("_CosTime", new Vector4(Mathf.Cos(seconds / 8f), Mathf.Cos(seconds / 4f), Mathf.Cos(seconds / 2f), Mathf.Cos(seconds)));
        }

        /// <summary>
        /// 渲染目标几何的独立轮廓遮罩。遮罩是几何覆盖范围而非材质 Alpha 后的可见像素：
        /// 它用于限定比较区域，实际 Alpha/透明响应仍由原始材质截图中的像素差异验证。
        /// </summary>
        private static bool[] RenderTargetSilhouetteMask(Camera camera, Renderer targetRenderer, Scene scene, int width, int height)
        {
            RenderTexture renderTexture = null;
            Texture2D image = null;
            Material maskMaterial = null;
            var originalClearFlags = camera.clearFlags;
            var originalBackground = camera.backgroundColor;
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            var sceneRenderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            var enabledStates = sceneRenderers.ToDictionary(item => item, item => item.enabled);
            var originalMaterials = targetRenderer.sharedMaterials;
            try
            {
                var maskShader = Shader.Find("Hidden/Internal-Colored");
                if (maskShader == null) throw new InvalidOperationException("The Unity built-in Hidden/Internal-Colored Shader is unavailable; target silhouette masking cannot run.");
                maskMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
                maskMaterial.SetColor("_Color", Color.white);
                maskMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
                maskMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
                maskMaterial.SetInt("_ZWrite", 1);
                maskMaterial.SetInt("_Cull", (int)CullMode.Back);

                foreach (var item in sceneRenderers) item.enabled = item == targetRenderer && enabledStates[item];
                if (!targetRenderer.enabled) throw new InvalidOperationException("Fixed validation target Renderer is disabled.");
                targetRenderer.sharedMaterials = Enumerable.Repeat(maskMaterial, Math.Max(1, originalMaterials.Length)).ToArray();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false, false);
                return image.GetPixels32().Select(pixel => pixel.r > 127 || pixel.g > 127 || pixel.b > 127).ToArray();
            }
            finally
            {
                targetRenderer.sharedMaterials = originalMaterials;
                foreach (var pair in enabledStates) pair.Key.enabled = pair.Value;
                camera.clearFlags = originalClearFlags;
                camera.backgroundColor = originalBackground;
                camera.targetTexture = originalTarget;
                RenderTexture.active = originalActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (renderTexture != null) RenderTexture.ReleaseTemporary(renderTexture);
                if (maskMaterial != null) UnityEngine.Object.DestroyImmediate(maskMaterial);
            }
        }

        private static RectInt CalculateMaskPixelRect(bool[] mask, int width, int height)
        {
            if (mask == null || mask.Length != width * height) return new RectInt();
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            for (var index = 0; index < mask.Length; index++)
            {
                if (!mask[index]) continue;
                var x = index % width;
                var y = index / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            return maxX < minX || maxY < minY ? new RectInt() : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static float CalculateMaskedNonBackgroundRatio(Color32[] pixels, bool[] mask, Color background)
        {
            if (pixels == null || mask == null || pixels.Length != mask.Length) return 0f;
            var backgroundColor = (Color32)background;
            var nonBackground = 0;
            var count = 0;
            for (var index = 0; index < pixels.Length; index++)
            {
                if (!mask[index]) continue;
                count++;
                var pixel = pixels[index];
                if (Math.Abs(pixel.r - backgroundColor.r) > 4 || Math.Abs(pixel.g - backgroundColor.g) > 4 || Math.Abs(pixel.b - backgroundColor.b) > 4) nonBackground++;
            }
            return count == 0 ? 0f : nonBackground / (float)count;
        }

        private static bool[] UnionMasks(bool[] left, bool[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return Array.Empty<bool>();
            var result = new bool[left.Length];
            for (var index = 0; index < result.Length; index++) result[index] = left[index] || right[index];
            return result;
        }

        private static float CalculateMaskOverlapRatio(bool[] left, bool[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return 0f;
            var intersection = 0;
            var union = 0;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] && right[index]) intersection++;
                if (left[index] || right[index]) union++;
            }
            return union == 0 ? 0f : intersection / (float)union;
        }

        private static float CalculateMaskedDifferenceRatio(Color32[] reference, Color32[] generated, bool[] mask)
        {
            if (reference == null || generated == null || mask == null || reference.Length != generated.Length || reference.Length != mask.Length) return 0f;
            double difference = 0;
            var count = 0;
            for (var index = 0; index < mask.Length; index++)
            {
                if (!mask[index]) continue;
                var left = reference[index];
                var right = generated[index];
                difference += (Math.Abs(left.r - right.r) + Math.Abs(left.g - right.g) + Math.Abs(left.b - right.b)) / (3d * 255d);
                count++;
            }
            return count == 0 ? 0f : (float)(difference / count);
        }

        private static float CalculateRegionNonBackgroundRatio(Color32[] pixels, int width, RectInt region, Color background)
        {
            if (region.width <= 0 || region.height <= 0) return 0f;
            var backgroundColor = (Color32)background;
            var nonBackground = 0;
            var count = 0;
            for (var y = region.yMin; y < region.yMax; y++)
            for (var x = region.xMin; x < region.xMax; x++)
            {
                var pixel = pixels[y * width + x];
                count++;
                if (Math.Abs(pixel.r - backgroundColor.r) > 4 || Math.Abs(pixel.g - backgroundColor.g) > 4 || Math.Abs(pixel.b - backgroundColor.b) > 4) nonBackground++;
            }
            return count == 0 ? 0f : nonBackground / (float)count;
        }

        private static ImageStatistics CalculateImageStatistics(Color32[] pixels, int width, int height, Color background)
        {
            var backgroundColor = (Color32)background;
            long red = 0, green = 0, blue = 0;
            var nonBackgroundCount = 0;
            foreach (var pixel in pixels)
            {
                red += pixel.r;
                green += pixel.g;
                blue += pixel.b;
                if (Math.Abs(pixel.r - backgroundColor.r) > 4 || Math.Abs(pixel.g - backgroundColor.g) > 4 || Math.Abs(pixel.b - backgroundColor.b) > 4) nonBackgroundCount++;
            }
            var count = Math.Max(1, pixels.Length);
            var averageRed = red / (255f * count);
            var averageGreen = green / (255f * count);
            var averageBlue = blue / (255f * count);
            return new ImageStatistics
            {
                width = width,
                height = height,
                averageColor = new ColorStatistics { r = averageRed, g = averageGreen, b = averageBlue },
                averageLuminance = 0.2126f * averageRed + 0.7152f * averageGreen + 0.0722f * averageBlue,
                nonBackgroundRatio = nonBackgroundCount / (float)count
            };
        }

        private static object[] ReadJsonArray(string path)
        {
            if (!File.Exists(path)) return Array.Empty<object>();
            try { using (var doc = JsonDocument.Parse(File.ReadAllText(path))) { return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(item => (object)item.GetRawText()).ToArray() : new object[] { doc.RootElement.GetRawText() }; } }
            catch { return Array.Empty<object>(); }
        }

        private static object DescribeAsset(string path)
        {
            RequireReadPath(path);
            var exists = File.Exists(path) || Directory.Exists(path);
            return new { path, exists, kind = Directory.Exists(path) ? "directory" : "text", revision = exists && File.Exists(path) ? Revision(path) : "absent", contentHash = exists && File.Exists(path) ? Revision(path) : (string)null, writableByStructuredTool = IsGeneratedPath(path) || IsArtifactPath(path) };
        }

        private static object[] FindLines(string[] lines, string token, string kind) => lines.Select((line, index) => new { line, index }).Where(item => item.line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0).Select(item => (object)new { kind, sourceLocation = new { startLine = item.index + 1, endLine = item.index + 1 }, text = item.line.Trim() }).ToArray();
        private static string ParseShaderName(string source) { var marker = "Shader \""; var index = source.IndexOf(marker, StringComparison.Ordinal); if (index < 0) return null; index += marker.Length; var end = source.IndexOf('"', index); return end < 0 ? null : source.Substring(index, end - index); }
        private static string WriteRunArtifact(JsonElement args, string relative, string content) { var runId = SafePathSegment(TryGetRunId(args) ?? "unscoped"); var path = RunsRoot + runId + "/" + relative; Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RunsRoot); File.WriteAllText(path, content, new UTF8Encoding(false)); return path; }
        private static string TryGetRunId(JsonElement args) { if (args.TryGetProperty("operationContext", out var context)) return GetString(context, "runId"); return GetString(args, "runId"); }
        private static string Revision(string path) => Hash(File.ReadAllText(path));
        private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) { return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); } }
        private static string SafeName(string path) => path.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        private static string SafePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("/") || value.Contains("\\"))
                throw new UnauthorizedAccessException("Identifier is not safe for use as an artifact path segment.");
            return value;
        }
        private static bool IsGeneratedPath(string path) => path.Replace('\\', '/').StartsWith(GeneratedRoot, StringComparison.Ordinal);
        private static bool IsArtifactPath(string path) { var normalized = path.Replace('\\', '/'); return normalized.StartsWith(KnowledgeRoot, StringComparison.Ordinal) || normalized.StartsWith(RunsRoot, StringComparison.Ordinal); }
        private static void RequireGeneratedPath(string path) { ValidatePath(path); if (!IsGeneratedPath(path)) throw new UnauthorizedAccessException("Structured writes are limited to " + GeneratedRoot); }
        private static void RequireAuthorizedPlanForWrite(JsonElement args, string path, string currentRevision)
        {
            var codePlan = RequireProperty(args, "codePlan");
            var requestedPlanId = GetString(codePlan, "codePlanId");
            if (string.IsNullOrEmpty(requestedPlanId)) throw new UnauthorizedAccessException("A codePlanId is required for generated asset writes.");
            var context = RequireProperty(args, "operationContext");
            if (string.IsNullOrEmpty(GetString(context, "runId"))) throw new UnauthorizedAccessException("operationContext.runId is required for generated asset writes.");
            if (!string.Equals(requestedPlanId, GetString(context, "codePlanId"), StringComparison.Ordinal)) throw new UnauthorizedAccessException("codePlanId does not match operationContext.");
            UnityMcpAuthorizationRegistry.AuthorizeAndConsume(args, path, currentRevision);
        }
        private static void RequireReadPath(string path) { ValidatePath(path); if (!(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal) || path.StartsWith("ProjectSettings/", StringComparison.Ordinal) || path.StartsWith("Artifacts/", StringComparison.Ordinal))) throw new UnauthorizedAccessException("Path is outside project read roots."); }
        private static void ValidatePath(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == ".." || part == "."))
                throw new UnauthorizedAccessException("Path must be project-relative and may not escape the project.");
            var extension = Path.GetExtension(normalized);
            if (IsGeneratedPath(normalized) && !new[] { ".shader", ".hlsl", ".cginc", ".mat", ".json", ".txt" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Generated asset extension is not allow-listed.");
        }
        private static JsonElement RequireProperty(JsonElement element, string name) { if (!element.TryGetProperty(name, out var value)) throw new ArgumentException("Missing required property: " + name); return value; }
        private static string RequireString(JsonElement element, string name) => GetString(element, name) ?? throw new ArgumentException("Missing required string: " + name);
        private static string GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static int GetInt(JsonElement element, string name, int fallback, int minimum, int maximum)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed)) return fallback;
            return Math.Max(minimum, Math.Min(maximum, parsed));
        }
        private static float GetFloat(JsonElement element, string name, float fallback, float minimum, float maximum)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var parsed)) return fallback;
            return Mathf.Clamp(parsed, minimum, maximum);
        }
        private static string[] GetStringArray(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrEmpty(item)).ToArray() : Array.Empty<string>();
        private static object ParseStoredJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (var document = JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }

        [Serializable] private sealed class JobRecord { public string jobId; public string jobType; public string status; public string createdAtUtc; public string completedAtUtc; public string argsJson; public string contextJson; public string resultJson; public string error; public string idempotencyKey; public string inputHash; [NonSerialized] public CancellationTokenSource cancellation; public readonly List<string> artifacts = new List<string>(); }
        private sealed class IdempotentWriteRecord { public string requestHash; public object result; }

        private sealed class KnowledgePartition
        {
            public readonly string type;
            public readonly string path;
            public KnowledgePartition(string type, string path) { this.type = type; this.path = path; }
        }

        private sealed class KnowledgeQueryMatch
        {
            public string type;
            public string payload;
            public int score;
        }

        /// <summary>Validation state is cached in memory and persisted under its run artifact for domain-reload recovery.</summary>
        private sealed class ValidationSessionRecord
        {
            public string sessionId;
            public string runId;
            public string scenePath;
            public string materialPath;
            public string shaderPath;
            public string materialRevision;
            public string shaderRevision;
            public string compileEvidencePath;
            public string compileEvidenceRevision;
            public int materialSlot;
            public int width;
            public int height;
            public float minAverageLuminance;
            public float minNonBackgroundRatio;
            public float minProjectedBoundsRatio;
            public float minTargetRegionNonBackgroundRatio;
            public float minTargetRegionDifferenceRatio;
            public string createdAtUtc;
        }

        private sealed class FixedValidationBindings
        {
            public Camera camera;
            public Renderer renderer;
        }

        private sealed class ColorStatistics
        {
            public float r;
            public float g;
            public float b;
        }

        private sealed class ImageStatistics
        {
            public int width;
            public int height;
            public ColorStatistics averageColor;
            public float averageLuminance;
            public float nonBackgroundRatio;
            public int[] targetPixelRect;
            public float targetRegionNonBackgroundRatio;
            public int targetMaskPixelCount;
            public float targetMaskRatio;
        }

        private sealed class CaptureRenderData
        {
            public CaptureScreenshot screenshot;
            public Color32[] pixels;
            public bool[] targetMask;
        }

        private sealed class CaptureScreenshot
        {
            public string path;
            public string contentHash;
            public int width;
            public int height;
        }

        /// <summary>Internal carrier for a single Shader's live compile state plus its serialized payload.</summary>
        private sealed class ShaderScanResult
        {
            public bool hasErrors;
            public bool hasWarnings;
            public object payload;
            public readonly List<DiagnosticEntry> diagnostics = new List<DiagnosticEntry>();
        }

        /// <summary>Serialized as properties so System.Text.Json emits them reliably.</summary>
        [Serializable]
        private sealed class DiagnosticEntry
        {
            public string severity { get; set; }
            public string source { get; set; }
            public string message { get; set; }
            public string stackTrace { get; set; }
            public string timestamp { get; set; }
            public string assetPath { get; set; }
        }
    }
}
#endif
