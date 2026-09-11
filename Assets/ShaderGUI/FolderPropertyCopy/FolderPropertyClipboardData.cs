using System;
using System.Collections.Generic;
using UnityEngine;

namespace CodeGenShaderGUI
{
    /// <summary>
    /// Folder 属性复制粘贴的剪贴板数据格式。
    /// 序列化为 JSON 存入 GUIUtility.systemCopyBuffer。
    /// </summary>
    [Serializable]
    public class FolderPropertyClipboardData
    {
        /// <summary>剪贴板内容识别标识，用于快速校验</summary>
        public const string MAGIC = "THESEUS_FOLDER_PROPERTY_COPY_v1";

        public string magic;
        /// <summary>Shader 资源名，如 "Theseus/PBR_Hair_WidthControl"</summary>
        public string shaderName;
        /// <summary>ShaderGUI 类名，如 "Theseus_PBR_Hair_WidthControlGUI"</summary>
        public string shaderClassName;
        /// <summary>Folder 标识 key，如 "BaseSettings"</summary>
        public string folderId;
        /// <summary>Folder 显示名，如 "基础设置"</summary>
        public string folderDisplayName;
        /// <summary>文件夹内所有属性的序列化值</summary>
        public List<SerializedPropertyEntry> properties;
    }

    /// <summary>
    /// 单个 Shader 属性的序列化条目。包含所有可能类型的数据字段，
    /// 粘贴时按 type 字段选择使用哪个值。
    /// </summary>
    [Serializable]
    public class SerializedPropertyEntry
    {
        /// <summary>Shader uniform 名，如 "_albedoColor"</summary>
        public string uniform;

        /// <summary>(int)ShaderPropertyType: 0=Float, 1=Range, 2=Color, 3=Vector, 4=Texture</summary>
        public int type;

        /// <summary>Float/Range 类型的值</summary>
        public float floatValue;

        /// <summary>Color 类型: [r, g, b, a]</summary>
        public float[] colorValue;

        /// <summary>Vector 类型: [x, y, z, w]</summary>
        public float[] vectorValue;

        /// <summary>Texture 的 AssetDatabase GUID，使用 GUID 以避免资产重命名导致引用丢失</summary>
        public string textureGuid;

        /// <summary>Texture 的 Tiling: [x, y]</summary>
        public float[] textureScale;

        /// <summary>Texture 的 Offset: [x, y]</summary>
        public float[] textureOffset;
    }
}
