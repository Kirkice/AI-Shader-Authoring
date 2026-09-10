#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    public sealed class AIShaderProjectContextWindow : EditorWindow
    {
        private AIShaderProjectContext context;
        public static void Open() => GetWindow<AIShaderProjectContextWindow>("AI Shader Project Context");

        private void OnEnable() => context = AIShaderProjectContext.Load();

        private void OnGUI()
        {
            if (context == null) OnEnable();
            EditorGUILayout.HelpBox("在 LLM 分析工程前，请用户明确提供 Shader、HLSL Include、URP Pipeline/Renderer 资产和参考文件路径。Shader 生成文件必须放在 Assets 下；如果用户不指定，默认使用 Assets/AIShader/Generated。", MessageType.Info);
            context.shaderPaths = DrawPaths("Shader Paths", context.shaderPaths, "当前项目 Shader 文件或目录，相对工程根目录");
            context.includePaths = DrawPaths("Include Paths", context.includePaths, "HLSL Include 文件或目录，相对工程根目录");
            context.pipelinePaths = DrawPaths("Pipeline Paths", context.pipelinePaths, "URP Pipeline Asset、Renderer Asset 或相关配置文件");
            context.referencePaths = DrawPaths("Reference Paths", context.referencePaths, "参考 Shader、Lighting Include 或示例文件");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Assets Path", EditorStyles.boldLabel);
            context.generatedAssetsPath = EditorGUILayout.TextField(new GUIContent("路径", "LLM 生成的 Shader、HLSL 和 Material 输出目录，必须位于 Assets 下"), context.generatedAssetsPath);
            if (GUILayout.Button("使用默认生成目录")) context.generatedAssetsPath = AIShaderProjectContext.DefaultGeneratedAssetsPath;
            if (GUILayout.Button("保存工程上下文并创建生成目录"))
            {
                try
                {
                    context.Save();
                    context.EnsureGeneratedAssetsFolder();
                    Debug.Log($"AI Shader project context saved. Generated assets: {context.generatedAssetsPath}");
                }
                catch (System.Exception exception) { Debug.LogError($"AI Shader: failed to save project context: {exception.Message}"); }
            }
        }

        private static string[] DrawPaths(string label, string[] paths, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.boldLabel);
            var size = Mathf.Max(0, EditorGUILayout.IntField("数量", paths?.Length ?? 0));
            var result = new string[size];
            for (var i = 0; i < size; i++) result[i] = EditorGUILayout.TextField($"路径 {i + 1}", paths != null && i < paths.Length ? paths[i] : string.Empty);
            return result;
        }
    }
}
#endif
