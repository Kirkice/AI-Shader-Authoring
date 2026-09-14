using UnityEditor;
using UnityEngine;

namespace MarkupShaderGUI
{
    /// <summary>
    /// 折叠组标题右侧的上下文菜单：复制 / 粘贴整组属性。
    /// </summary>
    internal static class MarkupShaderGUIContextMenu
    {
        /// <summary>
        /// 写入剪贴板数据里的 GUI 类名标识。
        /// 粘贴时用它校验数据来源，改名后旧数据会自动失效而不是错粘。
        /// </summary>
        public const string ClipboardClassName = "MarkupShaderGUI";

        /// <summary>
        /// 在鼠标位置弹出菜单。
        /// <paramref name="folderId"/> 用于区分组，<paramref name="folderDisplayName"/> 仅用于菜单文案。
        /// </summary>
        public static void Show(
            Material material,
            string shaderPath,
            string folderId,
            string folderDisplayName,
            MaterialProperty[] properties)
        {
            if (material == null)
                return;

            var menu = new GenericMenu();

            menu.AddItem(
                new GUIContent("复制 (" + folderDisplayName + ")"),
                false,
                () =>
                {
                    FolderPropertyClipboardData data = FolderPropertySerializer.CaptureFolder(
                        material,
                        material.shader != null ? material.shader.name : string.Empty,
                        ClipboardClassName,
                        folderId,
                        folderDisplayName,
                        properties);

                    FolderPropertySerializer.CopyToClipboard(data);
                });

            if (FolderPropertySerializer.CanPaste(ClipboardClassName, folderId))
            {
                menu.AddItem(
                    new GUIContent("粘贴 (" + folderDisplayName + ")"),
                    false,
                    () =>
                    {
                        FolderPropertyClipboardData data =
                            FolderPropertySerializer.ReadFromClipboard(ClipboardClassName);

                        Undo.RecordObject(material, "粘贴组属性: " + folderDisplayName);
                        FolderPropertySerializer.ApplyToMaterial(material, data);
                        EditorUtility.SetDirty(material);
                    });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("粘贴 (无有效数据)"));
            }

            menu.ShowAsContext();
        }
    }
}
