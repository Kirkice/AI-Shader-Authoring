using System.Collections.Generic;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 一次注释标记解析的完整结果。
    /// 取代旧实现里跨次复用的静态可变状态（原 <c>Constants</c> 的 WarningText / FeatureDescribeText 等），
    /// 每次解析产生独立实例，杜绝上一个 Shader 的标记污染下一个 Shader。
    /// </summary>
    public sealed class ShaderParseResult
    {
        /// <summary>解析是否成功。标记拼写错误、组名不合规等都会置为 false。</summary>
        public bool Success = true;

        /// <summary>检测到的 Unity 原生属性标记（<c>[Toggle]</c> / <c>[KeywordEnum</c> / <c>[ToggleOff]</c>），空串表示无冲突。</summary>
        public string UnsupportedAttribute = "";

        /// <summary>功能描述；为空时 GUI 应拒绝接管以免出现空白 Inspector。</summary>
        public string FeatureDescription = "";

        /// <summary>警告文本。</summary>
        public string WarningText = "";

        /// <summary>警告文本的单色版本（用于叠加显示，当前未渲染）。</summary>
        public string WarningMono = "";

        /// <summary>警告展示时长（秒），-1 表示常驻。</summary>
        public float WarningTimer = -1f;

        public readonly List<ShaderGroupProperty> Groups = new List<ShaderGroupProperty>();

        /// <summary>是否包含可用的功能描述。</summary>
        public bool HasFeatureDescription => !string.IsNullOrEmpty(FeatureDescription);
    }
}
