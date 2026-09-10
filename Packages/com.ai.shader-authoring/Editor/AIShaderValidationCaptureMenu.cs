#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    public static class AIShaderValidationCaptureMenu
    {
        public static void CaptureValidationFrame()
        {
            var controller = Object.FindFirstObjectByType<global::AIShader.AIShaderValidationController>();
            if (controller == null)
            {
                Debug.LogError("No AIShaderValidationController found in the active scene.");
                return;
            }

            controller.ApplyConfiguration();
            var camera = controller.ValidationCamera;
            var targetMaterial = controller.TargetRenderer != null ? controller.TargetRenderer.sharedMaterial : null;
            var shaderPath = targetMaterial != null && targetMaterial.shader != null ? AssetDatabase.GetAssetPath(targetMaterial.shader) : string.Empty;
            var materialPath = targetMaterial != null ? AssetDatabase.GetAssetPath(targetMaterial) : string.Empty;
            var iteration = $"manual-{System.DateTime.UtcNow:yyyyMMdd-HHmmss}";
            var reportPath = AIShaderCaptureManifestWriter.Capture(camera, iteration, "manual", shaderPath, materialPath, controller.DebugChannel, controller);
            Debug.Log($"AI Shader validation capture written: {reportPath}");
        }

    }
}
#endif
