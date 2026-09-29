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

    }
}
#endif
