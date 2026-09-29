#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityMcp.Editor
{
    /// <summary>
    /// 保守的 Shader 词法结构提取器。它只报告可验证的文本结构，绝不把结果宣称为 HLSL 语义证明。
    /// </summary>
    internal static class UnityMcpShaderStructureAnalysis
    {
        internal sealed class Structure
        {
            public string shaderName;
            public List<Item> properties = new List<Item>();
            public List<Item> passes = new List<Item>();
            public List<Item> includes = new List<Item>();
            public List<Item> keywords = new List<Item>();
            public List<Item> entries = new List<Item>();
            public List<Item> renderStates = new List<Item>();
            public List<string> diagnostics = new List<string>();
        }

        internal sealed class Item
        {
            public string kind;
            public string key;
            public string value;
            public int startLine;
            public int endLine;
        }

        internal static Structure Parse(string source)
        {
            var result = new Structure();
            var clean = StripComments(source ?? string.Empty, result.diagnostics);
            var lines = clean.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            result.shaderName = MatchValue(lines, "^\\s*Shader\\s+\\\"([^\\\"]+)\\\"");
            ParseProperties(lines, result);
            ParsePasses(lines, result);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                AddRegex(result.includes, line, index, "include", "^\\s*#include\\s+[\\\"<]([^\\\">]+)[\\\">]", 1);
                AddRegex(result.keywords, line, index, "keyword", @"^\s*#pragma\s+(?:shader_feature(?:_local)?|multi_compile(?:_local)?)\s+(.+)$", 1);
                AddRegex(result.entries, line, index, "entry", @"^\s*#pragma\s+(vertex|fragment|hull|domain|geometry)\s+([^\s]+)", 1, 2);
                AddRegex(result.renderStates, line, index, "render_state", @"^\s*(Blend|ZWrite|ZTest|Cull|ColorMask|Offset|AlphaToMask|Queue|RenderType)\b\s*(.*)$", 1, 2);
            }
            return result;
        }

        internal static object Compare(Structure reference, Structure target, IEnumerable<string> approvedDifferenceKeys)
        {
            var approved = new HashSet<string>(approvedDifferenceKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var differences = new List<object>();
            CompareCategory("properties", reference.properties, target.properties, approved, differences);
            CompareCategory("passes", reference.passes, target.passes, approved, differences);
            CompareCategory("includes", reference.includes, target.includes, approved, differences);
            CompareCategory("keywords", reference.keywords, target.keywords, approved, differences);
            CompareCategory("entries", reference.entries, target.entries, approved, differences);
            CompareCategory("renderStates", reference.renderStates, target.renderStates, approved, differences);
            var unapproved = differences.Where(value => !(bool)value.GetType().GetProperty("approved").GetValue(value, null)).ToArray();
            return new
            {
                status = unapproved.Length == 0 ? "aligned" : "unapproved_differences",
                comparisonScope = "lexical_structure_only; texture sampling, macro branches and SurfaceData wiring require project-directed tests or human review",
                differences = differences.ToArray(),
                unapprovedDifferenceCount = unapproved.Length
            };
        }

        internal static object Serialize(Structure value)
        {
            return new
            {
                shaderName = value.shaderName,
                properties = value.properties.Select(SerializeItem).ToArray(),
                passes = value.passes.Select(SerializeItem).ToArray(),
                includes = value.includes.Select(SerializeItem).ToArray(),
                keywords = value.keywords.Select(SerializeItem).ToArray(),
                programEntries = value.entries.Select(SerializeItem).ToArray(),
                renderStates = value.renderStates.Select(SerializeItem).ToArray(),
                parseDiagnostics = value.diagnostics.Select(message => new { severity = "warning", message }).ToArray()
            };
        }

        private static object SerializeItem(Item value) => new
        {
            kind = value.kind,
            key = value.key,
            value = value.value,
            sourceLocation = new { startLine = value.startLine, endLine = value.endLine }
        };

        private static void CompareCategory(string category, IEnumerable<Item> reference, IEnumerable<Item> target, HashSet<string> approved, List<object> output)
        {
            var left = reference.GroupBy(item => item.key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => string.Join("|", group.Select(item => item.value).OrderBy(value => value, StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);
            var right = target.GroupBy(item => item.key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => string.Join("|", group.Select(item => item.value).OrderBy(value => value, StringComparer.Ordinal).ToArray()), StringComparer.Ordinal);
            foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
            {
                left.TryGetValue(key, out var before);
                right.TryGetValue(key, out var after);
                if (string.Equals(before, after, StringComparison.Ordinal)) continue;
                var differenceKey = category + ":" + key;
                output.Add(new { category, key, differenceKey, reference = before, target = after, approved = approved.Contains(differenceKey) });
            }
        }

        private static void ParseProperties(string[] lines, Structure result)
        {
            var start = FindBlock(lines, "Properties", 0, out var end);
            if (start < 0) return;
            // Properties blocks are commonly formatted over multiple lines but valid ShaderLab also allows
            // a declaration beside the opening/closing brace, so scan every lexical line in the block.
            var property = new Regex("([_A-Za-z][_A-Za-z0-9]*)\\s*\\(\\s*\"([^\"]*)\"\\s*,\\s*([^\\)]*)\\)\\s*=\\s*([^\\r\\n}]+)", RegexOptions.CultureInvariant);
            for (var index = start; index <= end; index++)
            foreach (Match match in property.Matches(lines[index]))
            {
                var key = match.Groups[1].Value;
                var value = "display=" + match.Groups[2].Value.Trim() + "; type=" + match.Groups[3].Value.Trim() + "; default=" + match.Groups[4].Value.Trim();
                result.properties.Add(new Item { kind = "property", key = key, value = value, startLine = index + 1, endLine = index + 1 });
            }
        }

        private static void ParsePasses(string[] lines, Structure result)
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (!Regex.IsMatch(lines[index], "^\\s*Pass\\b", RegexOptions.CultureInvariant)) continue;
                var end = FindBlockEnd(lines, index);
                var name = MatchValue(lines.Skip(index).Take(Math.Max(1, end - index + 1)).ToArray(), "^\\s*Name\\s+\"([^\"]+)\"") ?? "pass@" + (index + 1);
                var tags = lines.Skip(index).Take(Math.Max(1, end - index + 1))
                    .Select(line => Regex.Match(line, "\"([^\"]+)\"\\s*=\\s*\"([^\"]+)\""))
                    .Where(match => match.Success)
                    .Select(match => match.Groups[1].Value + "=" + match.Groups[2].Value)
                    .OrderBy(value => value, StringComparer.Ordinal);
                result.passes.Add(new Item { kind = "pass", key = name, value = string.Join(";", tags.ToArray()), startLine = index + 1, endLine = end + 1 });
            }
        }

        private static int FindBlock(string[] lines, string marker, int startAt, out int end)
        {
            end = -1;
            for (var index = startAt; index < lines.Length; index++)
            {
                if (lines[index].IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) continue;
                end = FindBlockEnd(lines, index);
                return index;
            }
            return -1;
        }

        private static int FindBlockEnd(string[] lines, int start)
        {
            var depth = 0;
            var opened = false;
            for (var index = start; index < lines.Length; index++)
            {
                foreach (var character in lines[index])
                {
                    if (character == '{') { depth++; opened = true; }
                    else if (character == '}' && opened && --depth == 0) return index;
                }
            }
            return Math.Max(start, lines.Length - 1);
        }

        private static void AddRegex(List<Item> output, string line, int index, string kind, string pattern, int keyGroup, int valueGroup = -1)
        {
            var match = Regex.Match(line, pattern, RegexOptions.CultureInvariant);
            if (!match.Success) return;
            var key = match.Groups[keyGroup].Value.Trim();
            var value = valueGroup > 0 ? match.Groups[valueGroup].Value.Trim() : key;
            output.Add(new Item { kind = kind, key = key, value = value, startLine = index + 1, endLine = index + 1 });
        }

        private static string MatchValue(string[] lines, string pattern)
        {
            foreach (var line in lines)
            {
                var match = Regex.Match(line, pattern, RegexOptions.CultureInvariant);
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        private static string StripComments(string source, List<string> diagnostics)
        {
            var output = new StringBuilder(source.Length);
            var inLine = false;
            var inBlock = false;
            var inString = false;
            for (var index = 0; index < source.Length; index++)
            {
                var current = source[index];
                var next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (inLine)
                {
                    if (current == '\n') { inLine = false; output.Append(current); }
                    else output.Append(' ');
                    continue;
                }
                if (inBlock)
                {
                    if (current == '*' && next == '/') { output.Append("  "); index++; inBlock = false; }
                    else output.Append(current == '\n' ? '\n' : ' ');
                    continue;
                }
                if (!inString && current == '/' && next == '/') { output.Append("  "); index++; inLine = true; continue; }
                if (!inString && current == '/' && next == '*') { output.Append("  "); index++; inBlock = true; continue; }
                if (current == '\"' && (index == 0 || source[index - 1] != '\\')) inString = !inString;
                output.Append(current);
            }
            if (inBlock) diagnostics.Add("Unterminated block comment; extraction continued with the available source.");
            return output.ToString();
        }
    }
}
#endif
