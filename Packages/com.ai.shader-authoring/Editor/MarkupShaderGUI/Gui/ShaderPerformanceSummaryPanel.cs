using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 读取由 Unity MCP 性能任务写入的只读摘要，并在材质 Inspector 中显示。
    /// 此类不依赖 UnityMcp.Editor 程序集，避免两个独立 Editor asmdef 之间形成反向依赖。
    /// </summary>
    internal static class ShaderPerformanceSummaryPanel
    {
        private const string SummaryRoot = "Artifacts/ShaderPerformance/";
        private static readonly Regex StringField = new Regex("\\\"(?<name>[^\\\"]+)\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Compiled);
        private static readonly Regex NumberField = new Regex("\\\"(?<name>[^\\\"]+)\\\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled);

        private static string cachedPath;
        private static string cachedRevision;
        private static Summary cachedSummary;

        public static void Draw(Shader shader)
        {
            if (shader == null)
                return;

            Summary summary = GetSummary(shader);
            if (summary == null)
            {
                EditorGUILayout.HelpBox("尚未分析当前 Shader revision 的性能。性能分析不会阻断视觉验收。", MessageType.None);
                EditorGUILayout.Space(4f);
                return;
            }

            MessageType type = summary.rating == "高风险" ? MessageType.Error :
                summary.rating == "预警" ? MessageType.Warning : MessageType.Info;
            string headline = "<b>Shader 性能评价：" + summary.rating + "</b>  " + summary.policy;
            string details = "修订: " + ShortRevision(summary.revision) + "\n" +
                "片元估计: " + summary.fragmentCost + " | 顶点估计: " + summary.vertexCost +
                " | 纹理采样: " + summary.textureSamples + " | 分支: " + summary.branches +
                "\nMali: " + summary.maliStatus + " | GLES 变体: " + summary.variantCount + "\n" + summary.message;
            EditorGUILayout.HelpBox(headline + "\n" + details, type);
            if (summary.maliDetails.Length > 0)
            {
                summary.expanded = EditorGUILayout.Foldout(summary.expanded, "Mali 变体详情", true);
                if (summary.expanded)
                {
                    foreach (string detail in summary.maliDetails)
                        EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.Space(4f);
        }

        private static Summary GetSummary(Shader shader)
        {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return null;

            string revision = Hash(File.ReadAllText(shaderPath));
            if (cachedSummary != null && cachedPath == shaderPath && cachedRevision == revision)
                return cachedSummary;

            cachedPath = shaderPath;
            cachedRevision = revision;
            string fileName = SafeName(shaderPath) + "-" + revision + ".json";
            string summaryPath = SummaryRoot + fileName;
            cachedSummary = File.Exists(summaryPath) ? ParseSummary(File.ReadAllText(summaryPath), revision) : null;
            return cachedSummary;
        }

        private static Summary ParseSummary(string json, string expectedRevision)
        {
            string revision = ReadString(json, "shaderRevision");
            if (!string.Equals(revision, expectedRevision, StringComparison.Ordinal))
                return null;

            return new Summary
            {
                revision = revision,
                rating = ReadString(json, "rating") ?? "未评级",
                policy = ReadString(json, "policyLabel") ?? "未配置阈值",
                maliStatus = ReadString(json, "maliStatus") ?? "未分析",
                message = ReadString(json, "message") ?? "未提供性能建议。",
                fragmentCost = ReadNumber(json, "fragmentEstimatedCost"),
                vertexCost = ReadNumber(json, "vertexEstimatedCost"),
                textureSamples = ReadNumber(json, "textureSampleCount"),
                branches = ReadNumber(json, "branchCount"),
                variantCount = ReadNumber(json, "compiledGlesVariantCount"),
                maliDetails = ReadMaliDetails(json)
            };
        }

        private static string ReadString(string json, string name)
        {
            foreach (Match match in StringField.Matches(json))
            {
                if (string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
                    return Regex.Unescape(match.Groups["value"].Value);
            }
            return null;
        }

        private static string ReadNumber(string json, string name)
        {
            foreach (Match match in NumberField.Matches(json))
            {
                if (string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
                    return match.Groups["value"].Value;
            }
            return "-";
        }

        private static string[] ReadMaliDetails(string json)
        {
            MatchCollection matches = Regex.Matches(json, "\\\"variant\\\"\\s*:\\s*\\\"(?<variant>(?:\\\\.|[^\\\"])*)\\\"[\\s\\S]*?\\\"target\\\"\\s*:\\s*\\\"(?<target>(?:\\\\.|[^\\\"])*)\\\"[\\s\\S]*?\\\"stage\\\"\\s*:\\s*\\\"(?<stage>(?:\\\\.|[^\\\"])*)\\\"[\\s\\S]*?\\\"status\\\"\\s*:\\s*\\\"(?<status>(?:\\\\.|[^\\\"])*)\\\"[\\s\\S]*?\\\"workRegisters\\\"\\s*:\\s*(?<registers>\\d+)[\\s\\S]*?\\\"stackSpilling\\\"\\s*:\\s*(?<spilling>true|false)[\\s\\S]*?\\\"longestPathWorstCycles\\\"\\s*:\\s*(?<cycles>\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var details = new System.Collections.Generic.List<string>();
            foreach (Match match in matches)
            {
                string spill = string.Equals(match.Groups["spilling"].Value, "true", StringComparison.OrdinalIgnoreCase) ? "栈溢出" : "无栈溢出";
                details.Add(Regex.Unescape(match.Groups["variant"].Value) + " · " +
                    Regex.Unescape(match.Groups["target"].Value) + " · " +
                    Regex.Unescape(match.Groups["stage"].Value) + " · " +
                    Regex.Unescape(match.Groups["status"].Value) +
                    " · 工作寄存器 " + match.Groups["registers"].Value +
                    " · 最长路径 " + match.Groups["cycles"].Value +
                    " · " + spill);
            }
            return details.ToArray();
        }

        private static string ShortRevision(string revision) => string.IsNullOrEmpty(revision) ? "-" : revision.Substring(0, Math.Min(12, revision.Length));
        private static string SafeName(string path) => path.Replace('/', '_').Replace('\\', '_').Replace(':', '_');

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private sealed class Summary
        {
            public string revision;
            public string rating;
            public string policy;
            public string maliStatus;
            public string message;
            public string fragmentCost;
            public string vertexCost;
            public string textureSamples;
            public string branches;
            public string variantCount;
            public string[] maliDetails;
            public bool expanded;
        }
    }
}
