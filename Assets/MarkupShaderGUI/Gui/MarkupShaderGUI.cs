using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// MarkupShaderGUI 的 Inspector 入口。
    /// Shader 通过 <c>CustomEditor "MarkupShaderGUI.MarkupShaderGUI"</c> 引用本类。
    ///
    /// 职责边界：
    /// - 解析交给 <see cref="ShaderMarkupParser"/>；
    /// - 缓存与读取交给 <see cref="MarkupShaderGUICache"/> / <see cref="MarkupShaderGUIResourceLoader"/>；
    /// - 本类只做「把解析结果画成 Inspector」这一件事。
    /// </summary>
    public sealed class MarkupShaderGUI : ShaderGUI
    {
        /// <summary>
        /// 折叠状态。key 为「Shader 路径 : 组英文名」。
        /// 之所以是静态字典：ShaderGUI 实例会在每次选中对象时重建，实例字段无法保持展开状态。
        /// </summary>
        private static readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            if (material == null)
                return;

            MarkupShaderGUIData data = MarkupShaderGUIDataFactory.GetOrParse(material.shader, properties);
            if (data == null)
            {
                EditorGUILayout.HelpBox(
                    "无法解析当前 Shader 的 MarkupShaderGUI 标记，已回退为默认绘制。",
                    MessageType.Warning);
                materialEditor.PropertiesDefaultGUI(properties);
                return;
            }

            DrawFeatureDescription(data.FeatureDescription);
            DrawWarning(data.WarningText);
            DrawGroups(materialEditor, material, data);
            materialEditor.RenderQueueField();
        }

        #region 顶部信息区

        private static void DrawFeatureDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
                return;

            GUIStyle style = MarkupShaderGUIStyles.FeatureDescriptionStyle;
            string content = "<size=15><b>Feature</b></size>\n" + description;

            // 绿底白字需要足够高度，按可用宽度算出实际所需高度。
            float availableWidth = EditorGUIUtility.currentViewWidth - 36f;
            float height = Mathf.Max(44f, style.CalcHeight(new GUIContent(content), availableWidth));

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

        #endregion

        #region 组绘制

        private static void DrawGroups(
            MaterialEditor editor,
            Material material,
            MarkupShaderGUIData data)
        {
            // 先把所有组声明的属性收集成固定集合：
            // 组可能因折叠或组开关关闭而整组不绘制，此时这些属性依然不能被兜底逻辑重复画一遍。
            var handled = new HashSet<string>();
            foreach (ShaderGroupProperty group in data.Groups)
                MarkGroupPropertiesAssigned(handled, group);

            foreach (ShaderGroupProperty group in data.Groups)
            {
                string key = data.ShaderPath + ":" + group.GroupName;
                if (!foldouts.ContainsKey(key))
                    foldouts[key] = true;

                MaterialProperty[] groupProperties = CollectGroupProperties(data, group);
                string displayName = string.IsNullOrEmpty(group.GroupNameCN)
                    ? group.GroupName
                    : group.GroupNameCN;

                foldouts[key] = GuiDrawUtils.DrawFoldout(
                    foldouts[key],
                    ShaderMarkupConstants.GroupTitlePrefix + displayName + ShaderMarkupConstants.GroupTitleSuffix,
                    () => MarkupShaderGUIContextMenu.Show(
                        material,
                        data.ShaderPath,
                        group.GroupName,
                        displayName,
                        groupProperties));

                if (!foldouts[key])
                    continue;

                // 组级开关关闭时整组不绘制。
                if (group.groupToggle != null && !DrawToggle(material, data, group.groupToggle))
                    continue;

                if (!string.IsNullOrEmpty(group.Label))
                    EditorGUILayout.LabelField(group.Label, EditorStyles.boldLabel);

                foreach (ShaderProperty property in group.groupShaderPropList)
                    DrawProperty(editor, data, property);

                foreach (ShaderEnum shaderEnum in group.enumList)
                    DrawEnum(data, shaderEnum);

                foreach (ShaderToggle toggle in group.toggleList)
                    DrawToggle(material, data, toggle);
            }

            DrawUnhandledProperties(editor, data, handled);
        }

        /// <summary>绘制未被任何组或标记声明的属性，避免它们从 Inspector 上消失。</summary>
        private static void DrawUnhandledProperties(
            MaterialEditor editor,
            MarkupShaderGUIData data,
            HashSet<string> handled)
        {
            foreach (MaterialProperty property in data.Properties.Values)
            {
                if (property == null || handled.Contains(property.name))
                    continue;

                editor.ShaderProperty(property, property.displayName);
            }
        }

        private static void MarkGroupPropertiesAssigned(HashSet<string> assigned, ShaderGroupProperty group)
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
            MarkupShaderGUIData data,
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

        #endregion

        #region 单条属性绘制

        private static void DrawProperty(
            MaterialEditor editor,
            MarkupShaderGUIData data,
            ShaderProperty metadata)
        {
            if (metadata == null || !data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                return;

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
                    MarkupShaderGUITexture.Draw(editor, property, metadata);
                    break;
            }
        }

        private static void DrawEnum(MarkupShaderGUIData data, ShaderEnum metadata)
        {
            if (metadata == null || !data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                return;

            if (metadata.enumValues.Count == 0)
                return;

            int current = Mathf.RoundToInt(property.floatValue);
            int selected = EditorGUILayout.Popup(
                metadata.enumNameCN,
                current,
                metadata.enumValues.ToArray());

            if (selected < 0 || selected >= metadata.enumValuesNumber.Count)
                return;

            if (ShaderMarkupTextUtils.TryParseFloat(metadata.enumValuesNumber[selected], out float value))
                property.floatValue = value;
        }

        /// <summary>
        /// 绘制开关。返回开关当前是否处于开启状态：
        /// 组级开关用它决定整组是否绘制，普通开关的返回值会被忽略。
        /// </summary>
        private static bool DrawToggle(
            Material material,
            MarkupShaderGUIData data,
            ShaderToggle metadata)
        {
            if (metadata == null)
                return true;

            switch (metadata.toggleType)
            {
                case ToggleType.group_keywords:
                case ToggleType.keywords:
                    return DrawKeywordToggle(material, metadata);

                case ToggleType.group_uniform:
                    if (!data.Properties.TryGetValue(metadata.variable, out MaterialProperty groupProperty))
                        return true;

                    groupProperty.floatValue = EditorGUILayout.Toggle(
                        metadata.toggleName,
                        groupProperty.floatValue > 0.5f) ? 1f : 0f;
                    return groupProperty.floatValue > 0.5f;

                case ToggleType.uniform:
                    if (!data.Properties.TryGetValue(metadata.variable, out MaterialProperty property))
                        return true;

                    property.floatValue = EditorGUILayout.Toggle(
                        metadata.toggleName,
                        property.floatValue > 0.5f) ? 1f : 0f;
                    return property.floatValue > 0.5f;

                default:
                    return true;
            }
        }

        private static bool DrawKeywordToggle(Material material, ShaderToggle metadata)
        {
            bool enabled = material.IsKeywordEnabled(metadata.variable);
            bool next = EditorGUILayout.Toggle(metadata.toggleName, enabled);

            if (next != enabled)
            {
                if (next)
                    material.EnableKeyword(metadata.variable);
                else
                    material.DisableKeyword(metadata.variable);
            }

            return next;
        }

        #endregion
    }
}
