# Knowledge Base Schema Appendix

## 证据适用性契约

所有分区、样本、能力结论和项目约定必须带以下字段：

```text
scope
  project: project | package | external
  paths[]
  assetKinds[]
  pipelines[]
  shaderModels[]
  sampleCount
  timeRange

evidenceCount
  total
  direct
  inferred
  compiled
  runtime

conflictCount
  total
  byRule[]
    ruleId, count, paths[]
    resolution: unresolved | prefer_recent | prefer_compiled | manual

applicability
  status: applicable | partial | unknown | not_applicable
  score: 0.0..1.0
  matchedScope[]
  unmatchedScope[]
  reasons[]
  evaluatedAgainst
```

## 放行规则

- `scope` 必须覆盖当前任务的项目/包、管线、Shader Model 和资产类型；范围不匹配时不能直接复用结论。
- `evidenceCount` 必须可由 evidence 条目重算；单一 inferred 证据不得单独升级为 confirmed capability。
- `conflictCount.total > 0` 时必须保留冲突路径和解决策略。关键字矩阵、纹理通道、Pass 或 Render State 的未解决冲突使 applicability 至少为 `partial`，必要时为 `blocked`。
- `applicability = applicable` 才能作为当前任务的直接依据；`partial` 只能作为候选证据，`unknown` 不得转换为 supported，`not_applicable` 不得进入 Shader 计划。
- `fresh` 仍是 Knowledge Base 发布状态；它不自动等价于当前任务 applicable。主链路必须同时检查 scope、证据、冲突和 applicability。

## 兼容旧版本

旧知识库缺少这些字段时，读取层将其视为 `unknown` 或 `0`，不得猜测补齐；增量刷新或完整重建后才能获得可放行的适用性结论。失败版本不得覆盖 `current.json`。
