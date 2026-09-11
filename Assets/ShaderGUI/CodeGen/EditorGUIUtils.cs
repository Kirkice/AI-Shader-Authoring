using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace ShaderGUIGenerator
{
    public static class EditorGUIUtils
    {
        #region Constants
        public const string SHADER_DOC_URL = "https://moonton.feishu.cn/wiki/Lbtlw3eUtizeJwkbqckcCak9ngd";
        public const string SHADER_GUI_DOC_URL = "https://moonton.feishu.cn/wiki/QytUwpQhfiN1h2kVO7gcbv5Nn0D";
        public const string BRDF_LUT_PATH = "Assets/Art/Characters/1_Theseus/Lut_BRDF_NoCompress.png";
        public const string SPECULAR_OCCLUSION_LUT_PATH = "Assets/Art/Characters/1_Theseus/Lut_SpecularOcclusion_NoCompress.png";
        public const string LUT_ACES_PATH = "Assets/Art/Characters/1_Theseus/Lut_ACES_Adjustmment_NoCompress.png";
        public const string LUT_CUSTOM_PATH = "Assets/Art/Characters/1_Theseus/Lut_Custom_NoCompress.png";
        #endregion

        #region GUI Methods
        public static bool Foldout(bool display, string title)
        {
            var style = new GUIStyle("ShurikenModuleTitle");
            style.font = new GUIStyle(EditorStyles.boldLabel).font;
            style.border = new RectOffset(15, 7, 4, 4);
            style.fixedHeight = 22;
            style.contentOffset = new Vector2(20f, -2f);

            var rect = GUILayoutUtility.GetRect(16f, 22f, style);
            GUI.Box(rect, title, style);

            var e = Event.current;

            var toggleRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
            if (e.type == EventType.Repaint)
            {
                EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
            }

            return display;
        }

        public static void BoxGUI(System.Action callback, int paddingH = 3, int paddingV = 3)
        {
            using (new GUILayout.HorizontalScope(GUI.skin.textField))
            {
                GUILayout.Space(paddingH);
                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Space(paddingV);
                    callback.Invoke();
                    GUILayout.Space(paddingV);
                }
                
                GUILayout.Space(paddingH);
            }
        }

        public static void DrawWarning(Material material)
        {
            string propertyName = ShaderUtil.GetPropertyName(material.shader, 0);
            if (propertyName.Equals("_warning"))
            {
                string warningLabel = ShaderUtil.GetPropertyDescription(material.shader, 0);
                
                GUIStyle alterStyle = new GUIStyle();
                alterStyle.fontStyle = FontStyle.Bold;
                alterStyle.fontSize = 15;
                alterStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                alterStyle.normal.background = MakeTex(2, 2, new Color(0.7f, 0.3f, 0.3f, 0.5f));
                alterStyle.wordWrap = true;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("注意 :\n" + warningLabel, alterStyle);
                EditorGUILayout.EndHorizontal();
            }
        }

        public static void OpenManualLink(Material material)
        {
            // 警告 ⚠
            DrawWarning(material);
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Shader文档"))
            {
                Application.OpenURL(SHADER_DOC_URL);
            }
            
            if (GUILayout.Button("GUI文档"))
            {
                Application.OpenURL(SHADER_GUI_DOC_URL);
            }
            EditorGUILayout.EndHorizontal();
        }

        public static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i)
            {
                pix[i] = col;
            }
            
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
        
        public static float[] ExtractAllNumbersToFloatArray(string inputStrings)
        {
            List<float> result = new List<float>();
            string pattern = @"[-+]?\d*\.?\d+";
    
            MatchCollection matches = Regex.Matches(inputStrings, pattern);
            foreach (Match match in matches)
            {
                if (float.TryParse(match.Value, out float number))
                {
                    result.Add(number);
                }
            }
    
            return result.ToArray();
        }
        #endregion
    }
} 