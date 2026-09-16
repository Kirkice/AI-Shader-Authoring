using System.Collections.Generic;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 注释标记解析出的数据模型。
    /// 本文件只描述结构，不依赖 UnityEditor，可被解析层与 GUI 层共同使用。
    /// </summary>
    /// <summary>Shader Properties 块中的一条属性。</summary>
    public class ShaderProperty
    {
        /// <summary>Shader uniform 名，如 <c>_albedo</c>。</summary>
        public string variable;

        /// <summary>Inspector 上显示的属性名。</summary>
        public string variableName;

        /// <summary>是否启用 Tiling / Offset（等价于缺少 [NoScaleOffset]）。</summary>
        public bool isFlag = false;

        public VariableType variableType = VariableType.Undefine;

        /// <summary>标记在 Shader 中的行号，-1 表示未记录。</summary>
        public int Index = -1;
    }

    /// <summary>一个注释开关标记。</summary>
    public class ShaderToggle
    {
        public ToggleType toggleType = ToggleType.uniform;
        public string toggleName;
        public string variable;

        /// <summary>向量通道，如 <c>x</c> / <c>r</c>；空串表示整变量。</summary>
        public string channel = "";

        public int Index = -1;
    }

    /// <summary>一个注释枚举标记。</summary>
    public class ShaderEnum
    {
        public string enumName;
        public string enumNameCN;
        public string variable;
        public string channel = "";

        /// <summary>枚举显示项。</summary>
        public List<string> enumValues = new List<string>();

        /// <summary>与 <see cref="enumValues"/> 一一对应的数值字符串。</summary>
        public List<string> enumValuesNumber = new List<string>();

        public int Index = -1;
    }

    /// <summary>一个折叠组。</summary>
    public class ShaderGroupProperty
    {
        /// <summary>组名中的英文部分，同时作为折叠状态与剪贴板的 key。</summary>
        public string GroupName;

        /// <summary>组名中的中文部分，Inspector 上的显示名。</summary>
        public string GroupNameCN;

        /// <summary>组内小标题。</summary>
        public string Label;

        public List<ShaderProperty> groupShaderPropList = new List<ShaderProperty>();

        /// <summary>组级开关（GroupUniform / GroupKeyWords），关闭时整组不绘制。</summary>
        public ShaderToggle groupToggle = null;

        public List<ShaderToggle> toggleList = new List<ShaderToggle>();
        public List<ShaderEnum> enumList = new List<ShaderEnum>();
    }

    /// <summary>属性类型，决定 Inspector 上使用哪种绘制方式。</summary>
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

    /// <summary>开关类型，决定开关操作的是 uniform 还是关键字。</summary>
    public enum ToggleType
    {
        uniform = 0,
        keywords = 1,
        group_uniform = 2,
        group_keywords = 3,
    }

}
