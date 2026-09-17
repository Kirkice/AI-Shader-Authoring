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
        private static long cachedSummaryWriteTicks;
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("性能评价", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(summary.rating, RatingStyle(summary.rating), GUILayout.Width(66f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(summary.policy, EditorStyles.miniLabel);
            EditorGUILayout.Space(3f);
            DrawMetricRow("片元估计", summary.fragmentCost, "顶点估计", summary.vertexCost);
            DrawMetricRow("纹理采样", summary.textureSamples, "分支", summary.branches);
            DrawMetricRow("GLES 变体", summary.variantCount, "Mali", summary.maliStatus);
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(summary.message, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);

            string buttonText = summary.expanded ? "收起详细数据" : "查看详细数据";
            if (GUILayout.Button(buttonText, EditorStyles.miniButton))
                summary.expanded = !summary.expanded;

            if (summary.expanded)
                DrawDetails(summary);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private static void DrawMetricRow(string leftLabel, string leftValue, string rightLabel, string rightValue)
        {
            EditorGUILayout.BeginHorizontal();
            DrawMetric(leftLabel, leftValue);
            GUILayout.Space(8f);
            DrawMetric(rightLabel, rightValue);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMetric(string label, string value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(62f));
            EditorGUILayout.LabelField(value, EditorStyles.miniBoldLabel, GUILayout.MinWidth(72f));
        }

        private static GUIStyle RatingStyle(string rating)
        {
            Color color = rating == "高风险" ? new Color(0.95f, 0.35f, 0.32f) :
                rating == "预警" ? new Color(0.96f, 0.70f, 0.22f) :
                new Color(0.35f, 0.78f, 0.48f);
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            style.normal.textColor = color;
            return style;
        }

        private static void DrawDetails(Summary summary)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            EditorGUILayout.LabelField("详细性能数据", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Shader 修订：" + ShortRevision(summary.revision), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Mali 状态：" + summary.maliStatus, EditorStyles.miniLabel);
            if (summary.maliDetails.Length == 0)
            {
                EditorGUILayout.HelpBox("当前没有可展示的 Mali 变体结果。可先导出 GLES 编译变体并配置 Mali Offline Compiler。", MessageType.None);
            }
            else
            {
                foreach (string detail in summary.maliDetails)
                    EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private static Summary GetSummary(Shader shader)
        {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return null;

            string revision = Hash(File.ReadAllText(shaderPath));
            string fileName = SafeName(shaderPath) + "-" + revision + ".json";
            string summaryPath = SummaryRoot + fileName;
            long summaryWriteTicks = File.Exists(summaryPath) ? File.GetLastWriteTimeUtc(summaryPath).Ticks : 0L;
            if (cachedPath == shaderPath && cachedRevision == revision && cachedSummaryWriteTicks == summaryWriteTicks)
                return cachedSummary;

            cachedPath = shaderPath;
            cachedRevision = revision;
            cachedSummaryWriteTicks = summaryWriteTicks;
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
