using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using ShaderGUIGenerator;

namespace UnifiedShaderGUI
{
    public sealed class UnifiedShaderGUI : ShaderGUI
    {
        private static readonly Dictionary<string, bool> foldouts =
            new Dictionary<string, bool>();

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            if (material == null)
                return;

            UnifiedShaderGUIData data =
                UnifiedShaderGUIParser.GetOrParse(material.shader, properties);

            if (data == null)
            {
                EditorGUILayout.HelpBox(
                    "无法解析当前 Shader 的 UnifiedShaderGUI 标记。",
                    MessageType.Error);
                materialEditor.PropertiesDefaultGUI(properties);
                return;
            }

            if (!string.IsNullOrEmpty(data.FeatureDescription))
                DrawFeatureDescription(data.FeatureDescription);

            DrawWarning(data.WarningText);
            LoadSpecialTextures(material);
            DrawGroups(materialEditor, material, data);
            materialEditor.RenderQueueField();
        }

        private static void DrawFeatureDescription(string description)
        {
            GUIStyle style = new GUIStyle(EditorStyles.helpBox)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(10, 10, 7, 7)
            };
            style.normal.textColor = Color.white;
            style.normal.background = MakeTexture(new Color(0.24f, 0.52f, 0.24f, 1f));

            string content = "<size=15><b>Feature</b></size>\n" + description;
            float height = Mathf.Max(44f, style.CalcHeight(new GUIContent(content), EditorGUIUtility.currentViewWidth - 36f));
            EditorGUILayout.LabelField(content, style, GUILayout.MinHeight(height));
            EditorGUILayout.Space(4f);
        }

        private static void DrawWarning(string warning)
        {
            if (string.IsNullOrEmpty(warning))
                return;

            EditorGUILayout.HelpBox(warning, MessageType.Warning);
            EditorGUILayout.Space();
        }

        private static void LoadSpecialTextures(Material material)
        {
            if (material == null)
                return;

            LoadTextureIfMissing(material, "_DfgTexture", ShaderGUIGenerator.EditorGUIUtils.BRDF_LUT_PATH);
            LoadTextureIfMissing(material, "_SpecularOcclusionLut3D", ShaderGUIGenerator.EditorGUIUtils.SPECULAR_OCCLUSION_LUT_PATH);
            LoadTextureIfMissing(material, "_ACESLutTex", ShaderGUIGenerator.EditorGUIUtils.LUT_ACES_PATH);
        }

        private static void LoadTextureIfMissing(Material material, string propertyName, string assetPath)
        {
            if (!material.HasProperty(propertyName) || material.GetTexture(propertyName) != null)
                return;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null)
                material.SetTexture(propertyName, texture);
        }

        private static void DrawGroups(
            MaterialEditor editor,
            Material material,
            UnifiedShaderGUIData data)
        {
            var assigned = new HashSet<string>();

            foreach (ShaderGroupProperty group in data.Groups)
            {
                string key = data.ShaderPath + ":" + group.GroupName;
                if (!foldouts.ContainsKey(key))
                    foldouts[key] = true;

                MaterialProperty[] groupProperties = CollectGroupProperties(data, group);
                MarkGroupPropertiesAssigned(assigned, group);
                string displayName = string.IsNullOrEmpty(group.GroupNameCN)
                    ? group.GroupName
                    : group.GroupNameCN;

                foldouts[key] = UnifiedShaderGUIStyles.DrawFoldout(
                    foldouts[key],
                    "【" + displayName + "】",
                    () => UnifiedShaderGUIContextMenu.Show(
                        material,
                        data.ShaderPath,
                        group.GroupName,
                        displayName,
                        groupProperties));

                if (!foldouts[key])
                    continue;

                bool groupEnabled = true;
                if (group.groupToggle != null)
                {
                    groupEnabled = DrawToggle(material, data, group.groupToggle);
                    assigned.Add(group.groupToggle.variable);
                }

                if (!groupEnabled)
                    continue;

                if (!string.IsNullOrEmpty(group.Label))
                    EditorGUILayout.LabelField(group.Label, EditorStyles.boldLabel);

                foreach (ShaderProperty property in group.groupShaderPropList)
                {
                    DrawProperty(editor, data, property);
                    if (property != null)
                        assigned.Add(property.variable);
                }

                foreach (ShaderEnum shaderEnum in group.enumList)
                {
                    DrawEnum(material, data, shaderEnum);
                    if (shaderEnum != null)
                        assigned.Add(shaderEnum.variable);
                }

                foreach (ShaderToggle toggle in group.toggleList)
                {
                    DrawToggle(material, data, toggle);
                    if (toggle != null)
                        assigned.Add(toggle.variable);
                }


            }

            // Only properties without any parser metadata use the default fallback.
            // A property already described by a group must never be drawn a second time.
            foreach (MaterialProperty property in data.Properties.Values)
            {
                if (!assigned.Contains(property.name) && !IsMetadataProperty(data, property.name))
                    editor.ShaderProperty(property, property.displayName);
            }
        }

        private static bool IsMetadataProperty(UnifiedShaderGUIData data, string propertyName)
        {
            foreach (ShaderGroupProperty group in data.Groups)
            {
                foreach (ShaderProperty property in group.groupShaderPropList)
                    if (property != null && property.variable == propertyName) return true;
                foreach (ShaderEnum shaderEnum in group.enumList)
                    if (shaderEnum != null && shaderEnum.variable == propertyName) return true;
                foreach (ShaderToggle toggle in group.toggleList)
                    if (toggle != null && toggle.variable == propertyName) return true;
                if (group.groupToggle != null && group.groupToggle.variable == propertyName)
                    return true;
            }
            return false;
        }

        private static void MarkGroupPropertiesAssigned(
            HashSet<string> assigned,
            ShaderGroupProperty group)
        {
            if (group == null)
                return;

            foreach (ShaderProperty property in group.groupShaderPropList)
            {
                if (property != null)
                    assigned.Add(property.variable);
            }

            foreach (ShaderEnum shaderEnum in group.enumList)
            {
                if (shaderEnum != null)
                    assigned.Add(shaderEnum.variable);
            }

            foreach (ShaderToggle toggle in group.toggleList)
            {
                if (toggle != null)
                    assigned.Add(toggle.variable);
            }

            if (group.groupToggle != null)
                assigned.Add(group.groupToggle.variable);
        }

        private static MaterialProperty[] CollectGroupProperties(
            UnifiedShaderGUIData data,
            ShaderGroupProperty group)
        {
            var result = new List<MaterialProperty>();
            if (group == null)
                return result.ToArray();

            foreach (ShaderProperty metadata in group.groupShaderPropList)
            {
                if (metadata != null && data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                    result.Add(property);
            }

            return result.ToArray();
        }

        private static void DrawProperty(
            MaterialEditor editor,
            UnifiedShaderGUIData data,
            ShaderProperty metadata)
        {
            if (metadata == null || !data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                return;

            foreach (ShaderGroupProperty group in data.Groups)
            {
                foreach (ShaderEnum shaderEnum in group.enumList)
                {
                    if (shaderEnum != null && shaderEnum.variable == metadata.variable)
                        return;
                }
            }

            switch (metadata.variableType)
            {
                case VariableType.Color:
                    editor.ColorProperty(property, metadata.variableName);
                    break;
                case VariableType.Vector:
                    editor.VectorProperty(property, metadata.variableName);
                    break;
                case VariableType.Range:
                    editor.RangeProperty(property, metadata.variableName);
                    break;
                case VariableType.Float:
                case VariableType.Int:
                case VariableType.Default:
                    editor.FloatProperty(property, metadata.variableName);
                    break;
                case VariableType.Texture2D:
                case VariableType.Cube:
                    UnifiedShaderGUITexture.Draw(
                        editor,
                        editor.target as Material,
                        property,
                        metadata);
                    break;
            }
        }

        private static void DrawEnum(
            Material material,
            UnifiedShaderGUIData data,
            ShaderEnum metadata)
        {
            if (metadata == null || !data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                return;

            int current = Mathf.RoundToInt(property.floatValue);
            int selected = EditorGUILayout.Popup(
                metadata.enumNameCN,
                current,
                metadata.enumValues.ToArray());

            if (selected < 0 || selected >= metadata.enumValuesNumber.Count)
                return;

            if (float.TryParse(
                    metadata.enumValuesNumber[selected],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value))
            {
                property.floatValue = value;
            }
        }

        private static bool DrawToggle(
            Material material,
            UnifiedShaderGUIData data,
            ShaderToggle metadata)
        {
            if (metadata == null)
                return true;

            if (metadata.toggleType == ToggleType.group_keywords)
            {
                bool enabled = material.IsKeywordEnabled(metadata.variable);
                bool next = EditorGUILayout.Toggle(metadata.toggleName, enabled);
                if (next != enabled)
                {
                    if (next) material.EnableKeyword(metadata.variable);
                    else material.DisableKeyword(metadata.variable);
                }
                return next;
            }

            if (metadata.toggleType == ToggleType.group_uniform)
            {
                if (!data.Properties.TryGetValue(metadata.variable, out MaterialProperty groupProperty))
                    return true;

                groupProperty.floatValue = EditorGUILayout.Toggle(
                    metadata.toggleName,
                    groupProperty.floatValue > 0.5f) ? 1f : 0f;
                return groupProperty.floatValue > 0.5f;
            }

            if (metadata.toggleType == ToggleType.keywords)
            {
                bool enabled = material.IsKeywordEnabled(metadata.variable);
                bool next = EditorGUILayout.Toggle(metadata.toggleName, enabled);
                if (next != enabled)
                {
                    if (next) material.EnableKeyword(metadata.variable);
                    else material.DisableKeyword(metadata.variable);
                }
            }
            else if (metadata.toggleType == ToggleType.uniform &&
                     data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
            {
                property.floatValue = EditorGUILayout.Toggle(
                    metadata.toggleName,
                    property.floatValue > 0.5f) ? 1f : 0f;
            }

            return true;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}