# Unity MCP WebSocket Integration

Unity MCP 使用外部 [`unity-mcp-server`](../../../Tools/unity-mcp-server/src/index.ts:1) 作为标准输入输出 MCP 服务，并由 Unity Editor 端的 [`UnityMcpConnection`](../Editor/UnityMcpConnection.cs:22) 建立本机 WebSocket 连接。

```text
MCP client ⇄ stdio server ⇄ ws://localhost:8080 ⇄ Unity Editor
```

## Unity Editor 插件

插件在 Unity 加载时启动，并会：

- 连接 `ws://localhost:8080`；
- 每秒发送编辑器状态；
- 转发 Unity Console 日志；
- 接收并执行服务端发来的编辑器命令；
- 断开后每 5 秒自动重连。

可从 [`Unity MCP/Dashboard`](../Editor/AIShaderMcpWindow.cs:87) 查看状态，或手动请求重连。

## 工具

| 工具 | 说明 |
| --- | --- |
| `get_editor_state` | 返回播放状态、场景、选择对象、层级和工程资产摘要。 |
| `execute_editor_command` | 编译并执行任意 Unity Editor C# 命令。 |
| `get_logs` | 返回 Node MCP 服务缓存的 Unity 日志。 |

## Node 服务

在 [`Tools/unity-mcp-server`](../../../Tools/unity-mcp-server/package.json:1) 安装依赖并构建：

```powershell
npm install
npm run build
```

构建入口是 [`build/index.js`](../../../Tools/unity-mcp-server/build/index.js)。客户端配置示例见 [`mcp-server.example.json`](../../../Tools/mcp-server.example.json:1)。

## 安全说明

`execute_editor_command` 是完全信任模式：它会编译并执行 MCP 客户端提供的 C# 源码。仅允许可信的本地 MCP 客户端连接此服务。此前 HTTP bridge 的路径白名单、Shader 验证、截图、生成文件写入和本地 Console REST API 均已移除。
