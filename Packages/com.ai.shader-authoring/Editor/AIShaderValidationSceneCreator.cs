#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIShader.Editor
{
    public static class AIShaderValidationSceneCreator
    {
        private const string Root = "Assets/AIShader/Validation";
        private const string ScenePath = Root + "/Scenes/PBRValidation.unity";
        private const string ReferenceMaterialPath = Root + "/Materials/ReferenceLit.mat";
        private const string TargetMaterialPath = Root + "/Materials/TargetGenerated.mat";

        public static void CreateValidationScene()
        {
            EnsureFolders();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controllerObject = new GameObject("AIShaderValidation");
            var controller = controllerObject.AddComponent<global::AIShader.AIShaderValidationController>();

            var cameraObject = new GameObject("Validation Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4.2f), Quaternion.identity);
            camera.fieldOfView = 35f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .065f, .08f, 1f); camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();

            var lightObject = new GameObject("Main Directional Light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(35f, -35f, 0f); light.intensity = 1.5f; light.shadows = LightShadows.Soft;

            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere); target.name = "Target Sphere"; target.transform.localScale = Vector3.one * 2f; target.GetComponent<Renderer>().sharedMaterial = GetMaterial(TargetMaterialPath, "TargetGenerated", new Color(.18f, .35f, .8f, 1f), .8f, .75f);
            var reference = GameObject.CreatePrimitive(PrimitiveType.Sphere); reference.name = "Reference Sphere"; reference.transform.position = new Vector3(2.6f, 0f, 0f); reference.transform.localScale = Vector3.one * 1.5f; reference.GetComponent<Renderer>().sharedMaterial = GetMaterial(ReferenceMaterialPath, "ReferenceLit", new Color(.35f, .42f, .55f, 1f), .25f, .65f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Neutral Ground"; floor.transform.position = new Vector3(0f, -1.05f, 0f); floor.transform.localScale = Vector3.one * .6f; floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ReferenceMaterialPath);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("targetRenderer").objectReferenceValue = target.GetComponent<Renderer>(); serialized.FindProperty("referenceRenderer").objectReferenceValue = reference.GetComponent<Renderer>(); serialized.FindProperty("mainLight").objectReferenceValue = light; serialized.FindProperty("validationCamera").objectReferenceValue = camera; serialized.ApplyModifiedPropertiesWithoutUndo(); controller.ApplyConfiguration();
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientSkyColor = new Color(.12f, .15f, .2f); RenderSettings.ambientEquatorColor = new Color(.06f, .07f, .1f); RenderSettings.ambientGroundColor = new Color(.02f, .02f, .025f); RenderSettings.ambientIntensity = .5f;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) { Debug.LogError($"Failed to save {ScenePath}"); return; }
            Selection.activeGameObject = controllerObject; Debug.Log($"AI Shader validation scene created: {ScenePath}");
        }

        private static Material GetMaterial(string path, string name, Color color, float metallic, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit"); if (shader == null) throw new System.InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader) { name = name }; material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness); AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssets(); return material;
        }
        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "AIShader"); EnsureFolder("Assets/AIShader", "Validation"); EnsureFolder("Assets/AIShader/Validation", "Scenes"); EnsureFolder("Assets/AIShader/Validation", "Materials"); EnsureFolder("Assets/AIShader/Validation", "Captures"); EnsureFolder("Assets/AIShader/Validation", "Reports");
        }
        private static void EnsureFolder(string parent, string child) { var path = $"{parent}/{child}"; if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child); }
    }
}
#endif
