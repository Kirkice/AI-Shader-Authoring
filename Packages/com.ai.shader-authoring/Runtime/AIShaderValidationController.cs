using UnityEngine;

namespace AIShader
{
    [ExecuteAlways]
    public sealed class AIShaderValidationController : MonoBehaviour
    {
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Renderer referenceRenderer;
        [SerializeField] private Light mainLight;
        [SerializeField] private Camera validationCamera;
        [SerializeField] private Color baseColor = new Color(0.18f, 0.35f, 0.8f, 1f);
        [SerializeField, Range(0f, 1f)] private float metallic = 0.8f;
        [SerializeField, Range(0f, 1f)] private float roughness = 0.25f;
        [SerializeField] private Color emissionColor = Color.black;
        [SerializeField, Min(0f)] private float emissionIntensity;
        [SerializeField] private AIShaderDebugChannel debugChannel;
        [SerializeField] private bool showReference = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int RoughnessId = Shader.PropertyToID("_Roughness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");
        private static readonly int DebugChannelId = Shader.PropertyToID("_AIShaderDebugChannel");

        public Renderer TargetRenderer => targetRenderer;
        public Renderer ReferenceRenderer => referenceRenderer;
        public Camera ValidationCamera => validationCamera;
        public AIShaderDebugChannel DebugChannel => debugChannel;
        public float Metallic => metallic;
        public float Roughness => roughness;
        public Vector3 MainLightEulerAngles => mainLight != null ? mainLight.transform.eulerAngles : Vector3.zero;
        public Color MainLightColor => mainLight != null ? mainLight.color : Color.black;
        public float MainLightIntensity => mainLight != null ? mainLight.intensity : 0f;

        public void ApplyConfiguration()
        {
            ApplyToRenderer(targetRenderer);
            if (referenceRenderer != null)
                referenceRenderer.gameObject.SetActive(showReference);
        }

        public void SetDebugChannel(AIShaderDebugChannel channel) { debugChannel = channel; ApplyConfiguration(); }

        private void ApplyToRenderer(Renderer target)
        {
            if (target == null || target.sharedMaterial == null) return;
            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            var material = target.sharedMaterial;
            if (material.HasProperty(BaseColorId)) block.SetColor(BaseColorId, baseColor);
            else if (material.HasProperty(ColorId)) block.SetColor(ColorId, baseColor);
            if (material.HasProperty(MetallicId)) block.SetFloat(MetallicId, metallic);
            if (material.HasProperty(RoughnessId)) block.SetFloat(RoughnessId, roughness);
            else if (material.HasProperty(SmoothnessId)) block.SetFloat(SmoothnessId, 1f - roughness);
            if (material.HasProperty(EmissionColorId)) block.SetColor(EmissionColorId, emissionColor * emissionIntensity);
            if (material.HasProperty(EmissionIntensityId)) block.SetFloat(EmissionIntensityId, emissionIntensity);
            if (material.HasProperty(DebugChannelId)) block.SetFloat(DebugChannelId, (float)debugChannel);
            target.SetPropertyBlock(block);
        }

        private void OnEnable() => ApplyConfiguration();
        private void OnValidate() => ApplyConfiguration();
    }
}
