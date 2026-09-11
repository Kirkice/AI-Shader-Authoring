using System;
using UnityEditor;
using UnityEngine;

namespace UnifiedShaderGUI
{
    internal static class UnifiedShaderGUIStyles
    {
        private static GUIStyle foldoutStyle;
        private static GUIStyle menuStyle;

        public static bool DrawFoldout(
            bool display,
            string title,
            Action contextMenuAction)
        {
            if (foldoutStyle == null)
                foldoutStyle = CreateFoldoutStyle();
            if (menuStyle == null)
                menuStyle = CreateMenuStyle();

            Rect rect = GUILayoutUtility.GetRect(16f, 22f, foldoutStyle);
            GUI.Box(rect, title, foldoutStyle);

            Rect toggleRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
            Rect menuRect = new Rect(rect.xMax - 24f, rect.y + 1f, 22f, rect.height - 2f);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
                GUI.Label(menuRect, "⋮", menuStyle);
            }

            if (Event.current.type == EventType.MouseDown)
            {
                if (menuRect.Contains(Event.current.mousePosition))
                {
                    contextMenuAction?.Invoke();
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

        private static GUIStyle CreateFoldoutStyle()
        {
            var style = new GUIStyle("ShurikenModuleTitle");
            style.font = new GUIStyle(EditorStyles.boldLabel).font;
            style.border = new RectOffset(15, 7, 4, 4);
            style.fixedHeight = 22f;
            style.contentOffset = new Vector2(20f, -2f);
            return style;
        }

        private static GUIStyle CreateMenuStyle()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            return style;
        }
    }
}