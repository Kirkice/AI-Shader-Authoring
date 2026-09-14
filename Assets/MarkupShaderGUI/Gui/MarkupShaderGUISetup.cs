using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 批量给 Shader 写入 <c>CustomEditor</c> 声明的菜单工具。
    /// </summary>
    public static class MarkupShaderGUISetup
    {
        /// <summary>写入 Shader 的 CustomEditor 声明，必须与实际类型全名一致。</summary>
        public const string CustomEditorDeclaration = "CustomEditor \"MarkupShaderGUI.MarkupShaderGUI\"";

        private const string MenuPath = "Assets/Use Markup ShaderGUI";

        /// <summary>已存在的 CustomEditor 声明，写入前先移除以免重复。</summary>
        private static readonly Regex ExistingCustomEditor =
            new Regex(@"CustomEditor\s+""[^"" ]+""");

        [MenuItem(MenuPath, false, 101)]
        private static void UseMarkupShaderGUI()
        {
            Object[] selected = Selection.GetFiltered(typeof(Shader), SelectionMode.Assets);
            if (selected.Length == 0)
                return;

            foreach (Object item in selected)
            {
                Shader shader = item as Shader;
                if (shader == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(shader);
                string source;

                try
                {
                    source = File.ReadAllText(path);
                }
                catch (IOException exception)
                {
                    Debug.LogError($"无法读取 Shader，已跳过：{path} （{exception.Message}）");
                    continue;
                }

                source = ExistingCustomEditor.Replace(source, string.Empty);

                // CustomEditor 是 Shader 块内的声明，因此插在最后一个 } 之前。
                int lastBrace = source.TrimEnd().LastIndexOf('}');
                if (lastBrace < 0)
                {
                    Debug.LogError($"无法设置 MarkupShaderGUI，Shader 格式无效：{path}");
                    continue;
                }

                source = source.Substring(0, lastBrace) +
                         "\n    " + CustomEditorDeclaration + "\n" +
                         source.Substring(lastBrace);

                File.WriteAllText(path, source);
            }

            AssetDatabase.Refresh();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateUseMarkupShaderGUI()
        {
            return Selection.GetFiltered(typeof(Shader), SelectionMode.Assets).Length > 0;
        }
    }
}
