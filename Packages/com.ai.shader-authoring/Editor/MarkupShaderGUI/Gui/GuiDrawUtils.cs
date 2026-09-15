using System;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
       /// 与注释标记语义无关的通用 GUI 绘制工具。
       /// 原实现中存在两份重复的折叠栏绘制代码，这里合并为唯一一份。
       /// </summary>
    public static class GuiDrawUtils
    {
        private const float FoldoutHeight = 22f;
        private const float MenuWidth = 22f;

        private static GUIStyle foldoutStyle;
        private static GUIStyle menuStyle;

        /// <summary>
        /// 绘制折叠标题栏。
        /// 点击右侧菜单区域弹出上下文菜单，点击其余标题区域切换展开状态。
        /// <paramref name="contextMenuAction"/> 为 null 时不绘制菜单按钮。
        /// </summary>
        public static bool DrawFoldout(bool display, string title, Action contextMenuAction = null)
        {
            if (foldoutStyle == null)
                foldoutStyle = CreateFoldoutStyle();
            if (menuStyle == null)
                menuStyle = CreateMenuStyle();

            Rect rect = GUILayoutUtility.GetRect(16f, FoldoutHeight, foldoutStyle);
            GUI.Box(rect, title, foldoutStyle);

            var toggleRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
            bool hasMenu = contextMenuAction != null;
            var menuRect = new Rect(rect.xMax - MenuWidth, rect.y + 1f, MenuWidth, rect.height - 2f);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
                if (hasMenu)
                    GUI.Label(menuRect, "⋮", menuStyle);
            }

            if (Event.current.type == EventType.MouseDown)
            {
                // 菜单区域优先判定，否则右侧按钮的点击会被标题区域抢走。
                if (hasMenu && menuRect.Contains(Event.current.mousePosition))
                {
                    contextMenuAction.Invoke();
                    Event.current.Use();
                }
                else if (rect.Contains(Event.current.mousePosition))
                {
                    display = !display;
                    Event.current.Use();
                }
            }

            return display;
        }

        /// <summary>生成 1x1 纯色贴图，用作自定义样式的背景。</summary>
        public static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static GUIStyle CreateFoldoutStyle()
        {
            var style = new GUIStyle("ShurikenModuleTitle");
            style.font = new GUIStyle(EditorStyles.boldLabel).font;
            style.border = new RectOffset(15, 7, 4, 4);
            style.fixedHeight = FoldoutHeight;
            style.contentOffset = new Vector2(20f, -2f);
            return style;
        }

        private static GUIStyle CreateMenuStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }
    }
}
