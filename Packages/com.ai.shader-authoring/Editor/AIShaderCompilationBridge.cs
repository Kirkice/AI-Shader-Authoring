#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    public static class AIShaderCompilationBridge
    {
        public static bool IsCompiling => EditorApplication.isCompiling;
        public static bool IsUpdating => EditorApplication.isUpdating;
        public static string UnityVersion => Application.unityVersion;
        public static string ActiveScene => UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        public static bool IsPlaying => EditorApplication.isPlaying;

        public static void RefreshAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
#endif
