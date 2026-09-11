namespace ShaderGUIGenerator
{
    /// <summary>
    /// 常量定义
    /// </summary>
    public static class Constants
    {
        //  一个Shader只能存在一个警告
        public static string WarningText = "";
        public static string WarningMono = "";
        public static float WarningTimer = -1.0f;
        public static string FeatureDescribeText = "";
        public static bool drawOcclusionLutTextureGUI = false;
        public static bool drawACESTextureGUI = false;
        public static bool drawBRDFLutTextureGUI = false;
        public static string MiddleQueue = "2450";
        public static int lineStart = -1;
        public static int lineEnd = -1;

        //TextureSplit
        public static int TextureSplitCount = 0;

        //  Path
        public static readonly string combineTexCSPath = "Assets/Editor/Theseus/ShaderGUI/CodeGen/GUITextureCombineCS.compute";
        
        #region Shader Parsing Constants
        public static class ShaderParsing
        {
            public const string GROUP_START_PATTERN = @"//\s*#\s*GroupStart:(.+)";
            public const string GROUP_END_PATTERN = @"//\s*#\s*GroupEnd";
            public const string LABEL_PATTERN = @"//\s*#\s*Label:(.+)";
            public const string TOGGLE_PATTERN = @"//\s*#\s*Toggle:(.+)";
            public const string ENUM_PATTERN = @"//\s*#\s*Enum_(.+)";
            public const string BLEND_PATTERN = @"//\s*#\s*Blend:(.+)";
            public const string VECTOR_SPLIT_PATTERN = @"//\s*#\s*VectorSplit:(.+)";
            public const string PROPERTIES_BLOCK_PATTERN = @"Properties\s*\{([^{}]*(?:\{(?:[^{}]*)\}[^{}]*)*)\}";
        }
        #endregion
        
        #region Code Generation Constants
        public static class CodeGeneration
        {
            public const string DEFAULT_NAMESPACE = "CodeGenShaderGUI";
            public const string DEFAULT_BASE_CLASS = "ShaderGUI";
            public const string MATERIAL_EDITOR_VAR = "m_MaterialEditor";
            public const string MATERIAL_VAR = "material";
            public const string SHORT_BUTTON_STYLE = "shortButtonStyle";
            public const string FOLDOUT_VAR_PREFIX = "_";
            public const string FOLDOUT_VAR_SUFFIX = "_Foldout";
        }
        #endregion
        
        #region GUI Constants
        public static class GUI
        {
            public const string BUTTON_OFF_TEXT = "Off";
            public const string BUTTON_ACTIVE_TEXT = "Active";
            public const string BUTTON_SPACE_WIDTH = "60";
            public const string SHORT_BUTTON_WIDTH = "130";
            public const string GROUP_PREFIX = "【";
            public const string GROUP_SUFFIX = "】";
            public const string LABEL_SUFFIX = "：";
        }
        #endregion
        
        #region Error Messages
        public static class ErrorMessages
        {
            public const string NO_SHADER_SELECTED = "请先选择一个Shader文件";
            public const string INVALID_SHADER_OBJECT = "无法获取Shader对象";
            public const string INVALID_SHADER_PATH = "Shader文件路径无效";
            public const string INVALID_GROUP_NAME = "ShaderGUI组名编写存在问题，中文和英文未使用_拆开，请检查。";
            public const string INVALID_SHADER_FORMAT = "Shader格式不正确，无法添加CustomEditor";
            public const string CUSTOM_EDITOR_ADD_FAILED = "添加CustomEditor到Shader时发生错误";
        }
        #endregion
        
        #region Warning Messages
        public static class WarningMessages
        {
            public const string EXISTING_CUSTOM_EDITOR = "Shader存在其他CustomEditor声明，将被替换";
        }
        #endregion
        
        #region Success Messages
        public static class SuccessMessages
        {
            public const string CUSTOM_EDITOR_ADDED = "已为Shader添加CustomEditor声明";
            public const string CORRECT_CUSTOM_EDITOR_EXISTS = "Shader已经包含正确的CustomEditor声明";
        }
        #endregion
    }
} 