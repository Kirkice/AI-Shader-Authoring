using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// MarkupShaderGUI 使用的自定义样式。
    /// 样式在首次绘制时创建，避免静态构造阶段访问 GUI 皮肤。
    /// </summary>
    internal static class MarkupShaderGUIStyles
    {
        /// <summary>功能描述条的颜色。</summary>
        private static readonly Color FeatureBackground = new Color(0.24f, 0.52f, 0.24f, 1f);

        private static GUIStyle featureDescriptionStyle;

        /// <summary>功能描述条样式：绿底、白字、加粗、自动换行。</summary>
        public static GUIStyle FeatureDescriptionStyle
        {
            get
            {
                if (featureDescriptionStyle == null)
                    featureDescriptionStyle = CreateFeatureDescriptionStyle();

                return featureDescriptionStyle;
            }
        }

        private static GUIStyle CreateFeatureDescriptionStyle()
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                richText = true,
                wordWrap = true,
                padding = new RectOffset(10, 10, 7, 7)
            };

            style.normal.textColor = Color.white;
            style.normal.background = GuiDrawUtils.CreateSolidTexture(FeatureBackground);
            return style;
        }
    }
}
