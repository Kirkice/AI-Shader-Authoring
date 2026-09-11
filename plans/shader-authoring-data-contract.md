# Shader Authoring 端到端数据交接契约

## 1. 目标

本文定义两个 Skill 与 Unity MCP 之间的不可变数据交接边界：

```text
RequirementEnvelope
  -> ClassificationResult
  -> MaterialIntent
  -> EffectOrderDecision
  -> RenderingSemanticGraph
  -> Knowledge Retrieval + ProjectShaderProfile
  -> ResolvedSemanticGraph
  -> ShaderCodePlan
  -> MCP Operation Records
  -> ValidationEvidence
  -> DecisionRecord + Checkpoint
```

实现代码、HLSL、Unity API 调用和任意 C# 均不属于这些中间对象。它们只能出现在经过审核的 `ShaderCodePlan` 所描述的生成资产内容中，或作为 MCP 工具的内部实现细节。

## 2. 全局身份与不可变性

所有对象共享以下关联字段：

```text
TraceContext
  requestId: string
  runId: string
  taskId: string
  iterationId: string
  parentArtifactId: string | null
  createdAtUtc: ISO-8601
  producer: user | shader-authoring-agent | shader-knowledge-base-builder | unity-mcp
  schemaVersion: string
```

规则：

1. 每次用户需求提交创建新的 `requestId` 和 `runId`。
2. 每次只验证一个语义假设的迭代创建新的 `iterationId`。
3. 所有对象发布后不可原地修改；修正产生新对象，并通过 `parentArtifactId` 指向前一版本。
4. 所有对象都应持久化为 JSON 工件，建议路径为 [`Artifacts/ShaderRuns/`](../Artifacts/ShaderRuns/) 下对应的 `runId` 目录。
5. `knowledgeBaseVersion`、资产路径和 revision 必须使用生成时的实际值，禁止仅记录“最新”或“当前”。

## 3. 输入与需求分类

### 3.1 `RequirementEnvelope`

```text
RequirementEnvelope
  trace: TraceContext
  originalText: string
  userProvidedAssets
    path: string
    declaredSemantic: string | null
  userConstraints
    targetObject: string | null
    targetPlatform: string | null
    allowedWriteRoots: string[]
  priorDecisionRefs: ArtifactRef[]
```

`originalText` 是唯一不可改写的原始需求证据。受控摘要、默认值与推断均不得回写原始文本。

### 3.2 `ClassificationResult`

```text
ClassificationResult
  trace: TraceContext
  sourceRequirementId: string
  category: material | vfx | post_process | ambiguous
  confidence: high | medium | low
  evidence: string[]
  route: proceed_material | clarify | unsupported_or_manual
  clarificationQuestions: ClarificationQuestion[]
  exclusions: string[]
```

进入 `MaterialIntent` 的前提：`category = material` 且 `route = proceed_material`。其他类别不创建伪装成材质的后续对象。

## 4. `MaterialIntent` 与效果排序确认

### 4.1 `MaterialIntent`

`MaterialIntent` 表达用户想要的材质结果，不表达实现。它沿用 [`shader-authoring-agent`](shader-authoring-agent/SKILL.md) 的槽位，补充本契约要求的来源与决策引用：

```text
MaterialIntent
  trace: TraceContext
  sourceRequirementId: string
  identity
    originalRequirementRef: ArtifactRef
    controlledSummary: string
  scope
    category: material
    workflow: metallic_roughness
    targetDescription: string | null
  surface
    baseColor: IntentValue
    metallic: IntentValue
    roughness: IntentValue
    normal: IntentValue
    ambientOcclusion: IntentValue
    emission: IntentValue
  alpha
    blendMode: opaque | alpha_blend
    alphaClipEnabled: boolean
    alphaSource: IntentValue
    alphaClipThreshold: IntentValue | null
  textures: TextureSemanticRequirement[]
  effects
    orderedLayers: EffectLayerIntent[]
    orderingDecisionRef: ArtifactRef | null
  assumptions: Assumption[]
  constraints
    supported: string[]
    excluded: string[]
    projectDependent: string[]
  acceptanceIntent
    visibleTargets: string[]
    testMatrixOverrides: object
    expectedDebugChannels: string[]

IntentValue
  value: scalar | color | texture_ref | expression_description | disabled | unknown
  source: explicit | default | inferred
  rationale: string
  confidence: high | medium | low
```

`MaterialIntent` 只可出现 `default` 与 `inferred`，但每项必须携带 `rationale`。当该字段影响透明、裁剪、贴图类型或效果组合时，除非用户已经确认，不得将其视为可执行事实。

### 4.2 `EffectLayerIntent`

```text
EffectLayerIntent
  id: string
  kind: dissolve | pulse_emission | fresnel_rim
  enabled: boolean
  inputs: object
  modulationTargets: string[]
  requestedComposition: EffectCompositionRequest[]
  timeDependency: none | optional | required
  requiredCapabilities: string[]
  unsupportedReason: string | null
```

`requestedComposition` 仅描述用户明确给出的组合意图。默认顺序不直接写到此对象，直到存在下面的确认记录。

### 4.3 `EffectOrderDecision`

当启用效果层数量大于等于 2 且用户未给出完整排序时，必须先建立此对象，再创建可执行的 `RenderingSemanticGraph`。

```text
EffectOrderDecision
  trace: TraceContext
  sourceMaterialIntentId: string
  mode: user_explicit | default_proposed | default_confirmed | user_adjusted | blocked
  proposedRequests: EffectCompositionRequest[]
  userPrompt: string
  userResponse: string | null
  resolvedRequests: EffectCompositionRequest[]
  unresolvedConflicts: string[]
  decisionEvidence: string[]
```

默认 `proposedRequests`：

```text
alpha.coverage / alpha.clipThreshold
  dissolve: order 10

emission.additive
  dissolve_edge: add, order 20
  pulse_emission: add, order 30
  fresnel_rim_emission: add, order 40
```

- `default_proposed` 不可用于图构建或任何 MCP 写入。
- 用户明确“不需要调整”后转为 `default_confirmed`。
- 用户明确重排、替换、乘法调制后转为 `user_adjusted`。
- 用户拒绝确认且未提供可解析顺序时转为 `blocked`。

## 5. `RenderingSemanticGraph`

图的节点、语义槽位、阶段和冲突规则由 [`shader-authoring-agent`](shader-authoring-agent/SKILL.md) 定义。本契约规定其输入输出引用：

```text
RenderingSemanticGraph
  trace: TraceContext
  sourceMaterialIntentId: string
  effectOrderDecisionId: string | null
  workflow: metallic_roughness
  nodes: GraphNode[]
  edges: GraphEdge[]
  effectComposition: EffectCompositionRequest[]
  outputContract
    blendMode: opaque | alpha_blend
    alphaClipEnabled: boolean
    requiredRenderStates: string[]
  unresolvedSemantics: string[]
  debugContract: DebugChannelContract[]
  graphStatus: valid | blocked
  blockedReasons: string[]
```

图构建前置条件：

- 所有必需 `MaterialIntent` 字段已解析或可安全默认。
- 多层效果存在已确认的 `EffectOrderDecision`。
- `effectComposition` 中每一个写入目标都能建立唯一 Compose 节点。
- 不存在循环、非法跨阶段读写或未定义合成操作。

`graphStatus = blocked` 时，不允许查询实时锚点、生成计划或调用写入 MCP 工具。

## 6. 知识库检索与 `ProjectShaderProfile`

### 6.1 `KnowledgeRetrievalRequest`

主 Skill 使用有效 `knowledgeBaseVersion` 请求证据；请求与 [`query_shader_knowledge_base`](unity-mcp-p0-p1-contract.md) 一致。

```text
KnowledgeRetrievalRequest
  trace: TraceContext
  knowledgeBaseVersion: string
  sourceGraphId: string
  semanticFeatures: string[]
  requiredCapabilities: string[]
  renderModes: opaque | alpha_clip | alpha_blend
  effectKinds: dissolve | pulse_emission | fresnel_rim
  targetPipeline: builtin | urp | hdrp | custom_srp | unknown
```

`KnowledgeRetrievalResult` 中每个样本和函数卡片必须附带 `sourceRevision` 与 `sourceLocation`，但它们仅是候选，不能直接成为写入锚点。

### 6.2 `ProjectShaderProfile`

`ProjectShaderProfile` 是候选证据经实时资产检查后的任务级事实快照。

```text
ProjectShaderProfile
  trace: TraceContext
  knowledgeBaseVersion: string
  sourceGraphId: string
  projectStateFingerprint: string
  selectedBaseline
    shaderAsset: AssetRevisionRef | null
    sourceExamples: EvidenceRef[]
    selectionReason: string
  targetAnchors
    passesAndLightModes: AnchorRef[]
    vertexAndFragmentEntries: AnchorRef[]
    propertiesAndKeywords: AnchorRef[]
    dataStructures: AnchorRef[]
    includeAnchors: AnchorRef[]
    renderStateAnchors: AnchorRef[]
  taskInputs
    targetRenderer: UnityObjectRef | null
    targetMaterial: AssetRevisionRef | null
    providedTextures: AssetRevisionRef[]
    missingAssets: MissingRequirement[]
  resolvedInterfaces: CapabilityResolution[]
  alphaAndOutput
    alphaClipConvention: CapabilityResolution
    transparentPassConvention: CapabilityResolution
    blendConvention: CapabilityResolution
    zWriteConvention: CapabilityResolution
    renderQueueConvention: CapabilityResolution
  capabilityEvidence: EvidenceRef[]
  unknowns: string[]
  incrementalEvidence: IncrementalEvidence[]
  profileStatus: ready | incomplete | blocked
```

```text
AnchorRef
  anchorId: string
  asset: AssetRevisionRef
  kind: property | pass | light_mode | entry | struct | include | keyword | render_state
  sourceLocation: SourceRange
  semanticRole: string

CapabilityResolution
  capability: string
  status: supported | unsupported | unknown
  evidence: EvidenceRef[]
  confidence: high | medium | low
  fallbackPolicy: string | null
```

生成 `ProjectShaderProfile` 前必须依次调用：

1. [`get_shader_knowledge_base_status`](unity-mcp-p0-p1-contract.md) 确认 `fresh` 和指纹匹配；
2. [`query_shader_knowledge_base`](unity-mcp-p0-p1-contract.md) 获得候选；
3. [`get_asset_revision`](unity-mcp-p0-p1-contract.md) 确认候选 revision；
4. [`inspect_shader_structure`](unity-mcp-p0-p1-contract.md) 获取实时锚点。

若知识库证据与实时检查不同，必须将差异写入 `incrementalEvidence`，但本运行不允许静默刷新知识库。

## 7. `ResolvedSemanticGraph`

```text
ResolvedSemanticGraph
  trace: TraceContext
  sourceGraphId: string
  sourceProfileId: string
  status: resolved | degraded | blocked
  selectedNodes: string[]
  omittedNodes
    nodeId: string
    reason: string
  fallbackNodes
    originalNodeId: string
    replacementNodeId: string
    reason: string
  resolvedRenderState
    blendMode
    blend: string | null
    zWrite: string
    renderQueue: string
    alphaClip: boolean
  requiredProjectAnchors: AnchorRef[]
  unresolvedCapabilities: CapabilityResolution[]
  evidence: EvidenceRef[]
  acceptanceImpact: string[]
```

`degraded` 只能用于 `MaterialIntent.constraints` 与 `acceptanceIntent` 允许的降级。基础 PBR、世界法线、视线方向、主光、明确请求的透明路径或效果必要能力缺失时必须为 `blocked`。

## 8. `ShaderCodePlan`

`ShaderCodePlan` 是唯一允许触发写入或验证 MCP 操作的授权对象。

```text
ShaderCodePlan
  trace: TraceContext
  codePlanId: string
  sourceResolvedGraphId: string
  sourceProfileId: string
  knowledgeBaseVersion: string
  iterationObjective: string
  hypothesis: string
  baselineRevisions: AssetRevisionRef[]
  semanticNodes: string[]
  targetAssets: AssetRevisionRef[]
  allowedFiles: string[]
  forbiddenFiles: string[]
  anchors: AnchorRef[]
  plannedChanges: PlannedChange[]
  dependencies: AssetRevisionRef[]
  renderState: object
  invariants: string[]
  generatedMaterialProperties: MaterialPropertySchema[]
  validationPlan: ValidationPlan
  rollbackPlan: RollbackPlan
  authorizationStatus: approved | blocked

PlannedChange
  changeId: string
  operation: create_generated_shader | update_generated_shader | create_generated_hlsl | update_generated_hlsl
  assetPath: string
  baseRevision: string | absent
  sourceSemanticNodes: string[]
  sourceIntentFields: string[]
  expectedAnchors: string[]
  description: string
```

批准条件：

- `ResolvedSemanticGraph.status` 是 `resolved`，或接受影响已记录的 `degraded`。
- `allowedFiles` 只包含 [`Assets/AIShader/Generated/`](../Assets/AIShader/Generated/) 内路径，运行工件只包含 [`Artifacts/ShaderRuns/`](../Artifacts/ShaderRuns/) 内路径。
- 每个更新操作都有实时 `baseRevision`；每个新建操作显式使用 `absent`。
- 每个修改至少关联一个语义节点和一个 Intent 字段。
- 单一 `iterationId` 只能验证一个假设；其他变化必须在后续计划中处理。

## 9. MCP 操作记录

每个工具调用都产生不可变 `McpOperationRecord`；它是验证与审计的输入，不直接等同于通过结果。

```text
McpOperationRecord
  trace: TraceContext
  operationId: string
  codePlanId: string | null
  toolName: string
  requestArtifact: ArtifactRef
  responseArtifact: ArtifactRef
  jobId: string | null
  status: succeeded | failed | blocked | cancelled
  inputRevisions: AssetRevisionRef[]
  outputRevisions: AssetRevisionRef[]
  producedArtifacts: ArtifactRef[]
  logCursor: string | null
  warnings: string[]
  error: ToolError | null
```

允许的工具序列：

```text
读阶段
  get_shader_knowledge_base_status
  query_shader_knowledge_base
  get_asset_revision
  inspect_shader_structure

写入与验证阶段
  write_generated_text_asset
  refresh_and_compile_assets
  ensure_validation_scene
  capture_validation
  create_shader_checkpoint
```

`write_generated_text_asset` 必须引用 `codePlanId`，且其 `asset.path`、`baseRevision`、锚点和语义节点必须与 `ShaderCodePlan` 一致。所有异步任务通过 [`run_unity_job`](unity-mcp-p0-p1-contract.md) 的 Job 协议记录其 `jobId` 与工件。

## 10. `ValidationEvidence`

`ValidationEvidence` 是对当前迭代假设的结构化判定输入，而非只是一组截图。

```text
ValidationEvidence
  trace: TraceContext
  sourceCodePlanId: string
  sourceResolvedGraphId: string
  evaluatedIterationId: string
  assetRevisions: AssetRevisionRef[]
  staticChecks: StaticCheckResult[]
  compilation: CompileEvidence
  console: ConsoleEvidence
  validationSession: ValidationSessionEvidence | null
  captures: CaptureEvidence[]
  debugChannels: DebugEvidence[]
  parameterScans: ParameterScanEvidence[]
  acceptanceChecks: AcceptanceCheck[]
  evidenceStatus: sufficient | insufficient | invalid
  invalidReasons: string[]

StaticCheckResult
  ruleId: string
  status: pass | fail | not_applicable
  evidence: EvidenceRef[]

CompileEvidence
  compileJobId: string
  status: passed | failed | blocked
  diagnostics: Diagnostic[]
  observedRevisions: AssetRevisionRef[]
  evidence: EvidenceRef[]

ConsoleEvidence
  baselineCursor: string
  finalCursor: string
  newErrors: LogEntryRef[]
  newWarnings: LogEntryRef[]

ValidationSessionEvidence
  sessionId: string
  manifest: ArtifactRef
  shaderRevision: string
  materialProperties: object
  cameraPreset: string
  lightingPreset: string
  environmentPreset: string

CaptureEvidence
  captureId: string
  channel: beauty | albedo | normal | metallic | roughness | emission | alpha | clip_mask
  image: ArtifactRef
  manifest: ArtifactRef
  expectedSemanticNodes: string[]

DebugEvidence
  channel: string
  sourceNodes: string[]
  captureRef: ArtifactRef
  interpretation: string

ParameterScanEvidence
  parameter: string
  sampledValues: scalar[]
  captureRefs: ArtifactRef[]
  expectedBehavior: string
  observedBehavior: string

AcceptanceCheck
  criterion: string
  status: pass | fail | blocked | inconclusive
  evidence: EvidenceRef[]
  rationale: string
```

最小有效性：

1. 静态检查、编译和 Console 三项都必须存在。
2. 所有编译的 observed revision 必须与本计划产生的 revision 相同。
3. Console 不得存在从本轮 `baselineCursor` 后产生的 Error 或 Exception。
4. 每个本轮激活效果节点都必须有至少一个对应 Debug 通道或参数扫描证据。
5. Alpha Clip 或透明激活时，必须含 `alpha` 与 `clip_mask` 证据及阈值或透明度扫描。
6. 任何工件缺少 Manifest、revision 或 `runId` 时，整体为 `invalid`。

## 11. `DecisionRecord` 与 Checkpoint

```text
DecisionRecord
  trace: TraceContext
  sourceValidationEvidenceId: string
  status: PASS | REVISE | BLOCKED
  decisionRationale: string
  satisfiedCriteria: string[]
  failedCriteria: string[]
  blockedReasons: string[]
  nextHypothesis: string | null
  checkpointRequest: CheckpointRequest

CheckpointRequest
  decision: pass | revise | blocked
  summary: string
  assetRevisions: AssetRevisionRef[]
  knowledgeBaseVersion: string
  codePlanRef: ArtifactRef
  validationRefs: ArtifactRef[]
  diagnosticsRefs: EvidenceRef[]
  rollback
    strategy: restore_generated_assets | no_mutation
    eligible: boolean
```

判定规则：

```text
PASS
  evidenceStatus = sufficient
  + 编译通过
  + 无新增 Error 或 Exception
  + 所有当前迭代 acceptanceChecks 通过

REVISE
  证据足够定位当前假设中的代码 数据 参数 公式 或验证配置问题
  + 未触发范围外阻塞

BLOCKED
  语义图或能力解析被阻塞
  或排序未确认
  或资产修订冲突
  或缺少必需资产
  或验证证据不足且无法安全推断
```

随后通过 [`create_shader_checkpoint`](unity-mcp-p0-p1-contract.md) 持久化 `DecisionRecord` 关联的资产版本、代码计划、证据和回滚边界。

## 12. 端到端禁止事项

- 不得从用户自然语言直接调用写入工具。
- 不得从知识库样本直接写入或覆盖项目 Shader。
- 不得使用不存在 `fresh` 状态的知识库进入计划阶段。
- 不得绕过 `EffectOrderDecision` 把默认效果顺序静默放入图。
- 不得在没有 `ShaderCodePlan` 的情况下调用 `write_generated_text_asset`。
- 不得以截图代替编译、静态检查、Console 或 revision 验证。
- 不得在 `conflict` 后重试写入并覆盖用户修改；必须返回到 Profile 与 Code Plan 阶段重新建立基线。
