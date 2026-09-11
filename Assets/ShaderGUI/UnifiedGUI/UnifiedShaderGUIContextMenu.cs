using UnityEditor;
using UnityEngine;
using ShaderGUIGenerator;
using CodeGenShaderGUI;

namespace UnifiedShaderGUI
{
    internal static class UnifiedShaderGUIContextMenu
    {
        public static void Show(
            Material material,
            string shaderPath,
            string folderId,
            string folderDisplayName,
            MaterialProperty[] properties)
        {
            var menu = new GenericMenu();
            const string clipboardClassName = "UnifiedShaderGUI";

            menu.AddItem(
                new GUIContent("复制 (" + folderDisplayName + ")"),
                false,
                () =>
                {
                    var data = FolderPropertySerializer.CaptureFolder(
                        material,
                        material.shader.name,
                        clipboardClassName,
                        folderId,
                        folderDisplayName,
                        properties);
                    FolderPropertySerializer.CopyToClipboard(data);
                });

            if (FolderPropertySerializer.CanPaste(clipboardClassName, folderId))
            {
                menu.AddItem(
                    new GUIContent("粘贴 (" + folderDisplayName + ")"),
                    false,
                    () =>
                    {
                        var data = FolderPropertySerializer.ReadFromClipboard(clipboardClassName);
                        Undo.RecordObject(material, "粘贴文件夹属性: " + folderDisplayName);
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