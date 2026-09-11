using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using ShaderGUIGenerator;

namespace UnifiedShaderGUI
{
    public static class UnifiedShaderGUIParser
    {
        private static readonly Dictionary<Shader, UnifiedShaderGUIData> cache =
            new Dictionary<Shader, UnifiedShaderGUIData>();

        public static UnifiedShaderGUIData GetOrParse(Shader shader, MaterialProperty[] properties)
        {
            if (shader == null)
                return null;

            if (!cache.TryGetValue(shader, out UnifiedShaderGUIData data))
            {
                data = Parse(shader);
                if (data != null)
                    cache[shader] = data;
            }

            data?.BuildPropertyMap(properties);
            return data;
        }

        public static void Invalidate(Shader shader)
        {
            if (shader != null)
                cache.Remove(shader);
        }

        private static UnifiedShaderGUIData Parse(Shader shader)
        {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return null;

            string shaderCode = File.ReadAllText(shaderPath);
            var groups = new List<ShaderGroupProperty>();
            var blend = new ShaderBlend();
            string unsupportedAttribute;

            if (!ShaderParser.ParseShaderProperties(
                    shaderCode,
                    ref groups,
                    ref blend,
                    out unsupportedAttribute))
            {
                return null;
            }

            if (string.IsNullOrEmpty(Constants.FeatureDescribeText))
                return null;

            var data = new UnifiedShaderGUIData
            {
                ShaderPath = shaderPath,
                Blend = blend,
                Groups = groups,
                FeatureDescription = ReadFeatureDescription(shaderCode),
                WarningText = Constants.WarningText,
                WarningMono = Constants.WarningMono
            };

            return data;
        }

        private static string ReadFeatureDescription(string shaderCode)
        {
            Match match = Regex.Match(
                shaderCode,
                @"//\s*#\s*FeatureDes:(.+)",
                RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }
    }
}