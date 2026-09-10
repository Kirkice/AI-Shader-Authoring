#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIShader.Editor
{
    [Serializable]
    public sealed class AIShaderCaptureManifest
    {
        public string iteration; public string stage; public string scene; public string shader; public string material;
        public string capturePath; public string debugChannel; public Vector3 cameraPosition; public Vector3 cameraEulerAngles;
        public Vector3 lightEulerAngles; public Color lightColor; public float lightIntensity; public float metallic; public float roughness;
        public int consoleErrorCount; public string createdUtc;
    }

    public static class AIShaderCaptureManifestWriter
    {
        private const string CaptureDirectory = "Assets/AIShader/Validation/Captures";
        private const string ReportDirectory = "Assets/AIShader/Validation/Reports";

        public static string Capture(Camera camera, string iteration, string stage, string shader, string material,
            AIShader.AIShaderDebugChannel channel, AIShader.AIShaderValidationController controller = null, int width = 1024, int height = 1024)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            EnsureFolders();
            var safeIteration = Sanitize(iteration);
            var relativeCapturePath = $"{CaptureDirectory}/{safeIteration}.png";
            var absoluteCapturePath = ToAbsolute(relativeCapturePath);
            if (File.Exists(absoluteCapturePath)) throw new IOException($"Capture already exists: {relativeCapturePath}");

            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = $"AIShaderCapture_{safeIteration}", useMipMap = false, autoGenerateMips = false };
            texture.Create();
            if (!texture.IsCreated()) throw new InvalidOperationException("Could not create capture RenderTexture.");
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                try
                {
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(false, false);
                    var png = image.EncodeToPNG();
                    if (png == null || png.Length == 0) throw new InvalidOperationException("PNG encoding returned no data.");
                    File.WriteAllBytes(absoluteCapturePath, png);
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }

            var manifest = new AIShaderCaptureManifest
            {
                iteration = iteration, stage = stage, scene = EditorSceneManager.GetActiveScene().path, shader = shader, material = material,
                capturePath = relativeCapturePath, debugChannel = channel.ToString(), cameraPosition = camera.transform.position,
                cameraEulerAngles = camera.transform.eulerAngles, lightEulerAngles = controller != null ? controller.MainLightEulerAngles : Vector3.zero,
                lightColor = controller != null ? controller.MainLightColor : Color.black, lightIntensity = controller != null ? controller.MainLightIntensity : 0f,
                metallic = controller != null ? controller.Metallic : 0f, roughness = controller != null ? controller.Roughness : 0f
            };
            return Write(manifest);
        }

        public static string Write(AIShaderCaptureManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            EnsureFolders(); manifest.createdUtc = DateTime.UtcNow.ToString("O");
            var safeIteration = Sanitize(manifest.iteration); var relativePath = $"{ReportDirectory}/{safeIteration}.manifest.json"; var absolutePath = ToAbsolute(relativePath);
            if (File.Exists(absolutePath)) throw new IOException($"Manifest already exists: {relativePath}");
            var temporaryPath = absolutePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8); File.Move(temporaryPath, absolutePath); AssetDatabase.Refresh(); return relativePath;
        }

        private static string ToAbsolute(string projectRelativePath)
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("Could not resolve Unity project root.");
            return Path.Combine(root, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) value = "capture"; var builder = new StringBuilder(value.Length);
            foreach (var c in value) builder.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
            var result = builder.ToString().Trim('_'); return string.IsNullOrEmpty(result) || result == "." || result == ".." ? "capture" : result;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "AIShader"); EnsureFolder("Assets/AIShader", "Validation"); EnsureFolder("Assets/AIShader/Validation", "Captures"); EnsureFolder("Assets/AIShader/Validation", "Reports");
        }
        private static void EnsureFolder(string parent, string child) { var path = $"{parent}/{child}"; if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child); }
    }
}
#endif
