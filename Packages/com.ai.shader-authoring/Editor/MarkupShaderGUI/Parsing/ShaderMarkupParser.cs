using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// MarkupShaderGUI 注释标记解析器。
    /// 输入 Shader 源码，输出 <see cref="ShaderParseResult"/>；
    /// 解析过程不再持有任何静态可变状态，可安全地重复调用。
    /// </summary>
    public static class ShaderMarkupParser
    {
        /// <summary>
        /// 解析 Shader 源码 Properties 块内的全部注释标记。
        /// 无论成功与否都会返回结果对象，调用方通过 <see cref="ShaderParseResult.Success"/> 判断。
        /// </summary>
        public static ShaderParseResult Parse(string shaderCode)
        {
            var result = new ShaderParseResult();
            if (string.IsNullOrEmpty(shaderCode))
            {
                result.Success = false;
                return result;
            }

            // 统一换行符，保证切分与行号统计使用同一套规则。
            string normalizedCode = ShaderMarkupTextUtils.NormalizeLineEndings(shaderCode);

            Match propertiesBlock = ShaderMarkupConstants.PropertiesBlock.Match(normalizedCode);
            if (!propertiesBlock.Success)
            {
                result.Success = false;
                return result;
            }

            string propertiesContent = propertiesBlock.Groups[1].Value;

            // 这些 Unity 原生属性标记会与注释标记冲突。
            // 只检查 Properties 块，避免误判 CGPROGRAM 或其他代码中的普通文本。
            foreach (string attribute in ShaderMarkupConstants.UnsupportedPropertyAttributes)
            {
                if (propertiesContent.IndexOf(attribute, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.UnsupportedAttribute = attribute;
                    result.Success = false;
                    Debug.LogError(ShaderMarkupConstants.UnsupportedAttributeMessage + " 冲突标记：" + attribute);
                    return result;
                }
            }

            string[] propertyLines = ShaderMarkupTextUtils.SplitLines(propertiesContent);

            // Properties 块内容在源码中的起始行号（从 0 计），用于报错定位。
            int propertiesStartIndex = normalizedCode.IndexOf(propertiesContent, StringComparison.Ordinal);
            int lineIndex = ShaderMarkupTextUtils.CountLineBreaks(normalizedCode, propertiesStartIndex);

            ShaderGroupProperty currentGroup = null;

            foreach (string line in propertyLines)
            {
                lineIndex++;
                string trimmedLine = line.Trim();

                Match match = ShaderMarkupConstants.GroupStart.Match(trimmedLine);
                if (match.Success)
                {
                    currentGroup = new ShaderGroupProperty
                    {
                        GroupName = match.Groups[1].Value.Trim(),
                        GroupNameCN = "",
                        Label = ""
                    };
                    continue;
                }

                match = ShaderMarkupConstants.GroupEnd.Match(trimmedLine);
                if (match.Success)
                {
                    ShaderGroupProperty sealedGroup = SealGroup(currentGroup, lineIndex, result);
                    if (sealedGroup != null)
                        result.Groups.Add(sealedGroup);
                    currentGroup = null;
                    continue;
                }

                match = ShaderMarkupConstants.Label.Match(trimmedLine);
                if (match.Success)
                {
                    if (currentGroup == null)
                        result.Success = ReportOrphanMarker("Label", lineIndex);
                    else
                        currentGroup.Label = match.Groups[1].Value.Trim();
                    continue;
                }

                match = ShaderMarkupConstants.Toggle.Match(trimmedLine);
                if (match.Success)
                {
                    if (currentGroup == null)
                    {
                        result.Success = ReportOrphanMarker("Toggle", lineIndex);
                        continue;
                    }

                    ShaderToggle toggle = ParseToggle(match.Groups[1].Value.Trim(), lineIndex, result);
                    if (toggle != null)
                    {
                        if (toggle.toggleType == ToggleType.group_uniform ||
                            toggle.toggleType == ToggleType.group_keywords)
                        {
                            currentGroup.groupToggle = toggle;
                        }
                        else
                        {
                            currentGroup.toggleList.Add(toggle);
                        }
                    }
                    continue;
                }

                match = ShaderMarkupConstants.Enum.Match(trimmedLine);
                if (match.Success)
                {
                    if (currentGroup == null)
                    {
                        result.Success = ReportOrphanMarker("Enum", lineIndex);
                        continue;
                    }

                    ShaderEnum shaderEnum = ParseEnum(match.Groups[1].Value.Trim(), lineIndex, result);
                    if (shaderEnum != null)
                        currentGroup.enumList.Add(shaderEnum);
                    continue;
                }

                match = ShaderMarkupConstants.Warning.Match(trimmedLine);
                if (match.Success)
                {
                    ParseWarning(match.Groups[1].Value, result);
                    continue;
                }

                match = ShaderMarkupConstants.FeatureDescription.Match(trimmedLine);
                if (match.Success)
                {
                    result.FeatureDescription = match.Groups[1].Value.Trim();
                    continue;
                }

                // 以 // 和 # 开头，但 Flag 拼写错误。
                match = ShaderMarkupConstants.UnknownMarker.Match(trimmedLine);
                if (match.Success)
                {
                    Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！此行内容：{trimmedLine}");
                    result.Success = false;
                    continue;
                }

                // 属性行，且当前在组内。
                if (currentGroup != null &&
                    !string.IsNullOrWhiteSpace(trimmedLine) &&
                    !trimmedLine.StartsWith("//", StringComparison.Ordinal))
                {
                    if (trimmedLine.Contains(ShaderMarkupConstants.HideInInspectorAttribute))
                        continue;

                    ShaderProperty property = BuildShaderPropertyData(trimmedLine, lineIndex);
                    if (property.variableType != VariableType.Undefine)
                        currentGroup.groupShaderPropList.Add(property);
                }
            }

            return result;
        }

        /// <summary>
        /// 收尾当前组：拆出中英文名并拷贝已收集的标记。
        /// 与旧实现的差异：只有确实用 <c>_</c> 分隔出中英文两段时才接受，
        /// 否则记录错误而不是访问越界下标。
        /// </summary>
        private static ShaderGroupProperty SealGroup(
            ShaderGroupProperty currentGroup,
            int lineIndex,
            ShaderParseResult result)
        {
            if (currentGroup == null)
            {
                result.Success = ReportOrphanMarker("GroupEnd", lineIndex);
                return null;
            }

            string[] nameParts = (currentGroup.GroupName ?? string.Empty).Split('_');
            if (nameParts.Length >= 2)
            {
                currentGroup.GroupNameCN = nameParts[0].Trim();
                currentGroup.GroupName = nameParts[1].Trim();
            }
            else
            {
                // 没有中文段时退化为仅英文名，不再产生 IndexOutOfRange。
                currentGroup.GroupNameCN = string.Empty;
                currentGroup.GroupName = (currentGroup.GroupName ?? string.Empty).Trim();
                result.Success = false;
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！{ShaderMarkupConstants.InvalidGroupNameMessage}");
            }

            return currentGroup;
        }

        /// <summary>标记出现在组外，记录错误并返回 false。</summary>
        private static bool ReportOrphanMarker(string markerName, int lineIndex)
        {
            Debug.LogError($"此Shader的第：{lineIndex}行编写错误：{markerName} 标记必须写在 GroupStart / GroupEnd 之间。");
            return false;
        }

        /// <summary>
        /// 解析警告：<c>提示文本:单色名:秒数</c>，后两段可省略。
        /// 与旧实现的差异：秒数改用固定区域解析且解析失败不再抛异常。
        /// </summary>
        private static void ParseWarning(string warningString, ShaderParseResult result)
        {
            result.WarningText = warningString.Trim();
            result.WarningMono = "";
            result.WarningTimer = -1f;

            string[] parts = warningString.Split(':');
            if (parts.Length <= 1)
                return;

            result.WarningText = parts[0].Trim();
            result.WarningMono = parts[1].Trim();

            if (parts.Length > 2 &&
                ShaderMarkupTextUtils.TryParseFloat(parts[2].Trim(), out float timer))
            {
                result.WarningTimer = timer;
            }
        }

        private static ShaderToggle ParseToggle(string switchString, int lineIndex, ShaderParseResult result)
        {
            string[] parts = switchString.Split(':');
            if (parts.Length < 3)
            {
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！Toggle 标记需要 类型:显示名:变量 三段。");
                result.Success = false;
                return null;
            }

            var newToggle = new ShaderToggle();
            switch (parts[0].Trim())
            {
                case "GroupUniform":
                    newToggle.toggleType = ToggleType.group_uniform;
                    break;
                case "GroupKeyWords":
                    newToggle.toggleType = ToggleType.group_keywords;
                    break;
                case "Uniform":
                    newToggle.toggleType = ToggleType.uniform;
                    break;
                case "KeyWords":
                    newToggle.toggleType = ToggleType.keywords;
                    break;
            }

            newToggle.toggleName = parts[1].Trim();
            newToggle.variable = parts[2].Trim();
            newToggle.Index = lineIndex;
            return newToggle;
        }

        private static ShaderEnum ParseEnum(string enumString, int lineIndex, ShaderParseResult result)
        {
            string[] parts = enumString.Split(':');
            if (parts.Length < 4)
            {
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！Enum 标记需要 名称:中文名:变量:取值 四段。");
                result.Success = false;
                return null;
            }

            var newEnum = new ShaderEnum
            {
                enumName = parts[0].Trim(),
                enumNameCN = parts[1].Trim(),
                variable = parts[2].Trim(),
                Index = lineIndex
            };

            return FillEnumValues(newEnum, parts[3], lineIndex, result) ? newEnum : null;
        }

        /// <summary>解析 <c>取值A|取值B=3</c> 形式的枚举取值列表。</summary>
        private static bool FillEnumValues(
            ShaderEnum target,
            string valueSection,
            int lineIndex,
            ShaderParseResult result)
        {
            target.enumValues.Clear();
            target.enumValuesNumber.Clear();

            string[] values = valueSection.Split('|');
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];

                if (value.Contains("="))
                {
                    string[] pair = value.Split('=');
                    if (pair.Length > 1)
                    {
                        target.enumValues.Add(pair[0].Trim());
                        target.enumValuesNumber.Add(pair[1].Trim());
                    }
                    else
                    {
                        Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！枚举取值缺少 = 后面的数值。");
                        result.Success = false;
                        return false;
                    }
                }
                else
                {
                    target.enumValues.Add(value.Trim());
                    target.enumValuesNumber.Add(i.ToString());
                }
            }

            return true;
        }

        private static ShaderProperty BuildShaderPropertyData(string propertyLine, int lineIndex)
        {
            var property = new ShaderProperty();
            bool isNativeAttributed = false;

            if (propertyLine.Contains("[Toggle") ||
                propertyLine.Contains("[Enum") ||
                propertyLine.Contains("[KeywordEnum"))
            {
                isNativeAttributed = true;
                propertyLine = ShaderMarkupConstants.NativePropertyAttribute.Replace(propertyLine, "");
            }

            MatchCollection matches = ShaderMarkupConstants.ShaderProperty.Matches(propertyLine);
            foreach (Match match in matches)
            {
                property.variable = "_" + match.Groups[1].Value;
                property.variableName = match.Groups[2].Value;
                property.Index = lineIndex;

                string typeText = isNativeAttributed
                    ? string.Empty
                    : match.Groups[3].Value.Trim().ToLowerInvariant();

                switch (typeText)
                {
                    case "int":
                        property.variableType = VariableType.Int;
                        break;
                    case "float":
                        property.variableType = VariableType.Float;
                        break;
                    case "range":
                        property.variableType = VariableType.Range;
                        break;
                    case "2d":
                        property.variableType = VariableType.Texture2D;
                        // 带 [NoScaleOffset] 表示不需要 Tiling / Offset。
                        property.isFlag = !propertyLine.Contains(ShaderMarkupConstants.NoScaleOffsetAttribute);
                        break;
                    case "color":
                        property.variableType = VariableType.Color;
                        break;
                    case "cube":
                        property.variableType = VariableType.Cube;
                        break;
                    case "vector":
                        property.variableType = VariableType.Vector;
                        break;
                    default:
                        property.variableType = VariableType.Default;
                        break;
                }

                if (typeText.Contains("range"))
                    property.variableType = VariableType.Range;
            }

            return property;
        }
    }
}
