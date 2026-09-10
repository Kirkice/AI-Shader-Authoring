#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    [Serializable]
    public sealed class AIShaderProjectContext
    {
        public const string FilePath = "ProjectSettings/AIShaderProjectContext.json";
        public const string DefaultGeneratedAssetsPath = "Assets/AIShader/Generated";

        public string[] shaderPaths = Array.Empty<string>();
        public string[] includePaths = Array.Empty<string>();
        public string[] pipelinePaths = Array.Empty<string>();
        public string[] referencePaths = Array.Empty<string>();
        public string generatedAssetsPath = DefaultGeneratedAssetsPath;

        public static AIShaderProjectContext Load()
        {
            if (!File.Exists(FilePath)) return new AIShaderProjectContext();
            try
            {
                var context = JsonUtility.FromJson<AIShaderProjectContext>(File.ReadAllText(FilePath)) ?? new AIShaderProjectContext();
                context.Normalize();
                return context;
            }
            catch (Exception exception)
            {
                Debug.LogError($"AI Shader: failed to read {FilePath}: {exception.Message}");
                return new AIShaderProjectContext();
            }
        }

        public void Save()
        {
            Normalize();
            if (!IsProjectAssetsPath(generatedAssetsPath))
                throw new InvalidOperationException("Generated assets path must be inside the Assets folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            AssetDatabase.Refresh();
        }

        public void EnsureGeneratedAssetsFolder()
        {
            Normalize();
            if (!IsProjectAssetsPath(generatedAssetsPath))
                throw new InvalidOperationException("Generated assets path must be inside the Assets folder.");
            var absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                generatedAssetsPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }

        public bool HasUserPaths => shaderPaths.Length > 0 || includePaths.Length > 0 || pipelinePaths.Length > 0 || referencePaths.Length > 0;

        private void Normalize()
        {
            shaderPaths ??= Array.Empty<string>();
            includePaths ??= Array.Empty<string>();
            pipelinePaths ??= Array.Empty<string>();
            referencePaths ??= Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(generatedAssetsPath)) generatedAssetsPath = DefaultGeneratedAssetsPath;
            generatedAssetsPath = generatedAssetsPath.Replace('\\', '/').TrimEnd('/');
        }

        private static bool IsProjectAssetsPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal));
        }
    }
}
#endif
