using System.Text.RegularExpressions;

namespace MarkupShaderGUI
{
    /// <summary>
    /// Shader 注释标记（Markup）的语法常量。
    /// 标记写在 Shader 的 Properties 块内，形如 <c>// # GroupStart:中文名_EnglishName</c>，
    /// 由 <see cref="ShaderMarkupParser"/> 解析成 GUI 可渲染的结构。
    /// </summary>
    public static class ShaderMarkupConstants
    {
        /// <summary>Properties 块，允许一层嵌套花括号（Range / 自定义属性）。</summary>
        public static readonly Regex PropertiesBlock = new Regex(
            @"Properties\s*\{([^{}]*(?:\{(?:[^{}]*)\}[^{}]*)*)\}",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>组开始：<c>// # GroupStart:基础设置_BaseSettings</c></summary>
        public static readonly Regex GroupStart =
            new Regex(@"//\s*#\s*GroupStart:(.+)", RegexOptions.Compiled);

        /// <summary>组结束：<c>// # GroupEnd</c></summary>
        public static readonly Regex GroupEnd =
            new Regex(@"//\s*#\s*GroupEnd(.*)", RegexOptions.Compiled);

        /// <summary>组内小标题：<c>// # Label:小标题</c></summary>
        public static readonly Regex Label =
            new Regex(@"//\s*#\s*Label:(.+)", RegexOptions.Compiled);

        /// <summary>开关：<c>// # Toggle:Uniform:显示名:_uniform</c>，类型见 <see cref="ToggleType"/> 的四种写法。</summary>
        public static readonly Regex Toggle =
            new Regex(@"//\s*#\s*Toggle:(.+)", RegexOptions.Compiled);

        /// <summary>枚举：<c>// # Enum_名称:中文名:_uniform:取值A|取值B=3</c></summary>
        public static readonly Regex Enum =
            new Regex(@"//\s*#\s*Enum_(.+)", RegexOptions.Compiled);

        /// <summary>渲染模式：<c>// # Blend:默认:...</c></summary>
        public static readonly Regex Blend =
            new Regex(@"//\s*#\s*Blend:(.+)", RegexOptions.Compiled);

        /// <summary>向量拆分（已解析，当前 GUI 尚未渲染）：<c>// # VectorSplit:...</c></summary>
        public static readonly Regex VectorSplit =
            new Regex(@"//\s*#\s*VectorSplit:(.+)", RegexOptions.Compiled);

        /// <summary>警告：<c>// # Warning:提示文本:单色名:秒数</c></summary>
        public static readonly Regex Warning =
            new Regex(@"//\s*#\s*Warning:(.+)", RegexOptions.Compiled);

        /// <summary>功能描述（必填，缺失时 GUI 拒绝接管）：<c>// # FeatureDes:描述文本</c></summary>
        public static readonly Regex FeatureDescription =
            new Regex(@"//\s*#\s*FeatureDes:(.+)", RegexOptions.Compiled);

        /// <summary>兜底匹配：以 <c>// #</c> 开头但 Flag 拼写错误。</summary>
        public static readonly Regex UnknownMarker =
            new Regex(@"//\s*#\s*(.+)", RegexOptions.Compiled);

        /// <summary>标准属性行：<c>_Name("显示名", Type)</c>。</summary>
        public static readonly Regex ShaderProperty =
            new Regex(@"_([a-zA-Z0-9_]+)\s*\(""([^""]+)"",\s*([^)]+)\)", RegexOptions.Compiled);

        /// <summary>Unity 原生属性标记，剔除后再解析属性类型（旧的 CodeGen 兼容路径）。</summary>
        public static readonly Regex NativePropertyAttribute =
            new Regex(@"\[\s*(Toggle|Enum|KeywordEnum)[^\]]*\]", RegexOptions.Compiled);

        /// <summary>这些 Unity 原生属性标记与注释标记语义冲突，出现即拒绝解析。</summary>
        public static readonly string[] UnsupportedPropertyAttributes =
        {
            "[Toggle]",
            "[KeywordEnum",
            "[ToggleOff]"
        };

        public const string HideInInspectorAttribute = "[HideInInspector]";
        public const string NoScaleOffsetAttribute = "[NoScaleOffset]";

        /// <summary>折叠组标题的包裹符，渲染为「【基础设置】」。</summary>
        public const string GroupTitlePrefix = "【";
        public const string GroupTitleSuffix = "】";

        public const string InvalidGroupNameMessage = "ShaderGUI组名编写存在问题，中文和英文未使用_拆开，请检查。";
        public const string UnsupportedAttributeMessage = "Unity 原生属性标记会与注释标记冲突，请移除该标记或改写为注释标记。";
    }
}
