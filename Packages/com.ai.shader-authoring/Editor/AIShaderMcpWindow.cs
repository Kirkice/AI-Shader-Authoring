#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    public sealed class AIShaderMcpWindow : EditorWindow
    {
        private enum ResultState { Empty, Loading, Success, Error }

        private sealed class ToolInfo
        {
            public string Id;
            public string Group;
            public string Summary;
            public string Description;
            public bool IsReadOnly;
        }

        private const float ToolbarHeight = 24f;
        private const float MinimumNavigatorWidth = 190f;
        private const float MaximumNavigatorWidth = 360f;
        private const float MinimumResultHeight = 130f;
        private const float InspectorHeight = 170f;
        private readonly List<ToolInfo> tools = new List<ToolInfo>();
        private readonly Dictionary<string, bool> groupExpanded = new Dictionary<string, bool>();

        private float navigatorWidth = 260f;
        private float resultHeight;
        private bool draggingNavigator;
        private bool draggingResult;
        private int selectedTool;
        private string search = string.Empty;
        private Vector2 navigatorScroll;
        private Vector2 resultScroll;
        private int formatIndex;
        private bool includeScripts;
        private bool includeHierarchy = true;
        private string commandCode = "Debug.Log(\"Hello from Unity MCP\");";
        private ResultState resultState;
        private string resultText = string.Empty;
        private string errorText = string.Empty;
        private string resultTimestamp = string.Empty;
        private long elapsedMilliseconds;
        private int resultView;
        private bool running;

        private GUIStyle toolbarLabel;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle mutedStyle;
        private GUIStyle sectionStyle;
        private GUIStyle toolNameStyle;
        private GUIStyle toolSelectedStyle;
        private GUIStyle codeStyle;
        private GUIStyle statusStyle;
        private GUIStyle groupHeaderStyle;
        private GUIStyle runButtonStyle;
        private Texture2D selectedTexture;
        private Texture2D accentTexture;
        private Texture2D splitterTexture;
        private Texture2D codeTexture;
        private Texture2D toolbarTexture;

        [MenuItem("Unity MCP/Dashboard", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<AIShaderMcpWindow>("Unity MCP");
            window.minSize = new Vector2(760f, 440f);
            window.Show();
        }

        private void OnEnable()
        {
            tools.Clear();
            tools.Add(new ToolInfo { Id = "get_editor_state", Group = "状态", Summary = "读取当前 Editor 上下文", Description = "读取当前 Unity Editor 上下文，包括场景、层级、选择对象与播放状态。", IsReadOnly = true });
            tools.Add(new ToolInfo { Id = "execute_editor_command", Group = "命令", Summary = "执行 C# Editor 命令", Description = "在 Unity Editor 上下文中编译并执行 C# 命令。", IsReadOnly = false });
            tools.Add(new ToolInfo { Id = "get_logs", Group = "诊断", Summary = "读取本地 Console 日志", Description = "读取 Unity Console 的本地日志缓冲区。", IsReadOnly = true });
            groupExpanded["状态"] = true;
            groupExpanded["命令"] = true;
            groupExpanded["诊断"] = true;
            var availableHeight = position.height - ToolbarHeight;
            resultHeight = Mathf.Clamp(availableHeight * 0.65f, MinimumResultHeight, Mathf.Max(MinimumResultHeight, availableHeight - InspectorHeight));
            CreateStyles();
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
            DestroyTexture(ref selectedTexture);
            DestroyTexture(ref accentTexture);
            DestroyTexture(ref splitterTexture);
            DestroyTexture(ref codeTexture);
            DestroyTexture(ref toolbarTexture);
        }

        private void OnGUI()
        {
            if (toolbarLabel == null) CreateStyles();
            HandleSplitters();
            DrawToolbar();

            var contentY = ToolbarHeight;
            var contentHeight = position.height - ToolbarHeight;
            DrawNavigator(new Rect(0f, contentY, navigatorWidth, contentHeight));
            DrawVerticalSplitter(new Rect(navigatorWidth, contentY, 1f, contentHeight));
            DrawMain(new Rect(navigatorWidth + 1f, contentY, position.width - navigatorWidth - 1f, contentHeight));
        }

        private void DrawToolbar()
        {
            GUI.DrawTexture(new Rect(0f, 0f, position.width, ToolbarHeight), toolbarTexture);
            var endpoint = UnityMcpConnection.ServerUri;
            GUI.Label(new Rect(6f, 3f, 76f, 18f), "UNITY MCP", toolbarLabel);
            DrawToolbarSeparator(85f);
            GUI.Label(new Rect(92f, 3f, 140f, 18f), Application.productName, mutedStyle);
            DrawToolbarSeparator(238f);

            var state = UnityMcpConnection.State;
            var stateText = state == UnityMcpConnection.ServiceState.Connected ? "已连接" : state == UnityMcpConnection.ServiceState.WaitingForConnection ? "连接中" : "连接失败";
            var stateColor = state == UnityMcpConnection.ServiceState.Connected ? new Color(0.36f, 0.78f, 0.44f) : state == UnityMcpConnection.ServiceState.WaitingForConnection ? new Color(0.92f, 0.68f, 0.25f) : new Color(0.89f, 0.36f, 0.34f);
            EditorGUI.DrawRect(new Rect(248f, 9f, 6f, 6f), stateColor);
            GUI.Label(new Rect(260f, 3f, 54f, 18f), stateText, new GUIStyle(statusStyle) { normal = { textColor = stateColor } });
            DrawToolbarSeparator(319f);
            GUI.Label(new Rect(326f, 3f, 154f, 18f), endpoint.Host + ":" + endpoint.Port + "  WebSocket", mutedStyle);

            var actionWidth = 68f;
            var actionX = position.width - actionWidth - 29f;
            var active = UnityMcpConnection.IsServiceEnabled;
            if (GUI.Button(new Rect(actionX, 2f, actionWidth, 20f), active ? "断开" : "连接", EditorStyles.toolbarButton))
            {
                if (active) UnityMcpConnection.StopService();
                else UnityMcpConnection.StartService();
            }
            DrawToolbarSeparator(position.width - 25f);
            var settingsContent = EditorGUIUtility.IconContent("SettingsIcon");
            settingsContent.tooltip = "打开项目设置";
            if (GUI.Button(new Rect(position.width - 22f, 2f, 20f, 20f), settingsContent, EditorStyles.toolbarButton))
                SettingsService.OpenProjectSettings("Project/Player");
        }

        private void DrawNavigator(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.19f));
            var searchRect = new Rect(rect.x + 5f, rect.y + 3f, rect.width - 10f, 19f);
            search = EditorGUI.TextField(searchRect, search, EditorStyles.toolbarSearchField);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 25f, rect.width, 1f), new Color(0.11f, 0.11f, 0.11f));

            var scrollRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            navigatorScroll = GUI.BeginScrollView(scrollRect, navigatorScroll, new Rect(0f, 0f, rect.width - 14f, CalculateNavigatorHeight()));
            var y = 4f;
            foreach (var group in new[] { "状态", "命令", "诊断" })
            {
                var visible = GetGroupTools(group);
                if (visible.Count == 0) continue;
                groupExpanded[group] = EditorGUI.Foldout(new Rect(7f, y, rect.width - 22f, 18f), groupExpanded[group], group, true, groupHeaderStyle);
                y += 20f;
                if (!groupExpanded[group]) continue;

                foreach (var index in visible)
                {
                    DrawToolRow(new Rect(4f, y, rect.width - 18f, 42f), tools[index], index == selectedTool, index);
                    y += 43f;
                }
                y += 5f;
            }
            GUI.EndScrollView();
        }

        private void DrawToolRow(Rect rect, ToolInfo tool, bool selected, int index)
        {
            if (selected)
            {
                GUI.DrawTexture(rect, selectedTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), accentTexture);
            }
            GUI.Label(new Rect(rect.x + 9f, rect.y + 4f, rect.width - 16f, 17f), tool.Id, selected ? toolSelectedStyle : toolNameStyle);
            GUI.Label(new Rect(rect.x + 9f, rect.y + 21f, rect.width - 16f, 16f), tool.Summary, mutedStyle);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) selectedTool = index;
        }

        private void DrawMain(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f));
            var tool = tools[Mathf.Clamp(selectedTool, 0, tools.Count - 1)];
            var resultTop = rect.yMax - resultHeight;
            DrawInspector(new Rect(rect.x, rect.y, rect.width, resultTop - rect.y - 3f), tool);
            DrawHorizontalSplitter(new Rect(rect.x, resultTop - 2f, rect.width, 3f));
            DrawResultPanel(new Rect(rect.x, resultTop + 1f, rect.width, rect.yMax - resultTop - 1f), tool);
        }

        private void DrawInspector(Rect rect, ToolInfo tool)
        {
            const float LabelWidth = 104f;
            const float HelpWidth = 112f;
            var x = rect.x + 16f;
            var width = rect.width - 32f;
            var labelX = x;
            var controlX = x + LabelWidth;
            var controlAreaWidth = width - LabelWidth;
            var actionY = rect.yMax - 29f;

            GUI.Label(new Rect(x, rect.y + 8f, width, 20f), tool.Id, titleStyle);
            GUI.Label(new Rect(x, rect.y + 29f, width, 18f), tool.Description, descriptionStyle);
            EditorGUI.DrawRect(new Rect(x, rect.y + 53f, width, 1f), new Color(0.12f, 0.12f, 0.12f));
            GUI.Label(new Rect(x, rect.y + 61f, width, 17f), "参数", sectionStyle);

            if (tool.Id == "get_editor_state")
            {
                var showInlineHelp = controlAreaWidth >= 430f;
                var controlWidth = showInlineHelp ? controlAreaWidth - HelpWidth - 8f : controlAreaWidth;
                DrawInspectorLabel(labelX, rect.y + 84f, "Format");
                formatIndex = EditorGUI.Popup(new Rect(controlX, rect.y + 82f, controlWidth, 20f), formatIndex, new[] { "full", "no scripts", "no hierarchy" });
                if (showInlineHelp)
                    GUI.Label(new Rect(controlX + controlWidth + 8f, rect.y + 84f, HelpWidth, 17f), "可选，默认 full", mutedStyle);
                else
                    GUI.Label(new Rect(controlX, rect.y + 104f, controlAreaWidth, 16f), "可选，默认 full", mutedStyle);

                var checkboxY = showInlineHelp ? rect.y + 108f : rect.y + 122f;
                DrawInspectorLabel(labelX, checkboxY, "Options");
                var optionColumnWidth = Mathf.Max(155f, controlAreaWidth * 0.5f);
                includeScripts = EditorGUI.Toggle(new Rect(controlX, checkboxY, 18f, 18f), includeScripts);
                GUI.Label(new Rect(controlX + 23f, checkboxY, optionColumnWidth - 23f, 18f), "Include scripts", mutedStyle);
                includeHierarchy = EditorGUI.Toggle(new Rect(controlX + optionColumnWidth, checkboxY, 18f, 18f), includeHierarchy);
                GUI.Label(new Rect(controlX + optionColumnWidth + 23f, checkboxY, controlAreaWidth - optionColumnWidth - 23f, 18f), "Include hierarchy", mutedStyle);
            }
            else if (tool.Id == "execute_editor_command")
            {
                DrawInspectorLabel(labelX, rect.y + 84f, "Code *");
                commandCode = EditorGUI.TextField(new Rect(controlX, rect.y + 82f, controlAreaWidth, 20f), commandCode);
                GUI.Label(new Rect(controlX, rect.y + 106f, controlAreaWidth, 17f), "将在 Unity Editor 中执行。", mutedStyle);
            }
            else
            {
                DrawInspectorLabel(labelX, rect.y + 84f, "Count");
                GUI.Label(new Rect(controlX, rect.y + 84f, controlAreaWidth, 17f), "使用本地日志缓冲区全部记录", mutedStyle);
            }

            EditorGUI.DrawRect(new Rect(x, actionY - 7f, width, 1f), new Color(0.12f, 0.12f, 0.12f));
            var connected = UnityMcpConnection.IsConnected;
            EditorGUI.BeginDisabledGroup(!connected || running);
            if (GUI.Button(new Rect(x, actionY, 90f, 22f), running ? "运行中…" : "▶  运行工具", runButtonStyle)) RunTool(tool);
            EditorGUI.EndDisabledGroup();
            if (!connected) GUI.Label(new Rect(x + 100f, actionY + 3f, width - 100f, 17f), "服务未连接，无法运行工具。", mutedStyle);
            EditorGUI.DrawRect(new Rect(x, rect.yMax - 1f, width, 1f), new Color(0.12f, 0.12f, 0.12f));
        }

        private void DrawResultPanel(Rect rect, ToolInfo tool)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 23f), toolbarTexture);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, 36f, 18f), "结果", toolbarLabel);
            EditorGUI.DrawRect(new Rect(rect.x + 47f, rect.y + 3f, 1f, 17f), new Color(0.20f, 0.20f, 0.20f));
            resultView = GUI.Toolbar(new Rect(rect.x + 53f, rect.y + 2f, 86f, 20f), resultView, new[] { "JSON", "Tree" }, EditorStyles.toolbarButton);
            if (GUI.Button(new Rect(rect.x + 145f, rect.y + 2f, 30f, 20f), "复制", EditorStyles.toolbarButton)) EditorGUIUtility.systemCopyBuffer = resultText;
            if (GUI.Button(new Rect(rect.x + 177f, rect.y + 2f, 30f, 20f), "清空", EditorStyles.toolbarButton)) ClearResult();
            if (resultState == ResultState.Success) GUI.Label(new Rect(rect.x + 217f, rect.y + 3f, 180f, 18f), elapsedMilliseconds + " ms   " + resultTimestamp, mutedStyle);

            var content = new Rect(rect.x + 16f, rect.y + 23f, rect.width - 32f, rect.height - 23f);
            if (resultState == ResultState.Empty)
            {
                DrawCenteredMessage(content, "尚无运行结果", "运行工具后，结果会显示在此面板中。", false, "›_");
                return;
            }
            if (resultState == ResultState.Loading)
            {
                DrawCenteredMessage(content, "正在运行工具…", "正在等待 Unity Editor 返回结果。", true, "…");
                return;
            }
            if (resultState == ResultState.Error)
            {
                DrawError(content, tool);
                return;
            }

            GUI.DrawTexture(content, codeTexture);
            var displayText = resultView == 0 ? resultText : BuildTreePreview(resultText);
            var contentHeight = Mathf.Max(content.height, EstimateHeight(displayText, content.width - 30f));
            resultScroll = GUI.BeginScrollView(content, resultScroll, new Rect(0f, 0f, content.width - 15f, contentHeight));
            GUI.Label(new Rect(9f, 7f, content.width - 32f, contentHeight - 10f), displayText, codeStyle);
            GUI.EndScrollView();
        }

        private void DrawError(Rect rect, ToolInfo tool)
        {
            var errorColor = new Color(0.47f, 0.22f, 0.22f);
            EditorGUI.DrawRect(new Rect(rect.x + 6f, rect.y + 7f, rect.width - 12f, 24f), errorColor);
            GUI.Label(new Rect(rect.x + 13f, rect.y + 10f, 60f, 17f), "错误", new GUIStyle(statusStyle) { normal = { textColor = new Color(1f, 0.78f, 0.76f) } });
            GUI.Label(new Rect(rect.x + 59f, rect.y + 10f, rect.width - 150f, 17f), errorText, mutedStyle);
            if (GUI.Button(new Rect(rect.xMax - 79f, rect.y + 9f, 67f, 19f), "重试", EditorStyles.miniButton)) RunTool(tool);
            GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 38f, rect.width - 12f, rect.height - 45f), codeTexture);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 47f, rect.width - 28f, rect.height - 55f), "建议：检查服务连接和参数，然后重新运行。\n\n" + errorText, codeStyle);
        }

        private void RunTool(ToolInfo tool)
        {
            running = true;
            resultState = ResultState.Loading;
            errorText = string.Empty;
            Repaint();
            try
            {
                var stopwatch = Stopwatch.StartNew();
                resultText = UnityMcpConnection.RunDashboardTool(tool.Id, tool.Id == "execute_editor_command" ? commandCode : null);
                stopwatch.Stop();
                elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                resultTimestamp = DateTime.Now.ToString("HH:mm:ss");
                resultState = ResultState.Success;
            }
            catch (Exception exception)
            {
                errorText = exception.Message;
                resultState = ResultState.Error;
            }
            finally { running = false; }
        }

        private void ClearResult()
        {
            resultState = ResultState.Empty;
            resultText = string.Empty;
            errorText = string.Empty;
            elapsedMilliseconds = 0;
            resultTimestamp = string.Empty;
            resultScroll = Vector2.zero;
        }

        private void DrawCenteredMessage(Rect rect, string title, string detail, bool loading = false, string icon = null)
        {
            var titleStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = loading ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.70f, 0.70f, 0.70f) } };
            if (!string.IsNullOrEmpty(icon))
                GUI.Label(new Rect(rect.x, rect.center.y - 39f, rect.width, 18f), icon, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = new Color(0.33f, 0.33f, 0.33f) } });
            GUI.Label(new Rect(rect.x, rect.center.y - 17f, rect.width, 18f), title, titleStyle);
            GUI.Label(new Rect(rect.x, rect.center.y + 3f, rect.width, 17f), detail, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
        }

        private void DrawInspectorLabel(float x, float y, string label)
        {
            GUI.Label(new Rect(x, y, 100f, 18f), label, new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleRight });
        }

        private List<int> GetGroupTools(string group)
        {
            var result = new List<int>();
            for (var i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                if (tool.Group == group && (string.IsNullOrEmpty(search) || tool.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || tool.Summary.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) result.Add(i);
            }
            return result;
        }

        private float CalculateNavigatorHeight()
        {
            var height = 8f;
            foreach (var group in new[] { "状态", "命令", "诊断" })
            {
                var count = GetGroupTools(group).Count;
                if (count == 0) continue;
                height += 20f + (groupExpanded[group] ? count * 43f + 5f : 0f);
            }
            return height;
        }

        private void HandleSplitters()
        {
            var mouse = Event.current;
            var vertical = new Rect(navigatorWidth - 3f, ToolbarHeight, 7f, position.height - ToolbarHeight);
            var horizontalY = position.height - resultHeight - 2f;
            var horizontal = new Rect(navigatorWidth, horizontalY - 3f, position.width - navigatorWidth, 7f);
            EditorGUIUtility.AddCursorRect(vertical, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(horizontal, MouseCursor.ResizeVertical);
            if (mouse.type == EventType.MouseDown && vertical.Contains(mouse.mousePosition)) { draggingNavigator = true; mouse.Use(); }
            if (mouse.type == EventType.MouseDown && horizontal.Contains(mouse.mousePosition)) { draggingResult = true; mouse.Use(); }
            if (mouse.type == EventType.MouseDrag && draggingNavigator) { navigatorWidth = Mathf.Clamp(mouse.mousePosition.x, MinimumNavigatorWidth, Mathf.Min(MaximumNavigatorWidth, position.width - 360f)); mouse.Use(); Repaint(); }
            if (mouse.type == EventType.MouseDrag && draggingResult) { resultHeight = Mathf.Clamp(position.height - mouse.mousePosition.y, MinimumResultHeight, Mathf.Max(MinimumResultHeight, position.height - ToolbarHeight - InspectorHeight)); mouse.Use(); Repaint(); }
            if (mouse.type == EventType.MouseUp) { draggingNavigator = false; draggingResult = false; }
        }

        private void DrawVerticalSplitter(Rect rect) => GUI.DrawTexture(rect, splitterTexture);
        private void DrawHorizontalSplitter(Rect rect) => GUI.DrawTexture(rect, splitterTexture);
        private void DrawToolbarSeparator(float x) => EditorGUI.DrawRect(new Rect(x, 3f, 1f, 17f), new Color(0.20f, 0.20f, 0.20f));

        private static string BuildTreePreview(string json)
        {
            return "Root\n" + json.Replace("{", "├─ ").Replace("}", "\n└─").Replace(",", "\n├─").Replace("[", "[ ").Replace("]", " ]");
        }

        private static float EstimateHeight(string text, float width)
        {
            return Mathf.Max(60f, Mathf.Ceil(text.Length / Mathf.Max(18f, width / 7f)) * 17f + 18f);
        }

        private void CreateStyles()
        {
            selectedTexture = MakeTexture(new Color(0.25f, 0.31f, 0.37f));
            accentTexture = MakeTexture(new Color(0.23f, 0.45f, 0.66f));
            splitterTexture = MakeTexture(new Color(0.11f, 0.11f, 0.11f));
            codeTexture = MakeTexture(new Color(0.125f, 0.125f, 0.125f));
            toolbarTexture = MakeTexture(new Color(0.25f, 0.25f, 0.25f));
            toolbarLabel = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.88f, 0.88f, 0.88f) } };
            titleStyle = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.91f, 0.91f, 0.91f) } };
            descriptionStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, normal = { textColor = new Color(0.82f, 0.82f, 0.82f) } };
            mutedStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, normal = { textColor = new Color(0.64f, 0.64f, 0.64f) } };
            sectionStyle = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11, normal = { textColor = new Color(0.78f, 0.78f, 0.78f) } };
            toolNameStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, normal = { textColor = new Color(0.84f, 0.84f, 0.84f) } };
            toolSelectedStyle = new GUIStyle(toolNameStyle) { normal = { textColor = Color.white } };
            codeStyle = new GUIStyle(EditorStyles.textArea) { fontSize = 11, wordWrap = true, padding = new RectOffset(0, 0, 0, 0), normal = { background = null, textColor = new Color(0.84f, 0.84f, 0.84f) } };
            statusStyle = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11 };
            groupHeaderStyle = new GUIStyle(EditorStyles.foldout) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.71f, 0.71f, 0.71f) }, onNormal = { textColor = new Color(0.71f, 0.71f, 0.71f) } };
            runButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { background = MakeTexture(new Color(0.23f, 0.45f, 0.66f)), textColor = new Color(0.98f, 0.98f, 0.98f) }, hover = { background = MakeTexture(new Color(0.27f, 0.50f, 0.71f)), textColor = Color.white } };
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null) return;
            DestroyImmediate(texture);
            texture = null;
        }
    }
}
#endif
