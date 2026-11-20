---
name: shader-authoring-agent
description: Unity Shader 统一入口：支持角色、场景、特效 Shader 的仓库参考驱动编写；知识库生成前发现目录分类并展示真实路径与代表 Shader，用户确认后正式构建。按用户用途检索已确认样本，仅在用户明确要求 PBR 时走原 PBR 意图与语义图流程；共享功能属性确认、编译、GUI、Review 修复复审和用户验收闭环。支持知识库刷新及静态/Mali 性能验收；后处理不在本次范围。---

# Shader Authoring Agent

## 统一入口与内部子流程

本目录只注册本 Skill。下列参考文档保留原有子 Skill 的完整流程、输入输出契约、权限边界与验收规则，作为内部子流程按需读取，不再单独注册为 Skill。

| 用户意图或主流程触发条件 | 必须完整读取并执行的文档 |
| --- | --- |
| 构建、刷新或验证项目 Shader 知识库；或 Phase 0 需要构建/刷新 | [shader-knowledge-base-builder](references/knowledge-base.md) |
| 仅维护 UnifiedShaderGUI 元数据；或 Step 7.5 委派 | [unified-shader-gui-authoring](references/shader-gui.md) |
| Shader 性能分析与验收 | [shader-performance-acceptance](references/performance-acceptance.md) |
| Shader Review；主流程写入前预检、生成后复审及用户确认修复循环 | [Review 接入协议](references/review-workflow.md)，并完整读取其中链接的原始 Review 规范及规则 |
| 知识库首次构建、范围或分类变化 | 先执行 [目录发现与用户确认](references/domain-discovery.md)，再执行知识库正式构建 |
| 角色、场景、特效 Shader 编写（默认参考路线） | 执行 [仓库参考驱动编写](references/reference-authoring.md)，再接入下方共享工程闭环 |
| 用户明确要求 PBR | Step 1 确认用途后执行下方 Step 2–5 PBR 专项，再接入共享工程闭环 |
 
执行约定：

1. 进入子流程之前完整读取对应文档，未触发的子流程无需预先加载。
2. 下文保留的“子 Skill”“调用”“委派”及原子流程标识均指执行对应参考文档，不要求运行环境注册同名独立 Skill，也不表示自动启动子 Agent。
3. 独立子流程请求仅执行该子流程及其自身前置条件；不得强制进入材质生成链路。所有原有用户确认、只读边界、备份、revision、编译和验收要求继续有效。
4. 主编排调用子流程时传递原有输入契约，按原有结果契约处理返回值；子流程不得扩大主任务授权或文件白名单。
5. 性能子流程的入口在此保留；其结果仅预警，不阻断后续视觉验收。不得借本次结构调整改变原主流程的步骤、支持范围或验收条件。


## 使命

将角色、场景、特效 Shader 需求转化为基于当前仓库事实的可审计实现。默认从用户确认分类的知识库检索同类参考 Shader 并派生；只有用户明确要求 PBR 时执行原有 MaterialIntent 与 PBR 语义图。两条路线共享用户授权、编译、GUI、Review 与证据驱动验收。

本 Skill 是领域编排协议，不是通用 Unity 操作层。所有 Unity 编辑器读取、资产操作、编译、场景配置、截图和日志收集均通过现有 Unity MCP 执行：[`get_editor_state`](../../Tools/unity-mcp-server/src/index.ts:359)、[`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406)、[`get_logs`](../../Tools/unity-mcp-server/src/index.ts:508)。

对于本 Skill 新建或实质性更新的材质 Shader，Inspector 元数据布局委派给 [`unified-shader-gui-authoring`](references/shader-gui.md)。主 Skill 只在 Shader 编译通过、真实 Properties 与关键字已确定后发起该委派；子 Skill 只维护 `Properties` 内 UnifiedShaderGUI 元数据、受支持 attributes 与最外层 `CustomEditor` 声明，不得反向改变渲染实现。

## 支持范围与路线

- 支持角色、场景、特效 Shader 编写，用途分类与着色模型独立；特效可为半透明自发光，场景可为 Lightmap/Unlit/经验模型，不强行 PBR 化。
- 默认执行 [参考驱动路线](references/reference-authoring.md)。功能支持依同类型真实参考、源码证据、工具与输入条件决定，不因文件名命中而自动承诺。
- 用户明确要求 PBR 时，保留下方 Step 2–5 的金属度-粗糙度、效果栈、Alpha Blend/Clip 及能力解析流程。该路线现有限制（折射、透明阴影、排序修正、深度预通道、未扩展的 Clear Coat/Anisotropy/Parallax 等）仅约束 PBR 专项，不作为全部参考 Shader 的禁令。
- 特效 Shader 支持不等于授权实现粒子系统、模拟、游戏事件或完整 VFX 系统；额外运行配置需单独确认。后处理仍不在本次支持范围。
- 下文 Step 2–5 的 MaterialIntent、RenderingSemanticGraph、PBR 能力必需项及输出公式只适用于 route=pbr；route=reference 由参考协议 R1–R3 替代，再进入 Step 6 的共享计划约束及后续流程。所有模型无关的安全约束保持生效。

## 不可违反的工作纪律

1. 先确认角色/场景/特效用途及参考/PBR 路线；不得将非 PBR 强制转成 PBR，也不得把 Shader 编写授权扩张成完整 VFX 或后处理系统。
2. PBR 路线按 MaterialIntent → RenderingSemanticGraph → ProjectShaderProfile → ShaderCodePlan；默认参考路线按 ShaderTaskIntent → ReferenceShaderProfile → ReferenceShaderPlan → 共享 ShaderCodePlan 约束。两者都必须先核验事实、后规划、再经用户确认写入。
3. 不得依据记忆猜测项目 Shader 接口、URP Include、Pass、宏或材质约定；必须通过 Unity MCP 收集事实。
4. 每轮只验证一个语义阶段或一个明确假设；禁止在同一轮同时重写直接光、间接光和 Alpha 输出。
5. 编译错误优先于视觉结论。编译失败时不得基于截图判断材质质量。
6. 不得伪造不存在的贴图、反射探针、BRDF LUT、阴影或间接光能力。
7. 不得突破 `ShaderCodePlan` 的文件白名单和锚点范围。
8. 任何默认补全必须记录来源、理由和置信度。
9. 任何 `pass` 必须有静态检查、Unity Console、截图或数值证据；截图不是唯一真值。
10. `revise` 必须保留当前工件和失败证据；`blocked` 必须说明缺失能力或最小人工决策。
11. 生成的 Shader 必须与工程既有 Shader 保持同一风格：工程已有 Pass 声明的关键字矩阵、`Attributes`/`Varyings` 布局、Pass 标签与命名约定必须一并继承；任务未映射到的能力以恒等接线保留结构，而不是省略关键字或结构。
12. 选择某个参考 Shader 作为生成基线时，必须将其材质语义、属性类型/默认值、纹理打包通道约定、关联关键字、`CBUFFER` 字段、采样与 `SurfaceData` 接线视作一个不可拆分的材质工艺契约；属性名称仅是当前管线的实现细节，不得跨管线硬编码或误当作通用规范。不得以「需求未明确提出」「本次用不到」或「最小实现」为由删除其中任一语义能力。
13. 参考 Shader 存在多工作流或互斥变体时（例如 Metallic 与 Specular），必须完整保留每套工作流的材质语义、贴图打包、关键字与运行时接线；不得只保留默认工作流。新增效果只能叠加在参考基线之上，不得替代、短接或降级原有工作流。
14. 生成的 Shader 与 HLSL 代码注释必须使用中文；注释说明意图、约束与来源，不复述代码字面。

## 状态机

```text
CHECK_KNOWLEDGE_BASE
  -> DISCOVER_AND_CONFIRM_SCOPE | CLASSIFY | BLOCKED
DISCOVER_AND_CONFIRM_SCOPE
  -> WAIT_USER | BUILD_KNOWLEDGE_BASE
BUILD_KNOWLEDGE_BASE
  -> CLASSIFY | BLOCKED
CLASSIFY
  -> UNSUPPORTED | CLARIFY | NORMALIZE_INTENT | RETRIEVE_REFERENCE
RETRIEVE_REFERENCE
  -> CLARIFY | VERIFY_REFERENCE
VERIFY_REFERENCE
  -> BLOCKED | PLAN_CODE
NORMALIZE_INTENT
  -> CLARIFY | BUILD_GRAPH
BUILD_GRAPH
  -> ANALYZE_PROJECT
ANALYZE_PROJECT
  -> RESOLVE_CAPABILITIES
RESOLVE_CAPABILITIES
  -> BLOCKED | PLAN_CODE
PLAN_CODE
  -> CONFIRM_SCOPE
CONFIRM_SCOPE
  -> REVISE_PLAN | EXECUTE
EXECUTE_SHADER
  -> AUTHOR_SHADER_GUI
AUTHOR_SHADER_GUI
  -> REVIEW
REVIEW
  -> CONFIRM_REVIEW | BLOCKED
CONFIRM_REVIEW
  -> FIX_APPROVED_ITEMS | VALIDATE | WAIT_USER | BLOCKED
FIX_APPROVED_ITEMS
  -> EXECUTE_SHADER
VALIDATE
  -> CONFIRM_FINAL_ACCEPTANCE | REVISE | BLOCKED
CONFIRM_FINAL_ACCEPTANCE
  -> PASS | REVISE | WAIT_USER
REVISE
  -> PLAN_CODE
```

## Phase 0：项目知识库门禁

在具体实现规划或生成 Shader 前，必须检查可持久化的 `ProjectShaderKnowledgeBase` 是否存在且有效；允许为分类确认而进行只读需求澄清。首次构建或分类范围变化时，必须先执行 [目录发现与用户确认](references/domain-discovery.md)：展示特效、场景、角色、通用库的真实路径与代表 Shader，用户确认最终范围后才正式生成。该知识库的构建、增量刷新、覆盖率验收、版本发布和失效处理全部委派给 [`shader-knowledge-base-builder`](references/knowledge-base.md)。

### 门禁决策

```text
检查 ProjectShaderKnowledgeBase
  ├── 存在、Schema 兼容、目录分类确认有效、范围及工程指纹匹配、状态为 fresh
  │     └── 记录 knowledgeBaseVersion，进入材质生成链路
  ├── 不存在
  │     └── 告知用户需要先生成项目专用知识库，等待同意
  ├── 已失效或工程指纹不匹配
  │     └── 告知用户需要先刷新项目专用知识库，等待同意
  └── 正在构建或构建失败
        └── 返回当前状态、失败证据和下一步处理建议，不进入生成链路
```

用户同意创建或刷新时，调用 `shader-knowledge-base-builder`，并传入 `full` 或 `incremental` 模式及失效原因。创建/刷新授权不等于目录分类确认；未确认时返回 blocked、reason=awaiting_scope_confirmation，不发布正式版本。仅当其返回 status=fresh 且分类确认有效后，重新执行门禁检查并进入当前 Shader 任务。

### 主 Skill 使用知识库的规则

1. 每个材质任务必须记录 `knowledgeBaseVersion`、Manifest 路径和任务开始时的指纹摘要。
2. 先按用户确认的角色/场景/特效用途检索同类样本与共享库；功能以源码证据核验。仅 PBR 路线按基础 PBR、Alpha 与效果图检索，不将角色规范或 PBR 输入当作全项目契约。
3. 项目无本地先例时，只可使用知识库中已确认适用于当前管线的 Library 函数卡片和 Capability Catalog；不得凭记忆引入其他管线代码。
4. 某项能力为 `unknown` 时，必须在 Capability Resolver 中保留不确定性，不得作为已支持能力使用。
5. 任务过程中发现新的 Shader、Include 或配置变化时，记录知识库增量刷新请求；本轮是否继续取决于现有证据是否足够，不得静默更新知识库。

## Step 1：用途确认与路线选择

```text
RequirementClassification
  domain: character | scene | vfx | mixed | unknown
  primaryCarrier: mesh | skinned_mesh | particle_system | trail | unknown
  route: reference | pbr | clarify | unsupported_manual_handoff
  explicitPbrRequest
  userDomainConfirmation
  targetDirectory
  referencePreferences
  evidence
  clarificationQuestion
```

- 用户未明确用途时，询问“这次编写角色、场景还是特效 Shader？是否有参考 Shader 或目标目录？”；已明确时记录依据，不重复询问。
- 明确要求 PBR（或明确同意采用 PBR）才走 Step 2–5；角色/场景/特效仍作为样本筛选维度。不能仅因参考是 PBR 或出现“金属/光照”就擅自选 PBR。
- 其余默认完整读取并执行 [参考驱动路线](references/reference-authoring.md) R1–R3，再进入 Step 6 共享计划约束、6.4/6.5 和后续执行，不运行 PBR 专属图、公式与必需能力门禁。
- 名称命中只用于召回，候选不兼容或无同类样本时列缺口等待选择，不自动跨类型套角色 Shader。
- 后处理请求仍转人工/明确说明不支持；时间驱动、溶解、粒子或拖尾载体本身不再成为拒绝 Shader 编写的依据。额外系统搭建需另行授权。

## Step 2：构建 Material Intent（仅 PBR 路线）

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

本步骤不是重新扫描全项目或构建知识库；项目级环境、Shader Library、样本语料、工程风格和能力目录由 [`shader-knowledge-base-builder`](references/knowledge-base.md) 维护。

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
8. 建立风格基线：从工程样本中提取目标 Pass 的完整关键字矩阵、`Attributes`/`Varyings` 布局、渲染状态与命名约定，写入 `targetAnchors.propertiesAndKeywords`、`targetAnchors.dataStructures` 和 `targetAnchors.renderStateAnchors`，作为纪律 11 的对齐依据。
9. 工程样本同时包含工程资产与包内资产；样本语料缺少 `Packages/` 覆盖时，不得据此断言工程关键字矩阵。

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
  shaderGuiAuthoring
    required: true | false
    delegate: unified-shader-gui-authoring | none
    trigger: post_shader_compile
    targetShader
    expectedProperties
    expectedKeywords
    inspectorFeatures: groups | toggles | enums | vectorSplits | ramps | dataTips | blendPresets
    customEditorPolicy: add_if_absent | preserve_existing | replace_confirmed_legacy
    cacheInvalidationScope
    excludedReason
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
- 必须写明风格对齐基线：继承的工程关键字矩阵、`Attributes`/`Varyings` 布局与命名约定（纪律 11），以及每条未继承项的偏差理由。
- 必须写明注释语言为中文（纪律 12）。
- 对新建或实质更新的材质 Shader，`shaderGuiAuthoring.required` 默认必须为 `true`，并在首次 Shader 编译通过后委派 [`unified-shader-gui-authoring`](references/shader-gui.md)。仅当用户明确要求不使用自定义 Inspector、目标 Shader 不是材质 Shader、或既有未知业务 `CustomEditor` 不允许替换时，才可设为 `false`；必须在 `excludedReason` 记录原因。
- `expectedProperties`、`expectedKeywords` 与 `inspectorFeatures` 必须来自生成后、编译前的实际 Shader 文本和项目锚点，不能凭 `MaterialIntent` 臆造。`customEditorPolicy` 默认使用 `add_if_absent`；不得静默覆盖未知 `CustomEditor`，且必须记录 UnifiedGUI 缓存失效范围。

## Step 6.4：Review 写入前预检

完整读取并执行 [Review 接入协议](references/review-workflow.md) 的 A 节：核验生效范围、存量白名单、命名规范及 ShaderLibrary 的 P001 事前审批。原有 Code Plan 和参考契约要求保持有效；遇到冲突先报告并阻断，不自动改名、删属性或扩大授权。

## Step 6.5：功能与属性确认门禁

在得到 `ShaderCodePlan`、但**首次写入 Shader、材质或任何渲染实现代码之前**，主 Skill 必须向用户提交一份简明的功能与属性确认单。PBR 路线从 MaterialIntent、ResolvedSemanticGraph、ProjectShaderProfile 与 generatedMaterialProperties 投影；参考路线从 ShaderTaskIntent、ReferenceShaderProfile、ReferenceShaderPlan 与实际属性投影，并列出选定参考及分类。两条路线都不得凭空增加未核验能力或虚构贴图资产。

### 强制确认内容

1. 以功能为单位列出：所选参考与实际着色/输出行为（仅 PBR 路线列基础 PBR）、透明或裁剪路径、每个启用效果，以及因能力、用户要求或基线契约未纳入的功能。参考路线的确认单来源为 ShaderTaskIntent、ReferenceShaderProfile 与 ReferenceShaderPlan，不虚构 PBR 图。
2. 每项功能标注状态：`保留`、`可选`、`缺失待补充`、`不支持` 或 `继承基线`；并简述其可见行为与关键依赖。
3. 按功能分组输出属性参数表。每行至少包含：显示名称、Shader 属性名、类型、默认值或范围、用途、状态（保留/可删/待补充）和来源（用户指定/工程基线/默认推断）。纹理属性还必须注明语义、通道约定与是否需要用户提供资产。
4. 单独列出默认推断、待补充信息、能力降级、排除项与会影响视觉结果的实现约束。
5. 不展示完整 HLSL、Pass 细节或冗长的内部分析；该确认单应仅包含用户决定范围所需的简略功能清单与参数表。

### 对外确认模板

```text
## 待确认的材质功能

| 功能 | 预期效果 | 状态 | 备注 |
| --- | --- | --- | --- |
| 所选参考/着色功能（PBR 路线为基础 PBR） | {简述} | 继承基线/保留 | {关键约束} |
| {效果名称} | {简述} | 保留/可选/缺失待补充/不支持 | {依赖或原因} |

## 按功能分类的属性参数

### {功能名称}
| 参数 | Shader 属性 | 类型 | 默认值/范围 | 用途 | 状态 | 来源 |
| --- | --- | --- | --- | --- | --- | --- |
| {显示名} | `{_PropertyName}` | Float/Range/Color/Texture/Toggle/Enum | {值或范围} | {简述} | 保留/可删/待补充 | 用户指定/工程基线/默认推断 |

## 待补充或默认假设
- {假设、缺失贴图、降级或排除项}

请逐项确认：哪些功能和参数需要保留、删除或补充；确认无误后才会开始 Shader 编码。
```

### 确认循环与执行条件

- 用户首次确认前，禁止调用 `write_generated_text_asset`、禁止修改 Shader 渲染实现、`Properties`、材质资产或相关 HLSL Include；允许继续执行只读项目核验以回答用户的补充问题。
- 用户要求增加、删除、替换功能，或调整任意属性的类型、默认值、范围、贴图语义或可见行为时，必须更新本路线的意图、能力核验和计划（PBR 路线还更新语义图；参考路线更新参考契约），再重新输出完整确认单；状态机转入 `REVISE_PLAN`。
- 用户指出“缺失”但未给出可实现定义时，将该项标为 `缺失待补充`，只提出完成该项所需的最小澄清；不得以默认算法静默补全。
- 只有当用户明确表示功能清单与属性参数表均满意、并授权开始编码时，才将确认记录为 `user_scope_confirmed`，进入 Step 7。
- 用户仅确认某些条目时，未确认、待补充或被标为可选的条目不得进入编码；必须重新展示收敛后的确认单。
- 每轮确认都必须记录：确认版本、用户保留/删除/新增项、参数变更和未决项；未决项不为空时不得开始编码。

## Step 7：受限执行

执行必须优先使用**结构化白名单工具**，而不是任意 C#。只有白名单无法表达、且已获得显式授权的诊断或维护场景，才允许使用 [`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:432)（裸 C# 在 Unity 6 上不作为常规执行路径）。执行类别与对应工具：

| 类别 | 结构化工具 |
| --- | --- |
| `READ_ANALYSIS` | [`get_asset_revision`](../../Tools/unity-mcp-server/src/index.ts:605)、[`inspect_shader_structure`](../../Tools/unity-mcp-server/src/index.ts:604) |
| `WRITE_SHADER` | [`write_generated_text_asset`](../../Tools/unity-mcp-server/src/index.ts:606) |
| `WRITE_MATERIAL` | `write_generated_text_asset`（`.mat` 属受控生成资产） |
| `REFRESH_AND_COMPILE` | [`refresh_and_compile_assets`](../../Tools/unity-mcp-server/src/index.ts:607) |
| `CAPTURE_EVIDENCE` | [`capture_validation`](../../Tools/unity-mcp-server/src/index.ts:609) |

### 工程风格对齐（硬性约束）

以下约束继承的是选定参考实际存在的完整契约；第 4–6 条的 PBR 属性与接线示例仅在基线存在该能力时适用。非 PBR 参考不新增虚构的 PBR 输入，使用 ReferenceShaderParityManifest 记录其真实顶点输入、着色、纹理、颜色与 Alpha/Blend 契约。

生成资产不是「最小可用 Shader」，而是**工程既有 Shader 的同工艺派生实现**。这里的“对齐”不仅指代码外观，而是保证同一项目中的美术材质、贴图打包规则和 Inspector 操作方式可复用：

1. 以知识库中的工程样本（`attributesVaryingsStyle`、`fragmentProgramStyle`、`keywords`、`renderStates`）为基线，而不是凭记忆。
2. 在写入前必须建立 `ReferenceShaderParityManifest`。它逐项列出参考 Shader 的材质语义、属性（名称、显示名、类型、默认值）、纹理打包通道、隐藏兼容属性、关联 `#pragma`、`CBUFFER` 字段、采样/解包逻辑、`SurfaceData` 接线、Render State 与 Pass；并明确每项在目标 Shader 中的对应锚点。属性名属于管线本地实现，Manifest 必须分别记录「语义标识」与「当前管线属性名」，不得只按属性名做跨管线对齐。此清单是 Code Plan 的必填证据，不得仅以文字概述代替。
3. 工程 Pass 已声明的关键字必须完整继承，包括随版本更名的关键字（例如 `_CLUSTER_LIGHT_LOOP` 启用后取代已废弃的 `_FORWARD_PLUS`）；删除任何关键字都必须给出证据与理由。
4. 参考 Shader 的**材质语义契约**必须完整继承：即使本次需求未提及，也必须保留基础色/透明度、法线、遮蔽、金属度或镜面反射率、粗糙度或光滑度、Emission、细节层、视差、透明/裁剪控制、兼容迁移语义及其原有打包约定。具体属性名和贴图布局必须以当前目标管线及工程基线为准：URP 可为 MetallicGloss/SpecGloss；其他管线或项目可为 MSO（金属/光滑/AO）、RMO（粗糙/金属/AO）或其他已核验布局。不得把 URP 属性名强加到非 URP 管线，也不得以「最小可用 PBR」「本次效果不使用」或「降低变体数」为由省略语义能力。
5. 对参考 Shader 的多工作流或互斥变体，必须同时保留属性、贴图打包、关键字、采样和 `SurfaceData`（或目标管线等价材质输入）接线。例如 URP Lit 同时包含 `_MetallicGlossMap` / `_METALLICSPECGLOSSMAP` 与 `_SpecGlossMap` / `_SPECULAR_SETUP` 时，目标 Shader 必须同时实现两条路径；若目标工程采用 MSO、RMO 或自定义打包，则必须完整继承其已核验的通道映射与解包逻辑，而不是套用 URP 命名或通道规则。
6. 任务未用到的能力以恒等接线保留结构（例如 Clear Coat 掩码置 `0`、法线贴图槽置默认值），不得因「本次用不到」而裁剪工程既有结构。新增效果仅允许在完整基线之后叠加，不能替换、旁路或降级既有 PBR 输入。
7. 直接复用当前目标管线/工程既有的 `Attributes` / `Varyings` 字段与顺序、渲染状态、Pass 标签和命名，除非 Code Plan 明确记录偏差；不得将其他渲染管线的 Include、宏、Pass 或属性名直接移植过来。
8. 每条差异化都必须写入 Code Plan 的 `plannedChanges`，并在证据中标注来源与置信度。
9. 若工程样本中不存在可对照的实现，必须在 `unknowns` 中记录，而不是自行发明风格。

#### 属性工艺对齐门禁

在首次写 Shader 前与最终 `PASS` 前各执行一次 `ReferenceShaderParityManifest` 核对：

- 任一参考材质语义、关键字、贴图采样、通道约定、工作流分支、Pass 或兼容语义缺失，且不存在用户明确批准的偏差记录：判定 `REVISE`，禁止进入视觉验收或 `PASS`。
- 仅在 `Properties` 中声明贴图但未声明对应关键字、未采样、未写入 `SurfaceData`（或目标管线等价输入），视为缺失，不得算作对齐。
- 参考材质的现有 `.mat` 必须能无丢失地迁移到目标 Shader：同管线迁移时检查同名属性可解析、纹理槽不丢失、默认值/通道语义一致；跨管线迁移时检查每个源语义都映射到目标管线已核验的等价属性与打包通道，不要求属性同名。失败即 `REVISE`。
- 用户只需描述新增效果时，默认语义是「在参考 Shader 的完整工艺基线上增加效果」，不是授权精简参考 Shader。

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

### Step 7.5：委派 UnifiedShaderGUI 元数据生成（强制后处理）

当 `ShaderCodePlan.shaderGuiAuthoring.required = true` 时，主 Skill 必须在 Step 7 写入 Shader 并完成首次编译/Console 门禁后，调用 [`unified-shader-gui-authoring`](references/shader-gui.md)。不得在 Shader 的 Properties、关键字或默认值尚未稳定前生成 GUI 元数据。参考路线的委派以 ShaderTaskIntent 和真实参考契约为依据，materialIntent 兼容字段标记 intentKind=reference，renderingSemanticGraph=not_applicable；不伪造 PBR 图，不覆盖未知业务 CustomEditor。

#### 委派输入

```text
UnifiedShaderGUIAuthoringRequest
  targetShader
  targetShaderRevision
  materialIntent
  renderingSemanticGraph
  generatedMaterialProperties
  expectedProperties
  expectedKeywords
  inspectorFeatures
  customEditorPolicy
  existingCustomEditor
  cacheInvalidationScope
  userInspectorRequirements
  sourceStyleEvidence
```

- `expectedProperties`：属性名、显示名、类型、默认值、语义与所属效果层。
- `expectedKeywords`：实际 `#pragma` 声明、关键字用途与对应效果层；没有真实关键字时不得要求子 Skill 生成 `KeyWords` 标记。
- `inspectorFeatures`：仅列出有真实属性、属性类型和渲染前置条件证据的 groups、toggles、enums、vectorSplits、ramps、dataTips、blendPresets；Ramp、DataTip 与 BlendPreset 不得凭名称猜测。
- `existingCustomEditor` 与 `customEditorPolicy`：已有未知业务 GUI 时必须保留或等待确认；切换 UnifiedGUI 必须使用备份/恢复策略。
- `cacheInvalidationScope`：记录目标 Shader 是否命中 UnifiedGUI 自动缓存失效范围；范围外改动后需显式重载或重新打开 Unity 验证。
- `userInspectorRequirements`：用户明确提出的 Inspector 分组、名称、隐藏/显示或开关偏好；未提供时由子 Skill 按材质语义生成。
- `sourceStyleEvidence`：当前工程 Shader 的命名、中文注释和 Inspector 布局证据；缺失时只能采用子 Skill 的保守默认分组。

#### 委派约束与返回处理

1. 子 Skill 只能改动目标 Shader 的 `Properties` 元数据、受支持 attributes 与最外层 `CustomEditor` 声明；主 Skill 的渲染代码、关键字、属性类型/默认值、Pass 与渲染状态均为不可修改锚点。
2. 子 Skill 生成后，主 Skill 必须将新的 Shader revision 记为本轮最终 revision，并重新执行 Step 7 的刷新、编译及 [`get_console_diagnostics`](../../Tools/unity-mcp-server/src/index.ts:611)。
3. 子 Skill 返回解析歧义、已有未知 `CustomEditor` 冲突、缓存失效限制或不可安全自动化项时，主 Skill 将其记录到 `ShaderCodePlan.shaderGuiAuthoring.excludedReason`；不以删除属性或覆盖未知 Inspector 的方式强行通过。
4. GUI 元数据失败不会被视觉截图掩盖：若目标 Shader 的编译或 UnifiedGUI 解析诊断存在 Error，本轮进入 `REVISE`；若仅为用户选择的 Inspector 策略冲突，则进入 `BLOCKED` 或保留默认 Inspector，并明确报告。
5. 在最终验证中，除渲染结果外，还必须确认 UnifiedGUI 分组、属性类型、组级开关、关键字开关、Enum、VectorSplit、适用的 Ramp/DataTip/BlendPreset 及 Render Queue 字段与委派计划一致。

## Step 7.6：Review、修改项确认与修复复审

在 Step 7 与适用的 Step 7.5 完成且编译/Console 门禁通过后，必须执行 [Review 接入协议](references/review-workflow.md) 的 B–E 节：扫描并人工复核 → 向用户列出修改项 → 等待批准 → 仅修复批准项 → 重新编译与 Review → 再列清单等待确认，直到用户针对最新版本明确同意进入验收。无问题或全部跳过也需用户确认；不得把未执行或跳过写作通过。用户确认修复不等于同意验收，Review 接受也不能替代 Step 8 技术门禁。任何后续写入使对应 Review 确认失效，需重新复审与确认。

## Step 8：验证

进入前必须具备 Step 7.6 最新 revision 的 `user_review_accepted` 记录且无未解决阻断项；未满足则返回 Review 确认循环。以下固定验证顺序及原有技术门禁不变。技术验证完成后展示验收结果并等待用户明确接受最终验收；用户要求修改时返回计划与授权、修复、编译和 Review 循环，不自动宣布最终 PASS。

### 验证路线选择

route=reference 时完整执行 [参考路线 R4](references/reference-authoring.md) 的载体/功能验收，保留下方通用静态、编译、Console、GUI、Review 和目标响应证据门禁；不执行 PBR 专属 Stage。下方固定 Sphere + Test.mat 流程适用于 PBR 路线及确实适配该夹具的参考路线，不能冒充粒子、蒙皮或 Lightmap 验证。当前工具不足时 BLOCKED，不假定已实现新的夹具工具。

### 固定验证顺序（PBR/适配固定夹具的参考路线）

1. 静态检查：计划白名单、锚点、属性、渲染状态、禁止特性，以及 `ReferenceShaderParityManifest`：工程关键字矩阵、Properties、纹理通道语义、工作流分支、`CBUFFER`、采样/`SurfaceData` 接线、`Attributes`/`Varyings`、Pass 是否完整继承；任一未继承项必须有用户明确批准的偏差理由，否则直接 `REVISE`。
2. Unity 刷新与 Shader 编译（`refresh_and_compile_assets` → `get_unity_job`）。
3. Console 读取（`get_console_diagnostics`）：确认无新增 Error；有错则回到 Step 7 修复循环。
4. 当 `shaderGuiAuthoring.required = true` 时，确认 UnifiedGUI 元数据解析与 Inspector 行为符合委派计划；失败则回到 Step 7.5 修复。
5. **固定测试夹具预检**：本固定夹具路线的 Shader 验收必须通过固定夹具，不得由调用方以层级路径选择任意场景对象。`ensure_validation_scene` 忽略传入的 `scenePath`、`cameraPath` 与 `objectPath`，并固定使用 [`AI Shader Authoring.unity`](../../../../Tests/AI%20Shader%20Authoring.unity) 和 [`Test.mat`](../../../../Tests/Test.mat)。实现必须以 Additive 方式打开磁盘场景，要求场景内恰有一个 `Camera`，并在所有名为 `Sphere` 的 GameObject 中要求恰有一个带 `Renderer` 的对象；零个或多个候选均为夹具失效，立即 `BLOCKED` 并输出候选层级。不得以当前编辑器已打开场景的 Hierarchy 代替对磁盘验证场景的核验。
6. **受控材质替换与响应验证**：验收流程固定为：Additive 打开 [`AI Shader Authoring.unity`](../../../../Tests/AI%20Shader%20Authoring.unity) → 找到既有 [`Test.mat`](../../../../Tests/Test.mat) → 将它临时绑定给带 `Renderer` 的唯一 `Sphere` → 快照 [`Test.mat`](../../../../Tests/Test.mat) 完整序列化状态 → 临时替换为待验收 Shader → 写入参考贴图和本次截图的参数覆盖 → 截图 → 无条件恢复材质和 Renderer。**禁止创建、复制、保存、加载或保留任何验证专用 `.mat` 资产**；待验收输入只能是已有 Shader 资产，低/高参数对比必须通过同一次或多次捕获中的临时 `propertyOverrides` 实现。可从已核验的 Theseus 闪点/流光/镭射参考 Shader 的材质中补齐缺失贴图，但仅限目标槽为空、纹理及属性类型兼容时，且必须在捕获报告中记录源材质路径、属性名和映射理由；不得盲目复制关键字、Render Queue 或不兼容属性。参考材质候选必须按目标 Shader revision 缓存并限制数量，禁止每次截图全工程扫描。无论成功、失败或取消，都必须从快照恢复 [`Test.mat`](../../../../Tests/Test.mat) 的完整状态；不得保存该材质、Renderer 共享材质或验证场景。随后至少捕获两张具有显著参数差异的目标材质图（例如溶解 `0` / `0.6`，或 Emission `0` / 高亮）。必须人工复核目标几何确实位于画面内，并确认目标材质变化使 PNG `contentHash` 或目标区域像素发生变化；否则立即 `REVISE`，先修复 Shader、参数或测试夹具，不得继续做效果判断。
7. 输出当前阶段的目标图、参考图、Debug 图；Alpha Clip 或透明需求还必须输出 Alpha 灰度图与裁剪遮罩图。
8. 运行当前阶段所需的参数扫描，并记录每个扫描点的材质 revision、参数值、截图 hash 与人工结论。
9. 保存 `PASS`、`REVISE` 或 `BLOCKED` 及其证据（必须包含第 2、3 步的诊断结果）。

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

### 验证证据的硬性防线

目标可见、正确绑定、差异化参数响应与人工复核原则对两条路线均生效；以下固定 Sphere/场景的具体描述适用于固定夹具路线，专用载体按参考协议 R4 核验，不以不适配的球体截图代替。

像素统计只能证明「渲染结果非空」，不能证明「渲染了目标」。`ensure_validation_scene` 必须验证固定场景、唯一相机、唯一名称为 `Sphere` 的 Renderer 和 [`Test.mat`](../../../../Tests/Test.mat) 都可读取，但仍不等同于目标已进入相机视锥或材质替换已对画面生效；因此必须执行固定验证顺序中的夹具预检、参考图和差异化材质捕获。出现以下任一情况时**不得**判定 `PASS`，必须记为 `REVISE` 并在证据中说明：

1. **材质替换无效**：两次 `capture_validation` 的 PNG `contentHash` 完全一致。材质 revision 变了而像素不变，说明画面对被替换材质零响应，目标物体不在画面中。
2. **背景基线不可靠**：统计基线取自 `pixels[0]`（左上角）时，渐变天空盒等非纯色背景会让 `nonBackgroundRatio` 量到背景自身的差异而非目标轮廓（可高达 `0.9+`）。此时必须改用固定背景色或真实背景采样，并人工复核截图。
3. **目标不可辨认**：截图中无法辨认目标几何，或目标轮廓、占屏比例与相机投影参数（位置、朝向、FOV、近远裁剪）不一致。
4. **场景与相机不一致**：目标所在场景若以叠加方式打开，渲染必须使用该场景内的相机，且相机与目标同场景；活动场景与验证场景不同时，`Render` 可能渲染的不是验证目标。

第 1 条是首选判别手段：先比对哈希，再人工复核截图，最后才看阈值。
截图不是唯一真值；当截图与数值证据冲突时，以人工复核截图为准。

### 决策条件

```text
PASS
  当前阶段编译通过，静态检查通过，Console 无新增错误，验证证据满足明确验收标准，
  最新资产 revision 已完成适用的 Review 并获得用户同意进入验收，且用户已明确接受本阶段验收结果；
  且通过工程风格与属性工艺对齐检查（纪律 11-13、`ReferenceShaderParityManifest`）与验证证据硬性防线（截图哈希随材质变化）。
  未对齐工程风格或参考属性工艺契约、注释非中文、或命中验证防线条目时，一律不得判定 PASS。

REVISE
  存在可定位的代码、数据、公式、参数、颜色空间或场景配置问题；保留现场，仅修正当前假设。

BLOCKED
  缺失必需项目能力、需求超出范围、存在无法安全默认的冲突，或需要人工提供资产/选择策略。
```

## 每轮对外输出格式

先输出 domain、route、用户类型确认、知识库分类确认版本和所选参考。下方 Material Intent / Rendering Semantic Graph / Capability Resolution 为 PBR 路线字段；参考路线使用 ShaderTaskIntent / ReferenceShaderProfile / ReferenceShaderPlan 与实际能力核验替代，不填写伪 PBR 字段。共享 Review、编译和用户验收记录保留。

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
- project style baseline:（继承的关键字矩阵、Attributes/Varyings、命名约定；未继承项与理由）
- comment language: 中文
- shader GUI authoring:（required/delegate、目标 Shader revision、Properties/Keywords 输入、CustomEditor 策略、委派结果或豁免理由）

## Validation
- static result:
- compile result:
- console result:
- shader GUI result:（分组/开关/枚举/CustomEditor、解析与 Inspector 验证结果）
- shader review:（轮次、审查 revision、扫描/人工证据、已修复/延期项、范围与白名单跳过、P001 状态）
- user review acceptance:（用户同意进入验收的版本与确认记录）
- user final acceptance:（用户最终接受/待确认/要求修改；技术门禁不满足时不得 PASS）
- captures and debug channels:
- capture hash comparison:（材质替换前后的 PNG contentHash 是否变化）
- comparison result:

## Decision
- PASS | REVISE | BLOCKED
- evidence:
- next action:
```

## 首次执行的推荐基线

PBR 路线首个端到端用例优先选择不透明 PBR 材质，仅验证基础色、金属度、粗糙度、主光、间接光能力和低强度自发光。Alpha Clip 与透明必须作为独立扩展轮次加入，避免首次闭环同时引入材质输入、光照、裁剪、混合和排序问题。
