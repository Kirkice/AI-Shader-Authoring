using System.Collections.Generic;

namespace ShaderGUIGenerator
{
    #region Shader Data Structures

    public class ShaderConstValue
    {
        public const string DEFAULT_BLEND_NAME = "渲染模式";
        public const string DEFAULT_BLEND_CUTOFF = "_cutoff";
        public const string DEFAULT_BLEND_SRCBLEDN = "_srcblend";
        public const string DEFAULT_BLEND_DSTBLEND = "_dstblend";
        public const string DEFAULT_BLEND_SRCBLENDALPHA = "_srcblendalpha";
        public const string DEFAULT_BLEND_DSTBLENDALPHA = "_dstblendalpha";
        public const string DEFAULT_BLEND_SPECULARALPHAMODE = "_specularAlphaMode";
    }
    
    public class ShaderProperty
    {
        public string variable;
        public string variableName;
        public bool isFlag = false;
        public bool isFlag2 = false;                                     
        //  IS_FLAG： 属性标志    
        //  TYPE：   TEXTURE2D   ->  NOSclaeOffset
        //  IS_FLAG2： 属性标志    
        //  TYPE：   TEXTURE2D   ->  TextureSplit
        public VariableType variableType = VariableType.Undefine;
        public int Index = -1;
    }

    public class ShaderToggle
    {
        public ToggleType toggleType = ToggleType.uniform;
        public string toggleName;
        public string variable;
        public string channel = "";
        public int Index = -1;
    }
    
    public class ShaderEnum
    {
        public string enumName;
        public string enumNameCN;
        public string variable;
        public string channel = "";
        public List<string> enumValues = new List<string>();
        public List<string> enumValuesNumber = new List<string>();
        public int Index = -1;
    }
    
    public class ShaderVectorSplit
    {
        public List<ShaderVectorValue> vectorValues = new List<ShaderVectorValue>();
        public List<ShaderEnum> vectorEnums = new List<ShaderEnum>();
        public List<ShaderToggle> vectorToggles = new List<ShaderToggle>();
        public int Index = -1;
    }
    
    public class ShaderVectorValue
    {
        public string vectorValueName;
        public string variable;
        public string channel;
        public VectorValueType type;
        public float min = 0.0f;
        public float max = 1.0f;
    }
    
    public class ShaderGroupProperty
    {
        public string GroupName;
        public string GroupNameCN;
        public string Label;
        public List<ShaderProperty> groupShaderPropList = new List<ShaderProperty>();
        public ShaderToggle groupToggle = null;
        public List<ShaderToggle> toggleList = new List<ShaderToggle>();
        public List<ShaderEnum> enumList = new List<ShaderEnum>();
        public List<ShaderVectorSplit> vectorList = new List<ShaderVectorSplit>();
    }
    
    public class ShaderBlend
    {
        public string variableNameCN;
        public string cutoff;
        public string srcblend;
        public string dstblend;
        public string srcblendalpha;
        public string dstblendalpha;
        public string specularAlphaMode;
    }
    
    public enum VariableType
    {
        Undefine = 0,
        Int = 1,
        Float = 2,
        Range = 3,
        Texture2D = 4,
        Color = 5,
        Vector = 6,
        Cube = 7,
        Default = 8
    }
    
    public enum ToggleType
    {
        uniform = 0,
        keywords = 1,
        group_uniform = 2,
        group_keywords = 3,
    }

    public enum VectorValueType
    {
        Int = 0,
        Float = 1,
        Range = 2,
        Vector2 = 3,
        Vector3 = 4
    }

    public enum GUICodeGenFlag
    {
        GroupStart = 0,
        GroupEnd = 1,
        Enum = 2,
        Blend = 3,
        Toggle = 4,
        Label = 5,
        VectorSplit = 6,
        Warning = 7
    }
    #endregion
} 