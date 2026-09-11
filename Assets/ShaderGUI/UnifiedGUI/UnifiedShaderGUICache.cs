using UnityEditor;
using UnityEngine;

namespace UnifiedShaderGUI
{
    public sealed class UnifiedShaderGUIAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                if (!path.EndsWith(".shader", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                UnifiedShaderGUIParser.Invalidate(shader);
            }
        }
    }
}