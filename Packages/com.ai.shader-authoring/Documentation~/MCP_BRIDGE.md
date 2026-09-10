# AI Shader MCP Bridge MVP

## Unity Editor

### MCP Dashboard

当前 Dashboard 的布局参考 Godot MCP Control Center：顶部 `MCP / 工具` 标签页；MCP 页采用左右 HSplit，包含 Server & Connection 与 Activity & Audit；工具页采用左侧分类 Tree、中间 Tool Details、右侧 Input JSON Schema 的三栏结构，并显示 Permission、Risk、Service 状态。仅参考界面布局，不复用 Godot 的引擎功能。

打开：

```text
AI Shader/MCP Dashboard
```

Dashboard 提供一个接近 Unity Profiler 的深色工作台布局：顶部多级工具栏、左侧 Captures List、中间 MCP Modules/Timeline/Hierarchy/Console 区域，以及右侧 Details Inspector。

Dashboard 提供：

- Local Bridge 状态；
- Endpoint、协议、传输方式和 Package 版本；
- 当前 MCP tools 列表；
- 单个 tool 的输入 schema；
- Project Context 摘要；
- Unity Console 最近日志摘要和 Error 数量；
- Start、Stop、Clear、Open Project Context 操作。

### 启动 HTTP Bridge

推荐直接在 Dashboard 顶部工具栏点击 `▶` 启动；Bridge 也会在 Editor 初始化时尝试自动启动。

默认地址：

```text
http://127.0.0.1:8765
```

Console 查询沿用现有 Unity Console MCP 的按需接口：

```text
GET /health
GET /status
GET /logs?level=errors&limit=50&includeStackTrace=true&search=...
GET /logs/{id}
```

Console MCP Server 可使用环境变量：

```text
UNITY_BRIDGE_URL=http://127.0.0.1:8765
UNITY_MCP_TOKEN=...
```

停止：

```text
AI Shader/MCP/Stop Local Bridge
```

## 外部 MCP Server

无第三方依赖的 stdio MCP Server：

```text
Tools/ai_shader_mcp_server.py
```

启动：

```text
python Tools/ai_shader_mcp_server.py
```

配置示例：

```text
Tools/mcp-server.example.json
```

## HTTP 请求格式

Unity HTTP MVP 使用 POST，并将 `params` 作为 JSON 字符串传递：

```json
{
  "method": "get_unity_status",
  "params": "{}"
}
```

## 已实现方法

```text
ping
get_unity_status
clear_console_logs
get_console_logs
get_console_errors
get_console_warnings
refresh_assets
get_project_context
write_generated_text
create_validation_scene
capture_validation_frame
```

## 写入安全规则

`write_generated_text` 只允许写入 `ProjectSettings/AIShaderProjectContext.json` 中的 `generatedAssetsPath` 及其子目录。禁止 `..` 路径穿越。

## Console 语义

`AIShaderConsoleBridge` 缓存最近 1000 条 Unity 日志，并保留：

- id
- level
- message
- stackTrace
- timestampUtc

每一轮 Shader 修改前，调用方应先执行：

```text
clear_console_logs
write_generated_text
refresh_assets
get_unity_status
get_console_errors
```
