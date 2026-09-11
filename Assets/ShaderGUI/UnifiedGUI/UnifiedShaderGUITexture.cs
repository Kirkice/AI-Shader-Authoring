using UnityEditor;
using UnityEngine;
using ShaderGUIGenerator;

namespace UnifiedShaderGUI
{
    internal static class UnifiedShaderGUITexture
    {
        public static void Draw(
            MaterialEditor editor,
            Material material,
            MaterialProperty property,
            ShaderProperty metadata)
        {
            if (metadata == null || property == null)
                return;

            GUIContent content = new GUIContent(
                metadata.variableName,
                metadata.variableName);

            editor.TexturePropertySingleLine(content, property);

            // The old CodeGen convention uses isFlag=true for scale/offset support.
            if (metadata.isFlag)
                editor.TextureScaleOffsetProperty(property);
        }
    }
}