# Validation and Evidence

所有静态检查、编译、Console、GUI、场景、截图和性能结果均投影为统一 `ValidationGateResult`，详见 [`unity-mcp-operation-protocol.md`](../../protocols/unity-mcp-operation-protocol.md)。

```text
ValidationGateResult
  gateId
  gateType: static | compile | console | gui | performance | scene | capture | visual | knowledge_base
  status: passed | warning | blocked | failed
  severity: info | warning | error | blocker
  ruleId, expected, actual
  evidence[]: sourceTool, path, revision, location, value, contentHash
  artifacts[], remediation, nextState, evaluatedAtUtc, operationContext
```

## 门禁投影

- 编译失败、Console Error、GUI 解析 Error、目标绑定不一致或截图无响应：阻断视觉验收，进入 `REVISE` 或 `BLOCKED`。
- 性能 `warning`、`high_risk`、`unrated` 只属于 `risk`，不得覆盖视觉 Gate。
- 每个结论必须绑定实际观察到的 Shader/Material revision；证据 revision 不匹配时结果无效。
- 截图必须记录 `contentHash`，并结合目标区域响应与人工目标可见性判断，不得以非空截图单独判定通过。

## 证据最低要求

1. 静态 Gate：计划白名单、锚点、属性、Pass、关键字、渲染状态和禁止特性。
2. 编译 Gate：`refresh_and_compile_assets` Job 终态及 `get_console_diagnostics`。
3. GUI Gate：Properties 标记解析、CustomEditor 和 Inspector 行为。
4. 视觉 Gate：固定场景、参考图、目标图、材质 revision 和 hash 响应。
5. 性能 Gate：真实 GLES 变体、Shader revision 和归档路径；性能失败不阻断视觉链路。
