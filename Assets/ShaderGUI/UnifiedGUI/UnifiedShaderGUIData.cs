using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ShaderGUIGenerator;

namespace UnifiedShaderGUI
{
    public sealed class UnifiedShaderGUIData
    {
        public string ShaderPath;
        public string FeatureDescription;
        public string WarningText;
        public string WarningMono;
        public ShaderBlend Blend;
        public List<ShaderGroupProperty> Groups = new List<ShaderGroupProperty>();

        public readonly Dictionary<string, MaterialProperty> Properties =
            new Dictionary<string, MaterialProperty>();

        public void BuildPropertyMap(MaterialProperty[] properties)
        {
            Properties.Clear();
            if (properties == null)
                return;

            foreach (MaterialProperty property in properties)
            {
                if (property != null && !Properties.ContainsKey(property.name))
                    Properties.Add(property.name, property);
            }
        }
    }
}