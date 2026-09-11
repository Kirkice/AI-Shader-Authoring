# Unity MCP WebSocket Integration

Unity MCP 使用外部 [`unity-mcp-server`](../../../Tools/unity-mcp-server/src/index.ts:1) 作为标准输入输出 MCP 服务，并由 Unity Editor 端的 [`UnityMcpConnection`](../Editor/Mcp/UnityMcpConnection.cs:24) 建立本机 WebSocket 连接。

```text
MCP client ⇄ stdio server ⇄ ws://localhost:8080 ⇄ Unity Editor
```

## Unity Editor 插件

插件在 Unity 加载时启动，并会：

- 连接 `ws://localhost:8080`；
- 每秒发送编辑器状态；
- 转发 Unity Console 日志；
- 接收服务端的受控结构化工具调用，并在 Unity 主线程执行允许列表操作；
- 接收人工批准的编辑器命令；
- 断开后每 5 秒自动重连。

可从 [`Unity MCP/Dashboard`](../Editor/Mcp/UnityMcpWindow.cs:68) 查看状态，或手动请求重连。

## 程序集隔离

Unity MCP 位于 [`Editor/Mcp`](../Editor/Mcp/)，并由 [`UnityMcp.Editor.asmdef`](../Editor/Mcp/UnityMcp.Editor.asmdef:1) 编译为独立的 `UnityMcp.Editor` 程序集。该程序集不引用 `AIShaderAuthoring.Editor` 或 `AIShaderAuthoring.Runtime`；AI Shader 仅是可被通用 C# Editor 命令操作的一个使用方。

## 工具

| 分组 | 工具 | 说明 |
| --- | --- | --- |
| 诊断 | `get_editor_state`、`get_logs` | 返回编辑器状态与 Node 缓存的 Unity 日志。 |
| 受控 Job | `run_unity_job`、`get_unity_job`、`cancel_unity_job` | 管理知识库、导入编译、验证与 Checkpoint Job。 |
| 知识库 | `get_shader_knowledge_base_status`、`build_shader_knowledge_base`、`query_shader_knowledge_base` | 读取、构建和检索持久化项目 Shader 知识库。 |
| 资产 | `inspect_shader_structure`、`get_asset_revision`、`write_generated_text_asset` | 读取 Shader 锚点/修订，或在生成目录执行 revision-protected 写入。 |
| 验证 | `refresh_and_compile_assets`、`ensure_validation_scene`、`capture_validation` | 执行异步导入编译与验证证据采集。 |
| 回滚 | `create_shader_checkpoint`、`restore_shader_checkpoint` | 创建 Checkpoint；恢复仅面向生成目录文本资产。 |
| 人工诊断 | `execute_editor_command` | 编译并执行任意 Unity Editor C#，不属于正式 Shader 流程。 |

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
