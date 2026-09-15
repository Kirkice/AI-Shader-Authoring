#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Unity MCP 的本地服务状态与调试工具面板。</summary>
    public sealed class UnityMcpWindow : EditorWindow
    {
        private sealed class ToolInfo
        {
            public string Id;
            public string Summary;
            public string Description;
        }

        private static readonly ToolInfo[] Tools =
        {
            new ToolInfo
            {
                Id = "get_editor_state",
                Summary = "读取当前 Editor 上下文",
                Description = "向 AI 客户端提供当前场景、层级、选中对象、播放状态与项目结构。"
            },
            new ToolInfo
            {
                Id = "get_logs",
                Summary = "读取本地 Console 日志",
                Description = "向 AI 客户端提供 Unity Console 的本地日志缓冲区，用于定位错误和警告。"
            },
            new ToolInfo
            {
                Id = "execute_editor_command",
                Summary = "执行 C# Editor 命令",
                Description = "允许 AI 客户端在 Unity Editor 上下文中编译并执行明确授权的 C# 命令。"
            }
        };

        private const float OuterPadding = 12f;
        private const float CardRadius = 8f;
        private const float StatusCardHeight = 162f;
        private const float ToolCardHeight = 42f;
        private const float ToolInspectorHeight = 82f;
        private const float ToolGap = 6f;

        private readonly bool[] toolExpanded = new bool[Tools.Length];
        private Vector2 windowScroll;

        private GUIStyle eyebrowStyle;
        private GUIStyle headingStyle;
        private GUIStyle statusStyle;
        private GUIStyle bodyStyle;
        private GUIStyle endpointStyle;
        private GUIStyle chipStyle;
        private GUIStyle secondaryButtonStyle;
        private GUIStyle toolTitleStyle;
        private GUIStyle toolSummaryStyle;
        private Texture2D panelTexture;
        private Texture2D selectedPanelTexture;
        private Texture2D secondaryButtonTexture;
        private Texture2D successTexture;
        private Texture2D warningTexture;
        private Texture2D errorTexture;

        [MenuItem("Unity MCP/Dashboard", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<UnityMcpWindow>("Unity MCP");
            window.minSize = new Vector2(560f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            toolExpanded[0] = true;
            CreateStyles();
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
            DestroyTexture(ref panelTexture);
            DestroyTexture(ref selectedPanelTexture);
            DestroyTexture(ref secondaryButtonTexture);
            DestroyTexture(ref successTexture);
            DestroyTexture(ref warningTexture);
            DestroyTexture(ref errorTexture);
        }

        private void OnGUI()
        {
            // Unity 在域重载和窗口布局恢复后可能保留纹理引用，但不会保留普通 C# 的 GUIStyle 字段。
            // 此处必须完整验证，不能只检查 panelTexture。
            if (!AreGuiResourcesReady()) CreateStyles();

            var canvas = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(canvas, new Color(0.22f, 0.22f, 0.22f));

            var contentWidth = Mathf.Max(1f, position.width - OuterPadding * 2f);
            var toolsHeight = CalculateToolsHeight();
            var contentHeight = OuterPadding + StatusCardHeight + 13f + 18f + 5f + toolsHeight + OuterPadding;
            windowScroll = GUI.BeginScrollView(canvas, windowScroll, new Rect(0f, 0f, position.width - 15f, contentHeight));

            var content = new Rect(OuterPadding, OuterPadding, contentWidth - 15f, contentHeight - OuterPadding * 2f);
            var statusRect = new Rect(content.x, content.y, content.width, StatusCardHeight);
            DrawStatusCard(statusRect);

            var toolsTop = statusRect.yMax + 13f;
            DrawSectionLabel(new Rect(content.x, toolsTop, content.width, 18f), "MCP 工具");
            var toolRect = new Rect(content.x, toolsTop + 23f, content.width, toolsHeight);
            DrawToolCards(toolRect);

            GUI.EndScrollView();
        }

        private void DrawStatusCard(Rect rect)
        {
            DrawRoundedPanel(rect, panelTexture);
            var state = UnityMcpConnection.State;
            var enabled = UnityMcpConnection.IsServiceEnabled;
            var connected = UnityMcpConnection.IsConnected;
            var stateColor = connected ? new Color(0.36f, 0.82f, 0.43f) : enabled ? new Color(0.95f, 0.69f, 0.26f) : new Color(0.91f, 0.37f, 0.36f);
            var stateText = connected ? "Unity MCP 已连接" : enabled ? "Unity MCP 等待连接" : "Unity MCP 已关闭";
            var chipText = connected ? "已连接 · 端口 " + UnityMcpConnection.ServerUri.Port : enabled ? "运行中 · 端口 " + UnityMcpConnection.ServerUri.Port : "已关闭";
            var summary = connected
                ? "AI 客户端已连接至本地 Unity MCP 服务，可读取项目状态和运行工具。"
                : enabled
                    ? "本地 Unity MCP 服务正在等待 AI 客户端连接。"
                    : "启用本地 Unity MCP 服务后，AI 客户端即可连接当前 Unity 编辑器。";

            SafeLabel(new Rect(rect.x + 12f, rect.y + 10f, 160f, 17f), "本地 MCP", eyebrowStyle);
            DrawStatusDot(new Rect(rect.x + 13f, rect.y + 37f, 9f, 9f), stateColor);
            SafeLabel(new Rect(rect.x + 29f, rect.y + 31f, rect.width - 220f, 22f), stateText, statusStyle);
            DrawChip(new Rect(rect.xMax - 170f, rect.y + 27f, 158f, 25f), chipText, stateColor);
            SafeLabel(new Rect(rect.x + 13f, rect.y + 60f, rect.width - 26f, 32f), summary, bodyStyle);
            SafeLabel(new Rect(rect.x + 13f, rect.y + 94f, rect.width - 26f, 19f), GetEndpointDisplay(), endpointStyle);

            const float gap = 8f;
            var actionsY = rect.yMax - 36f;
            var actionWidth = (rect.width - 26f - gap) * 0.5f;
            var toggleLabel = enabled ? "关闭 MCP" : "启用 MCP";
            if (GUI.Button(new Rect(rect.x + 13f, actionsY, actionWidth, 25f), toggleLabel, secondaryButtonStyle))
            {
                if (enabled) UnityMcpConnection.StopService();
                else UnityMcpConnection.StartService();
            }

            if (GUI.Button(new Rect(rect.x + 13f + actionWidth + gap, actionsY, actionWidth, 25f), "MCP 信息", secondaryButtonStyle))
                ShowMcpInfo();
        }

        private float CalculateToolsHeight()
        {
            var height = 0f;
            for (var index = 0; index < Tools.Length; index++)
            {
                height += ToolCardHeight;
                if (toolExpanded[index]) height += ToolGap + ToolInspectorHeight;
                if (index < Tools.Length - 1) height += ToolGap;
            }
            return height;
        }

        private void DrawToolCards(Rect rect)
        {
            var y = rect.y;
            for (var index = 0; index < Tools.Length; index++)
            {
                var tool = Tools[index];
                var expanded = toolExpanded[index];
                var headerRect = new Rect(rect.x, y, rect.width, ToolCardHeight);
                DrawRoundedPanel(headerRect, expanded ? selectedPanelTexture : panelTexture);
                if (expanded)
                    EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y + 7f, 2f, headerRect.height - 14f), new Color(0.48f, 0.64f, 0.79f));

                GUI.Label(new Rect(headerRect.x + 12f, headerRect.y + 7f, 18f, 19f), expanded ? "⌄" : "›", headingStyle);
                GUI.Label(new Rect(headerRect.x + 31f, headerRect.y + 6f, headerRect.width - 46f, 17f), tool.Id, toolTitleStyle);
                GUI.Label(new Rect(headerRect.x + 31f, headerRect.y + 23f, headerRect.width - 46f, 15f), tool.Summary, toolSummaryStyle);
                if (GUI.Button(headerRect, GUIContent.none, GUIStyle.none))
                    toolExpanded[index] = !expanded;

                y = headerRect.yMax;
                if (expanded)
                {
                    y += ToolGap;
                    var inspectorRect = new Rect(rect.x + 7f, y, rect.width - 7f, ToolInspectorHeight);
                    DrawToolInspector(inspectorRect, tool);
                    y = inspectorRect.yMax;
                }
                if (index < Tools.Length - 1) y += ToolGap;
            }
        }

        private void DrawToolInspector(Rect rect, ToolInfo tool)
        {
            DrawRoundedPanel(rect, selectedPanelTexture);
            const float padding = 12f;
            var contentX = rect.x + padding;
            var contentWidth = rect.width - padding * 2f;
            GUI.Label(new Rect(contentX, rect.y + 10f, contentWidth, 17f), "主要作用", eyebrowStyle);
            GUI.Label(new Rect(contentX, rect.y + 31f, contentWidth, 36f), tool.Description, bodyStyle);
        }

        private void ShowMcpInfo()
        {
            var endpoint = UnityMcpConnection.ServerUri;
            var message = "WebSocket 终端：" + endpoint + "\n\n"
                + "状态：" + GetStateDescription() + "\n"
                + "Console 缓冲：" + UnityMcpConnection.LogCount + " 条\n\n"
                + "服务启用后，Unity 会自动尝试连接本地 UnityMCP 服务端。";
            EditorUtility.DisplayDialog("Unity MCP 信息", message, "确定");
        }

        private string GetEndpointDisplay()
        {
            var endpoint = UnityMcpConnection.ServerUri;
            return endpoint.Scheme + "://" + endpoint.Host + ":" + endpoint.Port;
        }

        private string GetStateDescription()
        {
            switch (UnityMcpConnection.State)
            {
                case UnityMcpConnection.ServiceState.Connected: return "已连接";
                case UnityMcpConnection.ServiceState.WaitingForConnection: return "等待连接";
                default: return "已关闭";
            }
        }

        private static void SafeLabel(Rect rect, string text, GUIStyle style)
        {
            if (style != null)
            {
                GUI.Label(rect, text ?? string.Empty, style);
                return;
            }

            // 只在 Unity 重载期间的单帧降级路径使用；明确设为浅色，避免默认 GUI 样式在深色面板上显示黑字。
            var fallbackStyle = new GUIStyle(EditorStyles.label);
            fallbackStyle.normal.textColor = new Color(0.84f, 0.84f, 0.84f);
            GUI.Label(rect, text ?? string.Empty, fallbackStyle);
        }

        private static void DrawStatusDot(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = previous;
        }

        private void DrawChip(Rect rect, string label, Color color)
        {
            GUI.DrawTexture(rect, color == new Color(0.36f, 0.82f, 0.43f) ? successTexture : UnityMcpConnection.IsServiceEnabled ? warningTexture : errorTexture);
            SafeLabel(rect, label.ToUpperInvariant(), chipStyle);
        }

        private void DrawRoundedPanel(Rect rect, Texture2D texture)
        {
            GUI.DrawTexture(rect, texture);
            var border = new Color(0.14f, 0.14f, 0.14f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
        }

        private static void DrawSectionLabel(Rect rect, string label)
        {
            SafeLabel(rect, label, EditorStyles.miniBoldLabel);
        }

        private bool AreGuiResourcesReady()
        {
            return panelTexture != null
                && selectedPanelTexture != null
                && secondaryButtonTexture != null
                && successTexture != null
                && warningTexture != null
                && errorTexture != null
                && eyebrowStyle != null
                && headingStyle != null
                && statusStyle != null
                && bodyStyle != null
                && endpointStyle != null
                && chipStyle != null
                && secondaryButtonStyle != null
                && toolTitleStyle != null
                && toolSummaryStyle != null;
        }

        private void CreateStyles()
        {
            // 采用 Unity Editor 深色主题的中性灰阶；状态色只用于连接状态提示。
            panelTexture = MakeTexture(new Color(0.24f, 0.24f, 0.24f));
            selectedPanelTexture = MakeTexture(new Color(0.28f, 0.28f, 0.28f));
            secondaryButtonTexture = MakeTexture(new Color(0.32f, 0.32f, 0.32f));
            successTexture = MakeTexture(new Color(0.16f, 0.34f, 0.21f));
            warningTexture = MakeTexture(new Color(0.38f, 0.29f, 0.12f));
            errorTexture = MakeTexture(new Color(0.42f, 0.17f, 0.17f));

            eyebrowStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.74f, 0.74f, 0.74f) }
            };
            headingStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.88f, 0.88f, 0.88f) }
            };
            statusStyle = new GUIStyle(headingStyle) { fontSize = 14 };
            bodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.70f, 0.70f, 0.70f) }
            };
            endpointStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.63f, 0.72f, 0.80f) }
            };
            chipStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                normal = { textColor = new Color(0.86f, 0.91f, 0.86f) }
            };
            secondaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { background = secondaryButtonTexture, textColor = new Color(0.85f, 0.85f, 0.85f) },
                hover = { background = MakeTexture(new Color(0.40f, 0.40f, 0.40f)), textColor = Color.white }
            };
            toolTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.84f, 0.84f, 0.84f) }
            };
            toolSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.64f, 0.64f, 0.64f) }
            };
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
