using System.Collections.Generic;
using UnityEditor;

namespace MarkupShaderGUI
{
    /// <summary>
    /// GUI 层持有的 Shader 解析结果。
    /// 数据本身来自 <see cref="ShaderParseResult"/>，本类只额外负责
    /// 「Shader 资源路径」与「属性名 → MaterialProperty」的映射。
    /// </summary>
    public sealed class MarkupShaderGUIData
    {
        /// <summary>Shader 资源路径，同时作为折叠状态与剪贴板的 key 前缀。</summary>
        public string ShaderPath;

        /// <summary>本次解析结果，永不为 null。</summary>
        public ShaderParseResult Parse = new ShaderParseResult();

        public string FeatureDescription => Parse.FeatureDescription;

        public string WarningText => Parse.WarningText;

        public string WarningMono => Parse.WarningMono;

        public float WarningTimer => Parse.WarningTimer;

        public ShaderBlend Blend => Parse.Blend;

        public List<ShaderGroupProperty> Groups => Parse.Groups;

        /// <summary>属性名 → MaterialProperty 的映射，每次绘制前重建。</summary>
        public readonly Dictionary<string, MaterialProperty> Properties =
            new Dictionary<string, MaterialProperty>();

        /// <summary>用当前 Inspector 传入的属性数组重建映射表。</summary>
        public void BuildPropertyMap(MaterialProperty[] properties)
        {
            Properties.Clear();
            if (properties == null)
                return;

            foreach (MaterialProperty property in properties)
            {
                if (property != null && !Properties.ContainsKey(property.name))
                    Properties.Add(property.name, property);
            }
        }
    }
}
