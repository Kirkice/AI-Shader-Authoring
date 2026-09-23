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
            public string Parameters;
            public bool RequiresAuthorization;
        }

        // Dashboard 直接镜像当前 Unity MCP Server 的完整工具表，而非只展示调试入口。
        // 增删工具时应同时更新 UnityMcpShaderTools.Handle 的 allow-list 与此处描述。
        private static readonly ToolInfo[] Tools =
        {
            Tool("get_editor_state", "读取 Editor 状态", "读取当前场景、层级、选中对象、播放状态与项目结构。", "format: Raw | scripts only | no scripts"),
            Tool("execute_editor_command", "执行 C# Editor 命令", "在 Unity Editor 上下文中编译并执行已授权的 C# 命令。", "code: string", true),
            Tool("get_logs", "读取 Console 日志", "筛选 Unity Console 的日志、警告、错误与异常。", "types, count, fields, messageContains, stackTraceContains, timestampAfter, timestampBefore"),
            Tool("run_unity_job", "启动结构化 Unity Job", "启动 allow-list 中的异步 Unity 作业，不接受 C# 源码。", "operationContext, idempotencyKey, jobType, args", true),
            Tool("get_unity_job", "读取 Unity Job 状态", "读取异步 Job 的状态和工件。", "jobId"),
            Tool("cancel_unity_job", "取消 Unity Job", "请求取消处于队列或可取消阶段的结构化 Job。", "jobId, reason", true),
            Tool("get_shader_knowledge_base_status", "读取 Shader 知识库状态", "读取项目 Shader Knowledge Base 的新鲜度与版本。", "expectedSchemaVersion"),
            Tool("build_shader_knowledge_base", "构建 Shader 知识库", "排队构建完整或增量 Shader Knowledge Base。", "operationContext, idempotencyKey, mode, reason", true),
            Tool("query_shader_knowledge_base", "查询 Shader 知识库", "检索已持久化的 Shader 示例、函数卡片和能力证据。", "knowledgeBaseVersion, query"),
            Tool("get_asset_revision", "读取资产 Revision", "读取项目相对资产的内容 revision。", "assetPaths"),
            Tool("inspect_shader_structure", "检查 Shader 结构", "读取 Shader Properties、Pass、入口、Include 与渲染状态。", "assetPath, expectedRevision"),
            Tool("write_generated_text_asset", "写入生成文本资产", "仅在 Assets/AIShader/Generated 下写入 revision-protected 文本资产。", "operationContext, idempotencyKey, asset, codePlan", true),
            Tool("refresh_and_compile_assets", "刷新并编译资产", "排队刷新指定资产并返回编译证据。", "operationContext, idempotencyKey, assetPaths", true),
            Tool("export_compiled_gles_variants", "导出 GLES 编译变体", "从 Unity 编译器导出真实 GLES3x 顶点与片元 GLSL 变体。", "shaderPath, operationContext, idempotencyKey"),
            Tool("analyze_shader_performance", "分析 Shader 性能", "执行静态分析与可选 Mali Offline Compiler 性能分析。", "shaderPath, policy, maliTargets, maliCompilerPath, operationContext, idempotencyKey"),
            Tool("ensure_validation_scene", "准备验证场景", "创建或验证隔离、确定性的 Shader 验证会话。", "operationContext, idempotencyKey, validationProfile, target", true),
            Tool("capture_validation", "采集验证截图", "在既有验证会话中采集确定性验证截图。", "operationContext, idempotencyKey, validationSessionId, captures", true),
            Tool("get_console_diagnostics", "读取编译诊断", "读取生成 Shader 的 Unity Console 和编译器诊断。", "assetPaths, includeWarnings, since, operationContext")
        };

        private const float OuterPadding = 14f;
        private const float StatusCardHeight = 196f;
        private const float ToolCardHeight = 62f;
        private const float ToolInspectorHeight = 104f;
        private const float ToolGap = 8f;

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
        private GUIStyle sectionStyle;
        private GUIStyle parameterStyle;
        private GUIStyle readOnlyStyle;
        private GUIStyle authorizationStyle;
        private Texture2D panelTexture;
        private Texture2D selectedPanelTexture;
        private Texture2D secondaryButtonTexture;
        private Texture2D successTexture;
        private Texture2D warningTexture;
        private Texture2D errorTexture;

        private static ToolInfo Tool(string id, string summary, string description, string parameters, bool requiresAuthorization = false)
        {
            return new ToolInfo
            {
                Id = id,
                Summary = summary,
                Description = description,
                Parameters = parameters,
                RequiresAuthorization = requiresAuthorization
            };
        }

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
            // Domain reloads can leave textures alive while GUIStyle instances are reset. Rebuild the
            // entire skin whenever any style/texture dependency is missing, so GUI.Label never receives
            // a null style during the first repaint after compilation.
            if (!HasCompleteSkin()) CreateStyles();

            var canvas = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(canvas, new Color(0.035f, 0.025f, 0.055f));

            var contentWidth = Mathf.Max(1f, position.width - OuterPadding * 2f);
            var toolsHeight = CalculateToolsHeight();
            var contentHeight = OuterPadding + StatusCardHeight + 18f + 22f + 7f + toolsHeight + OuterPadding;
            windowScroll = GUI.BeginScrollView(canvas, windowScroll, new Rect(0f, 0f, position.width - 15f, contentHeight));

            var content = new Rect(OuterPadding, OuterPadding, contentWidth - 15f, contentHeight - OuterPadding * 2f);
            var statusRect = new Rect(content.x, content.y, content.width, StatusCardHeight);
            DrawStatusCard(statusRect);

            var toolsTop = statusRect.yMax + 13f;
            DrawSectionLabel(new Rect(content.x, toolsTop, content.width, 22f), "工具 (" + Tools.Length + ")");
            var toolRect = new Rect(content.x, toolsTop + 29f, content.width, toolsHeight);
            DrawToolCards(toolRect);

            GUI.EndScrollView();
        }

        private void DrawStatusCard(Rect rect)
        {
            DrawRoundedPanel(rect, panelTexture);
            var state = UnityMcpConnection.State;
            var enabled = UnityMcpConnection.IsServiceEnabled;
            var connected = UnityMcpConnection.IsConnected;
            var stateColor = connected ? new Color(0.0f, 1.0f, 0.61f) : enabled ? new Color(1.0f, 0.82f, 0.38f) : new Color(1.0f, 0.23f, 0.42f);
            var stateText = connected ? "Unity MCP 已连接" : enabled ? "Unity MCP 等待连接" : "Unity MCP 已关闭";
            var chipText = connected ? "CONNECTED · :" + UnityMcpConnection.ServerUri.Port : enabled ? "WAITING · :" + UnityMcpConnection.ServerUri.Port : "OFFLINE";
            var summary = connected
                ? "已注册当前 Editor 身份；Agent 必须按 editorInstanceId 或项目路径显式绑定后才能调用工具。"
                : enabled
                    ? "本地 MCP 服务正在等待 Unity WebSocket 连接。"
                    : "启用本地 Unity MCP 服务后，当前 Editor 将注册可绑定身份。";

            GUI.Label(new Rect(rect.x + 14f, rect.y + 12f, 160f, 17f), "MCP SERVER  ·  GLOBAL", eyebrowStyle);
            DrawStatusDot(new Rect(rect.x + 15f, rect.y + 42f, 9f, 9f), stateColor);
            GUI.Label(new Rect(rect.x + 31f, rect.y + 35f, rect.width - 230f, 24f), stateText, statusStyle);
            DrawChip(new Rect(rect.xMax - 180f, rect.y + 31f, 164f, 25f), chipText, stateColor);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 66f, rect.width - 30f, 32f), summary, bodyStyle);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 104f, rect.width - 30f, 19f), GetEndpointDisplay(), endpointStyle);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 123f, rect.width - 30f, 19f), "Editor ID: " + UnityMcpConnection.CurrentEditorInstanceId, endpointStyle);

            const float gap = 8f;
            var actionsY = rect.yMax - 35f;
            var actionWidth = (rect.width - 30f - gap) * 0.5f;
            var toggleLabel = enabled ? "关闭 MCP 服务" : "启用 MCP 服务";
            if (GUI.Button(new Rect(rect.x + 15f, actionsY, actionWidth, 25f), toggleLabel, secondaryButtonStyle))
            {
                if (enabled) UnityMcpConnection.StopService();
                else UnityMcpConnection.StartService();
            }

            if (GUI.Button(new Rect(rect.x + 15f + actionWidth + gap, actionsY, actionWidth, 25f), "连接信息", secondaryButtonStyle))
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

                // Unity IMGUI 的字体度量会把 q/g/y/p/j 等下行字符绘制到基线以下；
                // 17 像素文本框会裁掉下半部分，因此为标题和摘要预留完整行高。
                GUI.Label(new Rect(headerRect.x + 12f, headerRect.y + 8f, 18f, 22f), expanded ? "⌄" : "›", headingStyle);
                GUI.Label(new Rect(headerRect.x + 33f, headerRect.y + 6f, headerRect.width - 115f, 23f), "◈  " + tool.Id, toolTitleStyle);
                GUI.Label(new Rect(headerRect.x + 33f, headerRect.y + 32f, headerRect.width - 115f, 20f), tool.Summary, toolSummaryStyle);
                GUI.Label(new Rect(headerRect.xMax - 72f, headerRect.y + 8f, 62f, 20f), tool.RequiresAuthorization ? "需授权" : "只读", tool.RequiresAuthorization ? authorizationStyle : readOnlyStyle);
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
            GUI.Label(new Rect(contentX, rect.y + 10f, contentWidth, 17f), "说明", eyebrowStyle);
            GUI.Label(new Rect(contentX, rect.y + 30f, contentWidth, 31f), tool.Description, bodyStyle);
            GUI.Label(new Rect(contentX, rect.y + 66f, contentWidth, 16f), "参数", eyebrowStyle);
            GUI.Label(new Rect(contentX, rect.y + 83f, contentWidth, 16f), tool.Parameters, parameterStyle);
        }

        private void ShowMcpInfo()
        {
            var endpoint = UnityMcpConnection.ServerUri;
            var message = "WebSocket 终端：" + endpoint + "\n\n"
                + "状态：" + GetStateDescription() + "\n"
                + "Editor ID：" + UnityMcpConnection.CurrentEditorInstanceId + "\n"
                + "项目路径：" + UnityMcpConnection.CurrentProjectPath + "\n"
                + "Console 缓冲：" + UnityMcpConnection.LogCount + " 条\n\n"
                + "多 Agent 场景请为每个 MCP 服务设置 UNITY_MCP_TARGET_EDITOR_ID，或设置 UNITY_MCP_TARGET_PROJECT_PATH。";
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
            GUI.Label(rect, label.ToUpperInvariant(), chipStyle);
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

        private void DrawSectionLabel(Rect rect, string label)
        {
            GUI.Label(rect, label, sectionStyle);
        }

        private bool HasCompleteSkin()
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
                && toolSummaryStyle != null
                && sectionStyle != null
                && parameterStyle != null
                && readOnlyStyle != null
                && authorizationStyle != null;
        }

        private void CreateStyles()
        {
            DestroyTexture(ref panelTexture);
            DestroyTexture(ref selectedPanelTexture);
            DestroyTexture(ref secondaryButtonTexture);
            DestroyTexture(ref successTexture);
            DestroyTexture(ref warningTexture);
            DestroyTexture(ref errorTexture);

            // 对齐 Agent 的 JellyFish 主题：黑紫背景、紫色边框、洋红主色、青色工具标记。
            panelTexture = MakeTexture(new Color(0.075f, 0.045f, 0.105f));
            selectedPanelTexture = MakeTexture(new Color(0.13f, 0.065f, 0.18f));
            secondaryButtonTexture = MakeTexture(new Color(0.56f, 0.12f, 0.48f));
            successTexture = MakeTexture(new Color(0.0f, 0.42f, 0.27f));
            warningTexture = MakeTexture(new Color(0.45f, 0.27f, 0.06f));
            errorTexture = MakeTexture(new Color(0.50f, 0.05f, 0.20f));

            eyebrowStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.75f, 0.49f, 1.0f) }
            };
            headingStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.93f, 0.91f, 0.96f) }
            };
            statusStyle = new GUIStyle(headingStyle) { fontSize = 15 };
            bodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.79f, 0.75f, 0.84f) }
            };
            endpointStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.49f, 0.83f, 0.96f) }
            };
            chipStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                normal = { textColor = new Color(0.93f, 1.0f, 0.97f) }
            };
            secondaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { background = secondaryButtonTexture, textColor = new Color(1.0f, 0.93f, 0.99f) },
                hover = { background = MakeTexture(new Color(0.76f, 0.16f, 0.62f)), textColor = Color.white },
                active = { background = MakeTexture(new Color(0.37f, 0.05f, 0.34f)), textColor = new Color(1.0f, 0.82f, 0.95f) }
            };
            toolTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.49f, 0.83f, 0.96f) }
            };
            toolSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.77f, 0.72f, 0.82f) }
            };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(1.0f, 0.30f, 0.66f) }
            };
            parameterStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1.0f, 0.82f, 0.38f) }
            };
            readOnlyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                normal = { textColor = new Color(0.0f, 1.0f, 0.61f) }
            };
            authorizationStyle = new GUIStyle(readOnlyStyle)
            {
                normal = { textColor = new Color(1.0f, 0.82f, 0.38f) }
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
