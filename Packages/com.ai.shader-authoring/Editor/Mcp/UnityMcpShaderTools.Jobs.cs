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
            EditorApplication.delayCall += () => { var ignored = ExecuteJobAsync(record); };
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

        private static async Task ExecuteJobAsync(JobRecord record)
        {
            if (record.status == "cancelled") return;
            record.status = "running";
            record.phase = "scheduled";
            record.progress = 0.05f;
            PersistJob(record);
            try
            {
                // Never perform the accepted operation in the protocol callback/delayCall stack. Yielding one
                // editor update keeps job acceptance observable and lets repaint/input work run first.
                await NextEditorUpdateAsync(record.cancellation.Token);
                using (var args = JsonDocument.Parse(record.argsJson))
                {
                    record.phase = "executing";
                    record.progress = 0.15f;
                    PersistJob(record);
                    object result;
                    switch (record.jobType)
                    {
                        case "build_shader_knowledge_base": result = await BuildKnowledgeBaseAsync(args.RootElement, record); break;
                        case "refresh_and_compile_assets": result = RunCompile(args.RootElement, record); break;
                        case "export_compiled_gles_variants": result = CompiledGlesVariantExporter.Export(args.RootElement, record.cancellation.Token); break;
                        case "analyze_shader_performance":
                            // With caller-supplied compiled variants the analyzer is pure file/CPU/process work and
                            // may run off the Editor thread. Without them it must invoke Unity's Shader exporter.
                            if (HasCompiledGlesInput(args.RootElement))
                            {
                                var analysisArgs = args.RootElement.Clone();
                                result = await Task.Run(() => ShaderPerformanceAnalyzer.Analyze(analysisArgs, record.cancellation.Token), record.cancellation.Token);
                            }
                            else result = ShaderPerformanceAnalyzer.Analyze(args.RootElement, record.cancellation.Token);
                            break;
                        case "ensure_validation_scene": result = EnsureValidationScene(args.RootElement, record); break;
                        case "capture_validation": result = CaptureValidation(args.RootElement, record); break;
                        case "create_shader_checkpoint": result = CreateShaderCheckpoint(args.RootElement, record); break;
                        default: throw new InvalidOperationException("Unsupported job type: " + record.jobType);
                    }
                    ThrowIfJobCancellationRequested(record);
                    record.resultJson = JsonSerializer.Serialize(result, JsonOptions);
                    record.status = "succeeded";
                    record.phase = "completed";
                    record.progress = 1f;
                }
            }
            catch (OperationCanceledException)
            {
                record.status = "cancelled";
                record.phase = "cancelled";
            }
            catch (Exception exception)
            {
                record.status = "failed";
                record.phase = "failed";
                record.error = exception.Message;
                Debug.LogError("[Unity MCP] Structured job failed: " + record.jobType + "\n" + exception);
            }
            finally { record.completedAtUtc = DateTime.UtcNow.ToString("o"); PersistJob(record); }
        }

        private static bool HasCompiledGlesInput(JsonElement args)
        {
            return args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty("compiledGlesVariants", out var variants)
                && variants.ValueKind == JsonValueKind.Array
                && variants.EnumerateArray().Any();
        }

        private static Task NextEditorUpdateAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction callback = null;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            callback = () =>
            {
                EditorApplication.update -= callback;
                registration.Dispose();
                completion.TrySetResult(true);
            };
            registration = cancellationToken.Register(() =>
            {
                EditorApplication.update -= callback;
                completion.TrySetCanceled();
            });
            EditorApplication.update += callback;
            return completion.Task;
        }

        private static object GetJob(JsonElement args)
        {
            var id = RequireString(args, "jobId");
            RecoverPersistedJobs();
            if (!Jobs.TryGetValue(id, out var record)) throw new ArgumentException("Unknown jobId: " + id);
            var progress = record.status == "succeeded" ? 1f : record.progress;
            return new { record.jobId, status = record.status, phase = record.phase ?? record.jobType, progress, record.createdAtUtc, record.completedAtUtc, result = ParseStoredJson(record.resultJson), error = record.error, artifacts = record.artifacts.ToArray() };
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

    }
}
#endif
