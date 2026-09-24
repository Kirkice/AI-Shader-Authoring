# Unity MCP WebSocket Integration

Unity MCP 使用外部 [`unity-mcp-server`](../../../Tools/unity-mcp-server/src/index.ts:1) 作为标准输入输出 MCP 服务，并由 Unity Editor 端的 [`UnityMcpConnection`](../Editor/Mcp/UnityMcpConnection.cs:27) 建立 WebSocket 连接。

```text
Agent MCP stdio session ⇄ bound unity-mcp-server ⇄ configured ws://host:port ⇄ identified Unity Editor
```

## Unity Editor 插件

插件在 Unity 加载时启动，并会：

- 连接 Dashboard 保存的 `ws://主机:端口`（默认 `ws://127.0.0.1:8080`）；
- 首次连接发送 `hello`，注册会话级 `editorInstanceId`、项目路径、Unity 版本和进程 ID；
- 每秒发送带 `editorInstanceId` 与项目路径的编辑器状态；
- 转发 Unity Console 日志；
- 接收服务端的受控结构化工具调用，并在 Unity 主线程执行允许列表操作；
- 接收人工批准的编辑器命令；
- 断开后每 5 秒自动重连。

可从 [`Unity MCP/Dashboard`](../Editor/Mcp/UnityMcpWindow.cs:84) 查看当前 Editor ID、项目路径和连接状态；也可直接填写 Node 服务的主机名/IP 与端口。点击“应用”后，设置会通过 `EditorPrefs` 持久化，Unity 客户端将断开并自动重新连接到新端点。主机字段仅填写主机名、IPv4 或 IPv6 地址，不要添加 `ws://`、路径或查询参数。

## WebSocket 端点配置

Node 服务通过环境变量配置监听端点：

- `UNITY_MCP_WS_HOST`：监听主机，默认 `127.0.0.1`；需要局域网访问时可设置为具体网卡 IP 或 `0.0.0.0`。
- `UNITY_MCP_WS_PORT`：监听端口，默认 `8080`，范围 `1`–`65535`。

例如在局域网中让 Unity 连接 `192.168.1.20:8090`：

```powershell
$env:UNITY_MCP_WS_HOST = "192.168.1.20"
$env:UNITY_MCP_WS_PORT = "8090"
node H:/tmp/URP-AI/Tools/unity-mcp-server/build/index.js
```

然后在 Unity Dashboard 中填写服务器地址 `192.168.1.20`、端口 `8090` 并点击“应用”。对外网或不可信网络开放端口存在高风险；该服务可执行受信任客户端请求，建议仅使用回环地址、受控局域网或受保护的隧道。

## 多 Agent / 多 Unity 绑定

每个 stdio MCP 服务进程生成独立 `agentSessionId`，并且必须精确解析一个目标 Unity Editor：

- `UNITY_MCP_TARGET_EDITOR_ID`：Dashboard 中显示的 `editorInstanceId`，优先级最高；
- `UNITY_MCP_TARGET_PROJECT_PATH`：规范化后的 Unity 项目根路径，例如 `H:/tmp/URP-AI`。

若未设置绑定变量且只有一个已完成 `hello` 的 Unity Editor，服务自动使用该 Editor；零个候选或多个候选都会拒绝工具调用，而不会猜测、广播或使用“最后连接”的 Editor。所有工具调用带唯一 `requestId` 和当前 `agentSessionId`；Unity 回包原样回传它们，服务端只接受来自已绑定 Editor 的匹配回包。

PowerShell 示例：

```powershell
$env:UNITY_MCP_TARGET_EDITOR_ID = "从 Unity MCP Dashboard 复制的 Editor ID"
node H:/tmp/URP-AI/Tools/unity-mcp-server/build/index.js
```

或按项目绑定：

```powershell
$env:UNITY_MCP_TARGET_PROJECT_PATH = "H:/tmp/URP-AI"
node H:/tmp/URP-AI/Tools/unity-mcp-server/build/index.js
```

## 程序集隔离

Unity MCP 位于 [`Editor/Mcp`](../Editor/Mcp/)，并由 [`UnityMcp.Editor.asmdef`](../Editor/Mcp/UnityMcp.Editor.asmdef:1) 编译为独立的 `UnityMcp.Editor` 程序集。该程序集不引用 `AIShaderAuthoring.Editor` 或 `AIShaderAuthoring.Runtime`；AI Shader 仅是可被通用 C# Editor 命令操作的一个使用方。

## 工具

| 分组 | 工具 | 说明 |
| --- | --- | --- |
| 诊断 | `get_editor_state`、`get_logs` | 返回编辑器状态与 Node 缓存的 Unity 日志。 |
| 受控 Job | `run_unity_job`、`get_unity_job`、`cancel_unity_job` | 管理知识库、导入编译与验证 Job。 |
| 性能 | `export_compiled_gles_variants`、`analyze_shader_performance` | 先经 Unity 编译导出 GLES3x 顶点/片元 GLSL，再执行静态与可选 Mali Offline Compiler 分析；结果仅预警。 |
| 知识库 | `get_shader_knowledge_base_status`、`build_shader_knowledge_base`、`query_shader_knowledge_base` | 读取、构建和检索持久化项目 Shader 知识库。 |
| 资产 | `inspect_shader_structure`、`get_asset_revision`、`write_generated_text_asset` | 读取 Shader 锚点/修订，或在生成目录执行 revision-protected 写入。 |
| 验证 | `refresh_and_compile_assets`、`ensure_validation_scene`、`capture_validation` | 执行异步导入编译与验证证据采集。 |
| 人工诊断 | `execute_editor_command` | 编译并执行任意 Unity Editor C#，不属于正式 Shader 流程。 |

### 知识库查询

`query_shader_knowledge_base` 接受可选的 `query`、`tags`、`types` 和 `limit` 参数。查询会按词条匹配并按相关度排序，仅返回最多 `limit`（默认 10、最大 50）条结果；`types` 可限定为 `shaderExample`、`functionCard`、`convention` 或 `capability`。未提供查询条件时，仍会按 `limit` 返回各分区中的有限结果，而不会返回完整知识库。

### Job 取消

`cancel_unity_job` 返回实际 Job 状态。当前 `refresh_and_compile_assets` 在导入/等待编译的阶段支持取消，状态会经历 `running` → `cancelling` → `cancelled`；其他同步执行的 Job 会返回当前状态而不会虚报已取消。所有 Job 的最终状态通过 `get_unity_job` 获取。

### GLES 编译导出

`export_compiled_gles_variants` 接收 `shaderPath`（仅 `Assets/*.shader`），通过 Unity 内部的已编译 Shader 导出路径请求 GLES3x 平台产物，并只在解析到包含 `#version` 与 `void main` 的真实 GLSL Stage 时返回 `compiledGlesVariants`。该 Job 失败时会返回诊断，绝不会把 ShaderLab/HLSL 伪装成 GLSL；调用方仍可继续静态性能分析和视觉验收。

`analyze_shader_performance` 未显式传入 `compiledGlesVariants` 时，会自动执行同一导出步骤，然后才调用 `malioc`。外部 GLSL 输入仅供诊断与回归测试。

结构化工具的完整请求/响应定义见 [`unity-mcp-p0-p1-contract.md`](../../../plans/unity-mcp-p0-p1-contract.md)。

## Node 服务

在 [`Tools/unity-mcp-server`](../../../Tools/unity-mcp-server/package.json:1) 安装依赖并构建：

```powershell
npm install
npm run build
```

构建入口是 [`build/index.js`](../../../Tools/unity-mcp-server/build/index.js)。客户端配置示例见 [`mcp-server.example.json`](../../../Tools/mcp-server.example.json:1)。

## 安全说明

结构化工具不接受 C# 源码，并强制项目相对路径及允许写入根：`Assets/AIShader/Generated/`、`Artifacts/ShaderKnowledgeBase/`、`Artifacts/ShaderRuns/`。生成资产写入还必须满足 `baseRevision`、`operationContext.runId`、`codePlan.codePlanId` 与 `codePlan.allowedFiles` 校验。

`execute_editor_command` 是完全信任模式：它会编译并执行 MCP 客户端提供的 C# 源码。仅允许可信的本地 MCP 客户端连接此服务，且它只能用于人工批准的诊断、原型和维护，不可替代正式结构化 Shader 流程。
