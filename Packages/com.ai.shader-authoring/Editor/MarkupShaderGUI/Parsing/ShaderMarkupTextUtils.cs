using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 注释标记解析过程中使用的纯文本工具。
    /// 全部方法都不依赖 Unity 运行时，可被解析层与 GUI 层共用。
    /// </summary>
    public static class ShaderMarkupTextUtils
    {
        private static readonly Regex NumberPattern =
            new Regex(@"[-+]?\d*\.?\d+", RegexOptions.Compiled);

        /// <summary>
        /// 统一换行符为 <c>\n</c>。
        /// 旧实现按 <c>\n</c> 切分却按 <c>Environment.NewLine</c> 计行号，
        /// 在 CRLF 的 Shader 文件上会得到错位的行号。
        /// </summary>
        public static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>按 <c>\n</c> 切分文本（调用前应先经 <see cref="NormalizeLineEndings"/>）。</summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            return text.Split('\n');
        }

        /// <summary>统计 <paramref name="endIndex"/> 之前的换行数量，即该位置所处的行偏移。</summary>
        public static int CountLineBreaks(string text, int endIndex)
        {
            if (string.IsNullOrEmpty(text) || endIndex <= 0)
                return 0;

            int limit = Math.Min(endIndex, text.Length);
            int count = 0;
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                    count++;
            }

            return count;
        }

        /// <summary>以固定区域（InvariantCulture）解析浮点，避免系统区域设置影响小数点解析。</summary>
        public static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>提取字符串中的全部数字。</summary>
        public static float[] ExtractNumbers(string text)
        {
            var result = new List<float>();
            if (string.IsNullOrEmpty(text))
                return result.ToArray();

            foreach (Match match in NumberPattern.Matches(text))
            {
                if (TryParseFloat(match.Value, out float number))
                    result.Add(number);
            }

            return result.ToArray();
        }
    }
}
