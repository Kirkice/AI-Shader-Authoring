using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 纹理属性绘制。原实现把 <c>TexturePropertySingleLine</c> 与
    /// Tiling / Offset 的取舍写在内联代码里，这里收拢成一个方法。
    /// </summary>
    internal static class MarkupShaderGUITexture
    {
        /// <summary>
        /// 绘制一条纹理属性。
        /// <see cref="ShaderProperty.isFlag"/> 为 true 时额外绘制 Tiling / Offset，
        /// 对应 Shader 中没有写 <c>[NoScaleOffset]</c> 的情况。
        /// </summary>
        public static void Draw(
            MaterialEditor editor,
            MaterialProperty property,
            ShaderProperty metadata)
        {
            if (editor == null || metadata == null || property == null)
                return;

            var content = new GUIContent(metadata.variableName, metadata.variableName);
            editor.TexturePropertySingleLine(content, property);

            if (metadata.isFlag)
                editor.TextureScaleOffsetProperty(property);
        }
    }
}
