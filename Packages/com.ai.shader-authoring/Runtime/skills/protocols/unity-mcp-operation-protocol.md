# Unity MCP Operation Protocol

## 1. 适用范围与权威实现

本协议统一 `Runtime/skills/` 下各 Skill 与结构化 Unity MCP 的调用、状态、错误、写入和验收结果。工具能力以 [`unity-mcp-tool-capability-matrix.md`](./unity-mcp-tool-capability-matrix.md) 为准；实现以 `Editor/Mcp/UnityMcpWindow.cs`、`Editor/Mcp/UnityMcpShaderTools.cs` 和 `Editor/Mcp/UnityMcpShaderTools.Jobs.cs` 为准。

禁止将遗留的通用编辑器命令接口、旧 MCP 源码路径或任意裸 C# 作为常规执行接口。

## 2. 统一状态模型

不同维度不得复用同一个 `status` 字段：

```text
lifecycle: classifying | planning | awaiting_confirmation | executing | validating | completed
result: passed | degraded | blocked | failed | cancelled | interrupted
risk: info | warning | high_risk | unrated
job: queued | running | cancelling | succeeded | failed | cancelled | interrupted
knowledgeBase: missing | building | fresh | partial | failed | blocked
```

- `result` 是 Skill 或 Gate 的业务结果；`job` 只描述异步执行生命周期。
- `risk` 只描述性能或诊断风险，不得把 `warning` 当作主流程失败。
- Knowledge Base 的 `fresh` 不是通用 PASS；它只表示知识库可作为主链路输入。
- 主材质流程对外仍可投影为 `PASS | REVISE | BLOCKED`，但内部必须同时提供 `result` 和结构化 Gate 证据：`PASS = passed`，`REVISE = failed/degraded`，`BLOCKED = blocked`。

## 3. operationContext

所有写入或 Job 调用必须携带：

```text
operationContext
  runId: string
  skill: string
  codePlanId: string | optional
  requestId: string | optional
  parentJobId: string | optional
  userScopeConfirmed: boolean | optional
```

`runId` 是一次端到端运行的稳定关联 ID；`codePlanId` 必须与当前授权计划一致；断线重连不得生成新的 runId 来掩盖原操作。

## 4. 统一错误结果与错误码

失败不得只返回自由文本异常。可调用方应归一化为：

```text
ErrorResult
  ok: false
  error
    code
    category: transport | schema | authorization | path | revision | idempotency | job | unity | validation | knowledge_base | performance | markup
    message
    retryable: boolean
    evidence[]
    remediation
    sourceTool
    operationContext
    jobId: optional
```

标准错误码：

```text
MCP_TOOL_NOT_FOUND
MCP_SCHEMA_INVALID
UNITY_DISCONNECTED
UNITY_NOT_REGISTERED
AUTHORIZATION_REQUIRED
AUTHORIZATION_DENIED
PATH_OUTSIDE_ALLOWLIST
PATH_EXTENSION_NOT_ALLOWED
PLAN_REQUIRED
PLAN_CONTEXT_MISMATCH
STALE_REVISION
IDEMPOTENCY_CONFLICT
JOB_NOT_FOUND
JOB_QUEUE_FULL
JOB_NOT_CANCELLABLE
JOB_INTERRUPTED
JOB_CANCELLED
COMPILE_FAILED
CONSOLE_ERRORS_PRESENT
VALIDATION_SESSION_NOT_FOUND
VALIDATION_BINDING_MISMATCH
VALIDATION_HASH_NO_RESPONSE
KNOWLEDGE_BASE_NOT_FRESH
KNOWLEDGE_BASE_FINGERPRINT_MISMATCH
MARKUP_PARSE_FAILED
PERFORMANCE_INPUT_INVALID
PERFORMANCE_TOOL_UNAVAILABLE
```

## 5. ValidationGateResult

所有静态检查、编译、Console、GUI、性能输入和视觉验收均输出同一结构：

```text
ValidationGateResult
  gateId
  gateType: static | compile | console | gui | performance | scene | capture | visual | knowledge_base
  status: passed | warning | blocked | failed
  severity: info | warning | error | blocker
  ruleId
  expected
  actual
  evidence[]
    sourceTool
    path
    revision
    location
    value
    contentHash: optional
  artifacts[]
  remediation
  nextState
  evaluatedAtUtc
  operationContext
```

规则：

- `passed` 才能满足阻断型 Gate；`warning` 只有在 Gate 明确声明非阻断时可继续。
- 性能 Gate 的 `warning/high_risk/unrated` 是 `risk` 投影，不得覆盖视觉 Gate 的 `status`。
- 每个 `evidence` 必须能回到工具、路径、revision 或工件；截图必须带 `contentHash`。
- 编译、Console 和 GUI Error 会阻断视觉验收；性能工具缺失不会阻断视觉验收。

## 6. 写入、路径与授权

1. 生成资产写入只能位于 `Assets/AIShader/Generated/`；知识库和运行工件位于 `Artifacts/ShaderKnowledgeBase/`、`Artifacts/ShaderRuns/`。
2. 写入必须有 `codePlanId`、匹配的 `operationContext`、显式授权和 `baseRevision`（新文件按实现约定使用空基线）。
3. 写入前调用 `get_asset_revision`；写入后记录 `newRevision`，并保留失败版本、差异和诊断。
4. 不得修改 Code Plan 白名单之外的文件；需要新增资产必须先更新计划并重新授权。
5. 授权是按 scope/path/run 消费的，不得把一次授权静默复用于另一轮。

## 7. revision 与并发保护

- revision 是目标文件内容的 SHA-256 小写十六进制摘要。
- `baseRevision` 与当前 revision 不一致时，拒绝写入并返回 `STALE_REVISION`；调用方必须重新读取、重新生成差异并取得新授权。
- 编译、截图、性能和 GUI 结果必须记录它们实际观察到的 Shader/Material revision；结果与目标 revision 不一致时不可用于 PASS。
- revision 变化不等于视觉响应成功；截图还必须满足 contentHash 或目标区域像素响应规则。

## 8. 幂等协议

- 所有写入和 Job 使用稳定的 `idempotencyKey`，建议由 `runId + codePlanId + operation + targetRevision + inputDigest` 组成。
- 相同 key 且输入 hash 相同：返回原结果或原 `jobId`，并标记 `reused: true`，不得重复副作用。
- 相同 key 但输入 hash 不同：返回 `IDEMPOTENCY_CONFLICT`，不得覆盖原操作。
- 断线重试前先查询 `get_unity_job` 或读取写入结果；不得通过换 key 强行重放未知是否完成的写入。

## 9. Job、取消与断线恢复

Job 状态只允许以下转移：

```text
queued -> running -> succeeded | failed
queued -> cancelled
running -> cancelling -> cancelled | succeeded | failed
queued/running/cancelling -- Unity domain reload/process restart --> interrupted
```

- `get_unity_job` 是恢复流程的首选；`jobId` 未知时返回 `JOB_NOT_FOUND`，不得猜测结果。
- `cancel_unity_job` 对 queued Job 可直接取消；对协作可取消的 running Job 返回 `cancelling`，必须继续轮询到终态。
- 当前可协作取消：知识库构建、刷新编译、GLES 导出、性能分析、验证截图。场景配置和检查点当前不可依赖协作取消。
- `interrupted` 表示 Unity Domain Reload 或进程重启中断；实现会持久化 Job，但不会自动重放非幂等操作。调用方必须重新读取输入、revision 和授权后决定是否以新 key 重试。
- 断线恢复顺序：保留原 `runId` → `get_editor_state` 检查连接 → `get_unity_job(jobId)` 查询 → 校验 Job 状态/输入 hash/工件/revision → 仅在明确未完成且授权仍有效时继续或重新计划。
- 取消或中断后已产生的工件必须保留并标记，不得删除证据来伪造干净重试。

## 10. 统一调用与验收最小闭环

```text
get_editor_state
  -> get_asset_revision / inspect_shader_structure
  -> propose_authorization_grant
  -> write_generated_text_asset (如需写入)
  -> refresh_and_compile_assets
  -> get_unity_job
  -> get_console_diagnostics
  -> ensure_validation_scene
  -> capture_validation
  -> ValidationGateResult 汇总
```

性能分支为 `export_compiled_gles_variants -> analyze_shader_performance`，其风险结果仅作为非阻断预警。任何阻断 Gate 失败都必须返回 `ErrorResult`、保留证据并进入 `REVISE` 或 `BLOCKED`，不得用截图或性能报告覆盖编译错误。
