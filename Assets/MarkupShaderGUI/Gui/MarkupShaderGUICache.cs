using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 按 Shader 资源缓存解析结果，避免 Inspector 每帧重复读盘与正则解析。
    /// 只负责缓存，不负责读取与解析。
    /// </summary>
    public static class MarkupShaderGUICache
    {
        private static readonly Dictionary<Shader, MarkupShaderGUIData> Cache =
            new Dictionary<Shader, MarkupShaderGUIData>();

        public static bool TryGet(Shader shader, out MarkupShaderGUIData data)
        {
            data = null;
            return shader != null && Cache.TryGetValue(shader, out data);
        }

        public static void Set(Shader shader, MarkupShaderGUIData data)
        {
            if (shader == null || data == null)
                return;

            Cache[shader] = data;
        }

        public static void Invalidate(Shader shader)
        {
            if (shader != null)
                Cache.Remove(shader);
        }

        /// <summary>
        /// 按资源路径失效缓存。
        /// Shader 资产被删除或移动后无法再通过 <see cref="Shader"/> 引用定位，
        /// 只能按路径比对缓存中记录的 <see cref="MarkupShaderGUIData.ShaderPath"/>。
        /// </summary>
        public static void InvalidateByPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || Cache.Count == 0)
                return;

            var stale = new List<Shader>();
            foreach (KeyValuePair<Shader, MarkupShaderGUIData> pair in Cache)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    stale.Add(pair.Key);
                    continue;
                }

                if (string.Equals(pair.Value.ShaderPath, assetPath, System.StringComparison.OrdinalIgnoreCase))
                    stale.Add(pair.Key);
            }

            foreach (Shader shader in stale)
                Cache.Remove(shader);
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }

    /// <summary>
    /// Shader 资产导入 / 删除 / 移动后失效对应缓存。
    /// 旧的实现只在导入时失效，删除与移动会留下过期条目。
    /// </summary>
    public sealed class MarkupShaderGUIAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            InvalidateForPaths(importedAssets);
            InvalidateForPaths(deletedAssets);
            InvalidateForPaths(movedFromAssetPaths);
        }

        private static void InvalidateForPaths(string[] paths)
        {
            if (paths == null)
                return;

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith(".shader", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MarkupShaderGUICache.InvalidateByPath(path);
            }
        }
    }
}
