# Baseline and Capability Resolution

## 三类基线

### MigrationBaseline

用于既有 Shader/Material 的语义迁移，不定义新代码风格。

```text
MigrationBaseline
  baselineId
  sourceShaderPath, sourceShaderRevision, sourcePipeline
  sourceProperties[], sourceKeywords[], sourceTexturePacking[]
  sourceMaterialSemantics[], sourcePasses[], sourceRenderStates[]
  targetShaderPath, targetPipeline
  semanticMappings[]
    sourceSemantic, targetProperty, targetChannel, conversion
    lossPolicy: preserve | default | unsupported | blocked
  unmappedSemantics[]
  migrationStatus: compatible | degraded | blocked
  evidence[]
```

### ProjectStyleBaseline

由 Knowledge Base 中多个项目/包样本归纳，用于目标工程风格对齐。

```text
ProjectStyleBaseline
  baselineId, knowledgeBaseVersion
  sampleScope: assetsIncluded, packagesIncluded, sampleCount
  shaderNaming, passNaming, attributesVaryingsStyle
  keywordMatrix, includeConventions, cbufferConventions
  propertyOrdering, commentLanguage, renderStateConventions
  confidence, evidenceCount, conflictCount
  applicability: status, score, matchedScope, unmatchedScope, reasons[]
```

### NewAssetBaseline

用于没有迁移来源的新 Shader、Material 或验证资产的最低契约。

```text
NewAssetBaseline
  baselineId
  assetKind: shader | material | validation_scene
  requiredSemanticInputs[], requiredProperties[], requiredPasses[]
  requiredRenderStates[], requiredValidationGates[], forbiddenFeatures[]
  defaultValuesPolicy, generatedPathPolicy, rollbackPolicy
```

组合规则：迁移资产使用三类基线；新资产使用项目风格基线 + 新资产基线；纯诊断使用项目风格基线。迁移语义保留和编译事实优先于风格便利。

## CapabilityResolution

```text
CapabilityResolution
  result: resolved | degraded | blocked
  nodes[]
    nodeId, requested
    resolution: selected | fallback | omitted | unsupported | blocked
    reasonCode, fallback, evidence[]
  summary
    selectedCount, fallbackCount, omittedCount
    unsupportedCount, blockedCount
```

- `selected`：请求与证据直接匹配。
- `fallback`：达到用户目标但使用较弱的已确认替代路径。
- `omitted`：未请求或经确认不纳入本轮，不是失败。
- `unsupported`：Skill 或项目明确不支持；必需目标出现时整体 `blocked`。
- `blocked`：缺少关键证据、资产、授权、基线或能力，禁止写入。
- `degraded`：存在 fallback、能力损失或可选 unsupported，但仍可安全继续；必须说明影响。

推导：必需节点 `blocked` 或必需目标 `unsupported` → `blocked`；可选 unsupported 或任意 fallback → `degraded`；仅 selected/合法 omitted → `resolved`。
