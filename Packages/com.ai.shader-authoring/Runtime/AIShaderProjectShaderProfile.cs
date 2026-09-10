using System;
using UnityEngine;

namespace AIShader
{
    [Serializable]
    public sealed class AIShaderProjectShaderProfile
    {
        public string unityVersion;
        public string urpVersion;
        public string renderPath;
        public ShaderPathConfiguration paths = new ShaderPathConfiguration();
        public string generatedAssetsPath = "Assets/AIShader/Generated";
        public ShaderConvention conventions = new ShaderConvention();
        public ShaderCapabilities capabilities = new ShaderCapabilities();
        public string[] requiredPasses = Array.Empty<string>();
        public ShaderEvidence[] evidence = Array.Empty<ShaderEvidence>();
    }

    [Serializable]
    public sealed class ShaderPathConfiguration
    {
        public string[] shaderFiles = Array.Empty<string>();
        public string[] includeDirectories = Array.Empty<string>();
        public string[] pipelineAssets = Array.Empty<string>();
        public string[] rendererAssets = Array.Empty<string>();
        public string[] referenceFiles = Array.Empty<string>();
    }

    [Serializable] public sealed class ShaderConvention
    {
        public string worldNormal;
        public string viewDirectionWS;
        public string mainLight;
        public string indirectDiffuse;
    }

    [Serializable] public sealed class ShaderCapabilities
    {
        public bool mainLight;
        public bool additionalLights;
        public bool shadowSampling;
        public bool indirectDiffuse;
        public string indirectSpecular = "unknown";
        public string reflectionProbe = "unknown";
        public string brdfLut = "unknown";
    }

    [Serializable] public sealed class ShaderEvidence
    {
        public string capability;
        public string source;
        public float confidence;
    }
}
