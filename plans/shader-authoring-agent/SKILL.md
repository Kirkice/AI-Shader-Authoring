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

图表达渲染计算、数据依赖与效果合成，不保存特定 HLSL、URP Include、具体函数名、Shader 路径或 Unity API 调用。它是 `MaterialIntent` 到 `ShaderCodePlan` 之间唯一的计算语义真相。

### 图的基本结构

```text
RenderingSemanticGraph
  graphId
  sourceIntentId
  workflow: metallic_roughness
  nodes
  edges
  effectComposition
  outputContract
  unresolvedSemantics
  debugContract
```

```text
GraphNode
  id
  kind
  phase
  inputs
  outputs
  dependsOn
  reads
  writes
  preconditions
  requiredCapabilities
  fallbackBehavior
  debugChannels
  validationStage
  sourceIntentFields
  sourceEffectLayerId
```

- `reads` 与 `writes` 使用语义槽位，不使用变量名，例如 `surface.baseColor`、`alpha.coverage`、`emission.additive`。
- `phase` 是合法性约束而非固定效果排序。它只定义效果可以读写哪些生命周期阶段。
- 同一图内节点 `id` 必须唯一；边、读写关系与 `dependsOn` 必须形成有向无环图。出现循环时，返回 `BLOCKED` 并要求拆分效果或明确反馈语义。

### 基础节点词表

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

### 效果节点词表

每个 `MaterialIntent.effects.orderedLayers` 中启用的层均展开为一个或多个带 `sourceEffectLayerId` 的节点；图不得凭空加入用户未请求的效果。

```text
EffectInput
  EffectMask
  EffectParameter
  TimeSignal

Dissolve
  DissolveMask
  DissolveThreshold
  DissolveCoverage
  DissolveEdgeBand
  DissolveEdgeEmission

PulseEmission
  PulseWave
  PulseIntensity
  PulseEmission

FresnelRim
  FresnelFactor
  FresnelRimEmission

EffectCompose
  EffectAlphaCompose
  EffectSurfaceCompose
  EffectEmissionCompose
```

节点语义：

- `DissolveMask` 从显式遮罩、贴图通道或常量取得连续遮罩值。
- `DissolveThreshold` 根据阈值和可选时间变化生成裁剪边界；它不直接决定最终输出。
- `DissolveCoverage` 生成 `alpha.coverage` 的候选修改；只有其被后续组合节点消费时，才会影响 Alpha Clip 或透明 Alpha。
- `DissolveEdgeBand` 生成溶解边缘带；`DissolveEdgeEmission` 将该边缘带转换为可组合的发光贡献。
- `PulseWave` 仅表达时间函数的归一化输出；`PulseIntensity` 将其转换为强度或调制因子；`PulseEmission` 产生可组合发光贡献。
- `FresnelFactor` 只表示法线与视线方向得到的艺术化边缘因子，不能替代物理 BRDF 中的 Fresnel 项；`FresnelRimEmission` 产生可组合发光贡献。
- `EffectAlphaCompose`、`EffectSurfaceCompose`、`EffectEmissionCompose` 是显式的冲突消解节点：所有对同一语义槽位的多效果写入必须经过相应 Compose 节点。

### 语义槽位与生命周期阶段

```text
Phase 0  input
  geometry.*
  material.*
  effect.*

Phase 1  surface
  surface.baseColor
  surface.metallic
  surface.roughness
  surface.normal
  surface.ambientOcclusion

Phase 2  lighting
  lighting.directDiffuse
  lighting.directSpecular
  lighting.indirectDiffuse
  lighting.indirectSpecular
  lighting.litColor

Phase 3  emission
  emission.base
  emission.additive
  emission.composed

Phase 4  alpha
  alpha.base
  alpha.coverage
  alpha.clipThreshold
  alpha.composed

Phase 5  output
  output.color
  output.alpha
  output.renderState
```

- 允许 `MaterialIntent.compositionOrder` 在同一目标槽位的多个效果层之间定义顺序。
- 不允许任意跨阶段重排：节点只能读取本阶段及更早阶段的槽位；只能写本阶段或更晚阶段中已被该节点类型授权的槽位。
- 效果层必须声明 `modulationTargets` 与合成模式。`compositionOrder` 未由用户指定时，按下文默认方案预填，并且必须先向用户展示并询问是否调整。

### 默认效果排序确认门禁

只要请求包含两个及以上启用的效果层，且用户尚未明确给出效果排序，主 Skill 必须在生成 `RenderingSemanticGraph` 前展示默认排序并询问用户是否调整。不得把默认值静默写入 `MaterialIntent`。

默认方案：

```text
1. Dissolve 掩码与裁剪优先
   DissolveMask
     -> DissolveThreshold
     -> DissolveCoverage
     -> EffectAlphaCompose
     -> AlphaClip 或最终 Alpha

2. Dissolve 边缘发光
   DissolveEdgeBand
     -> DissolveEdgeEmission
     -> EffectEmissionCompose

3. Pulse 发光
   PulseWave
     -> PulseIntensity
     -> PulseEmission
     -> EffectEmissionCompose

4. Fresnel 边缘光
   FresnelFactor
     -> FresnelRimEmission
     -> EffectEmissionCompose

5. 最终合成
   emission.base
     -> DissolveEdgeEmission
     -> PulseEmission
     -> FresnelRimEmission
     -> EmissionContribution
     -> Final Color
```

对应的默认局部顺序为：

```text
alpha.coverage / alpha.clipThreshold
  dissolve: order 10

emission.additive
  dissolve edge: order 20
  pulse emission: order 30
  fresnel rim emission: order 40
```

对外确认格式：

```text
检测到多个材质效果。默认按以下顺序合成：
1. 溶解掩码与裁剪优先；
2. 溶解边缘发光；
3. 呼吸光以加法叠加至 Emission；
4. 菲涅尔边缘光以加法叠加至 Emission。

是否需要调整效果顺序、某个效果的叠加方式，或指定一个效果调制另一个效果？
```

- 用户确认“不需要调整”时，主 Skill 将上述顺序与操作显式写入 `MaterialIntent.effects.orderedLayers` 和各目标的 `EffectCompositionRequest`，其 `source` 标记为 `default_confirmed`。
- 用户提出调整时，按用户的局部目标顺序生成 `EffectCompositionRequest`；允许例如 `Pulse` 调制 `Fresnel` 强度，但必须明确目标、操作和依赖关系。
- 仅一个启用效果层时，不弹出排序确认；仍记录其默认操作和 `source = default_single_layer`。
- 用户描述已经包含顺序语义时，例如“先溶解再出现边缘光”，视为显式排序；仅回显解析结果供确认，不再询问是否采用默认。
- 用户拒绝确认且未提供排序时，返回 `BLOCKED`，不得进入代码计划。

### 可排序效果层与冲突规则

`compositionOrder` 不是简单整数列表，而是效果层对一个或多个目标槽位提出的有序写入请求：

```text
EffectCompositionRequest
  effectLayerId
  target: surface.baseColor | emission.additive | alpha.coverage | alpha.clipThreshold
  operation: add | multiply | replace | min | max | lerp
  order: integer
  requiresBefore: effectLayerId[]
  requiresAfter: effectLayerId[]
  condition: always | surviving_fragments_only | clipped_edge_only
```

解析规则：

1. 先按 `target` 分组，再将 `order`、`requiresBefore` 与 `requiresAfter` 解析为局部有向图。
2. `order` 相同且无显式依赖的两个 `replace` 操作构成冲突，返回 `BLOCKED`；不得依赖模型的隐式顺序。
3. 同序 `add` 可交换；同序 `multiply` 可交换；`add` 与 `multiply` 不可交换，必须由显式顺序或合成节点表达。
4. `min`、`max` 仅可用于 `alpha.coverage`、`alpha.clipThreshold` 或明确标注的标量效果参数；不允许用于最终 LitColor。
5. `lerp` 必须声明权重来源、输入 A/B 和覆盖对象；缺失时返回 `BLOCKED`。
6. 解析后的每个目标必须生成唯一 Compose 节点，Compose 节点的输入顺序就是该目标的最终效果顺序。
7. 层的全局 `compositionOrder` 仅是默认局部顺序；当同一层写多个目标时，各目标可有不同的实际位置。

### 首期效果层的合法读写范围

| 效果层 | 必需读取 | 可写目标 | 禁止写入 |
|---|---|---|---|
| dissolve | `material`、可选 `effect.time` | `alpha.coverage`、`alpha.clipThreshold`、`emission.additive` | `lighting.*`、`output.renderState` |
| pulse_emission | `effect.time`、层参数 | `emission.additive`、显式声明时 `emission.base` | `alpha.*`、`lighting.*`、`output.renderState` |
| fresnel_rim | `geometry.worldNormal`、`geometry.viewDirection`、层参数 | `emission.additive`、显式声明时 `surface.baseColor` | `alpha.clipThreshold`、`lighting.*`、`output.renderState` |

- `dissolve` 可以参与透明 Alpha 与 Alpha Clip，但它不能改变 `blendMode`、`ZWrite` 或 Render Queue。
- `pulse_emission` 和 `fresnel_rim` 默认以 `add` 贡献 `emission.additive`；若用户要求调制其他效果，必须在对应 `EffectCompositionRequest` 中显式指向目标与操作。
- “仅保留的片元才发光”通过 `condition = surviving_fragments_only` 表达。实现上由效果发光链依赖 Alpha Clip 判定语义，但图仍保持无循环：裁剪判定读取已组合覆盖值，最终输出只消费仍存活片元的颜色。

### 必须遵守的基础组合与输出规则

1. 统一从 `MaterialInput` 构建 `SurfaceParameters`；任何 `surface.*` 效果修改都必须在该节点之前或通过明确的 `EffectSurfaceCompose` 回写。
2. `LitColor = DirectDiffuse + DirectSpecular + IndirectDiffuse + IndirectSpecular`；效果发光不混入直接或间接光计算。
3. `EmissionContribution = emission.composed`，其中 `emission.composed = emission.base + 按顺序组合的 emission.additive`，除非具体 Compose 节点明确了乘法或替换语义。
4. 最终颜色为 `output.color = lighting.litColor + EmissionContribution`。
5. AO 只能作用于图明确允许的间接光路径，不能成为任意全局乘法。
6. Alpha 从指定常量、Base Color Alpha 或独立遮罩通道读取，形成 `alpha.base`；效果仅可通过 `EffectAlphaCompose` 影响 `alpha.coverage` 或 `alpha.clipThreshold`。
7. 启用 Alpha Clip 时，使用 `alpha.composed` 与最终 `alpha.clipThreshold` 执行片元裁剪。启用效果不自动开启 Alpha Clip；`MaterialIntent.alpha.alphaClipEnabled` 必须为真或效果层显式声明其为必需前置条件。
8. `blendMode = opaque` 时，采用 `OpaqueOutput`；`blendMode = alpha_blend` 时，采用 `AlphaBlendOutput`，渲染状态固定为 `Blend SrcAlpha OneMinusSrcAlpha` 与 `ZWrite Off`。
9. Alpha Blend 与 Alpha Clip 同时开启时，阈值以下完全丢弃，阈值以上使用最终连续 Alpha 混合。
10. 折射、透明阴影、排序修正、深度预通道不得被加入图；任何效果层也不得间接引入这些能力。

### 默认图与效果图的关系

无效果层时，基础图为：

```text
GeometryInput + MaterialInput
  -> SurfaceParameters
  -> DirectDiffuse + DirectSpecular + IndirectDiffuse + IndirectSpecular
  -> LitColor
MaterialInput.Emission
  -> EmissionContribution
LitColor + EmissionContribution
  -> OutputControl
```

效果层被插入为显式子图，而不是直接覆盖基础节点。例如：

```text
DissolveMask -> DissolveCoverage -> EffectAlphaCompose -> AlphaClip
DissolveEdgeBand -> DissolveEdgeEmission -> EffectEmissionCompose
PulseWave -> PulseIntensity -> PulseEmission -> EffectEmissionCompose
WorldNormal + ViewDirection -> FresnelFactor -> FresnelRimEmission -> EffectEmissionCompose
EffectEmissionCompose -> EmissionContribution -> Final Color
```

### 节点元数据与验证要求

每个节点必须包含：

```text
id
kind
phase
inputs
outputs
dependsOn
reads
writes
preconditions
requiredCapabilities
fallbackBehavior
debugChannels
validationStage
sourceIntentFields
sourceEffectLayerId
```

每个效果子图至少提供一个可观察 Debug 通道：

```text
dissolve
  dissolve_mask
  dissolve_coverage
  dissolve_edge_band

pulse_emission
  pulse_wave
  pulse_emission

fresnel_rim
  fresnel_factor
  fresnel_rim_emission
```

能力不足、非法跨阶段读写、目标槽位冲突、效果顺序循环或缺少必要 Compose 语义时，图构建必须返回 `BLOCKED`；不得静默重排或删除用户请求的效果。

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

执行必须优先使用**结构化白名单工具**，而不是任意 C#。只有白名单无法表达、且已获得显式授权的诊断或维护场景，才允许使用 [`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:432)（裸 C# 在 Unity 6 上不作为常规执行路径）。执行类别与对应工具：

| 类别 | 结构化工具 |
| --- | --- |
| `READ_ANALYSIS` | [`get_asset_revision`](../../Tools/unity-mcp-server/src/index.ts:605)、[`inspect_shader_structure`](../../Tools/unity-mcp-server/src/index.ts:604) |
| `WRITE_SHADER` | [`write_generated_text_asset`](../../Tools/unity-mcp-server/src/index.ts:606) |
| `WRITE_MATERIAL` | `write_generated_text_asset`（`.mat` 属受控生成资产） |
| `REFRESH_AND_COMPILE` | [`refresh_and_compile_assets`](../../Tools/unity-mcp-server/src/index.ts:607) |
| `CAPTURE_EVIDENCE` | [`capture_validation`](../../Tools/unity-mcp-server/src/index.ts:609) |
| `SAVE_CHECKPOINT` | [`create_shader_checkpoint`](../../Tools/unity-mcp-server/src/index.ts:610) |

### 执行后强制 Console 检查（硬性门禁）

每次产生写入（`WRITE_SHADER` / `WRITE_MATERIAL`）后，必须执行并记录：

1. 读取命令返回值与资产 revision。
2. 调用 [`refresh_and_compile_assets`](../../Tools/unity-mcp-server/src/index.ts:607)（异步 job），并用 [`get_unity_job`](../../Tools/unity-mcp-server/src/index.ts:599) 轮询到 `succeeded` / `failed`，读取 `diagnostics`、`errorCount`、`warningCount`。
3. 调用 [`get_console_diagnostics`](../../Tools/unity-mcp-server/src/index.ts:611)（建议带 `assetPaths` 限定到本次生成资产，`includeWarnings: true`），获取 Console Error / Warning 与 Shader 编译错误状态。
4. 判定：
   - `errorCount == 0`：Console 干净，方可进入 Step 8 的视觉验证。
   - `errorCount > 0`：**禁止**进行任何视觉判断，进入修复循环。

### Console 报错修复循环

```text
读取 get_console_diagnostics 的 diagnostics
→ 定位报错资产与源码位置（include / CBUFFER / 语义 / 渲染状态）
→ 在 Code Plan 白名单内修改
→ write_generated_text_asset（携带新的 baseRevision）
→ refresh_and_compile_assets → get_unity_job
→ get_console_diagnostics 复检
```

- 最多循环 `3` 轮；仍未清零则进入 `blocked`，并保留失败版本、差异与完整诊断作为证据。
- 修复不得超出 Code Plan 的 `allowedFiles`；需要新资产时先更新 Code Plan 并重新授权。
- 每次修复都必须记录：命中的诊断、修改点、修改前后 revision、复检结果。

## Step 8：验证与 Checkpoint

### 固定验证顺序

1. 静态检查：计划白名单、锚点、属性、渲染状态、禁止特性。
2. Unity 刷新与 Shader 编译（`refresh_and_compile_assets` → `get_unity_job`）。
3. Console 读取（`get_console_diagnostics`）：确认无新增 Error；有错则回到 Step 7 修复循环。
4. 在确定性验证场景中配置球体、相机、主光、环境和材质。
5. 输出当前阶段的目标图、参考图、Debug 图。
6. 运行当前阶段所需的参数扫描。
7. 保存 `PASS`、`REVISE` 或 `BLOCKED` 及其证据（必须包含第 2、3 步的诊断结果）。

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
