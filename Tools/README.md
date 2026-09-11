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
2. 在 Unity 中打开 [`Unity MCP/Dashboard`](../Packages/com.ai.shader-authoring/Editor/AIShaderMcpWindow.cs:87)。
3. Unity Editor 自动连接 `ws://localhost:8080`；也可在面板中点击 **Start service** 发起重连。

## MCP 工具

- `get_editor_state`：获取播放状态、当前场景、选择对象、场景层级及工程资产摘要。
- `execute_editor_command`：动态编译并执行 Unity Editor C# 命令。
- `get_logs`：查询 Unity 编辑器发送到服务端的日志缓冲区。

## 运行边界

- WebSocket 仅绑定为本机 `localhost:8080`。
- Unity 端自动重连间隔为 5 秒。
- `execute_editor_command` 可以运行任意 Unity Editor C# 代码，应仅连接可信的本地 MCP 客户端。
- 原有 HTTP bridge、受限文件写入、Shader 验证和本地 Console 查询工具已移除，不再通过 Unity MCP 暴露。
