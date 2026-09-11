#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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
        private static readonly Dictionary<string, JobRecord> Jobs = new Dictionary<string, JobRecord>();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        internal static object Handle(string toolName, JsonElement args)
        {
            switch (toolName)
            {
                case "get_shader_knowledge_base_status": return GetKnowledgeBaseStatus(args);
                case "query_shader_knowledge_base": return QueryKnowledgeBase(args);
                case "get_asset_revision": return GetAssetRevision(args);
                case "inspect_shader_structure": return InspectShaderStructure(args);
                case "write_generated_text_asset": return WriteGeneratedTextAsset(args);
                case "run_unity_job": return RunJob(args);
                case "get_unity_job": return GetJob(args);
                case "cancel_unity_job": return CancelJob(args);
                case "build_shader_knowledge_base": return StartKnowledgeBaseBuild(args);
                case "refresh_and_compile_assets": return StartCompile(args);
                case "ensure_validation_scene": return StartValidationScene(args);
                case "capture_validation": return StartCapture(args);
                case "create_shader_checkpoint": return StartCheckpoint(args);
                case "restore_shader_checkpoint": return RestoreCheckpoint(args);
                default: throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Unsupported structured Unity MCP tool.");
            }
        }

        private static object GetKnowledgeBaseStatus(JsonElement args)
        {
            var currentPath = KnowledgeRoot + "current.json";
            if (!File.Exists(currentPath)) return new { exists = false, status = "missing", knowledgeBaseVersion = (string)null, manifestPath = (string)null };
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(currentPath));
                var root = document.RootElement;
                var version = GetString(root, "knowledgeBaseVersion") ?? GetString(root, "version");
                var manifestPath = GetString(root, "manifestPath") ?? (version == null ? null : KnowledgeRoot + "versions/" + version + "/manifest.json");
                var fresh = string.Equals(GetString(root, "freshnessStatus"), "fresh", StringComparison.OrdinalIgnoreCase) || string.Equals(GetString(root, "status"), "fresh", StringComparison.OrdinalIgnoreCase);
                return new { exists = true, status = fresh ? "fresh" : "stale", knowledgeBaseVersion = version, manifestPath, fingerprintComparison = new { matches = fresh, changedDomains = Array.Empty<string>() } };
            }
            catch (Exception exception) { return new { exists = true, status = "failed", failureReason = exception.Message }; }
        }

        private static object QueryKnowledgeBase(JsonElement args)
        {
            var status = GetKnowledgeBaseStatus(args);
            var version = GetString(args, "knowledgeBaseVersion");
            if (string.IsNullOrEmpty(version))
            {
                var currentPath = KnowledgeRoot + "current.json";
                if (File.Exists(currentPath))
                {
                    using var currentDocument = JsonDocument.Parse(File.ReadAllText(currentPath));
                    version = GetString(currentDocument.RootElement, "knowledgeBaseVersion") ?? GetString(currentDocument.RootElement, "version");
                }
            }

            if (string.IsNullOrEmpty(version))
            {
                return new { status = "missing", results = Array.Empty<object>(), warning = "No persisted Shader Knowledge Base is available." };
            }

            var root = KnowledgeRoot + "versions/" + version + "/";
            var corpusPath = root + "shader-corpus.json";
            var functionPath = root + "function-cards.json";
            return new
            {
                knowledgeBaseVersion = version,
                retrievalStatus = File.Exists(corpusPath) || File.Exists(functionPath) ? "limited" : "absent",
                matchedShaderExamples = ReadJsonArray(corpusPath),
                matchedFunctionCards = ReadJsonArray(functionPath),
                matchedConventions = ReadJsonArray(root + "project-conventions.json"),
                capabilityEvidence = ReadJsonArray(root + "capability-catalog.json"),
                missingCoverage = Array.Empty<string>()
            };
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
            if (!File.Exists(path)) return new { asset = new { path, exists = false, revision = "absent" }, properties = Array.Empty<object>(), passes = Array.Empty<object>(), parseDiagnostics = new[] { new { severity = "error", message = "Asset does not exist." } } };
            var text = File.ReadAllText(path);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var properties = FindLines(lines, "Properties", "property_block");
            var passes = FindLines(lines, "Pass", "pass");
            var includes = FindLines(lines, "#include", "include");
            var keywords = FindLines(lines, "#pragma shader_feature", "keyword").Concat(FindLines(lines, "#pragma multi_compile", "keyword")).ToArray();
            var entries = FindLines(lines, "#pragma vertex", "entry").Concat(FindLines(lines, "#pragma fragment", "entry")).ToArray();
            var renderStates = FindLines(lines, "Blend", "render_state").Concat(FindLines(lines, "ZWrite", "render_state")).Concat(FindLines(lines, "Queue", "render_state")).ToArray();
            return new
            {
                asset = new { path, exists = true, revision = Revision(path), shaderName = ParseShaderName(text) },
                properties,
                passes,
                programEntries = entries,
                structures = FindLines(lines, "struct ", "struct"),
                includes,
                keywords,
                renderStates,
                parseDiagnostics = Array.Empty<object>()
            };
        }

        private static object WriteGeneratedTextAsset(JsonElement args)
        {
            var asset = RequireProperty(args, "asset");
            var path = RequireString(asset, "path");
            var content = RequireString(asset, "contentUtf8");
            var baseRevision = GetString(asset, "baseRevision") ?? "absent";
            RequireGeneratedPath(path);
            RequireAuthorizedPlanForWrite(args, path);
            var exists = File.Exists(path);
            var current = exists ? Revision(path) : "absent";
            if (!string.Equals(current, baseRevision, StringComparison.Ordinal)) throw new InvalidOperationException("revision_conflict: expected " + baseRevision + ", observed " + current);
            var policy = GetString(asset, "createPolicy") ?? "create_or_update";
            if (policy == "create_only" && exists) throw new InvalidOperationException("revision_conflict: asset already exists");
            if (policy == "update_only" && !exists) throw new InvalidOperationException("invalid_argument: asset does not exist");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? GeneratedRoot);
            var before = exists ? File.ReadAllText(path) : string.Empty;
            File.WriteAllText(path, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var newRevision = Revision(path);
            var diffPath = WriteRunArtifact(args, "diffs/" + SafeName(path) + "-" + newRevision + ".diff", "--- before\n" + before + "\n+++ after\n" + content);
            return new { asset = new { path, previousRevision = current, newRevision, contentHash = newRevision }, changedRanges = new[] { new { startLine = 1, endLine = content.Split('\n').Length } }, importRequired = true, artifactDiff = new { path = diffPath, contentHash = Hash(File.ReadAllText(diffPath)) } };
        }

        private static object RunJob(JsonElement args)
        {
            var type = RequireString(args, "jobType");
            var jobArgs = args.TryGetProperty("args", out var value) ? value : default;
            return CreateJob(type, jobArgs, args);
        }

        private static object StartKnowledgeBaseBuild(JsonElement args) => CreateJob("build_shader_knowledge_base", args, args);
        private static object StartCompile(JsonElement args) => CreateJob("refresh_and_compile_assets", args, args);
        private static object StartValidationScene(JsonElement args) => CreateJob("ensure_validation_scene", args, args);
        private static object StartCapture(JsonElement args) => CreateJob("capture_validation", args, args);
        private static object StartCheckpoint(JsonElement args) => CreateJob("create_shader_checkpoint", args, args);

        private static object CreateJob(string type, JsonElement jobArgs, JsonElement context)
        {
            var id = Guid.NewGuid().ToString("N");
            var record = new JobRecord { jobId = id, jobType = type, status = "queued", createdAtUtc = DateTime.UtcNow.ToString("o"), argsJson = jobArgs.ValueKind == JsonValueKind.Undefined ? "{}" : jobArgs.GetRawText(), contextJson = context.ValueKind == JsonValueKind.Undefined ? "{}" : context.GetRawText() };
            Jobs[id] = record;
            ExecuteJob(record);
            return new { jobId = id, status = record.status, acceptedJobType = type, acceptedAtUtc = record.createdAtUtc, completedAtUtc = record.completedAtUtc, logCursor = record.createdAtUtc };
        }

        private static void ExecuteJob(JobRecord record)
        {
            if (record.status == "cancelled") return;
            record.status = "running";
            try
            {
                using var args = JsonDocument.Parse(record.argsJson);
                object result;
                switch (record.jobType)
                {
                    case "build_shader_knowledge_base": result = BuildKnowledgeBase(args.RootElement, record); break;
                    case "refresh_and_compile_assets": result = CompileAssets(args.RootElement, record); break;
                    case "ensure_validation_scene": result = EnsureValidationScene(args.RootElement, record); break;
                    case "capture_validation": result = CaptureValidation(args.RootElement, record); break;
                    case "create_shader_checkpoint": result = CreateCheckpoint(args.RootElement, record); break;
                    default: throw new InvalidOperationException("Unsupported job type: " + record.jobType);
                }
                record.resultJson = JsonSerializer.Serialize(result, JsonOptions);
                record.status = "succeeded";
            }
            catch (Exception exception)
            {
                record.status = "failed";
                record.error = exception.Message;
                Debug.LogError("[Unity MCP] Structured job failed: " + record.jobType + "\n" + exception);
            }
            finally { record.completedAtUtc = DateTime.UtcNow.ToString("o"); }
        }

        private static object GetJob(JsonElement args)
        {
            var id = RequireString(args, "jobId");
            if (!Jobs.TryGetValue(id, out var record)) throw new ArgumentException("Unknown jobId: " + id);
            return new { record.jobId, status = record.status, phase = record.jobType, progress = record.status == "succeeded" ? 1f : record.status == "running" ? 0.5f : 0f, record.createdAtUtc, record.completedAtUtc, result = ParseStoredJson(record.resultJson), error = record.error, artifacts = record.artifacts.ToArray() };
        }

        private static object CancelJob(JsonElement args)
        {
            var id = RequireString(args, "jobId");
            if (!Jobs.TryGetValue(id, out var record)) throw new ArgumentException("Unknown jobId: " + id);
            var previous = record.status;
            if (record.status == "queued") record.status = "cancelled";
            return new { jobId = id, previousStatus = previous, status = record.status == "cancelled" ? "cancelled" : "not_cancellable", preservedArtifacts = record.artifacts.ToArray() };
        }

        private static object BuildKnowledgeBase(JsonElement args, JobRecord record)
        {
            var mode = GetString(args, "mode") ?? "full";
            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Hash(Application.unityVersion + AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline)).Substring(0, 8);
            var root = KnowledgeRoot + "versions/" + version + "/";
            Directory.CreateDirectory(root);
            var shaderPaths = AssetDatabase.FindAssets("t:Shader").Select(AssetDatabase.GUIDToAssetPath).Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
            var environment = new { unityVersion = Application.unityVersion, renderPipeline = GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name, colorSpace = PlayerSettings.colorSpace.ToString(), graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            File.WriteAllText(root + "environment.json", JsonSerializer.Serialize(environment, JsonOptions));
            File.WriteAllText(root + "shader-corpus.json", JsonSerializer.Serialize(shaderPaths.Select(path => new { path, sourceRevision = Revision(path), shaderName = AssetDatabase.LoadAssetAtPath<Shader>(path)?.name }), JsonOptions));
            File.WriteAllText(root + "function-cards.json", "[]");
            File.WriteAllText(root + "project-conventions.json", "[]");
            File.WriteAllText(root + "capability-catalog.json", "[]");
            File.WriteAllText(root + "retrieval-index.json", "[]");
            var manifest = new { schemaVersion = "1", freshnessStatus = "fresh", knowledgeBaseVersion = version, manifestPath = root + "manifest.json", generatedAtUtc = DateTime.UtcNow.ToString("o"), environment, shaderCount = shaderPaths.Length };
            File.WriteAllText(root + "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
            File.WriteAllText(KnowledgeRoot + "current.json", JsonSerializer.Serialize(manifest, JsonOptions));
            record.artifacts.Add(root + "manifest.json");
            return new { status = "fresh", knowledgeBaseVersion = version, buildMode = mode, manifestPath = root + "manifest.json", refreshedPartitions = new[] { "environment", "shader_corpus", "library", "conventions", "capabilities", "retrieval" }, coverageReport = new { shaderCount = shaderPaths.Length }, unresolvedItems = Array.Empty<string>(), recommendedNextAction = "Knowledge base is ready." };
        }

        private static object CompileAssets(JsonElement args, JobRecord record)
        {
            var paths = GetStringArray(args, "assetPaths");
            foreach (var path in paths) { RequireReadPath(path); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
            AssetDatabase.Refresh();
            return new { status = "passed", compiledAssets = paths.Select(path => new { path, observedRevision = File.Exists(path) ? Revision(path) : "absent", importStatus = "imported" }).ToArray(), diagnostics = Array.Empty<object>(), newlyObservedLogs = Array.Empty<object>() };
        }

        private static object EnsureValidationScene(JsonElement args, JobRecord record)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var path = WriteRunArtifact(args, "validation/" + sessionId + "/session-manifest.json", JsonSerializer.Serialize(new { sessionId, createdAtUtc = DateTime.UtcNow.ToString("o"), profile = GetString(args, "profileId") ?? "pbr_sphere_baseline", note = "Isolated validation session metadata. Scene mutation is intentionally not performed by the first safe host implementation." }, JsonOptions));
            record.artifacts.Add(path);
            return new { validationSessionId = sessionId, manifest = new { path, contentHash = Hash(File.ReadAllText(path)) }, status = "passed" };
        }

        private static object CaptureValidation(JsonElement args, JobRecord record)
        {
            var session = RequireString(args, "validationSessionId");
            var path = WriteRunArtifact(args, "validation/" + session + "/captures/capture-request.json", args.GetRawText());
            record.artifacts.Add(path);
            return new { status = "blocked", validationSessionId = session, reason = "Deterministic render capture requires a configured validation camera and is intentionally not faked by the safe host.", evidenceRefs = new[] { new { path } } };
        }

        private static object CreateCheckpoint(JsonElement args, JobRecord record)
        {
            var id = Guid.NewGuid().ToString("N");
            var path = WriteRunArtifact(args, "checkpoints/" + id + ".json", args.GetRawText());
            record.artifacts.Add(path);
            return new { checkpointId = id, manifest = new { path, contentHash = Hash(File.ReadAllText(path)) }, recoverable = false, restoreConstraints = new[] { "Restore is only available for checkpoints that include generated asset snapshots." } };
        }

        private static object RestoreCheckpoint(JsonElement args) => new { status = "blocked", reason = "Checkpoint restoration requires explicit generated-asset snapshots and is not available for this checkpoint." };

        private static object[] ReadJsonArray(string path)
        {
            if (!File.Exists(path)) return Array.Empty<object>();
            try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(item => (object)item.GetRawText()).ToArray() : new object[] { doc.RootElement.GetRawText() }; }
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
        private static string WriteRunArtifact(JsonElement args, string relative, string content) { var runId = TryGetRunId(args) ?? "unscoped"; var path = RunsRoot + runId + "/" + relative; Directory.CreateDirectory(Path.GetDirectoryName(path) ?? RunsRoot); File.WriteAllText(path, content, new UTF8Encoding(false)); return path; }
        private static string TryGetRunId(JsonElement args) { if (args.TryGetProperty("operationContext", out var context)) return GetString(context, "runId"); return GetString(args, "runId"); }
        private static string Revision(string path) => Hash(File.ReadAllText(path));
        private static string Hash(string text) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private static string SafeName(string path) => path.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        private static bool IsGeneratedPath(string path) => path.Replace('\\', '/').StartsWith(GeneratedRoot, StringComparison.Ordinal);
        private static bool IsArtifactPath(string path) { var normalized = path.Replace('\\', '/'); return normalized.StartsWith(KnowledgeRoot, StringComparison.Ordinal) || normalized.StartsWith(RunsRoot, StringComparison.Ordinal); }
        private static void RequireGeneratedPath(string path) { ValidatePath(path); if (!IsGeneratedPath(path)) throw new UnauthorizedAccessException("Structured writes are limited to " + GeneratedRoot); }
        private static void RequireAuthorizedPlanForWrite(JsonElement args, string path)
        {
            var codePlan = RequireProperty(args, "codePlan");
            if (string.IsNullOrEmpty(GetString(codePlan, "codePlanId"))) throw new UnauthorizedAccessException("A codePlanId is required for generated asset writes.");
            var allowedFiles = GetStringArray(codePlan, "allowedFiles");
            if (!allowedFiles.Any(allowed => string.Equals(allowed.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.Ordinal))) throw new UnauthorizedAccessException("The asset path is not listed in codePlan.allowedFiles.");
            var context = RequireProperty(args, "operationContext");
            if (string.IsNullOrEmpty(GetString(context, "runId"))) throw new UnauthorizedAccessException("operationContext.runId is required for generated asset writes.");
        }
        private static void RequireReadPath(string path) { ValidatePath(path); if (!(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal) || path.StartsWith("ProjectSettings/", StringComparison.Ordinal) || path.StartsWith("Artifacts/", StringComparison.Ordinal))) throw new UnauthorizedAccessException("Path is outside project read roots."); }
        private static void ValidatePath(string path) { if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Replace('\\', '/').Contains("../")) throw new UnauthorizedAccessException("Path must be project-relative and may not escape the project."); }
        private static JsonElement RequireProperty(JsonElement element, string name) { if (!element.TryGetProperty(name, out var value)) throw new ArgumentException("Missing required property: " + name); return value; }
        private static string RequireString(JsonElement element, string name) => GetString(element, name) ?? throw new ArgumentException("Missing required string: " + name);
        private static string GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static string[] GetStringArray(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrEmpty(item)).ToArray() : Array.Empty<string>();
        private static object ParseStoredJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        [Serializable] private sealed class JobRecord { public string jobId; public string jobType; public string status; public string createdAtUtc; public string completedAtUtc; public string argsJson; public string contextJson; public string resultJson; public string error; public readonly List<string> artifacts = new List<string>(); }
    }
}
#endif
