#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>
    /// Exports Unity's own GLES3 compiled Shader text and extracts vertex/fragment GLSL stages.
    /// This deliberately uses Unity's internal compiled-shader export path; it never treats
    /// ShaderLab or HLSL source as Mali input.
    /// </summary>
    internal static class CompiledGlesVariantExporter
    {
        private const string CompiledShaderCacheRelativePath = "Temp";

        internal static object Export(JsonElement args, CancellationToken cancellationToken)
        {
            string shaderPath = RequireShaderPath(args);
            var result = ExportVariants(shaderPath, cancellationToken);
            return new
            {
                status = result.status,
                shaderPath,
                unityPlatform = "GLES3x",
                compiledArtifactPath = result.compiledArtifactPath,
                diagnostics = result.diagnostics.ToArray(),
                compiledGlesVariants = result.variants.Select(item => new
                {
                    item.name,
                    item.keywords,
                    item.vertexGlsl,
                    item.fragmentGlsl
                }).ToArray()
            };
        }

        internal static ExportResult ExportVariants(string shaderPath, CancellationToken cancellationToken)
        {
            var result = new ExportResult();
            if (string.IsNullOrWhiteSpace(shaderPath) || !shaderPath.StartsWith("Assets/", StringComparison.Ordinal) || !shaderPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
            {
                result.status = "failed";
                result.diagnostics.Add("shaderPath must be an existing Assets/*.shader path.");
                return result;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                result.status = "failed";
                result.diagnostics.Add("Unity could not load the Shader asset before GLES export.");
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string cachePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CompiledShaderCacheRelativePath));
            if (!Directory.Exists(cachePath))
            {
                result.status = "failed";
                result.diagnostics.Add("Unity compiled Shader cache does not exist: " + cachePath);
                return result;
            }

            DateTime exportStartedUtc = DateTime.UtcNow;
            try
            {
                int glesMask = ResolveGles3PlatformMask();
                InvokeOpenCompiledShader(shader, glesMask);
            }
            catch (Exception exception)
            {
                result.status = "failed";
                result.diagnostics.Add("Unity compiled Shader export failed: " + exception.Message);
                return result;
            }

            // OpenCompiledShader writes into Temp asynchronously on some Editor versions. Poll briefly
            // rather than accepting an older file as proof of a fresh GLES compilation.
            string expectedName = "Compiled-" + SafeName(shader.name) + ".shader";
            string candidate = null;
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadlineUtc && candidate == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidate = FindFreshCompiledArtifact(cachePath, expectedName, shader.name, exportStartedUtc);
                if (candidate == null) Thread.Sleep(100);
            }
            if (candidate == null)
            {
                result.status = "failed";
                result.diagnostics.Add("Unity did not produce a fresh compiled Shader artifact in Temp.");
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string compiledText;
            try
            {
                compiledText = File.ReadAllText(candidate);
            }
            catch (Exception exception)
            {
                result.status = "failed";
                result.diagnostics.Add("Could not read Unity compiled Shader artifact: " + exception.Message);
                return result;
            }

            result.compiledArtifactPath = candidate.Replace('\\', '/');
            result.variants.AddRange(ParseGlesVariants(compiledText, result.diagnostics));
            result.status = result.variants.Count > 0 ? "completed" : "failed";
            if (result.variants.Count == 0)
                result.diagnostics.Add("The exported Unity artifact contained no valid GLES vertex/fragment GLSL pair. No Mali analysis will be attempted.");
            return result;
        }

        private static string FindFreshCompiledArtifact(string cachePath, string expectedName, string shaderName, DateTime exportStartedUtc)
        {
            return Directory.GetFiles(cachePath, "*.shader", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).IndexOf(SafeName(shaderName), StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(path => File.GetLastWriteTimeUtc(path) >= exportStartedUtc.AddSeconds(-2))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static int ResolveGles3PlatformMask()
        {
            Type platformType = Type.GetType("UnityEditor.Rendering.ShaderCompilerPlatform, UnityEditor")
                ?? Type.GetType("UnityEditor.ShaderCompilerPlatform, UnityEditor");
            if (platformType == null || !platformType.IsEnum) throw new InvalidOperationException("Unity does not expose ShaderCompilerPlatform for GLES export.");
            string name = Enum.GetNames(platformType).FirstOrDefault(item => string.Equals(item, "GLES3x", StringComparison.OrdinalIgnoreCase))
                ?? Enum.GetNames(platformType).FirstOrDefault(item => item.IndexOf("GLES3", StringComparison.OrdinalIgnoreCase) >= 0);
            if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Unity does not expose a GLES3x ShaderCompilerPlatform.");
            int platformValue = Convert.ToInt32(Enum.Parse(platformType, name));
            if (platformValue < 0) throw new InvalidOperationException("Unity returned an invalid GLES platform value: " + platformValue);
            // ShaderInspectorPlatformsPopup.currentPlatformMask stores platform bits, while the enum
            // already exposes bit flags on other Unity versions. Preserve either representation.
            return platformValue > 0 && (platformValue & (platformValue - 1)) == 0 ? platformValue : 1 << platformValue;
        }

        private static void InvokeOpenCompiledShader(Shader shader, int glesMask)
        {
            Type shaderUtilType = Type.GetType("UnityEditor.ShaderUtil, UnityEditor");
            MethodInfo method = shaderUtilType?.GetMethod("OpenCompiledShader", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Shader), typeof(int), typeof(int), typeof(bool) }, null);
            if (method == null) throw new MissingMethodException("UnityEditor.ShaderUtil.OpenCompiledShader(Shader,int,int,bool)");
            // mode 0 is Unity's compiled-shader export mode. The platform mask is explicitly GLES3x.
            method.Invoke(null, new object[] { shader, 0, glesMask, true });
        }

        private static IEnumerable<CompiledGlesVariant> ParseGlesVariants(string compiledText, List<string> diagnostics)
        {
            var variants = new List<CompiledGlesVariant>();
            int cursor = 0;
            while (cursor < compiledText.Length)
            {
                int marker = compiledText.IndexOf("SubProgram \"gles", cursor, StringComparison.OrdinalIgnoreCase);
                if (marker < 0) break;
                int openBrace = compiledText.IndexOf('{', marker);
                if (openBrace < 0) break;
                int closeBrace = FindMatchingBrace(compiledText, openBrace);
                if (closeBrace < 0) break;
                string block = compiledText.Substring(marker, closeBrace - marker + 1);
                cursor = closeBrace + 1;

                string decoded = DecodeQuotedLines(block);
                string vertex = ExtractConditionalProgram(decoded, "VERTEX") ?? ExtractProgram(block, "vp");
                string fragment = ExtractConditionalProgram(decoded, "FRAGMENT") ?? ExtractProgram(block, "fp");
                if (!IsValidGlsl(vertex) && !IsValidGlsl(fragment)) continue;
                variants.Add(new CompiledGlesVariant
                {
                    name = "gles3-" + variants.Count,
                    keywords = ExtractKeywords(block),
                    vertexGlsl = IsValidGlsl(vertex) ? vertex : null,
                    fragmentGlsl = IsValidGlsl(fragment) ? fragment : null
                });
            }
            if (variants.Count == 0 && compiledText.IndexOf("gles", StringComparison.OrdinalIgnoreCase) < 0)
                diagnostics.Add("Unity compiled artifact does not contain a GLES subprogram; verify GLES3x is supported by this Shader and Editor.");
            return variants;
        }

        private static string ExtractConditionalProgram(string decodedBlock, string define)
        {
            if (string.IsNullOrWhiteSpace(decodedBlock)) return null;
            string marker = "#ifdef " + define;
            int start = decodedBlock.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start = decodedBlock.IndexOf('\n', start);
            if (start < 0) return null;
            int end = decodedBlock.IndexOf("#endif", start, StringComparison.Ordinal);
            if (end < 0) return null;
            string body = decodedBlock.Substring(start + 1, end - start - 1);
            int version = decodedBlock.LastIndexOf("#version", start, StringComparison.Ordinal);
            if (version >= 0 && decodedBlock.IndexOf('\n', version) < start)
            {
                int versionEnd = decodedBlock.IndexOf('\n', version);
                return decodedBlock.Substring(version, versionEnd - version) + "\n" + body;
            }
            return body;
        }

        private static string ExtractProgram(string block, string stage)
        {
            int marker = block.IndexOf("Program \"" + stage + "\"", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return null;
            int openBrace = block.IndexOf('{', marker);
            if (openBrace < 0) return null;
            int closeBrace = FindMatchingBrace(block, openBrace);
            if (closeBrace < 0) return null;
            return DecodeQuotedLines(block.Substring(openBrace + 1, closeBrace - openBrace - 1));
        }

        private static string[] ExtractKeywords(string block)
        {
            int marker = block.IndexOf("Keywords", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return Array.Empty<string>();
            int openBrace = block.IndexOf('{', marker);
            if (openBrace < 0) return Array.Empty<string>();
            int closeBrace = FindMatchingBrace(block, openBrace);
            if (closeBrace < 0) return Array.Empty<string>();
            string keywordText = block.Substring(openBrace + 1, closeBrace - openBrace - 1);
            return keywordText.Split(new[] { ' ', '\t', '\r', '\n', '"' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(item => item != "Keywords")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string DecodeQuotedLines(string text)
        {
            var builder = new StringBuilder();
            int index = 0;
            while (index < text.Length)
            {
                int start = text.IndexOf('"', index);
                if (start < 0) break;
                int end = start + 1;
                bool escaped = false;
                while (end < text.Length)
                {
                    char character = text[end];
                    if (character == '"' && !escaped) break;
                    escaped = character == '\\' && !escaped;
                    if (character != '\\') escaped = false;
                    end++;
                }
                if (end >= text.Length) break;
                builder.Append(UnescapeUnityString(text.Substring(start + 1, end - start - 1)));
                builder.Append('\n');
                index = end + 1;
            }
            return builder.ToString();
        }

        private static string UnescapeUnityString(string value)
        {
            return value.Replace("\\\\", "\\").Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
        }

        private static bool IsValidGlsl(string source)
        {
            return !string.IsNullOrWhiteSpace(source) && source.IndexOf("void main", StringComparison.Ordinal) >= 0 && source.IndexOf("#version", StringComparison.Ordinal) >= 0;
        }

        private static int FindMatchingBrace(string text, int openBrace)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = openBrace; index < text.Length; index++)
            {
                char character = text[index];
                if (inString)
                {
                    if (character == '"' && !escaped) inString = false;
                    escaped = character == '\\' && !escaped;
                    if (character != '\\') escaped = false;
                    continue;
                }
                if (character == '"') { inString = true; continue; }
                if (character == '{') depth++;
                else if (character == '}' && --depth == 0) return index;
            }
            return -1;
        }

        private static string RequireShaderPath(JsonElement args)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("shaderPath", out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())) return element.GetString();
            throw new ArgumentException("shaderPath is required.");
        }

        private static string SafeName(string value)
        {
            return string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '-'));
        }

        internal sealed class ExportResult
        {
            internal string status = "failed";
            internal string compiledArtifactPath;
            internal readonly List<string> diagnostics = new List<string>();
            internal readonly List<CompiledGlesVariant> variants = new List<CompiledGlesVariant>();
        }

        internal sealed class CompiledGlesVariant
        {
            internal string name;
            internal string[] keywords;
            internal string vertexGlsl;
            internal string fragmentGlsl;
        }
    }
}
#endif
