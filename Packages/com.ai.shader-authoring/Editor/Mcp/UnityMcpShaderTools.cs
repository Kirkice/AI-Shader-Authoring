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
    /// <summary>
    /// Structured, allow-listed Shader authoring operations. All file mutations are limited to
    /// generated assets and artifacts; arbitrary editor commands must not be used by this host.
    /// </summary>
    internal static partial class UnityMcpShaderTools
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

        [Serializable] private sealed class JobRecord { public string jobId; public string jobType; public string status; public string phase; public float progress; public string createdAtUtc; public string completedAtUtc; public string argsJson; public string contextJson; public string resultJson; public string error; public string idempotencyKey; public string inputHash; [NonSerialized] public CancellationTokenSource cancellation; public readonly List<string> artifacts = new List<string>(); }
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
