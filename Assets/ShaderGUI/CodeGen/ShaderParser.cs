using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ShaderGUIGenerator
{
    public class ShaderParser
    {
        public static bool ParseShaderProperties(
            string shaderCode,
            ref List<ShaderGroupProperty> groupProperties,
            ref ShaderBlend globalBlend,
            out string unsupportedAttribute)
        {
            bool isParseSuccess = true;
            unsupportedAttribute = "";
            groupProperties.Clear();

            // 解析每个 Shader 前重置跨次生成的静态状态，避免沿用上一个 Shader 的标记。
            Constants.FeatureDescribeText = "";
            Constants.WarningText = "";
            Constants.WarningMono = "";
            Constants.WarningTimer = -1.0f;
            
            var propertiesBlockRegex = new Regex(@"Properties\s*\{([^{}]*(?:\{(?:[^{}]*)\}[^{}]*)*)\}", RegexOptions.Singleline);
            Match propertiesBlock = propertiesBlockRegex.Match(shaderCode);

            if (!propertiesBlock.Success)
            {
                isParseSuccess = false;
                return isParseSuccess;
            }
    
            
            string propertiesContent = propertiesBlock.Groups[1].Value;

            // 这些 Unity 原生属性标记会与 ShaderGUI CodeGen 的自定义注释标记冲突。
            // 只检查 Properties 块，避免误判 CGPROGRAM 或其他代码中的普通文本。
            string[] unsupportedAttributes =
            {
                "[Toggle]",
                "[KeywordEnum",
                "[ToggleOff]"
            };

            foreach (string attribute in unsupportedAttributes)
            {
                if (propertiesContent.IndexOf(attribute, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    unsupportedAttribute = attribute;
                    return false;
                }
            }

            string[] propertiesline = propertiesContent.Split(new[] { "\n" }, StringSplitOptions.None);

            //  检测默认系统贴图
            Constants.drawOcclusionLutTextureGUI = propertiesContent.ToLower().Contains("specularocclusionlut3d");
            Constants.drawBRDFLutTextureGUI = propertiesContent.ToLower().Contains("dfgtexture");
            Constants.drawACESTextureGUI = propertiesContent.ToLower().Contains("acesluttex");
            Constants.TextureSplitCount = 0;
            // 计算Properties块在原始shaderCode中的起始行号
            int propertiesStartIndex = shaderCode.IndexOf(propertiesContent);
            int lineIndex = shaderCode.Substring(0, propertiesStartIndex)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length - 1;

            Constants.lineStart = lineIndex;

            ShaderGroupProperty currentGroup = null;
            foreach (string line in propertiesline)
            {
                lineIndex++;
                string trimmedLine = line.Trim();
                
                // 检测组开始
                var groupStartMatch = Regex.Match(trimmedLine, @"//\s*#\s*GroupStart:(.+)");
                if (groupStartMatch.Success)
                {
                    string startGroupName = groupStartMatch.Groups[1].Value.Trim();
                    currentGroup = new ShaderGroupProperty
                    {
                        GroupName = startGroupName,
                        GroupNameCN = "",
                        Label = "",
                        groupShaderPropList = new List<ShaderProperty>(),
                        toggleList = new List<ShaderToggle>(),
                        enumList = new List<ShaderEnum>(),
                        vectorList = new List<ShaderVectorSplit>()
                    };
                    continue;
                }
                
                // 检测组结束
                var groupEndMatch = Regex.Match(trimmedLine, @"//\s*#\s*GroupEnd(.*)");
                if (groupEndMatch.Success)
                {
                    var newGroup = ProcessGroup(currentGroup, lineIndex, ref isParseSuccess);
                    groupProperties.Add(newGroup);
                    currentGroup = null;
                    continue;
                }
                
                // 检测Label
                var groupLabelMatch = Regex.Match(trimmedLine, @"//\s*#\s*Label:(.+)");
                if (groupLabelMatch.Success && currentGroup != null)
                {
                    currentGroup.Label = groupLabelMatch.Groups[1].Value.Trim();
                    continue;
                }
                
                // 检测按钮
                var groupSwitchMatch = Regex.Match(trimmedLine, @"//\s*#\s*Toggle:(.+)");
                if (groupSwitchMatch.Success && currentGroup != null)
                {
                    var toggle = ParseToggle(groupSwitchMatch.Groups[1].Value.Trim(), lineIndex, ref isParseSuccess);
                    if (toggle != null)
                    {
                        if (toggle.toggleType == ToggleType.group_uniform || toggle.toggleType == ToggleType.group_keywords)
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
                
                //  检测枚举
                var enumMatch = Regex.Match(trimmedLine, @"//\s*#\s*Enum_(.+)");
                if (enumMatch.Success && currentGroup != null)
                {
                    var shaderEnum = ParseEnum(enumMatch.Groups[1].Value.Trim(), lineIndex, ref isParseSuccess);
                    currentGroup.enumList.Add(shaderEnum);
                    continue;
                }
                
                //  检测Blend
                var blendMatch = Regex.Match(trimmedLine, @"//\s*#\s*Blend:(.+)");
                if (blendMatch.Success)
                {
                    globalBlend = ParseBlend(blendMatch.Groups[1].Value.Trim(), lineIndex, ref isParseSuccess);
                    continue;
                }
                
                // 检测Vector Split
                var vectorSplitMatch = Regex.Match(trimmedLine, @"//\s*#\s*VectorSplit:(.+)");
                if (vectorSplitMatch.Success)
                {
                    var shaderVectorSplit = ParseVectorSplit(vectorSplitMatch.Groups[1].Value.Trim(), lineIndex, ref isParseSuccess);
                    currentGroup.vectorList.Add(shaderVectorSplit);
                    continue;
                }
                
                //  检测 Warning
                var warningMatch = Regex.Match(trimmedLine, @"//\s*#\s*Warning:(.+)");
                if (warningMatch.Success)
                {
                    ParseWarning(warningMatch.Groups[1].Value);
                    continue;
                }
                                
                //  检测 FeatureDescribe
                var featureMatch = Regex.Match(trimmedLine, @"//\s*#\s*FeatureDes:(.+)");
                if (featureMatch.Success)
                {
                    Constants.FeatureDescribeText = featureMatch.Groups[1].Value.Trim();
                    continue;
                }
                
                //  以 // 和 # 开头但是内容Flag拼写错误了
                var flagMatch = Regex.Match(trimmedLine, @"//\s*#\s*(.+)");
                if (flagMatch.Success)
                {
                    Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！此行内容：{trimmedLine}");
                    isParseSuccess = false;
                }
                
                
                // 如果是属性行且当前在组内
                if (currentGroup != null && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("//"))
                {
                    if(trimmedLine.Contains("[HideInInspector]"))
                        continue;
                    
                    var property = BuildShaderPropertyData(trimmedLine, lineIndex);
                    if (property.variableType != VariableType.Undefine)
                    {
                        currentGroup.groupShaderPropList.Add(property);
                    }
                }
            }

            Constants.lineEnd = lineIndex;
            
            return isParseSuccess;
        }

        private static ShaderGroupProperty ProcessGroup(ShaderGroupProperty currentGroup, int lineIndex ,ref bool isParseSuccess)
        {
            var newGroup = new ShaderGroupProperty();
            string[] str = currentGroup.GroupName.Split('_');
            if (str.Length > 0)
            {
                newGroup.GroupName = str[1];
                newGroup.GroupNameCN = str[0];
            }
            else
            {
                newGroup.GroupName = currentGroup.GroupName;
                newGroup.GroupNameCN = currentGroup.GroupNameCN;
                isParseSuccess = false;
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
            }

            newGroup.Label = currentGroup.Label;
            newGroup.groupShaderPropList = currentGroup.groupShaderPropList;
            newGroup.groupToggle = currentGroup.groupToggle;
            newGroup.toggleList = currentGroup.toggleList;
            newGroup.enumList = currentGroup.enumList;
            newGroup.vectorList = currentGroup.vectorList;
            return newGroup;
        }

        private static void ParseWarning(string warningString)
        {
            Constants.WarningText = warningString.Trim();
            Constants.WarningMono = "";
            Constants.WarningTimer = -1.0f;
            string[] warningStrArray = warningString.Split(':');
            if (warningStrArray.Length > 1)
            {
                Constants.WarningText = warningStrArray[0].Trim();
                Constants.WarningMono = warningStrArray[1].Trim();
                if(warningStrArray.Length > 2)
                    Constants.WarningTimer = float.Parse(warningStrArray[2].Trim());
            }
        }
        
        private static ShaderToggle ParseToggle(string switchString, int lineIndex, ref bool isParseSuccess)
        {
            string[] switchStrArray = switchString.Split(':');
            if (switchStrArray.Length < 3)
            {
                isParseSuccess = false;
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                return null;
            }
            
            var newToggle = new ShaderToggle();
            switch (switchStrArray[0])
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
            newToggle.toggleName = switchStrArray[1].Trim();
            newToggle.variable = switchStrArray[2].Trim();
            newToggle.Index = lineIndex;
            return newToggle;
        }
        
        private static ShaderEnum ParseEnum(string enumString, int lineIndex, ref bool isParseSuccess)
        {
            string[] enumStrArray = enumString.Split(':');
            if (enumStrArray.Length < 4)
            {
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                isParseSuccess = false;
                return null;
            }

            var newEnum = new ShaderEnum();
            newEnum.enumName = enumStrArray[0].Trim();
            newEnum.enumNameCN = enumStrArray[1].Trim();
            newEnum.variable = enumStrArray[2].Trim();
            newEnum.Index = lineIndex;
            
            newEnum.enumValues.Clear();
            newEnum.enumValuesNumber.Clear();
            enumStrArray = enumStrArray[3].Split('|');
            for (int i = 0; i < enumStrArray.Length; i++)
            {
                if (enumStrArray[i].Contains("="))
                {
                    string[] enumValueArray = enumStrArray[i].Split('=');
                    if (enumValueArray.Length > 1)
                    {
                        newEnum.enumValues.Add(enumValueArray[0]);
                        newEnum.enumValuesNumber.Add(enumValueArray[1]);
                    }
                    else
                    {
                        Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                        isParseSuccess = false;
                        return null;
                    }
                }
                else
                {
                    newEnum.enumValues.Add(enumStrArray[i]);
                    newEnum.enumValuesNumber.Add(i.ToString());
                }
            }

            return newEnum;
        }

        private static ShaderBlend ParseBlend(string blendString, int lineIndex, ref bool isParseSuccess)
        {
            string[] enumStrArray = blendString.Split(':');
            if (enumStrArray.Length < 1)
            {
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                isParseSuccess = false;
                return null;
            }
            
            var newBlend = new ShaderBlend();
            if (enumStrArray[0].Equals("Default") || enumStrArray.Length < 6)
            {
                newBlend.variableNameCN = ShaderConstValue.DEFAULT_BLEND_NAME;
                newBlend.cutoff = ShaderConstValue.DEFAULT_BLEND_CUTOFF;
                newBlend.srcblend = ShaderConstValue.DEFAULT_BLEND_SRCBLEDN;
                newBlend.dstblend = ShaderConstValue.DEFAULT_BLEND_DSTBLEND;
                newBlend.srcblendalpha = ShaderConstValue.DEFAULT_BLEND_SRCBLENDALPHA;
                newBlend.dstblendalpha = ShaderConstValue.DEFAULT_BLEND_DSTBLENDALPHA;
                newBlend.specularAlphaMode = ShaderConstValue.DEFAULT_BLEND_SPECULARALPHAMODE;
            }
            else
            {
                newBlend.variableNameCN = enumStrArray[0].Trim();
                newBlend.cutoff = enumStrArray[1].Trim();
                newBlend.srcblend = enumStrArray[2].Trim();
                newBlend.dstblend = enumStrArray[3].Trim();
                newBlend.srcblendalpha = enumStrArray[4].Trim();
                newBlend.dstblendalpha = enumStrArray[5].Trim();
                newBlend.specularAlphaMode = enumStrArray[6].Trim();
            }
            
            return newBlend;
        }

        private static ShaderVectorSplit ParseVectorSplit(string blendString, int lineIndex, ref bool isParseSuccess)
        {
            string[] vectorStrArray = blendString.Split('!');
            if (vectorStrArray.Length < 1)
            {
                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                isParseSuccess = false;
                return null;
            }


            ShaderVectorSplit newVectorSplit = new ShaderVectorSplit();
            newVectorSplit.Index = lineIndex;
            
            for (int i = 0; i < vectorStrArray.Length; i++)
            {
                //  枚举
                if (vectorStrArray[i].Contains("Enum_"))
                {
                    vectorStrArray[i] = vectorStrArray[i].Remove(0, 5);
                    string[] enumStrArray = vectorStrArray[i].Split(':');
                    if (enumStrArray.Length < 4) return null;
                    
                    var newEnum = new ShaderEnum();
                    newEnum.enumName = enumStrArray[0].Trim();
                    newEnum.enumNameCN = enumStrArray[1].Trim();
                    newEnum.variable = enumStrArray[2].Trim();
                    
                    if (newEnum.variable.Contains("."))
                    {
                        string[] tempStrArray = newEnum.variable.Split('.');
                        newEnum.variable = tempStrArray[0].Trim();
                        newEnum.channel = tempStrArray[1].Trim();
                    }
                    
                    newEnum.enumValues.Clear();
                    
                    
                    enumStrArray = enumStrArray[3].Split('|');
                    for (int k = 0; k < enumStrArray.Length; k++)
                    {
                        if (enumStrArray[k].Contains("="))
                        {
                            string[] enumValueArray = enumStrArray[k].Split('=');
                            if (enumValueArray.Length > 1)
                            {
                                newEnum.enumValues.Add(enumValueArray[0].Trim());
                                newEnum.enumValuesNumber.Add(enumValueArray[1].Trim());
                            }
                            else
                            {
                                Debug.LogError($"此Shader的第：{lineIndex}行编写错误，请检查！");
                                isParseSuccess = false;
                                return null;
                            }
                        }
                        else
                        {
                            newEnum.enumValues.Add(enumStrArray[k]);
                            newEnum.enumValuesNumber.Add(k.ToString());
                        }
                        

                    }
                    newVectorSplit.vectorEnums.Add(newEnum);
                }
                //  开关
                else if(vectorStrArray[i].Contains("Toggle:"))
                {
                    vectorStrArray[i] = vectorStrArray[i].Remove(0, 7);
                    string[] switchStrArray = vectorStrArray[i].Split(':');
                    if (switchStrArray.Length < 3) return null;
            
                    var newToggle = new ShaderToggle();
                    switch (switchStrArray[0])
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
                    newToggle.toggleName = switchStrArray[1].Trim();
                    newToggle.variable = switchStrArray[2].Trim();
                    if (newToggle.variable.Contains("."))
                    {
                        string[] tempStrArray = newToggle.variable.Split('.');
                        newToggle.variable = tempStrArray[0].Trim();
                        newToggle.channel = tempStrArray[1].Trim();
                    }
                    
                    newVectorSplit.vectorToggles.Add(newToggle);
                }
                else
                {
                    ShaderVectorValue value = new ShaderVectorValue();
                    string[] commonStrArray = vectorStrArray[i].Split(':');
                    if (commonStrArray.Length > 2)
                    {
                        switch (commonStrArray[1].ToLower())
                        {
                            case "int": 
                                value.type = VectorValueType.Int; 
                                break;
                            case "float": 
                                value.type = VectorValueType.Float; 
                                break;
                            case "vector2": 
                                value.type = VectorValueType.Vector2; 
                                break;
                            case "vector3": 
                                value.type = VectorValueType.Vector3; 
                                break;
                        }
                        if (commonStrArray[1].ToLower().Contains("range"))
                        {
                            value.type = VectorValueType.Range;
                            float[] rangeArray = EditorGUIUtils.ExtractAllNumbersToFloatArray(commonStrArray[1].Trim().ToLower());

                            if (rangeArray.Length > 1)
                            {
                                value.min = rangeArray[0];
                                value.max = rangeArray[1];
                            }
                        }
                        
                        value.vectorValueName = commonStrArray[2];
                        commonStrArray = commonStrArray[0].Split('.');
                        value.variable = commonStrArray[0].Trim();
                        value.channel = commonStrArray[1].Trim();
                    }
                    
                    newVectorSplit.vectorValues.Add(value);
                }
            }

            return newVectorSplit;
        }
        
        private static ShaderProperty BuildShaderPropertyData(string shaderCode, int lineIndex)
        {
            var property = new ShaderProperty();
            bool isDefault = false;
            if (shaderCode.Contains("[Toggle") || shaderCode.Contains("[Enum") || shaderCode.Contains("[KeywordEnum"))
            {
                isDefault = true;
                shaderCode = Regex.Replace(shaderCode, @"\[\s*(Toggle|Enum|KeywordEnum)[^\]]*\]", "");
            }
            
            string pattern = @"_([a-zA-Z0-9_]+)\s*\(""([^""]+)"",\s*([^)]+)\)";
            Regex regex = new Regex(pattern);
            MatchCollection matches = regex.Matches(shaderCode);
            
            foreach (Match match in matches)
            {
                property.variable = "_" + match.Groups[1].Value;
                property.variableName = match.Groups[2].Value;
                property.Index = lineIndex;
                string typeStr = match.Groups[3].Value.Trim().ToLower();
                typeStr = isDefault ? "" : typeStr;
                
                switch (typeStr)
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
                        if (shaderCode.Contains("[NoScaleOffset]"))
                            property.isFlag = false;
                        else
                            property.isFlag = true;

                        if(shaderCode.Contains("[Split]"))
                        {
                            Constants.TextureSplitCount++;
                            property.isFlag2 = true;
                        }
                        else
                            property.isFlag2 = false;
                            
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

                if (typeStr.Contains("range"))
                {
                    property.variableType = VariableType.Range;
                }
            }
            return property;
        }
    }
} 