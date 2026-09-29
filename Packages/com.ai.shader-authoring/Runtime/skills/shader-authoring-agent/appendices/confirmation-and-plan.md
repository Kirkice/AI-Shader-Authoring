# Confirmation and Plan Revision

## ShaderCodePlan 扩展

```text
ShaderCodePlan
  planId
  planRevision
  planDigest
  parentPlanRevision
  confirmationRequired
  confirmationId
  iterationId
  baselineRevision
  objective
  semanticNodes, targetAssets, allowedFiles, forbiddenFiles
  anchors, plannedChanges, dependencies, renderState, invariants
  generatedMaterialProperties, shaderGuiAuthoring
  validationPlan, rollbackPlan
```

`planRevision` 从 `1` 开始递增；任何功能、属性、默认值、范围、贴图语义、目标资产、允许文件或基线变化都必须递增。`planDigest` 是当前计划结构化内容的摘要，用于防止确认后静默修改。

## ScopeConfirmationRecord

```text
ScopeConfirmationRecord
  confirmationId, requestId, runId
  planId, planRevision, planDigest, createdAtUtc
  userDecision: confirmed | partially_confirmed | revise_requested | rejected
  retainedFeatures[], removedFeatures[], addedFeatures[]
  propertyDecisions[]
    propertyName, decision: retain | remove | change | pending
    previousValue, newValue, reason
  unresolvedItems[], capabilitySummary, evidence[]
  authorization
    writeAllowed, allowedFiles[]
  supersedesConfirmationId
```

## 写入门禁

只有 `userDecision = confirmed`、`unresolvedItems` 为空、当前 `planRevision` 和 `planDigest` 与记录一致，且授权引用 `confirmationId + planRevision + codePlanId` 时，才允许调用 `write_generated_text_asset`。部分确认、过期确认或计划摘要不匹配均不得写入。

用户提出修改后，旧确认记录只读归档，生成新的 plan revision 和确认记录；不得沿用旧授权或仅修改自然语言摘要。确认记录与写入 Job 必须共享 `runId` 和稳定 `idempotencyKey`。
