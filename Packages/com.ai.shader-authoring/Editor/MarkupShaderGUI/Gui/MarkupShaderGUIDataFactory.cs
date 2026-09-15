using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 解析编排：缓存 → 读取源码 → 解析 → 判定是否可接管。
    /// 由 <see cref="MarkupShaderGUI"/> 调用，是 GUI 层唯一的数据来源。
    /// </summary>
    public static class MarkupShaderGUIDataFactory
    {
        /// <summary>
        /// 取得指定 Shader 的解析结果并绑定当前属性数组。
        /// 返回 null 表示不可接管（读取失败或解析失败）。
        /// FeatureDes 为可选的顶部功能说明，不影响接管资格。
        /// </summary>
        public static MarkupShaderGUIData GetOrParse(Shader shader, MaterialProperty[] properties)
        {
            if (shader == null)
                return null;

            if (!MarkupShaderGUICache.TryGet(shader, out MarkupShaderGUIData data))
            {
                data = Parse(shader);
                if (data == null)
                    return null;

                MarkupShaderGUICache.Set(shader, data);
            }

            data.BuildPropertyMap(properties);
            return data;
        }

        private static MarkupShaderGUIData Parse(Shader shader)
        {
            string shaderCode = MarkupShaderGUIResourceLoader.ReadShaderSource(shader, out string error);
            if (string.IsNullOrEmpty(shaderCode))
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[MarkupShaderGUI] " + error);

                return null;
            }

            ShaderParseResult result = ShaderMarkupParser.Parse(shaderCode);

            // FeatureDes 仅控制顶部功能说明；只在读取或解析失败时回退默认 GUI。
            if (!result.Success)
                return null;

            return new MarkupShaderGUIData
            {
                ShaderPath = AssetDatabase.GetAssetPath(shader),
                Parse = result
            };
        }
    }
}
