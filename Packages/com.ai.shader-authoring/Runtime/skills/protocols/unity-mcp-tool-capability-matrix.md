# Unity MCP Tool Capability Matrix

> 权威实现：`Editor/Mcp/UnityMcpWindow.cs` 的工具目录与 `Editor/Mcp/UnityMcpShaderTools.cs` 的 dispatch。本文档只记录当前包真实存在的结构化工具；Skill 不得引用不存在的通用命令或旧 MCP 路径。

## 工具矩阵

| 工具 | 类别 | 读/写 | 授权 | 异步 Job | 关键输入 | 返回/证据 | revision / 幂等 / 取消 | 主要 Skill |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `get_editor_state` | 连接与 Editor 状态 | 读 | 否 | 否 | 无或状态筛选 | Editor 连接、生命周期、项目状态 | 不适用 | 全部 |
| `get_logs` | 兼容日志读取 | 读 | 否 | 否 | 日志筛选 | 日志条目 | 不适用；新流程优先 `get_console_diagnostics` | 诊断 |
| `get_shader_knowledge_base_status` | 知识库状态 | 读 | 否 | 否 | 可选版本/指纹 | 状态、版本、指纹、Manifest | 绑定知识库版本 | Knowledge Base、Authoring |
| `query_shader_knowledge_base` | 知识库检索 | 读 | 否 | 否 | 查询、分区、限制 | 命中、来源路径、位置、revision、置信度 | 结果必须带证据 | Knowledge Base、Authoring |
| `get_asset_revision` | 资产指纹 | 读 | 否 | 否 | `path` | SHA-256 revision | 写入前必须调用 | 全部 |
| `inspect_shader_structure` | Shader 结构分析 | 读 | 否 | 否 | `path` | Properties、Pass、Keywords、CustomEditor、结构证据 | 绑定当前 revision | Authoring、GUI |
| `get_console_diagnostics` | Console/编译诊断 | 读 | 否 | 否 | `assetPaths`、`includeWarnings` | 结构化 diagnostics、error/warning 计数 | 绑定资产 revision/runId | Authoring、GUI |
| `propose_authorization_grant` | 授权申请 | 写/授权 | 是 | 否 | `operationContext`、scope | 待审批/已授权信息 | 消费型授权，不可跨 run 复用 | Authoring、Knowledge Base |
| `write_generated_text_asset` | 受控资产写入 | 写 | 是 | 否 | `operationContext`、`idempotencyKey`、`asset`、`codePlan`、`baseRevision` | 写入结果、new revision、artifact | 仅 `Assets/AIShader/Generated/`；幂等冲突失败；stale revision 拒绝 | Authoring、GUI（仅协议允许范围） |
| `run_unity_job` | 通用结构化 Job 入口 | 写/执行 | 是 | 是 | `operationContext`、`idempotencyKey`、`jobType`、`args` | `jobId`、状态、acceptedAt、logCursor | 同 key 同输入复用；同 key 不同输入冲突；按 Job 类型取消 | 全部异步操作 |
| `get_unity_job` | Job 查询 | 读 | 否 | 否 | `jobId` | 状态、phase、progress、result、error、artifacts | 断线恢复的首选查询 | 全部异步操作 |
| `cancel_unity_job` | Job 取消 | 写/控制 | 是 | 否 | `jobId`、`reason` | previousStatus、status、preservedArtifacts | 仅协作可取消 Job；`cancelling` 需继续轮询 | 全部异步操作 |
| `build_shader_knowledge_base` | 构建/刷新知识库 | 写/构建 | 是 | 是 | `operationContext`、`idempotencyKey`、`mode`、`reason` | 构建结果、版本、覆盖率、工件 | 可取消；失败不得覆盖 `current.json` | Knowledge Base |
| `refresh_and_compile_assets` | Refresh + Unity 编译 | 写/编译 | 是 | 是 | `operationContext`、`idempotencyKey`、`assetPaths` | 编译 diagnostics、error/warning、revision | 可取消；需轮询至终态 | Authoring、GUI |
| `export_compiled_gles_variants` | GLES 变体导出 | 编译/性能 | 是 | 是 | `operationContext`、`idempotencyKey`、`shaderPath` | 真实 GLES GLSL、变体、shader revision | 可取消；不得把 HLSL 当 GLSL | Performance |
| `analyze_shader_performance` | 静态/Mali 性能分析 | 性能 | 是 | 是 | `operationContext`、`idempotencyKey`、`shaderPath`、策略、变体 | 指标、预算评估、归档、性能风险 | 可取消；风险不阻断视觉验收 | Performance |
| `ensure_validation_scene` | 固定场景配置 | 写/验证准备 | 是 | 是 | `operationContext`、`idempotencyKey`、`validationProfile`、`target` | validationSessionId、绑定 revision | 当前实现未列入协作取消 | Authoring |
| `capture_validation` | 固定场景截图/统计 | 写/验证 | 是 | 是 | `operationContext`、`idempotencyKey`、`validationSessionId`、`captures` | PNG、contentHash、统计、绑定 revision | 可取消；材质替换必须通过 hash/区域响应验证 | Authoring |
| `create_shader_checkpoint` | Shader 检查点 | 写/回滚证据 | 是 | 是 | `operationContext`、`idempotencyKey`、目标资产 | 检查点工件、revision | 当前实现未列入协作取消 | Authoring |

## 调用规则

1. Skill 文档引用的工具名必须来自本矩阵；不得使用遗留的通用编辑器命令接口作为常规路径。
2. 真实实现文件是 `Editor/Mcp/` 下的 C#。文档可引用实现文件，但不得把源码路径伪装成可调用 MCP endpoint。
3. 任何异步操作都必须保存 `operationContext.runId`、`operationContext.skill`、`operationContext.codePlanId`（如适用）、`idempotencyKey` 和 `jobId`。
4. 只读工具不替代授权；写入、编译、场景配置、截图和性能导出必须满足授权策略。
5. `get_logs` 仅用于兼容性诊断；编译和验收门禁必须使用 `get_console_diagnostics`。
