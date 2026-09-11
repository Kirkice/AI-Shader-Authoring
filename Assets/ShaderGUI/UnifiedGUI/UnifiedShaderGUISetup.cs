using UnityEditor;
using UnityEngine;

namespace UnifiedShaderGUI
{
    public static class UnifiedShaderGUISetup
    {
        [MenuItem("Assets/Use Unified ShaderGUI", false, 101)]
        private static void UseUnifiedShaderGUI()
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
                string source = System.IO.File.ReadAllText(path);
                string declaration = "CustomEditor \"UnifiedShaderGUI.UnifiedShaderGUI\"";

                source = System.Text.RegularExpressions.Regex.Replace(
                    source,
                    @"CustomEditor\s+""[^"" ]+""", 
                    string.Empty);

                int lastBrace = source.TrimEnd().LastIndexOf('}');
                if (lastBrace < 0)
                {
                    Debug.LogError($"无法设置 UnifiedShaderGUI，Shader 格式无效：{path}");
                    continue;
                }

                source = source.Substring(0, lastBrace) +
                         "\n    " + declaration + "\n" +
                         source.Substring(lastBrace);
                System.IO.File.WriteAllText(path, source);
            }

            AssetDatabase.Refresh();
        }

        [MenuItem("Assets/Use Unified ShaderGUI", true)]
        private static bool ValidateUseUnifiedShaderGUI()
        {
            return Selection.GetFiltered(typeof(Shader), SelectionMode.Assets).Length > 0;
        }
    }
}