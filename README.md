<div align="center">

# AI Shader Authoring

### Unity 智能材质创作与验证工具包

<sub>从自然语言意图到可编译、可验证、可追溯的 Shader 交付</sub>

<br />

<table>
<tr>
<td><img src="https://img.shields.io/badge/UNITY-PACKAGE-111827?style=for-the-badge&logo=unity&logoColor=white" alt="Unity Package" /></td>
<td><img src="https://img.shields.io/badge/PROJECT-AWARE-7C3AED?style=for-the-badge" alt="Project Aware" /></td>
<td><img src="https://img.shields.io/badge/EVIDENCE-DRIVEN-0891B2?style=for-the-badge" alt="Evidence Driven" /></td>
<td><img src="https://img.shields.io/badge/PIPELINE-NEUTRAL-059669?style=for-the-badge" alt="Pipeline Neutral" /></td>
</tr>
</table>

<br />

<sub>✦ Project Facts　·　◈ Semantic IR　·　✓ Controlled Delivery</sub>

</div>

---

> 💡 **产品定位**  
> 面向 Unity Shader 的 AI Shader Authoring 工具包。它将自然语言材质需求转化为 Shader、材质资产和验收证据，并在 Unity Editor 内提供受控 MCP 工具链、项目知识检索、视觉验证与性能分析能力；当前以 **URP PBR** 材质工作流作为首个完整落地与验证范式，可通过渲染管线适配扩展至 Built-in、HDRP 与自定义 SRP。

## 产品实机截图

<table>
<tr>
<td width="50%" valign="top">

<a href="screen%20shot/0823864f-8c95-45e2-9c25-ea72ed8137f6.jpeg">
  <img src="screen%20shot/0823864f-8c95-45e2-9c25-ea72ed8137f6.jpeg" alt="Unity 中的生成材质、结构化 Inspector 与 Unity MCP Dashboard" />
</a>

**Unity 内的真实验收闭环**  
生成材质、结构化 Inspector、性能摘要与 Unity MCP Dashboard 同屏呈现；可直接查看真实资产绑定、参数编排与工具连接状态。

</td>
<td width="50%" valign="top">

<a href="screen%20shot/e2b64747-b72d-453b-8685-2f671d2b81a2.jpeg">
  <img src="screen%20shot/e2b64747-b72d-453b-8685-2f671d2b81a2.jpeg" alt="Agent、Unity 与项目工作流协同界面" />
</a>

**Agent × Unity 协同工作流**  
从材质预览和 Inspector，到 MCP 工具实现与需求确认，展示自然语言需求进入受控 Shader 交付流程的实际工作界面。

</td>
</tr>
</table>

<sub>点击截图查看原图。示例来自本项目的 Unity 工程与实际工作流。</sub>

---

<div align="center">

**能力全景**　·　**协同流程**　·　**核心能力**　·　**配套能力**　·　**技术实现**　·　**交付体系**

</div>

---

# 1. ✦ 能力全景

<table>
<tr>
<td width="50%" valign="top">

## 创作

**Shader 生成**  
根据当前渲染管线与项目实现生成可编译 Shader，并输出可配置的 `Properties` 与材质资产。

**Inspector 编排**  
以 MarkupShaderGUI 将参数组织为语义化、可折叠的材质面板。

**知识库检索**  
从项目渲染环境、Shader Library 和既有样例中提取可复用的实现事实。

</td>
<td width="50%" valign="top">

## 验证

**编译与诊断**  
以 revision 保护写入，驱动 Unity 编译并回收 Console 与编译器诊断。

**视觉验收**  
在固定场景中绑定本轮真实生成材质，回读关键证据并采集截图。

**性能验收**  
结合静态预算、GLES3x 变体导出和 Mali Offline Compiler 进行策略评级。

</td>
</tr>
</table>

---

# 2. ◈ Skill 与 MCP 协同流程

> ✦ **从工程事实到可验证交付**  
> Skill 负责编排需求、语义与验收决策；Unity MCP 负责在 Editor 内受控地读取事实、写入资产、编译并采集证据。两者以明确的计划、授权与 revision 共同约束每一次交付。

<table>
<tr>
<td align="center" width="25%"><b>① 分类</b><br/><sub>Material Only</sub></td>
<td align="center" width="25%"><b>② 门禁</b><br/><sub>Knowledge Fresh</sub></td>
<td align="center" width="25%"><b>③ 授权</b><br/><sub>Plan Before Write</sub></td>
<td align="center" width="25%"><b>④ 证据</b><br/><sub>Verify Before Deliver</sub></td>
</tr>
</table>

## 完整执行树

```text
用户：描述材质需求
        │
        ▼
shader-authoring-agent（主编排）
        │
        ├─ Step 1：分类
        │    ├─ material → 继续
        │    ├─ vfx / post_process → 明确拒绝或转人工
        │    └─ ambiguous → 最小澄清后重试
        │
        ├─ Phase 0：知识库门禁
        │    ├─ 知识库 fresh → 记录版本，继续
        │    └─ 缺失 / 失效 → 征得用户同意
        │                         │
        │                         ▼
        │              shader-knowledge-base-builder（子 Skill）
        │                         ├─ 收集 Unity / Pipeline / Renderer 事实
        │                         ├─ 索引 Shader Library 与函数卡片
        │                         ├─ 分析 Assets 与 Packages 中的 Shader 样本
        │                         ├─ 提取项目风格、能力与证据
        │                         └─ 发布 fresh，或返回 partial / failed / blocked
        │                                      │
        │                                      └─ 仅 fresh 时回到主编排
        │
        ├─ Step 2：Material Intent
        ├─ Step 3：Rendering Semantic Graph
        ├─ Step 4：项目锚点与实时能力核验
        ├─ Step 5：Capability Resolver
        ├─ Step 6：Shader Code Plan
        ├─ Step 6.5：展示功能与属性确认单 → 必须取得明确编码授权
        │
        ├─ Step 7：受限生成、刷新和编译（MCP）
        │    ├─ Shader 编译 / Console 检查失败
        │    │    └─ 仅在白名单内修复，最多三轮
        │    └─ 首次通过且需要自定义 Inspector
        │                         │
        │                         ▼
        │              markup-shader-gui-authoring（子 Skill）
        │                         ├─ 读取完整目标 Shader
        │                         ├─ 只修改 Properties 内的注释标记
        │                         ├─ 仅按策略添加或处理 CustomEditor
        │                         ├─ 不改 HLSL、Pass、关键字、默认值、渲染状态
        │                         └─ 返回 Inspector 分组、开关、枚举与歧义信息
        │                                      │
        │                                      ▼
        │                         主 Skill 再次经 MCP 刷新、编译、读取诊断
        │
        └─ Step 8：静态 / 编译 / Console / Inspector / 画面验证
                     ├─ PASS → 固化证据与最终版本
                     ├─ REVISE → 返回 Code Plan，单阶段迭代
                     └─ BLOCKED → 输出缺失能力、资产或人工决策项
```

> **流程门禁**：只有项目知识库为 `fresh` 才进入生成链路；只有用户完成明确编码授权才允许写入；只有固定场景绑定本轮真实 `.mat` 才可作为视觉验收证据。

| 协同层 | 职责 | 关键约束 |
| :--- | :--- | :--- |
| **Skill** | 需求规范化、知识库门禁、语义建模、代码计划、验收编排 | 不把未知能力伪装为可用；生成前展示效果顺序与写入范围 |
| **MCP** | 读取项目事实、写入资产、刷新编译、执行 Job、采集诊断与验证证据 | 写入须携带 `codePlan`、`runId`、白名单与资产 revision |
| **Unity Editor** | 编译 Shader、绑定真实材质、渲染固定场景、输出平台与性能信息 | 真实资产、真实场景、真实平台输入共同决定结论 |

---

# 3. ◆ 核心能力

> ✦ **知识沉淀 → 语义建模 → 受控实现**  
> 系统先理解当前项目，再将需求表达为跨管线语义，最后在明确授权和证据约束下交付可追溯的 Shader。

<table>
<tr>
<td align="center" width="33%">

### 01　工程知识
**生成 · 检索 · 缓存**

</td>
<td align="center" width="33%">

### 02　通用语义
**Intent · Graph · Plan**

</td>
<td align="center" width="33%">

### 03　可信交付
**实现 · 验收 · 证据**

</td>
</tr>
</table>

## 3.1　项目专用 Shader 知识库

<sub>PROJECT FACTS · GENERATE / RETRIEVE / CACHE</sub>

每个材质任务先检查知识库版本、工程指纹与 `fresh` 状态。缺失或失效时，构建流程扫描当前渲染环境、Shader Library、Assets / Packages 样例与项目约定；只有事实缓存可用，才允许进入生成链路。

| 能力阶段 | 生成或检索的工程事实 | 缓存带来的决策价值 |
| :--- | :--- | :--- |
| **构建与刷新** | Render Pipeline、Renderer、Shader Library、函数卡片、能力目录、项目 Shader 样例与实现风格 | 将一次扫描沉淀为知识库版本与工程指纹，避免每轮从零猜测 |
| **任务门禁** | 检查是否存在、是否 `fresh`、指纹是否匹配 | 工程变化触发刷新；构建失败或证据不足时明确 **BLOCKED** |
| **按需检索** | 按 PBR、Alpha 路径与效果层查询相似 Shader、能力证据、工程约定和 Library 函数 | 优先复用项目事实；`unknown` 不会被误判为已支持 |

相关入口：[`Runtime/skills/shader-knowledge-base-builder/SKILL.md`](Runtime/skills/shader-knowledge-base-builder/SKILL.md) · [`Editor/Mcp/UnityMcpShaderTools.cs`](Editor/Mcp/UnityMcpShaderTools.cs)

## 3.2　自然语言 → 通用材质语义

<sub>SEMANTIC IR · MATERIAL INTENT / RENDERING SEMANTIC GRAPH</sub>

系统不会直接把需求翻译成某条管线的 HLSL。**Material Intent** 将“要做成什么材质”规范为机器真值；**Rendering Semantic Graph** 将“如何计算、依赖与合成”规范为可验证的数据流。两者不绑定 URP、HDRP、Built-in、具体 Include、函数名或 Unity API，是跨管线复用的核心。

| 语义层 | 保存的机器真值 | 通用性保证 |
| :--- | :--- | :--- |
| **Material Intent**<br/><sub>材质需求合同</sub> | 目标、表面参数、贴图语义与通道、Alpha、效果层、假设、约束与可见验收目标 | 将“金属、溶解、呼吸发光”等自然语言转换为与管线无关的材质定义 |
| **Rendering Semantic Graph**<br/><sub>计算语义真相</sub> | 输入 / 表面 / 光照 / 发光 / Alpha / 输出节点，及依赖、读写槽位、阶段、能力、回退与调试契约 | 以 `surface.baseColor`、`emission.additive` 等语义槽位替代变量名；多效果冲突必须显式组合 |
| **效果顺序确认** | 效果层声明 `modulationTargets` 与 `compositionOrder`；默认排序须在生成前展示确认 | 使 Dissolve、Pulse、Fresnel 的叠加规则可解释、可调整、可验证 |

```text
自然语言材质需求
        ↓
Material Intent                 · 需求合同
        ↓
Rendering Semantic Graph        · 计算 / 依赖 / 合成
        ↓
Shader Code Plan                · 当前项目与管线的落地映射
```

相关设计：[`Documentation~/semantic-intermediate-representation.xml`](Documentation~/semantic-intermediate-representation.xml) · [`Runtime/skills/shader-authoring-agent/SKILL.md`](Runtime/skills/shader-authoring-agent/SKILL.md)

## 3.3　Shader Code 实现与证据驱动验收

<sub>CONTROLLED DELIVERY · IMPLEMENT / VERIFY / PROVE</sub>

Capability Resolver 按当前项目管线、知识库与语义图生成 Shader Code Plan。仅在取得明确编码授权后，MCP 才能在白名单内写入资产、刷新编译、读取诊断并执行验证。生成不是终点：revision 绑定的真实材质、固定场景与性能结果共同决定最终结论。

| 执行环节 | 受控动作 | 证据与安全边界 |
| :--- | :--- | :--- |
| **Code Plan 与授权** | 映射属性、Pass、关键字、Shader Library、允许文件与验证目标；展示确认单 | 写入必须携带 `codePlan`、`runId` 与白名单路径；无授权或越界将被拒绝 |
| **受限生成与编译** | 写入 Shader / `.mat`，刷新 Unity，读取编译器与 Console；失败仅在白名单内修复 | 回传资产 revision、诊断、平台与时间信息，使代码与结论可追溯 |
| **视觉与性能验收** | 固定场景绑定本轮真实生成材质，回读属性/贴图并截图；执行静态预算、GLES3x 与 Mali 流程 | 复制旧材质后只替换 Shader 的证据无效；环境缺失时必须标记 **BLOCKED** |

<table>
<tr>
<td align="center" width="33%"><b>✓ PASS</b><br/><sub>证据完整，交付固化</sub></td>
<td align="center" width="33%"><b>↻ REVISE</b><br/><sub>回到单阶段定向迭代</sub></td>
<td align="center" width="33%"><b>! BLOCKED</b><br/><sub>缺失能力、环境或决策项</sub></td>
</tr>
</table>

> ❗ **不可接受的证据**  
> 固定验证场景 [`Tests/AI Shader Authoring.unity`](Tests/AI%20Shader%20Authoring.unity) 中的目标对象必须绑定本轮真实生成的 `.mat` 资产。复制旧材质后仅替换 Shader，不能证明真实参数与贴图生效，不能作为视觉验收结论依据。

---

# 4. ✦ 配套能力

> ✦ **让生成结果可编辑、可衡量、可落地**  
> 配套能力围绕材质编辑体验与性能风险控制展开：自动把参数组织成可读 Inspector，并以静态预算、GLES3x 编译产物和 Mali Offline Compiler 形成分层、可追溯的性能结论。

<table>
<tr>
<td align="center" width="33%"><b>01　Inspector 编排</b><br/><sub>解析 · 标注 · 安全回写</sub></td>
<td align="center" width="33%"><b>02　性能验收</b><br/><sub>预算 · 分级 · 归档</sub></td>
<td align="center" width="33%"><b>03　Mali 离线分析</b><br/><sub>GLES3x · 指标 · 证据</sub></td>
</tr>
</table>

## 4.1　ShaderGUI 自动标注注释生成

<sub>MARKUP SHADER GUI · STRUCTURE WITHOUT SEMANTIC DRIFT</sub>

**MarkupShaderGUI** 自动分析 Shader 的 `Properties` 区域，为材质参数生成可被 Inspector 识别的结构化标记：标题、折叠分组、开关与枚举。它让艺术家看到的是按工作流组织的参数面板，而不是无序属性列表。

| 处理阶段 | 自动化结果 | 安全边界 |
| :--- | :--- | :--- |
| **属性解析** | 识别纹理、颜色、数值、开关与枚举属性，并结合命名语义归入 Surface、Alpha、Emission、Effects 等编辑分区 | 读取目标 Shader 的真实属性定义，不依赖猜测或另建一份参数描述 |
| **标记生成** | 在 `Properties` 中写入分组、标题、开关和枚举注释；按策略应用 `CustomEditor` | 仅写注释标记与 Editor 声明，不改 HLSL、Pass、关键字、默认值或渲染状态 |
| **复编译验证** | 刷新 Unity 并读取编译诊断，确认结构化 Inspector 与 Shader 本体可共同工作 | 注释生成失败时可定向修复；材质渲染语义始终以原 Shader 为准 |

> ◈ **设计原则**：编排改善的是“如何编辑”，不改变“如何渲染”。因此 ShaderGUI 自动化是低风险增强步骤，而不是另一个会改写材质语义的生成器。

相关实现：[`Editor/MarkupShaderGUI`](Editor/MarkupShaderGUI) · [`Runtime/skills/markup-shader-gui-authoring/SKILL.md`](Runtime/skills/markup-shader-gui-authoring/SKILL.md)

## 4.2　性能验收维度与分层结论

<sub>PERFORMANCE ACCEPTANCE · FORECAST / EVIDENCE / DECISION</sub>

性能验收不以单一分数替代判断，而是把 Shader 的采样、分支、数学计算、透明路径和目标平台证据拆开评估。结果被写入报告与 Inspector 摘要，用于暴露风险、辅助取舍；它不会阻断视觉验收或正常交付链路。

| 验收维度 | 观察内容 | 结论用途 |
| :--- | :--- | :--- |
| **静态成本预测** | 纹理采样数、分支、循环、透明与 Alpha Clip、三角函数及高代价数学操作 | 在目标平台编译证据不可用时，仍提供可解释的基础预算与风险提示 |
| **变体与平台证据** | 按关键字与 Pass 区分变体；记录 Unity 平台、编译状态、GLES3x 导出状态与诊断 | 避免用桌面 HLSL 或错误平台产物替代移动端结论 |
| **预算评级** | 将预测值与项目策略、目标档位和设备预算比较，输出可读的评级、原因与建议 | 使“可用、需优化、证据不足”成为可追溯的产品决策，而非主观印象 |

<table>
<tr>
<td align="center" width="33%"><b>LOW</b><br/>预算健康<br/><sub>成本处于策略范围内</sub></td>
<td align="center" width="33%"><b>WATCH</b><br/>需要关注<br/><sub>风险已定位，可定向优化</sub></td>
<td align="center" width="33%"><b>EVIDENCE</b><br/>证据不足<br/><sub>不把缺失平台数据伪装为通过</sub></td>
</tr>
</table>

## 4.3　Mali Offline Compiler 离线编译性能验收

<sub>MALIOC · REAL GLES3X ARTIFACT · TARGET GPU METRICS</sub>

当 Unity 已产生目标平台的 GLES3x 编译产物、且提供有效的 **Mali Offline Compiler** 路径时，系统将真实 GLSL 变体交给 `malioc`，按指定 Mali GPU 分析 Vertex / Fragment 阶段指标，并将结果与静态预测并列归档。

| 验收步骤 | 真实离线编译证据 | 诚实性约束 |
| :--- | :--- | :--- |
| **GLES3x 导出** | 从 Unity 目标平台的已编译 Shader 中提取实际 GLES3x GLSL 变体、关键字与阶段源代码 | 仅接受新鲜、可验证的 GLES3x 产物；桌面平台输出不能冒充移动端编译证据 |
| **Mali 指标解析** | 调用 `malioc` 分析 Vertex / Fragment，读取周期、管线组件、寄存器、堆栈与编译诊断 | 分析目标、编译器路径、变体与原始输出进入报告，支持复核与横向比较 |
| **策略评估** | 以最重片元路径和目标 GPU 预算进行评级，并与纹理采样、分支等静态风险联合解释 | Android Build Support、GLES3x 产物或编译器任一缺失时明确标记 **BLOCKED** |

```text
Shader Source
    ↓  静态扫描
Static Forecast                 · 始终可用的基础风险判断
    ↓  Unity Android / GLES3x 编译产物
Compiled GLSL Variants          · 目标平台真实输入
    ↓  Mali Offline Compiler
Mali GPU Metrics                · 指定设备架构的离线性能证据
```

> ✓ **非阻断式验收**：具备条件时给出 Mali 目标 GPU 指标；条件缺失时准确说明阻断原因，并继续交付静态预测与视觉验收结果。

相关实现：[`Editor/Mcp/ShaderPerformanceAnalyzer.cs`](Editor/Mcp/ShaderPerformanceAnalyzer.cs) · [`Editor/Mcp/CompiledGlesVariantExporter.cs`](Editor/Mcp/CompiledGlesVariantExporter.cs)

---

# 5. ◈ 关键技术实现

## Unity Editor 模块

| 模块 | 职责 |
| :--- | :--- |
| [`UnityMcpConnection`](Editor/Mcp/UnityMcpConnection.cs) | 维护 WebSocket 连接、主线程消息分发、动态 C# 指令执行与 Editor 状态序列化 |
| [`UnityMcpShaderTools`](Editor/Mcp/UnityMcpShaderTools.cs) | 分发结构化 MCP 工具，管理异步 Job、生成资产授权、知识库构建、编译、验证与 Console 诊断 |
| [`CompiledGlesVariantExporter`](Editor/Mcp/CompiledGlesVariantExporter.cs) | 经 `UnityEditor.ShaderUtil` 导出 GLES 编译文本，并兼容 Unity 6 的 `OpenCompiledShader` 重载 |
| [`ShaderPerformanceAnalyzer`](Editor/Mcp/ShaderPerformanceAnalyzer.cs) | 结合静态启发式与 Mali Offline Compiler，输出策略预算、阶段指标与归档报告 |
| [`UnityMcpWindow`](Editor/Mcp/UnityMcpWindow.cs) | Dashboard 呈现 MCP 工具、运行状态、参数说明与授权标识 |

## Skill 工作流

| Skill | 职责 |
| :--- | :--- |
| [`shader-authoring-agent`](Runtime/skills/shader-authoring-agent/SKILL.md) | 需求规范化、Shader 生成、真实材质绑定、固定场景验收与证据归档 |
| [`shader-knowledge-base-builder`](Runtime/skills/shader-knowledge-base-builder/SKILL.md) | 项目 Shader 知识库的构建、刷新、验证与查询 |
| [`markup-shader-gui-authoring`](Runtime/skills/markup-shader-gui-authoring/SKILL.md) | `Properties` 标记分析与 MarkupShaderGUI 的安全写入 |
| [`shader-performance-acceptance`](Runtime/skills/shader-performance-acceptance/SKILL.md) | 静态预算、GLES3x 导出、Mali 分析、结果归档与 Inspector 预警 |

## MCP Bridge

```text
MCP Client   ⇄   stdio Server   ⇄   WebSocket   ⇄   Unity Editor
```

> 📌 **写入安全边界**：生成资产限定在 `Assets/AIShader/Generated`。每次写入需携带 `codePlan`、允许文件列表和 `runId`。Unity 侧负责结构性授权校验，Agent / LLM 侧负责授权发起与流程编排。

相关说明：[`Documentation~/MCP_BRIDGE.md`](Documentation~/MCP_BRIDGE.md)

---

# 6. ✓ 验证与交付体系

## 固定场景视觉验收

视觉验收固定使用 [`Tests/AI Shader Authoring.unity`](Tests/AI%20Shader%20Authoring.unity) 与 `AIShader_Sphere`。验收必须绑定本轮真实生成的材质资产，并回读材质路径、实例、Shader、revision、槽位及效果相关属性 / 贴图快照后再截图。

## 交付证据

每次运行可关联生成资产 revision、代码计划、编译诊断、验证会话、截图和性能报告。验收结论明确区分 **PASS**、**REVISE** 与 **BLOCKED**，确保交付结论与实际证据一致。

---

# 7. ⟐ 工程边界

首期聚焦 Unity **材质 Shader**；复杂特效与后处理不在自动生成范围内。框架不锁定 URP，但 URP 是当前已完成端到端生成与验证的首个成熟范式；Built-in、HDRP 与自定义 SRP 需要依据各自的 Shader Library、材质接口和编译结果补充适配策略。

视觉验收仅证明已绑定真实材质及其已赋值资源：若 Cubemap、噪声贴图或 Mask 尚未赋值，对应艺术效果需在补齐资源后重新验证。真实 Android / Mali 数据受 Android Build Support 与目标平台编译产物可用性约束。

---

<div align="center">
<br />

**AI Shader Authoring · Unity Shader · Evidence-driven Delivery**

</div>
