# Unity MCP Server

本项目的 Unity MCP 使用 WebSocket 架构：Unity Editor 插件主动连接本机的 Node.js MCP 服务，MCP 服务再通过标准输入输出与 AI 客户端通信。

```text
AI Client ⇄ stdio MCP server ⇄ WebSocket :8080 ⇄ Unity Editor
```

## 安装与构建

在 [`Tools/unity-mcp-server`](unity-mcp-server/package.json:1) 中执行：

```powershell
npm install
npm run build
```

构建后入口为 [`Tools/unity-mcp-server/build/index.js`](unity-mcp-server/src/index.ts:1)。将 [`mcp-server.example.json`](mcp-server.example.json:1) 的配置加入 MCP 客户端。

## 启动顺序

1. 先启动或由 MCP 客户端启动 Node.js 服务。
2. 在 Unity 中打开 [`Unity MCP/Dashboard`](../Packages/com.ai.shader-authoring/Editor/Mcp/UnityMcpWindow.cs:68)。
3. Unity Editor 自动连接 `ws://localhost:8080`；也可在面板中点击 **Start service** 发起重连。

## MCP 工具

### 通用诊断

- `get_editor_state`：获取播放状态、当前场景、选择对象、场景层级及工程资产摘要。
- `get_logs`：查询 Unity 编辑器发送到服务端的日志缓冲区。
- `execute_editor_command`：人工批准的 Unity Editor C# 诊断、原型与维护逃生口；正式 Shader 流程不依赖它。

### P0：异步 Unity Job

- `run_unity_job`、`get_unity_job`、`cancel_unity_job`：启动、轮询和取消允许列表内的 Unity 主线程 Job。

### P1：Shader Knowledge Base 与受限资产

- `get_shader_knowledge_base_status`、`build_shader_knowledge_base`、`query_shader_knowledge_base`：管理持久化项目 Shader 知识库。
- `inspect_shader_structure`、`get_asset_revision`：读取 Shader 结构锚点与内容修订。
- `write_generated_text_asset`：仅在 `Assets/AIShader/Generated/` 下写入经 `codePlan.allowedFiles` 和 `baseRevision` 校验的文本资产。
- `refresh_and_compile_assets`、`ensure_validation_scene`、`capture_validation`：异步导入/编译、验证会话与验证证据接口。
- `create_shader_checkpoint`、`restore_shader_checkpoint`：生成不可变 Checkpoint；恢复仅支持具备生成资产快照的 Checkpoint。

完整输入输出契约见 [`plans/unity-mcp-p0-p1-contract.md`](../plans/unity-mcp-p0-p1-contract.md)。

## 运行边界

- WebSocket 仅绑定为本机 `localhost:8080`。
- Unity 端自动重连间隔为 5 秒。
- 结构化写入仅可落在 `Assets/AIShader/Generated/`、`Artifacts/ShaderKnowledgeBase/` 与 `Artifacts/ShaderRuns/`；禁止路径逃逸。
- 生成资产写入必须提供 `operationContext.runId`、`codePlan.codePlanId`、`codePlan.allowedFiles` 与正确的 `baseRevision`。
- `execute_editor_command` 可以运行任意 Unity Editor C# 代码，应仅连接可信的本地 MCP 客户端；不得作为标准 Shader 流程的写入途径。
