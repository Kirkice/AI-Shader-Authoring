---
name: shader-knowledge-base-builder
description: 为 Unity 项目构建、刷新、验证并持久化项目专用 Shader Knowledge Base；索引渲染环境、Shader Library、项目 Shader 样本、工程实现风格、能力证据与检索关系。
---

# Shader Knowledge Base Builder

## 使命

构建可持久化、可追溯、可增量刷新、可被材质生成流程复用的 `ProjectShaderKnowledgeBase`。

知识库记录该 Unity 项目的真实渲染事实、可调用的 Shader Library 接口、现有 Shader 实现模式与工程约定。它不是通用 PBR 教程，不是原始源码镜像，也不应将整份 Unity Shader Library 直接堆入 LLM 上下文。

本 Skill 通过现有通用 Unity MCP 执行只读分析：[`get_editor_state`](../../Tools/unity-mcp-server/src/index.ts:359)、[`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406)、[`get_logs`](../../Tools/unity-mcp-server/src/index.ts:508)。

## 调用契约

### 调用方

通常由 [`shader-authoring-agent`](../shader-authoring-agent/SKILL.md:70) 在以下情况委派：

- 项目不存在知识库；
- 已有知识库的 Schema 不兼容；
- 工程指纹与知识库 Manifest 不匹配；
- 用户明确要求刷新；
- 先前构建状态为 `building` 或 `failed`，且用户要求继续处理。

### 前提

1. 用户已明确同意创建或刷新项目专用知识库。
2. Unity MCP 已连接，且 Unity Editor 可响应只读命令。
3. 调用方提供构建模式：`full` 或 `incremental`，以及已知失效原因。

### 返回

```text
KnowledgeBaseBuildResult
  status: fresh | partial | failed | blocked
  knowledgeBaseVersion
  buildMode: full | incremental
  manifestPath
  refreshedPartitions
  coverageReport
  unresolvedItems
  evidenceSummary
  failureReason
  recommendedNextAction
```

只有 `status = fresh` 的结果可以放行 Shader Authoring 主链路。`partial` 可供人工查看，但不得被视为生成 Shader 的可靠依据。

## 目录与工件原则

知识库应保存到项目内一个明确、可版本化、默认不被运行时代码加载的工件位置。推荐位置：[`Artifacts/ShaderKnowledgeBase`](../../Artifacts/ShaderKnowledgeBase)。

每一个发布版本至少包含：

```text
Artifacts/ShaderKnowledgeBase/
  current.json
  versions/
    <knowledge-base-version>/
      manifest.json
      environment.json
      library-index.json
      function-cards.json
      shader-corpus.json
      project-conventions.json
      capability-catalog.json
      retrieval-index.json
      coverage-report.json
      evidence/
        source-fingerprints.json
        sampled-source-locations.json
        build-log.json
```

- `current.json` 只指向最后一个成功的 `fresh` 版本。
- 失败构建不得覆盖 `current.json`。
- 原始 Shader 与 Library 文本不复制到知识库；仅保存可追溯的路径、revision/hash、结构化提取结果和必要的小型证据片段。

## 不可违反的纪律

1. 先收集工程事实，后做归纳；不得按记忆猜测当前项目的 Pipeline、函数或实现习惯。
2. 所有归纳结论必须有来源文件、位置、revision/hash 与置信度。
3. 找不到的能力必须标记 `unknown`，不能视为 `unsupported` 或 `supported`。
4. 不得把 Built-in、URP、HDRP 或不同版本库的函数混为当前可调用能力。
5. 不得仅凭单个 Shader 样本宣布其为项目规范。
6. 不得修改 Shader、材质、场景、Render Pipeline Asset 或 Project Settings。
7. 不得把完整外部 Library 源码塞入知识库或 LLM 上下文；应提取函数卡片和结构索引。
8. 构建失败、证据不足或环境不稳定时，返回 `failed`、`partial` 或 `blocked`，不得发布 `fresh`。

## Phase A：检查现有知识库与确定构建模式

### A1. 查找与验证

读取 `current.json` 和目标版本 Manifest，验证：

```text
schemaVersion
freshnessStatus
unityVersion
packageLockFingerprint
projectAssetFingerprint
sourceRevisions
renderPipeline fingerprint
rendererData fingerprint
colorSpace fingerprint
```

### A2. 决策

```text
无 current.json 或目标版本不存在
  -> full

Schema 不兼容
  -> full

Unity / Pipeline / Renderer / Color Space / Rendering Path 指纹变化
  -> full environment refresh + dependent partition refresh

仅 Shader / HLSL / Include / Material 约定发生变化
  -> incremental corpus/library/convention refresh

无相关变化，且 freshnessStatus = fresh
  -> 返回 fresh，复用当前版本

当前构建为 building 或 failed
  -> 报告状态，不覆盖；等待调用方或用户决定是否重试
```

## Phase B：收集渲染环境

通过 [`get_editor_state`](../../Tools/unity-mcp-server/src/index.ts:359) 与只读 [`execute_editor_command`](../../Tools/unity-mcp-server/src/index.ts:406)，建立环境事实：

```text
environment
  unityVersion
  renderPipeline: builtin | urp | hdrp | custom_srp
  renderPipelineVersion
  activeRenderPipelineAsset
  activeRendererData
  renderingPath: forward | deferred | forward_plus | unknown
  colorSpace: gamma | linear
  hdrEnabled
  graphicsApis
  targetPlatforms
  qualityLevel
  qualityAndPlatformNotes
```

### 证据要求

- 每一项值均记录来源 API、资产路径或配置文件位置。
- 无法从可用 API 可靠判断的字段使用 `unknown`。
- 仅凭 [`Packages/manifest.json`](../../Packages/manifest.json:1) 出现某包，不能推断它是实际启用的 RP Asset 或 Renderer。

## Phase C：建立 Shader Library 索引

### C1. 根据环境选择 Library 范围

```text
Built-in
  -> UnityCG 与项目本地 CG/HLSL 为主

URP
  -> Core RP、URP ShaderLibrary 与项目本地 HLSL 为主
  -> UnityCG 仅作为遗留兼容参考，不自动标为当前推荐实现

HDRP
  -> Core RP、HDRP ShaderLibrary 与项目本地 HLSL 为主

Custom SRP
  -> 项目本地 SRP 库为主，外部 Library 仅作为候选参考
```

### C2. 文件级索引

对每个可访问的 `.hlsl`、`.cginc`、`.shader` Library 文件提取：

```text
path
owner: package | project
sourceRevision
includeDependencies
guardsAndKeywords
structs
macros
texturesAndSamplers
exportedFunctions
coordinateSpaceConventions
precisionConventions
```

### C3. 函数卡片

将高价值可调用符号提炼为函数卡片：

```text
symbol
signature
purpose
inputsOutputs
preconditions
coordinateSpace
precision
includeDependencies
sourceLocation
compatiblePipeline
relatedCapabilities
```

优先覆盖：

- 顶点对象空间、世界空间、切线空间与裁剪空间转换；
- 世界法线、视线方向、切线基；
- 主光、附加光、阴影和衰减；
- PBR BRDF、直接光、间接漫反射与间接镜面；
- 反射探针、BRDF LUT、环境采样；
- 贴图采样、颜色空间、HDR 编解码；
- 时间输入、Alpha Clip、透明相关宏与函数。

## Phase D：建立项目 Shader 样本语料

### D1. 枚举候选资产

枚举范围必须同时覆盖工程资产与包内资产，二者不得互为省略：

```text
Assets/          工程 Shader 与材质（owner: project）
Packages/        包内 Shader 与材质（owner: package）
```

候选扩展名：

```text
.shader
.hlsl
.cginc
.shadergraph
.mat
```

同时记录引用关系、同目录资产、材质引用的 Shader 和资产修改 revision。

按 `owner` 分区标注来源：`Assets/` 记为 `project`，`Packages/` 记为 `package`。不得只凭 [`Packages/manifest.json`](../../Packages/manifest.json:1) 中出现某包就推断其 Shader 可用；必须实际枚举到文件并按 revision 记录。

渲染管线包内的 Shader Library（例如 URP/HDRP Shader 目录）既是工程风格的对照基线，也是风格对齐类需求的样本来源；不纳入 `Packages/` 会导致样本语料与工程实际使用的关键字矩阵、`Attributes`/`Varyings` 约定脱节。

### D2. 提取每个 Shader 的结构卡片

```text
path
shaderName
sourceRevision
materialPropertySchema
keywords
renderStates
passes
lightModes
vertexProgramStyle
fragmentProgramStyle
attributesVaryingsStyle
includeUsage
lightingAndAlphaConventions
categoryTags
similarityTags
```

重点观察：

- `Properties` 的命名、类型、默认值与贴图槽；
- `Blend`、`ZWrite`、`ZTest`、`Cull`、Render Queue；
- Pass 和 `LightMode`；
- 顶点与片元入口、数据结构、CBuffer；
- 主光、阴影、GI、反射与颜色合成；
- Alpha Clip、透明与 ShadowCaster 的实现关系；
- 时间、溶解、Emission、Fresnel Rim 的数据和函数链。

### D3. 代表性样本选择

不要只选择一个看似相近的 Shader。按以下类别分别选择多个高相关样本，并保留缺失状态：

```text
pbrOpaque
alphaClip
transparent
alphaClipTransparent
dissolve
pulseEmission
fresnelRim
```

对每个类别记录：

```text
selectedExamples
selectionReason
coverage: sufficient | limited | absent
implementationPatterns
sourceLocations
confidence
```

本地无样本时，记录 `absent`；不得将外部通用代码伪装成项目先例。

## Phase E：归纳项目约定与能力目录

### E1. 项目约定

从多个样本中归纳，但每条结论都包含来源与置信度：

```text
propertyNaming
textureSlotNaming
cbufferLayout
attributesAndVaryingsStyle
vertexFragmentEntryStyle
includeStyle
precisionStyle
keywordStyle
alphaClipStyle
transparentStyle
materialAssetLayout
```

归纳规则：

- 多个相同模式且无冲突：`high`。
- 有少量样本支持、无足够覆盖：`medium` 或 `low`。
- 样本冲突、只有孤例或根本没有样本：`unknown`。

### E2. 能力目录

```text
capability
status: supported | unsupported | unknown
evidence
sourceLocations
confidence
fallbackPolicy
```

至少评估：

```text
worldPosition
worldNormal
viewDirection
tangentBasis
uvSampling
mainLight
additionalLights
shadowSampling
indirectDiffuse
indirectSpecular
reflectionProbe
brdfLut
timeInput
alphaClip
alphaClipShadowCaster
transparentAlphaBlend
transparentDepthWriteOff
```

`unsupported` 只能在工程明确不具备或明确禁止时使用；缺少证据一律为 `unknown`。

## Phase F：构建检索索引

建立从未来材质意图到工程证据的检索关系：

```text
queryTags
semanticFeatures
implementationPatterns
relatedShaderExamples
relatedLibraryFunctions
requiredCapabilities
```

例如：

```text
query: 透明加溶解边缘发光
  -> alpha_blend
  -> alpha_clip
  -> dissolve
  -> edge_emission
  -> transparent Alpha Blend 样本
  -> alpha clip 样本
  -> dissolve 样本
  -> Alpha Clip 和时间函数卡片
```

## Phase G：覆盖率校验、发布与报告

### G1. 最低 `fresh` 条件

| 分类 | 最低条件 |
|---|---|
| 环境 | Unity、Pipeline、RP Asset、Renderer、颜色空间有证据；无法确认的字段明确为 `unknown` |
| 基础 PBR | 有材质属性、世界法线、视线方向、主光和主 Pass 的证据或明确缺失 |
| Library | 有基础采样、坐标转换和光照相关函数卡片 |
| 样本 | 至少一个基础 PBR 样本；每个效果类别明确为 sufficient、limited 或 absent |
| Alpha | 不透明、Alpha Clip、透明分别具有 supported、unsupported 或 unknown 结论和证据 |
| 约定 | 每条已归纳约定均有来源与置信度 |
| 追溯 | 所有分区有 source revision/hash、Manifest 和构建日志 |

### G2. 发布规则

```text
全部最低条件满足
  -> 写入版本目录
  -> freshnessStatus = fresh
  -> 原子更新 current.json
  -> 返回 fresh

核心环境或基础 PBR 证据缺失
  -> freshnessStatus = partial 或 failed
  -> 不更新 current.json
  -> 返回原因和最小修复建议
```

### G3. 报告格式

```text
## Knowledge Base Build
- mode:
- result: fresh | partial | failed | blocked
- knowledge base version:
- manifest:

## Environment
- confirmed facts:
- unknowns:

## Library Index
- library families:
- function-card coverage:

## Project Shader Corpus
- candidate count:
- representative examples by category:
- absent categories:

## Conventions and Capabilities
- high-confidence conventions:
- unknown or conflicting conventions:
- capability summary:

## Evidence and Freshness
- fingerprints:
- refreshed partitions:
- source revisions:

## Decision
- publish status:
- caller may enter shader-generation chain: yes | no
- recommended next action:
```

## 增量刷新规则

- Unity、Render Pipeline 包、RP Asset、Renderer Data、颜色空间或渲染路径变化：刷新环境、Library 可用范围、能力目录及其依赖分区。
- Library 文件、项目 Shader/HLSL、材质属性约定或 Include 变化：仅重建受影响的 Library 卡片、样本、工程约定和检索条目。
- 新增或修改 Shader：更新该 Shader 结构卡片、样本分类、相关能力证据和检索关系。
- 无相关指纹变化：返回现有 `fresh` 知识库，不做全量扫描。
