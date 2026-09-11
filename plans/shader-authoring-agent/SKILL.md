---
name: shader-authoring-agent
description: 将自然语言材质需求规范化为标准 PBR 材质意图，基于 Unity 项目事实生成、验证和迭代 Shader；首期仅支持材质，特效与后处理转人工或明确拒绝。
---

# Shader Authoring Agent

## 使命

将用户的自然语言渲染需求转化为可审计的标准 PBR 材质实现，并通过 Unity 真实编译、固定验证场景和证据驱动的 Checkpoint 决策完成闭环。

本 Skill 是领域编排协议，不是通用 Unity 操作层。所有 Unity 编辑器读取、资产操作、编译、场景配置、截图和日志收集均通过现有 Unity MCP 执行：[`get_editor_state`](../../Tools/unity-mcp-server/src/index.ts:359)、[`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406)、[`get_logs`](../../Tools/unity-mcp-server/src/index.ts:508)。

## 首期范围

### 支持

- 类型：以标准 PBR 为基础、可叠加局部材质效果的材质需求。
- 基础工作流：Metallic-Roughness。
- PBR 表面语义：基础色、金属度、粗糙度、法线、AO、自发光、透明、Alpha Clip。
- 附加效果模型：效果栈，可在 PBR 基础表面上按明确顺序叠加局部材质效果。
- 首批候选效果：Dissolve、Pulse Emission、Fresnel Rim；每项实际可用性仍由项目 Profile 和 Capability Resolver 决定。
- Alpha 混合：`SrcAlpha OneMinusSrcAlpha`。
- 透明材质：默认 `ZWrite Off`。
- 透明与 Alpha Clip：允许同时启用，先裁剪再混合。

### 不支持

- 特效和 VFX：粒子、拖尾、模拟、事件驱动效果、屏幕空间粒子、游戏状态驱动效果。
- 后处理：全屏滤镜、调色、Bloom、景深、屏幕空间效果。
- 折射、透明阴影、透明排序修正、深度预通道。
- Clear Coat、Anisotropy、Parallax、程序噪声和未定义的风格化光照，除非后续明确扩展范围。

## 不可违反的工作纪律

1. 先分类，再处理材质；不得把特效或后处理伪装成材质。
2. 先构建 `MaterialIntent`，再构建 `RenderingSemanticGraph`，再读取项目事实并生成 `ProjectShaderProfile`，最后生成 `ShaderCodePlan`。
3. 不得依据记忆猜测项目 Shader 接口、URP Include、Pass、宏或材质约定；必须通过 Unity MCP 收集事实。
4. 每轮只验证一个语义阶段或一个明确假设；禁止在同一轮同时重写直接光、间接光和 Alpha 输出。
5. 编译错误优先于视觉结论。编译失败时不得基于截图判断材质质量。
6. 不得伪造不存在的贴图、反射探针、BRDF LUT、阴影或间接光能力。
7. 不得突破 `ShaderCodePlan` 的文件白名单和锚点范围。
8. 任何默认补全必须记录来源、理由和置信度。
9. 任何 `pass` 必须有静态检查、Unity Console、截图或数值证据；截图不是唯一真值。
10. `revise` 必须保留当前工件和失败证据；`blocked` 必须说明缺失能力或最小人工决策。

## 状态机

```text
CLASSIFY
  -> UNSUPPORTED | CLARIFY | NORMALIZE_INTENT
NORMALIZE_INTENT
  -> CLARIFY | BUILD_GRAPH
BUILD_GRAPH
  -> ANALYZE_PROJECT
ANALYZE_PROJECT
  -> RESOLVE_CAPABILITIES
RESOLVE_CAPABILITIES
  -> BLOCKED | PLAN_CODE
PLAN_CODE
  -> EXECUTE
EXECUTE
  -> VALIDATE
VALIDATE
  -> PASS | REVISE | BLOCKED
REVISE
  -> PLAN_CODE
```

## Phase 0：项目知识库门禁

在解析具体材质需求或生成 Shader 前，必须检查可持久化的 `ProjectShaderKnowledgeBase` 是否存在且有效。该知识库的构建、增量刷新、覆盖率验收、版本发布和失效处理全部委派给 [`shader-knowledge-base-builder`](../shader-knowledge-base-builder/SKILL.md)。

### 门禁决策

```text
检查 ProjectShaderKnowledgeBase
  ├── 存在、Schema 兼容、工程指纹匹配、状态为 fresh
  │     └── 记录 knowledgeBaseVersion，进入材质生成链路
  ├── 不存在
  │     └── 告知用户需要先生成项目专用知识库，等待同意
  ├── 已失效或工程指纹不匹配
  │     └── 告知用户需要先刷新项目专用知识库，等待同意
  └── 正在构建或构建失败
        └── 返回当前状态、失败证据和下一步处理建议，不进入生成链路
```

用户同意创建或刷新时，调用 `shader-knowledge-base-builder`，并传入 `full` 或 `incremental` 模式及失效原因。仅当其返回 `status = fresh` 后，重新执行门禁检查并进入当前材质任务。

### 主 Skill 使用知识库的规则

1. 每个材质任务必须记录 `knowledgeBaseVersion`、Manifest 路径和任务开始时的指纹摘要。
2. 对需求中的基础 PBR、Alpha 路径与每个附加效果，从知识库检索相似项目 Shader、已确认的工程约定、能力证据和匹配的 Library 函数卡片。
3. 项目无本地先例时，只可使用知识库中已确认适用于当前管线的 Library 函数卡片和 Capability Catalog；不得凭记忆引入其他管线代码。
4. 某项能力为 `unknown` 时，必须在 Capability Resolver 中保留不确定性，不得作为已支持能力使用。
5. 任务过程中发现新的 Shader、Include 或配置变化时，记录知识库增量刷新请求；本轮是否继续取决于现有证据是否足够，不得静默更新知识库。

## Step 1：分类与路由

将用户需求输出为 `RequirementClassification`：

```text
category: material | vfx | post_process | ambiguous
confidence: high | medium | low
primaryCarrier: renderer_material | particle_system | camera_frame | unknown
evidence: 原始需求中的短语和结构化理由
route: continue_material_pipeline | unsupported_manual_handoff | clarify
unsupportedFeatures: 已识别但首期不支持的能力
clarificationQuestion: 仅在需要时存在
```

### 分类标准

- `material`：描述附着于 Renderer 的局部表面外观，例如金属、粗糙、发光、透明、贴图、法线。
- `vfx`：描述粒子、拖尾、生成/消散、模拟、命中触发、时间驱动或多个实体协作。
- `post_process`：描述相机帧、全屏画面、屏幕边缘、颜色分级、Bloom、景深、镜头效果。
- `ambiguous`：主要载体无法判断，或用户同时要求多种类别但未说明优先目标。

### 路由规则

- `material`：继续 Step 2。
- `vfx` 或 `post_process`：返回明确的“已识别但首期不支持 / 转人工”结果，不调用代码生成。
- `ambiguous`：只提出一个最小澄清问题，例如“这是附着在单个物体表面的材质，还是由粒子或全屏相机效果实现？”

## Step 2：构建 Material Intent

创建以下字段。结构化字段是机器真值；受控自然语言摘要只是其可读投影。

```text
MaterialIntent
  identity
    requestId
    originalRequirement
    controlledSummary
  scope
    category: material
    workflow: metallic_roughness
    targetDescription
  surface
    baseColor
    metallic
    roughness
    normal
    ambientOcclusion
    emission
  effects
    orderedLayers
      id
      kind: dissolve | pulse_emission | fresnel_rim
      enabled
      inputs
      modulationTargets
      compositionOrder
      timeDependency
      requiredCapabilities
      unsupportedReason
  alpha
    blendMode: opaque | alpha_blend
    alphaClipEnabled: true | false
    alphaSource
    alphaClipThreshold
  textures
    semantic
    source
    channel
    requiredness
  assumptions
    value
    source: explicit | default | inferred
    rationale
    confidence
  constraints
    supported
    excluded
    projectDependent
  acceptanceIntent
    visibleTargets
    testMatrixOverrides
    expectedDebugChannels
```

### 受控自然语言摘要模板

```text
为 {对象描述} 生成 {不透明或透明} 的标准金属度-粗糙度 PBR 材质。
基础色为 {颜色或纹理来源}；金属度为 {值或贴图来源}；粗糙度为 {值或贴图来源}。
法线为 {关闭或来源与强度}；AO 为 {关闭或来源与强度}；自发光为 {关闭或颜色与强度}。
Alpha 使用 {来源}；Alpha Clip 为 {关闭或阈值}；透明混合为 {关闭或 SrcAlpha OneMinusSrcAlpha}。
默认项：{按字段列举默认值和理由}。
不支持或未实现项：{按字段列举}。
验收目标：{可观察视觉目标}。
```

### 宽松补全策略

自动补全，但必须在 `assumptions` 中记录：

- 未指定金属度：`0`。
- 未指定粗糙度：采用项目可配置的中等粗糙默认值。
- 未指定透明：不透明。
- 未指定法线、AO、自发光：关闭，除非项目 Profile 或已提供资产明确要求启用。
- 未提供贴图：使用材质常量参数，不生成虚构资产路径。
- 描述了细节但未提供贴图：记录为资产需求，不擅自生成程序噪声或额外算法。

### 必须澄清的情况

- 同时出现互相矛盾的 Alpha 来源、贴图语义或渲染路径描述。
- 同一纹理被要求作为互不兼容的贴图类型，且没有明确通道规则。
- 用户要求首期不支持的折射、透明排序修正、透明阴影或深度预通道，并且它是视觉目标的必要条件。
- 无法判定需求是材质、特效还是后处理。

## Step 3：构建 Rendering Semantic Graph

图表达渲染计算与依赖，不保存特定 HLSL、URP Include 或具体函数名。

### 固定节点词表

```text
GeometryInput
  Position
  WorldNormal
  ViewDirection
  UV
  TangentBasis

MaterialInput
  BaseColor
  Metallic
  Roughness
  Normal
  AmbientOcclusion
  Emission
  Alpha

SurfaceBuild
  SurfaceParameters

Lighting
  DirectDiffuse
  DirectSpecular
  IndirectDiffuse
  IndirectSpecular
  EmissionContribution
  LitColor

OutputControl
  AlphaClip
  OpaqueOutput
  AlphaBlendOutput
```

### 必须遵守的组合规则

1. 统一从 `MaterialInput` 构建 `SurfaceParameters`。
2. `LitColor = DirectDiffuse + DirectSpecular + IndirectDiffuse + IndirectSpecular + EmissionContribution`。
3. AO 只能作用于语义图明确允许的间接光路径，不能成为任意全局乘法。
4. 自发光不受直接光、阴影或光照衰减影响；最终是否受 Alpha 影响由输出节点决定。
5. Alpha 从指定常量、Base Color Alpha 或独立遮罩通道读取。
6. 启用 Alpha Clip 时，先执行片元裁剪。
7. `blendMode = opaque` 时，采用 `OpaqueOutput`。
8. `blendMode = alpha_blend` 时，采用 `AlphaBlendOutput`，其渲染状态固定为 `Blend SrcAlpha OneMinusSrcAlpha` 和 `ZWrite Off`。
9. Alpha Blend 与 Alpha Clip 同时开启时，阈值以下完全丢弃，阈值以上使用连续 Alpha 混合。
10. 折射、透明阴影、排序修正、深度预通道不得被加入图。

### 节点元数据

每个节点必须包含：

```text
id
kind
inputs
outputs
dependsOn
preconditions
requiredCapabilities
fallbackBehavior
debugChannels
validationStage
sourceIntentFields
```

## Step 4：基于知识库确认当前任务的 Shader 锚点与实时能力

本步骤不是重新扫描全项目或构建知识库；项目级环境、Shader Library、样本语料、工程风格和能力目录由 [`shader-knowledge-base-builder`](../shader-knowledge-base-builder/SKILL.md) 维护。

输入为已通过门禁的 `knowledgeBaseVersion`、当前 `MaterialIntent` 与 `RenderingSemanticGraph`。在任何代码计划之前，调用 [`get_editor_state`](../../Tools/unity-mcp-server/src/index.ts:359) 确认连接与项目状态，再通过只读 [`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406) 实时核验知识库检索得到的目标资产与实现锚点。

输出为任务级 `ProjectShaderProfile`：它回答本次材质任务应复用哪个 Shader、Pass、函数、属性、渲染状态和 Library 函数卡片，以及它们在当前 revision 下是否依然存在和可用。

```text
ProjectShaderProfile
  knowledgeBaseVersion
  taskId
  projectStateFingerprint

  selectedBaseline
    shaderAsset
    shaderRevision
    sourceExamples
    selectionReason

  targetAnchors
    passesAndLightModes
    vertexAndFragmentEntries
    propertiesAndKeywords
    dataStructures
    includeAnchors
    renderStateAnchors

  taskInputs
    targetRenderer
    targetMaterial
    providedTextures
    missingAssets

  resolvedInterfaces
    worldPositionNormalTangentViewDirectionSources
    mainLightInterface
    additionalLightsInterface
    shadowInterface
    indirectDiffuseInterface
    indirectSpecularInterface
    reflectionProbeInterface
    brdfLutInterface
    timeInterface

  alphaAndOutput
    alphaClipConvention
    transparentPassConvention
    blendConvention
    zWriteConvention
    renderQueueConvention

  capabilityEvidence
  capabilityConfidence
  unknowns
  incrementalEvidence
```

### 本步骤的任务级核验

1. 从知识库检索基础 PBR、Alpha 路径和每个请求效果的相似 Shader 样本与 Library 函数卡片。
2. 核验候选 Shader、Include、Pass、函数、Properties、Keywords 和材质资产仍存在，记录当前 revision。
3. 为当前语义图确认精确锚点：可修改的 Forward Pass、片元入口、数据结构、材质属性块和渲染状态位置。
4. 核验任务依赖的实时能力：UV、世界法线、视线方向、主光、时间、贴图、Alpha Clip、透明混合及所需阴影路径。
5. 核验用户提供或计划引用的贴图、目标 Material、Renderer 是否存在且类型可用；缺失资产列入 `missingAssets`。
6. 将知识库结论与当前项目状态不一致、新发现的 Shader/Include 或配置变化写入 `incrementalEvidence`，请求后续知识库增量刷新；本步骤不得静默刷新知识库。
7. 未知能力必须保持 `unknown`；不得把知识库中未确认或本轮未核验的功能作为可用能力。

### 明确不属于本步骤的工作

- 全量索引 Unity/URP/HDRP Library；
- 全工程枚举和分类 Shader 样本；
- 归纳项目编码或渲染风格；
- 发布或刷新 `ProjectShaderKnowledgeBase`；
- 写入 Shader、材质、场景或项目配置。

## Step 5：Capability Resolver

将语义图与 `ProjectShaderProfile` 对齐，输出：

```text
ResolvedSemanticGraph
  status: resolved | degraded | blocked
  selectedNodes
  omittedNodes
  fallbackNodes
  resolvedRenderState
  requiredProjectAnchors
  unresolvedCapabilities
  evidence
```

### 处理原则

- 基础 PBR 输入、世界法线、视线方向和主光源是必需能力；缺失则 `blocked`。
- 间接漫反射、间接镜面、AO 可在明确记录的条件下做降级；不得伪造完整 IBL。
- 透明路径必须确认可以声明 `Blend SrcAlpha OneMinusSrcAlpha`、`ZWrite Off` 和透明 Render Queue；否则 `blocked`。
- Alpha Clip 必须确认属性、阈值与需要的 Pass 约定；若 ShadowCaster 约定未知，报告为能力风险，不能假定透明或裁剪阴影正确。
- 首期明确排除的功能被请求时，返回 `blocked` 或不支持说明，不进行隐式替代。

## Step 6：生成 Shader Code Plan

只有 `resolved` 或可接受的 `degraded` 图才能生成计划。

```text
ShaderCodePlan
  iterationId
  baselineRevision
  objective
  semanticNodes
  targetAssets
  allowedFiles
  forbiddenFiles
  anchors
  plannedChanges
  dependencies
  renderState
  invariants
  generatedMaterialProperties
  validationPlan
  rollbackPlan
```

### 计划约束

- 每轮只改变一个阶段或一个可验证假设。
- 对每个改动列出对应的语义图节点和 Intent 字段。
- 明确不改动的 Pass、函数、宏、Include、材质属性和渲染状态。
- 不得整文件重写，除非 Code Plan 明确标注该文件为新生成资产或不存在可复用锚点。
- 不得修改计划白名单之外的资产。
- 每次写入前记录基线版本；每次失败保留失败版本和差异。

## Step 7：受限执行

使用 [`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406) 执行计划中允许的 Unity Editor C#。命令必须按以下类别标记：

```text
READ_ANALYSIS
WRITE_SHADER
WRITE_MATERIAL
CONFIGURE_VALIDATION_SCENE
REFRESH_AND_COMPILE
CAPTURE_EVIDENCE
SAVE_CHECKPOINT
```

每次命令执行后：

1. 读取命令返回值。
2. 调用 [`get_logs`](../../Tools/unity-mcp-server/src/index.ts:508) 获取新增 Console 信息。
3. 若命令、导入或编译失败，停止后续视觉判断并进入 `revise` 或 `blocked`。

## Step 8：验证与 Checkpoint

### 固定验证顺序

1. 静态检查：计划白名单、锚点、属性、渲染状态、禁止特性。
2. Unity 刷新与 Shader 编译。
3. Console 读取：确认无新增 Error。
4. 在确定性验证场景中配置球体、相机、主光、环境和材质。
5. 输出当前阶段的目标图、参考图、Debug 图。
6. 运行当前阶段所需的参数扫描。
7. 保存 `PASS`、`REVISE` 或 `BLOCKED` 及其证据。

### PBR 阶段顺序

```text
Stage 0  工程与参考基线
Stage 1  几何与材质输入
Stage 2  直接漫反射
Stage 3  直接镜面反射
Stage 4  直接光组合
Stage 5  间接漫反射
Stage 6  间接镜面反射
Stage 7  间接光组合
Stage 8  自发光
Stage 9  最终颜色合成
Stage 10 法线、Alpha Clip、透明等首期扩展
```

每轮只推进一个阶段。若当前请求不需要某节点，记录跳过理由，而不是无证据地标记通过。

### Alpha 专项验证

对于透明或裁剪材质，额外验证：

```text
- 实际 Blend、ZWrite、Render Queue 状态
- Alpha 灰度 Debug 输出
- 裁剪遮罩 Debug 输出
- Alpha Clip 阈值扫描
- Alpha 透明度扫描
- 有背景和无背景对比
- 透明加裁剪时阈值上下的可见性和混合一致性
- 不存在折射、透明阴影、排序修正和深度预通道的错误承诺
```

### 决策条件

```text
PASS
  当前阶段编译通过，静态检查通过，Console 无新增错误，验证证据满足明确验收标准。

REVISE
  存在可定位的代码、数据、公式、参数、颜色空间或场景配置问题；保留现场，仅修正当前假设。

BLOCKED
  缺失必需项目能力、需求超出范围、存在无法安全默认的冲突，或需要人工提供资产/选择策略。
```

## 每轮对外输出格式

```text
## Requirement Classification
- category:
- route:
- evidence:

## Material Intent
- controlled summary:
- explicit fields:
- defaults and assumptions:
- unsupported or excluded features:
- clarification required:

## Rendering Semantic Graph
- active nodes:
- dependencies:
- alpha path:
- fallback nodes:

## Project Shader Profile
- confirmed capabilities:
- unknown capabilities:
- evidence:

## Capability Resolution
- status:
- resolved render state:
- blocked or degraded reasons:

## Shader Code Plan
- objective:
- allowed files:
- anchors:
- planned changes:
- invariants:

## Validation
- static result:
- compile result:
- console result:
- captures and debug channels:
- comparison result:

## Decision
- PASS | REVISE | BLOCKED
- evidence:
- next action:
```

## 首次执行的推荐基线

首个端到端用例优先选择不透明 PBR 材质，仅验证基础色、金属度、粗糙度、主光、间接光能力和低强度自发光。Alpha Clip 与透明必须作为独立扩展轮次加入，避免首次闭环同时引入材质输入、光照、裁剪、混合和排序问题。
