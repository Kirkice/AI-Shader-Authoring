using System.IO;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// Shader 源码读取器。职责单一：只负责把 Shader 资产取成字符串。
    /// 缓存与解析编排分别在 <see cref="MarkupShaderGUICache"/> 与
    /// <see cref="MarkupShaderGUIDataFactory"/> 中，避免再次出现一个类同时管三件事。
    /// </summary>
    public static class MarkupShaderGUIResourceLoader
    {
        /// <summary>读取 Shader 源码；失败时返回 null 并给出 <paramref name="error"/>。</summary>
        public static string ReadShaderSource(Shader shader, out string error)
        {
            string assetPath = shader == null ? null : AssetDatabase.GetAssetPath(shader);
            return ReadShaderSource(assetPath, out error);
        }

        /// <summary>按资源路径读取 Shader 源码；失败时返回 null 并给出 <paramref name="error"/>。</summary>
        public static string ReadShaderSource(string assetPath, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(assetPath))
            {
                error = "Shader 资源路径为空。";
                return null;
            }

            if (!File.Exists(assetPath))
            {
                error = "找不到 Shader 文件：" + assetPath;
                return null;
            }

            try
            {
                return File.ReadAllText(assetPath);
            }
            catch (IOException exception)
            {
                error = "读取 Shader 文件失败：" + assetPath + " （" + exception.Message + "）";
                return null;
            }
            catch (System.UnauthorizedAccessException exception)
            {
                error = "无权读取 Shader 文件：" + assetPath + " （" + exception.Message + "）";
                return null;
            }
        }
    }
}
