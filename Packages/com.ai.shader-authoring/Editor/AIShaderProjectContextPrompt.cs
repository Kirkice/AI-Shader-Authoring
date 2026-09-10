#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    [InitializeOnLoad]
    public static class AIShaderProjectContextPrompt
    {
        static AIShaderProjectContextPrompt()
        {
            EditorApplication.delayCall += PromptIfMissing;
        }

        private static void PromptIfMissing()
        {
            var context = AIShaderProjectContext.Load();
            if (!context.HasUserPaths)
            {
                Debug.LogWarning("AI Shader: 请先打开 AI Shader/Project Context，并提供 Shader、HLSL Include、URP Pipeline/Renderer 和参考文件路径。LLM 在分析工程前必须先向用户确认这些路径。");
            }
        }
    }
}
#endif
