using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// Folder 属性的序列化 / 反序列化，以及剪贴板读写。
    /// 纯静态工具类，与具体 ShaderGUI 解耦，可被任意 ShaderGUI 复用。
    /// </summary>
    public static class FolderPropertySerializer
    {
        /// <summary>
        /// 从 Material 中读取指定 MaterialProperty 数组的值，构建剪贴板数据。
        /// </summary>
        public static FolderPropertyClipboardData CaptureFolder(
            Material material,
            string shaderName,
            string shaderClassName,
            string folderId,
            string folderDisplayName,
            MaterialProperty[] properties)
        {
            var data = new FolderPropertyClipboardData
            {
                magic = FolderPropertyClipboardData.MAGIC,
                shaderName = shaderName,
                shaderClassName = shaderClassName,
                folderId = folderId,
                folderDisplayName = folderDisplayName,
                properties = new List<SerializedPropertyEntry>()
            };

            if (material == null || properties == null)
                return data;

            foreach (MaterialProperty prop in properties)
            {
                if (prop == null)
                    continue;

                var entry = new SerializedPropertyEntry
                {
                    uniform = prop.name,
                    type = (int)prop.type
                };

                switch (prop.type)
                {
                    case MaterialProperty.PropType.Float:
                    case MaterialProperty.PropType.Range:
                        entry.floatValue = material.GetFloat(prop.name);
                        break;

                    case MaterialProperty.PropType.Color:
                        Color color = material.GetColor(prop.name);
                        entry.colorValue = new float[] { color.r, color.g, color.b, color.a };
                        break;

                    case MaterialProperty.PropType.Vector:
                        Vector4 vector = material.GetVector(prop.name);
                        entry.vectorValue = new float[] { vector.x, vector.y, vector.z, vector.w };
                        break;

                    case MaterialProperty.PropType.Texture:
                        Texture texture = material.GetTexture(prop.name);
                        if (texture != null)
                        {
                            string assetPath = AssetDatabase.GetAssetPath(texture);
                            if (!string.IsNullOrEmpty(assetPath))
                                entry.textureGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        }

                        Vector2 textureScale = material.GetTextureScale(prop.name);
                        Vector2 textureOffset = material.GetTextureOffset(prop.name);
                        entry.textureScale = new float[] { textureScale.x, textureScale.y };
                        entry.textureOffset = new float[] { textureOffset.x, textureOffset.y };
                        break;
                }

                data.properties.Add(entry);
            }

            return data;
        }

        /// <summary>将数据序列化为 JSON 并写入系统剪贴板。</summary>
        public static void CopyToClipboard(FolderPropertyClipboardData data)
        {
            if (data == null)
                return;

            GUIUtility.systemCopyBuffer = JsonUtility.ToJson(data, prettyPrint: false);
        }

        /// <summary>
        /// 从剪贴板读取并验证数据，验证失败或格式不匹配时返回 null。
        /// </summary>
        /// <param name="expectedClassName">期望的 ShaderGUI 类名标识，用于校验数据来源。</param>
        public static FolderPropertyClipboardData ReadFromClipboard(string expectedClassName)
        {
            string clipboardText = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboardText))
                return null;

            FolderPropertyClipboardData data;
            try
            {
                data = JsonUtility.FromJson<FolderPropertyClipboardData>(clipboardText);
            }
            catch (Exception)
            {
                return null;
            }

            if (data == null)
                return null;

            if (data.magic != FolderPropertyClipboardData.MAGIC)
                return null;

            if (data.shaderClassName != expectedClassName)
                return null;

            if (data.properties == null || data.properties.Count == 0)
                return null;

            return data;
        }

        /// <summary>
        /// 检查剪贴板中是否有针对指定 folder 的有效粘贴数据。
        /// 比 <see cref="ReadFromClipboard"/> 多一层 folderId 匹配检查。
        /// </summary>
        public static bool CanPaste(string expectedClassName, string expectedFolderId)
        {
            FolderPropertyClipboardData data = ReadFromClipboard(expectedClassName);
            return data != null && data.folderId == expectedFolderId;
        }

        /// <summary>
        /// 将剪贴板数据应用到目标 Material。调用者负责 <c>Undo.RecordObject</c>。
        /// </summary>
        public static void ApplyToMaterial(Material material, FolderPropertyClipboardData data)
        {
            if (material == null || data == null || data.properties == null)
                return;

            foreach (SerializedPropertyEntry entry in data.properties)
            {
                if (entry == null || string.IsNullOrEmpty(entry.uniform))
                    continue;

                // 确保目标 material 有此属性。
                if (!material.HasProperty(entry.uniform))
                {
                    Debug.LogWarning($"[MarkupShaderGUI] 目标材质没有属性 {entry.uniform}，已跳过。");
                    continue;
                }

                switch ((UnityEngine.Rendering.ShaderPropertyType)entry.type)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        material.SetFloat(entry.uniform, entry.floatValue);
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        if (entry.colorValue != null && entry.colorValue.Length >= 4)
                        {
                            material.SetColor(entry.uniform, new Color(
                                entry.colorValue[0],
                                entry.colorValue[1],
                                entry.colorValue[2],
                                entry.colorValue[3]));
                        }
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        if (entry.vectorValue != null && entry.vectorValue.Length >= 4)
                        {
                            material.SetVector(entry.uniform, new Vector4(
                                entry.vectorValue[0],
                                entry.vectorValue[1],
                                entry.vectorValue[2],
                                entry.vectorValue[3]));
                        }
                        break;

                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        Texture texture = null;
                        if (!string.IsNullOrEmpty(entry.textureGuid))
                        {
                            string assetPath = AssetDatabase.GUIDToAssetPath(entry.textureGuid);
                            if (!string.IsNullOrEmpty(assetPath))
                                texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
                        }

                        material.SetTexture(entry.uniform, texture);

                        if (entry.textureScale != null && entry.textureScale.Length >= 2)
                            material.SetTextureScale(entry.uniform, new Vector2(entry.textureScale[0], entry.textureScale[1]));

                        if (entry.textureOffset != null && entry.textureOffset.Length >= 2)
                            material.SetTextureOffset(entry.uniform, new Vector2(entry.textureOffset[0], entry.textureOffset[1]));
                        break;
                }
            }
        }
    }
}
