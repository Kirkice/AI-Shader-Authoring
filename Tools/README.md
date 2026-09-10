# AI Shader MCP Server

这是一个无第三方依赖的 stdio MCP Server，负责把标准 MCP `tools/list` 和 `tools/call` 请求转发到 Unity Editor 内的 HTTP Bridge。

## 启动

先在 Unity Editor 中执行：

```text
AI Shader/MCP Dashboard 中点击 Start MCP Service
```

然后将 MCP 客户端的 Server 配置指向：

```text
python Tools/ai_shader_mcp_server.py
```

Windows 也可以使用：

```text
py Tools/ai_shader_mcp_server.py
```

可通过环境变量修改 Unity Bridge 地址：

```text
AI_SHADER_UNITY_BRIDGE=http://127.0.0.1:8765
```

## 工具

- `unity_get_console`：按需获取 Console，支持 level、limit、search、includeStackTrace
- `unity_get_errors`：获取 Error、Exception、Assert
- `unity_get_log_detail`：按 id 获取完整堆栈
- `unity_get_status`
- `unity_clear_console`
- `get_unity_status`
- `clear_console_logs`
- `get_console_logs`
- `get_console_errors`
- `get_console_warnings`
- `refresh_assets`
- `get_project_context`
- `write_generated_text`
- `create_validation_scene`
- `capture_validation_frame`

## 重要限制

- Unity Editor 必须保持运行并已启动 Local Bridge；
- Console 读取的是 Unity Package Bridge 缓存的最近 1000 条日志；
- 文件写入由 Unity 侧校验，只允许写入配置的 `generatedAssetsPath`；
- 外部 MCP Server 不直接读写 Unity 工程，也不执行任意 Shell 命令；
- 修改 Shader 的推荐顺序是先 `clear_console_logs`，再写入、刷新、检查编译和读取 Console。
