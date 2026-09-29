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

    }
}
#endif
