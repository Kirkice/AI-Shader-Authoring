#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    /// <summary>
    /// MCP control panel inspired by the supplied reference UI.
    /// Provides MCP connection settings, activity audit, categorized tools and schemas.
    /// </summary>
    public sealed class AIShaderMcpWindow : EditorWindow
    {
        private const string PortKey = "AIShader.McpPort";
        private enum Page { Mcp, Tools }

        private struct ToolInfo
        {
            public string Group;
            public string Name;
            public string Description;
            public string Schema;
            public ToolInfo(string group, string name, string description, string schema)
            {
                Group = group; Name = name; Description = description; Schema = schema;
            }
        }

        private static readonly Color WindowBackground = new Color(0.145f, 0.145f, 0.145f);
        private static readonly Color TopBar = new Color(0.045f, 0.045f, 0.045f);
        private static readonly Color Panel = new Color(0.115f, 0.115f, 0.115f);
        private static readonly Color Input = new Color(0.075f, 0.075f, 0.075f);
        private static readonly Color Border = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color Text = new Color(0.78f, 0.78f, 0.78f);
        private static readonly Color Muted = new Color(0.52f, 0.52f, 0.52f);
        private static readonly Color Cyan = new Color(0.50f, 0.80f, 0.88f);
        private static readonly Color Purple = new Color(0.74f, 0.64f, 0.92f);
        private static readonly Color Green = new Color(0.34f, 0.90f, 0.50f);
        private static readonly Color Red = new Color(0.95f, 0.32f, 0.28f);

        private Page page;
        private string bindAddress = "127.0.0.1";
        private int port = 8765;
        private bool autoSelectPort;
        private bool startWithEditor = true;
        private bool confirmWriteTools;
        private string searchText = string.Empty;
        private int selectedTool = -1;
        private Vector2 toolScroll;
        private Vector2 auditScroll;
        private readonly List<ToolInfo> tools = new List<ToolInfo>();
        private readonly List<string> audit = new List<string>();
        private GUIStyle tabStyle;
        private GUIStyle activeTabStyle;
        private GUIStyle panelStyle;
        private GUIStyle inputStyle;
        private GUIStyle headingStyle;
        private GUIStyle labelStyle;
        private GUIStyle mutedStyle;
        private GUIStyle monoStyle;
        private GUIStyle tableHeaderStyle;
        private GUIStyle toolRowStyle;
        private GUIStyle selectedToolStyle;
        private GUIStyle groupStyle;
        private GUIStyle toolButtonStyle;
        private GUIStyle selectedToolButtonStyle;
        private GUIStyle bottomStyle;
        private GUIStyle bottomButtonStyle;

        [MenuItem("AI Shader/MCP Dashboard", priority = 0)]
        public static void Open() => GetWindow<AIShaderMcpWindow>("AI Shader MCP");

        private void OnEnable()
        {
            port = EditorPrefs.GetInt(PortKey, 8765);
            minSize = new Vector2(860f, 520f);
            BuildTools();
            CreateStyles();
            AddAudit("MCP dashboard opened.");
            EditorApplication.update += RepaintOnUpdate;
        }

        private void OnDisable() => EditorApplication.update -= RepaintOnUpdate;

        private void RepaintOnUpdate()
        {
            if (page == Page.Mcp) Repaint();
        }

        private void OnGUI()
        {
            if (panelStyle == null) CreateStyles();
            DrawBackground();
            DrawTabs();
            var contentHeight = Mathf.Max(220f, position.height - 112f);
            GUILayout.BeginArea(new Rect(0f, 72f, position.width, contentHeight));
            if (page == Page.Mcp) DrawMcpPage(); else DrawToolsPage();
            GUILayout.EndArea();
            DrawBottomStatus();
        }

        private void DrawBackground()
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), WindowBackground);
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, 40f), TopBar);
            EditorGUI.DrawRect(new Rect(0f, position.height - 38f, position.width, 38f), TopBar);
        }

        private void DrawTabs()
        {
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(32f));
            if (GUILayout.Button("MCP", page == Page.Mcp ? activeTabStyle : tabStyle, GUILayout.Width(62f), GUILayout.Height(32f))) page = Page.Mcp;
            if (GUILayout.Button("Tools", page == Page.Tools ? activeTabStyle : tabStyle, GUILayout.Width(62f), GUILayout.Height(32f))) page = Page.Tools;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMcpPage()
        {
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawConnectionPanel();
            GUILayout.Space(5f);
            DrawAuditPanel();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConnectionPanel()
        {
            EditorGUILayout.BeginVertical(panelStyle, GUILayout.MinWidth(560f), GUILayout.Width(Mathf.Clamp(position.width * 0.46f, 560f, 760f)), GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Server & Connection", headingStyle);
            GUILayout.FlexibleSpace();
            var serviceRunning = BridgeRunning();
            var statusStyle = new GUIStyle(labelStyle) { normal = { textColor = serviceRunning ? Green : Muted } };
            EditorGUILayout.LabelField(serviceRunning ? "● MCP" : "○ MCP", statusStyle, GUILayout.Width(62f));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(12f);

            EditorGUILayout.LabelField("Bind Address", labelStyle);
            EditorGUILayout.BeginHorizontal();
            bindAddress = EditorGUILayout.TextField(bindAddress, inputStyle);
            EditorGUILayout.LabelField("Port", labelStyle, GUILayout.Width(34f));
            port = EditorGUILayout.IntField(port, inputStyle, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            autoSelectPort = EditorGUILayout.Toggle(autoSelectPort, GUILayout.Width(18f));
            EditorGUILayout.LabelField("Auto-select available port", labelStyle, GUILayout.Width(185f));
            startWithEditor = EditorGUILayout.Toggle(startWithEditor, GUILayout.Width(18f));
            EditorGUILayout.LabelField("Start with editor", labelStyle, GUILayout.Width(130f));
            confirmWriteTools = EditorGUILayout.Toggle(confirmWriteTools, GUILayout.Width(18f));
            EditorGUILayout.LabelField("Confirm write tools", labelStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16f);
            EditorGUILayout.LabelField("Endpoint", labelStyle);
            EditorGUILayout.BeginHorizontal();
            var endpoint = $"http://{bindAddress}:{Mathf.Clamp(port, 1, 65535)}";
            EditorGUILayout.SelectableLabel(endpoint, inputStyle, GUILayout.Height(24f));
            EditorPrefs.SetInt(PortKey, Mathf.Clamp(port, 1, 65535));
            if (GUILayout.Button("复制", GUILayout.Width(48f), GUILayout.Height(24f)))
            {
                EditorGUIUtility.systemCopyBuffer = endpoint;
                AddAudit("Endpoint copied to clipboard.");
            }
            EditorGUILayout.EndHorizontal();
            if (GUI.changed) EditorPrefs.SetInt(PortKey, Mathf.Clamp(port, 1, 65535));

            GUILayout.Space(18f);
            EditorGUILayout.BeginHorizontal();
            var connected = BridgeRunning();
            var old = GUI.backgroundColor;
            GUI.backgroundColor = connected ? new Color(.22f, .45f, .28f) : new Color(.30f, .30f, .30f);
            var serviceButtonStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = Color.white, background = MakeTexture(connected ? new Color(.08f, .28f, .16f) : new Color(.18f, .22f, .26f)) }, hover = { textColor = Color.white, background = MakeTexture(connected ? new Color(.10f, .36f, .20f) : new Color(.24f, .30f, .36f)) } };
            if (GUILayout.Button(connected ? "Stop MCP Service" : "Start MCP Service", serviceButtonStyle, GUILayout.Height(30f)))
            {
                if (connected) { AIShaderMcpBridge.Stop(); AddAudit("MCP service stopped."); }
                else { AIShaderMcpBridge.TryStart(); AddAudit($"MCP service started at {endpoint}."); }
            }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(18f);
            EditorGUILayout.LabelField("Transport", labelStyle);
            EditorGUILayout.LabelField("Unity Editor HTTP Bridge + external stdio MCP", mutedStyle);
            EditorGUILayout.LabelField("Package", mutedStyle);
            EditorGUILayout.LabelField("com.ai.shader-authoring  0.1.0", mutedStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawAuditPanel()
        {
            EditorGUILayout.BeginVertical(panelStyle, GUILayout.MinWidth(480f), GUILayout.Width(Mathf.Clamp(position.width * 0.42f, 480f, 720f)), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Activity & Audit", headingStyle);
            GUILayout.Space(9f);
            auditScroll = EditorGUILayout.BeginScrollView(auditScroll, monoStyle, GUILayout.ExpandHeight(true));
            if (audit.Count == 0)
                EditorGUILayout.LabelField("No activity yet.", mutedStyle);
            else
            {
                for (var i = audit.Count - 1; i >= 0; i--)
                    EditorGUILayout.LabelField(audit[i], labelStyle);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.Space(7f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Console entries: " + AIShaderConsoleService.Count + "   errors: " + AIShaderConsoleService.ErrorCount, mutedStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear audit", GUILayout.Width(90f))) { audit.Clear(); AIShaderConsoleService.Clear(); AddAudit("Console cache cleared."); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawToolsPage()
        {
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawToolList();
            GUILayout.Space(5f);
            DrawToolDetails();
            GUILayout.Space(5f);
            DrawSchemaPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolList()
        {
            EditorGUILayout.BeginVertical(panelStyle, GUILayout.Width(Mathf.Clamp(position.width * .29f, 300f, 430f)), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Tools", headingStyle);
            GUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("⌕", mutedStyle, GUILayout.Width(18f));
            searchText = EditorGUILayout.TextField(searchText, inputStyle);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(9f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tool", tableHeaderStyle);
            EditorGUILayout.LabelField("State", tableHeaderStyle, GUILayout.Width(55f));
            EditorGUILayout.EndHorizontal();
            toolScroll = EditorGUILayout.BeginScrollView(toolScroll, GUIStyle.none);
            var currentGroup = string.Empty;
            for (var i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                if (!string.IsNullOrEmpty(searchText) && tool.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0 && tool.Description.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (tool.Group != currentGroup)
                {
                    currentGroup = tool.Group;
                    EditorGUILayout.LabelField("⌄  " + currentGroup, groupStyle);
                }
                var selected = selectedTool == i;
                var row = GUILayoutUtility.GetRect(0f, 25f, GUILayout.ExpandWidth(true));
                if (selected) EditorGUI.DrawRect(row, new Color(.16f, .27f, .23f));
                EditorGUI.DrawRect(new Rect(row.x, row.y, 2f, row.height), selected ? Green : new Color(.22f, .22f, .22f));
                if (GUI.Button(new Rect(row.x + 5f, row.y, row.width - 62f, row.height), tool.Name, selected ? selectedToolButtonStyle : toolButtonStyle)) selectedTool = i;
                GUI.Label(new Rect(row.xMax - 52f, row.y + 4f, 48f, 18f), "Ready", mutedStyle);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawToolDetails()
        {
            EditorGUILayout.BeginVertical(panelStyle, GUILayout.Width(Mathf.Clamp(position.width * .29f, 300f, 430f)), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Tool Details", new GUIStyle(headingStyle) { normal = { textColor = Purple } });
            GUILayout.Space(15f);
            if (selectedTool < 0 || selectedTool >= tools.Count)
            {
                EditorGUILayout.LabelField("Select a tool", labelStyle);
                GUILayout.FlexibleSpace();
            }
            else
            {
                var tool = tools[selectedTool];
                EditorGUILayout.LabelField(tool.Name, new GUIStyle(headingStyle) { normal = { textColor = Purple } });
                GUILayout.Space(10f);
                EditorGUILayout.LabelField(tool.Description, labelStyle);
                GUILayout.Space(20f);
                DrawKeyValue("Group", tool.Group);
                DrawKeyValue("State", "Ready");
                DrawKeyValue("Transport", "MCP HTTP Bridge");
                DrawKeyValue("Permission", PermissionFor(tool));
                DrawKeyValue("Risk", RiskFor(tool));
                DrawKeyValue("Method", tool.Name);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSchemaPanel()
        {
            EditorGUILayout.BeginVertical(panelStyle, GUILayout.MinWidth(300f), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Input JSON Schema", new GUIStyle(headingStyle) { normal = { textColor = new Color(.62f, .70f, .82f) } });
            GUILayout.Space(9f);
            var schema = selectedTool >= 0 && selectedTool < tools.Count ? tools[selectedTool].Schema : "";
            EditorGUILayout.TextArea(schema, monoStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndVertical();
        }

        private void DrawBottomStatus()
        {
            var connected = BridgeRunning();
            EditorGUI.DrawRect(new Rect(0f, position.height - 38f, 8f, 38f), connected ? Green : Red);
            GUILayout.BeginArea(new Rect(18f, position.height - 34f, position.width - 30f, 30f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("MCP", labelStyle, GUILayout.Width(40f));
            EditorGUILayout.LabelField(connected ? "●" : "○", new GUIStyle(labelStyle) { normal = { textColor = connected ? Green : Red } }, GUILayout.Width(20f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶", bottomButtonStyle, GUILayout.Width(30f), GUILayout.Height(22f))) { AIShaderMcpBridge.TryStart(); AddAudit("MCP service start requested."); }
            if (GUILayout.Button("■", bottomButtonStyle, GUILayout.Width(30f), GUILayout.Height(22f))) { AIShaderMcpBridge.Stop(); AddAudit("MCP service stop requested."); }
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static string PermissionFor(ToolInfo tool)
        {
            return tool.Name.Contains("write") || tool.Name.Contains("create") || tool.Name.Contains("capture") || tool.Name.Contains("refresh") ? "Project / Asset operation" : "Read-only";
        }

        private static string RiskFor(ToolInfo tool)
        {
            return tool.Name.Contains("write") || tool.Name.Contains("create") ? "High" : tool.Name.Contains("capture") || tool.Name.Contains("refresh") ? "Medium" : "Low";
        }

        private void DrawKeyValue(string key, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(key, mutedStyle, GUILayout.Width(75f));
            EditorGUILayout.LabelField(value, labelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private bool BridgeRunning() => EditorPrefs.GetBool("AIShader.McpBridgeStarted", false);

        private void AddAudit(string message)
        {
            audit.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (audit.Count > 200) audit.RemoveAt(0);
        }

        private void CreateStyles()
        {
            tabStyle = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Text }, fixedHeight = 32f };
            activeTabStyle = new GUIStyle(tabStyle) { normal = { textColor = Color.white, background = MakeTexture(new Color(.12f, .12f, .12f)) } };
            panelStyle = new GUIStyle("box") { padding = new RectOffset(8, 8, 10, 8), normal = { background = MakeTexture(Panel) } };
            inputStyle = new GUIStyle(EditorStyles.textField) { fontSize = 12, normal = { textColor = Text, background = MakeTexture(Input) } };
            headingStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Cyan } };
            labelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, normal = { textColor = Text } };
            mutedStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, normal = { textColor = Muted } };
            monoStyle = new GUIStyle(EditorStyles.textArea) { fontSize = 11, wordWrap = true, normal = { textColor = Text, background = MakeTexture(new Color(.075f, .075f, .075f)) } };
            tableHeaderStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 4, 3, 3), normal = { textColor = Muted } };
            toolRowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(4, 4, 3, 3), normal = { textColor = Text, background = MakeTexture(new Color(.10f, .12f, .11f)) } };
            selectedToolStyle = new GUIStyle(toolRowStyle) { normal = { textColor = Color.white, background = MakeTexture(new Color(.15f, .25f, .21f)) } };
            toolButtonStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 4, 4, 3), normal = { textColor = new Color(.72f, .72f, .72f) } };
            selectedToolButtonStyle = new GUIStyle(toolButtonStyle) { normal = { textColor = Color.white } };
            groupStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, padding = new RectOffset(5, 6, 7, 4), normal = { textColor = new Color(.68f, .72f, .78f) } };
            bottomButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.86f, .86f, .86f), background = MakeTexture(new Color(.22f, .22f, .22f)) }, hover = { textColor = Color.white, background = MakeTexture(new Color(.30f, .30f, .30f)) }, active = { textColor = Color.white, background = MakeTexture(new Color(.15f, .15f, .15f)) } };
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }

        private void BuildTools()
        {
            tools.Clear();
            Add("Project", "get_unity_status", "读取 Unity 版本、场景、编译状态和 Console 错误数量", "{}");
            Add("Project", "get_project_context", "读取用户确认的 Shader、Include、Pipeline 和生成路径", "{}");
            Add("Project", "refresh_assets", "刷新 AssetDatabase 并触发资源导入", "{}");
            Add("Shader", "write_generated_text", "写入配置生成目录下的 Shader/HLSL/Material 文件", "{\n  \"path\": \"Assets/AIShader/Generated/Test.shader\",\n  \"content\": \"...\"\n}");
            Add("Console", "clear_console_logs", "清理当前迭代前的 Console 缓存", "{}");
            Add("Console", "get_console_logs", "读取最近 Unity Console 日志和堆栈", "{\n  \"limit\": 200\n}");
            Add("Console", "get_console_errors", "读取 Error、Exception 和 Assert", "{\n  \"limit\": 200\n}");
            Add("Console", "get_console_warnings", "读取 Warning 日志", "{\n  \"limit\": 200\n}");
            Add("Validation", "create_validation_scene", "创建 PBR 分阶段验证场景和 Sphere", "{}");
            Add("Validation", "capture_validation_frame", "捕获 PNG 并生成 Manifest", "{}");
        }

        private void Add(string group, string name, string description, string schema) => tools.Add(new ToolInfo(group, name, description, schema));
    }
}
#endif
