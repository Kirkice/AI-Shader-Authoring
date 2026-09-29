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
        private static async Task<object> BuildKnowledgeBaseAsync(JsonElement args, JobRecord record)
        {
            var mode = GetString(args, "mode") ?? "full";
            var pipelineAssetPath = GraphicsSettings.currentRenderPipeline == null ? "builtin" : AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline);
            var packageLockPath = "Packages/packages-lock.json";
            var packageLockFingerprint = File.Exists(packageLockPath) ? Revision(packageLockPath) : "no-package-lock";
            var version = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Hash(Application.unityVersion + pipelineAssetPath + packageLockFingerprint).Substring(0, 8);
            var root = KnowledgeRoot + "versions/" + version + "/";
            Directory.CreateDirectory(root);

            // 知识库同时覆盖项目资产与所有可解析的包资产，不预设某一渲染管线。
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
            var pipelineIdentity = CurrentPipelineIdentity();
            // Package discovery uses Unity APIs and stays on the Editor thread. Recursive enumeration, hashing and
            // declaration parsing use only immutable path snapshots and therefore run safely in the worker phase.
            var libraryRoots = CaptureLibraryRoots();
            var environment = new { unityVersion = Application.unityVersion, renderPipeline = GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name, colorSpace = PlayerSettings.colorSpace.ToString(), graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), packageLockFingerprint };

            record.phase = "indexing_sources";
            record.progress = 0.3f;
            PersistJob(record);
            ThrowIfJobCancellationRequested(record);
            var background = await Task.Run(() =>
            {
                record.cancellation.Token.ThrowIfCancellationRequested();
                var librarySources = EnumerateLibrarySourceFiles(libraryRoots, record.cancellation.Token);
                var index = BuildLibraryIndex(librarySources, pipelineIdentity, record.cancellation.Token);
                var declarations = ExtractLibraryDeclarations(librarySources, record.cancellation.Token);
                return new KnowledgeBuildBackgroundResult { libraryIndex = index, declarations = declarations };
            }, record.cancellation.Token);
            ThrowIfJobCancellationRequested(record);

            record.phase = "publishing";
            record.progress = 0.8f;
            PersistJob(record);
            File.WriteAllText(root + "environment.json", JsonSerializer.Serialize(environment, JsonOptions));
            File.WriteAllText(root + "shader-corpus.json", JsonSerializer.Serialize(corpus, JsonOptions));

            var libraryIndex = background.libraryIndex;
            File.WriteAllText(root + "library-index.json", JsonSerializer.Serialize(libraryIndex, JsonOptions));

            var libraryDeclarations = background.declarations;
            var functionCards = BuildFunctionCards(libraryDeclarations, pipelineIdentity);
            File.WriteAllText(root + "function-cards.json", JsonSerializer.Serialize(functionCards, JsonOptions));

            var capabilityCatalog = BuildCapabilityCatalog(libraryDeclarations, pipelineIdentity);
            File.WriteAllText(root + "capability-catalog.json", JsonSerializer.Serialize(capabilityCatalog, JsonOptions));

            // Project-local conventions stay a separate partition so library facts are never mistaken for house style.
            File.WriteAllText(root + "project-conventions.json", "[]");

            var retrievalIndex = functionCards.Select(card => new { term = card.function, partition = "library", target = card.include, pipeline = card.pipeline, package = card.package, sourceRevision = card.sourceRevision, confidence = card.confidence })
                .Concat(capabilityCatalog.Select(item => new { term = item.capability, partition = "capabilities", target = item.include, pipeline = item.pipeline, package = item.package, sourceRevision = item.sourceRevision, confidence = item.confidence }))
                .ToArray();
            File.WriteAllText(root + "retrieval-index.json", JsonSerializer.Serialize(retrievalIndex, JsonOptions));

            var supportedCapabilityCount = capabilityCatalog.Count(item => item.status == "supported");
            var integrity = await UnityMcpKnowledgeBaseIntegrity.CaptureAsync(record.cancellation.Token);
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

        /// <summary>
        /// 当前工程可访问的 Shader Library 根目录。目录由项目资产和 Package Manager 的实际内容发现，
        /// 不把任何渲染管线、包名或 Include 路径视为全局前提。
        /// </summary>
        private static string[] DiscoverLibraryRoots()
        {
            var roots = new List<string> { "Assets/" };
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.name) || string.IsNullOrEmpty(package.resolvedPath)) continue;
                roots.Add("Packages/" + package.name + "/");
            }
            return roots.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static string CurrentPipelineIdentity()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline == null ? "builtin" : pipeline.GetType().FullName;
        }

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

        /// <summary>索引当前工程可访问的 HLSL、CGINC 与 Shader 源文件，并保留真实来源证据。</summary>
        private static LibraryIncludeEntry[] BuildLibraryIndex(LibrarySourceFile[] sources, string pipelineIdentity, CancellationToken cancellationToken)
        {
            var entries = new List<LibraryIncludeEntry>();
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(new LibraryIncludeEntry
                {
                    path = source.logicalPath,
                    sourceRevision = Revision(source.physicalPath),
                    kind = Path.GetExtension(source.physicalPath).TrimStart('.').ToLowerInvariant(),
                    pipeline = pipelineIdentity,
                    package = PackageNameFromLogicalPath(source.logicalPath),
                    evidence = "Discovered from the registered project or package source tree."
                });
            }
            return entries.ToArray();
        }

        // 函数卡片不由预设符号表驱动；所有卡片均来自当前工程实际可读源码中的声明。

        private sealed class LibraryRoot
        {
            public string logicalRoot;
            public string physicalRoot;
        }

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

        private static LibraryRoot[] CaptureLibraryRoots()
        {
            return DiscoverLibraryRoots()
                .Select(root => root.TrimEnd('/'))
                .Select(logicalRoot => new LibraryRoot { logicalRoot = logicalRoot, physicalRoot = ResolvePhysicalPath(logicalRoot) })
                .Where(root => !string.IsNullOrEmpty(root.physicalRoot))
                .ToArray();
        }

        /// <summary>
        /// Enumerates project and registered-package Shader sources once, in a stable order. The supplied roots were
        /// captured through Unity APIs on the Editor thread; this method itself performs only worker-safe file I/O.
        /// </summary>
        private static LibrarySourceFile[] EnumerateLibrarySourceFiles(LibraryRoot[] roots, CancellationToken cancellationToken)
        {
            var results = new List<LibrarySourceFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(root.physicalRoot)) continue;
                var files = Directory.EnumerateFiles(root.physicalRoot, "*.*", SearchOption.AllDirectories)
                    .Where(file => file.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var normalized = file.Replace('\\', '/');
                    if (!normalized.StartsWith(root.physicalRoot, StringComparison.Ordinal)) continue;
                    var logicalPath = root.logicalRoot + normalized.Substring(root.physicalRoot.Length);
                    if (!seen.Add(logicalPath)) continue;
                    results.Add(new LibrarySourceFile { logicalPath = logicalPath, physicalPath = normalized, fileName = Path.GetFileName(normalized) });
                }
            }
            return results.OrderBy(source => source.logicalPath, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// 从所有已发现源码中枚举真实函数声明。这里不提供包、文件名或渲染管线优先级；
        /// 同名声明仅按声明质量和稳定路径择优，避免把某个示例管线固化为系统事实。
        /// </summary>
        private static Dictionary<string, DeclarationLocation> ExtractLibraryDeclarations(LibrarySourceFile[] sources, CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, DeclarationLocation>(StringComparer.Ordinal);
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var codeLines = BuildCodeLines(File.ReadAllLines(source.physicalPath));
                for (var index = 0; index < codeLines.Length; index++)
                {
                    var token = DeclarationToken(codeLines[index]);
                    if (string.IsNullOrEmpty(token) || !IsDeclarationCandidate(codeLines, index, token)) continue;
                    var score = CandidateScore(codeLines, source.fileName, index, token);
                    if (scores.TryGetValue(token, out var currentScore) && currentScore <= score) continue;
                    var signature = BuildSignature(codeLines, index, out var endLine);
                    scores[token] = score;
                    results[token] = new DeclarationLocation
                    {
                        logicalPath = source.logicalPath,
                        fileName = source.fileName,
                        startLine = index + 1,
                        endLine = endLine,
                        signature = signature,
                        sourceRevision = Revision(source.physicalPath)
                    };
                }
            }
            return results;
        }

        private static string DeclarationToken(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var openParenthesis = line.IndexOf('(');
            if (openParenthesis <= 0) return null;
            var end = openParenthesis - 1;
            while (end >= 0 && char.IsWhiteSpace(line[end])) end--;
            var start = end;
            while (start >= 0 && (char.IsLetterOrDigit(line[start]) || line[start] == '_')) start--;
            return end >= start + 1 ? line.Substring(start + 1, end - start) : null;
        }

        private static FunctionCard[] BuildFunctionCards(Dictionary<string, DeclarationLocation> declarations, string pipelineIdentity)
        {
            var cards = new List<FunctionCard>();
            foreach (var pair in declarations.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var token = pair.Key;
                var location = pair.Value;
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
                    pipeline = pipelineIdentity,
                    package = PackageNameFromLogicalPath(location.logicalPath),
                    confidence = "high",
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

        /// <summary>优先选择具有函数体的定义，并降低已弃用兼容头的优先级。</summary>
        private static int CandidateScore(string[] codeLines, string fileName, int index, string token)
        {
            var score = 1;
            for (var probe = index; probe < codeLines.Length && probe - index < 8; probe++)
            {
                if (codeLines[probe].IndexOf('{') >= 0) { score = 0; break; }
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

        /// <summary>
        /// 管线无关的语义能力规则。规则只描述名称中应出现的概念词，不指定包、Include 或函数名；
        /// 匹配结果仍必须指向当前工程扫描到的真实声明，未匹配时保持 unknown。
        /// </summary>
        private static readonly CapabilityRequirement[] CapabilityRequirements = new[]
        {
            new CapabilityRequirement { capability = "surface_parameters_metallic_roughness", requiredTerms = new[] { "metallic" }, alternativeTerms = new[] { "roughness", "smoothness", "brdf" }, note = "Metallic-Roughness 或等价表面参数构建能力。" },
            new CapabilityRequirement { capability = "direct_lighting_main", requiredTerms = new[] { "light" }, alternativeTerms = new[] { "main", "primary", "directional" }, note = "主要直接光访问能力。" },
            new CapabilityRequirement { capability = "additional_lights", requiredTerms = new[] { "light" }, alternativeTerms = new[] { "additional", "extra", "punctual" }, note = "附加或局部光访问能力。" },
            new CapabilityRequirement { capability = "indirect_diffuse", requiredTerms = new[] { "indirect" }, alternativeTerms = new[] { "diffuse", "irradiance", "sphericalharmonic" }, note = "间接漫反射或辐照度采样能力。" },
            new CapabilityRequirement { capability = "indirect_specular_reflection", requiredTerms = new[] { "reflection" }, alternativeTerms = new[] { "specular", "environment", "probe", "ibl" }, note = "环境镜面反射或反射探针采样能力。" },
            new CapabilityRequirement { capability = "world_normal_transform", requiredTerms = new[] { "normal", "world" }, alternativeTerms = new[] { "transform", "convert", "object" }, note = "世界空间法线转换能力。" },
            new CapabilityRequirement { capability = "view_direction_world_space", requiredTerms = new[] { "view", "world" }, alternativeTerms = new[] { "direction", "dir", "camera" }, note = "世界空间视线方向能力。" },
            new CapabilityRequirement { capability = "vertex_position_transform", requiredTerms = new[] { "position" }, alternativeTerms = new[] { "vertex", "transform", "clip", "world" }, note = "顶点位置空间转换能力。" },
            new CapabilityRequirement { capability = "alpha_clip_discard", requiredTerms = new[] { "alpha" }, alternativeTerms = new[] { "clip", "discard", "cutout" }, note = "Alpha Clip 片元裁剪能力。" },
            new CapabilityRequirement { capability = "surface_normal_sampling", requiredTerms = new[] { "normal" }, alternativeTerms = new[] { "sample", "texture", "map" }, note = "法线贴图采样能力。" },
            new CapabilityRequirement { capability = "shadow_sampling", requiredTerms = new[] { "shadow" }, alternativeTerms = new[] { "sample", "attenuation", "visibility" }, note = "阴影或可见性采样能力。" }
        };

        private static CapabilityCatalogEntry[] BuildCapabilityCatalog(Dictionary<string, DeclarationLocation> declarations, string pipelineIdentity)
        {
            var results = new List<CapabilityCatalogEntry>();
            foreach (var requirement in CapabilityRequirements)
            {
                var match = FindCapabilityDeclaration(declarations, requirement);
                var found = !string.IsNullOrEmpty(match.Key);
                var location = match.Value;
                results.Add(new CapabilityCatalogEntry
                {
                    capability = requirement.capability,
                    status = found ? "supported" : "unknown",
                    include = found ? location.logicalPath : null,
                    function = found ? match.Key : null,
                    sourceLocation = found ? location.startLine + "-" + location.endLine : null,
                    sourceRevision = found ? location.sourceRevision : null,
                    signature = found ? location.signature : null,
                    confidence = found ? "medium" : "unknown",
                    evidence = found ? "Semantic rule matched the real declaration " + match.Key + " in " + location.logicalPath + " at line " + location.startLine + "." : "No declaration matching the pipeline-neutral semantic rule was found; the capability stays unknown.",
                    note = requirement.note,
                    pipeline = pipelineIdentity,
                    package = found ? PackageNameFromLogicalPath(location.logicalPath) : null
                });
            }
            return results.ToArray();
        }

        private static KeyValuePair<string, DeclarationLocation> FindCapabilityDeclaration(Dictionary<string, DeclarationLocation> declarations, CapabilityRequirement requirement)
        {
            foreach (var pair in declarations.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var normalized = pair.Key.ToLowerInvariant();
                if (!requirement.requiredTerms.All(normalized.Contains)) continue;
                if (requirement.alternativeTerms.Length > 0 && !requirement.alternativeTerms.Any(normalized.Contains)) continue;
                return pair;
            }
            return default(KeyValuePair<string, DeclarationLocation>);
        }

        private sealed class KnowledgeBuildBackgroundResult
        {
            public LibraryIncludeEntry[] libraryIndex;
            public Dictionary<string, DeclarationLocation> declarations;
        }

        private sealed class LibraryIncludeEntry
        {
            public string path { get; set; }
            public string sourceRevision { get; set; }
            public string kind { get; set; }
            public string pipeline { get; set; }
            public string package { get; set; }
            public string evidence { get; set; }
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
            public string confidence { get; set; }
            public string evidence { get; set; }
            public string pipeline { get; set; }
            public string package { get; set; }
        }

        private sealed class CapabilityCatalogEntry
        {
            public string capability { get; set; }
            public string status { get; set; }
            public string include { get; set; }
            public string function { get; set; }
            public string sourceLocation { get; set; }
            public string sourceRevision { get; set; }
            public string signature { get; set; }
            public string confidence { get; set; }
            public string evidence { get; set; }
            public string note { get; set; }
            public string pipeline { get; set; }
            public string package { get; set; }
        }

        private sealed class CapabilityRequirement
        {
            public string capability;
            public string[] requiredTerms;
            public string[] alternativeTerms;
            public string note;
        }

    }
}
#endif
