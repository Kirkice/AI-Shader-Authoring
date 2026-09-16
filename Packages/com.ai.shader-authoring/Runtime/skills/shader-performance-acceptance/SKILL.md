---
name: shader-performance-acceptance
description: 对 Unity 材质 Shader 执行非阻断性能验收：收集用户性能策略与 Mali Offline Compiler 路径，通过 Unity 编译导出 GLES GLSL 变体，再输出静态/Mali 指标、预算评级、归档和 Inspector 预警；绝不阻断后续视觉验收。
---

# Shader Performance Acceptance

## 使命

为已生成且通过 Unity 编译门禁的材质 Shader 生成可审计、可复现的性能评价。分析结果仅用于预警、优化排序和 Inspector 展示，绝不能决定视觉验收是否执行或是否通过。

本 Skill 只处理性能验收，不修改 Shader 源码、材质、场景或视觉验收结论。Shader 优化改动必须由主编排 Skill 基于本报告另行规划、写入、编译并重新分析。

## 输入契约

调用方必须提供：

1. `shaderPath`：现有 `Assets/*.shader` 项目相对路径。
2. `runId`：当前 Shader 生成/验收运行的唯一标识。
3. `policy`：以下之一：
   - `low_android`：Mali-G31。
   - `medium_android`：Mali-G52。
   - `high_android`：Mali-G78。
   - `all_android`：Mali-G31、Mali-G52、Mali-G78。
   - `custom`：用户自定义预算。
   - `collect_only`：只采集，不评级。
4. 可选提供 `maliTargets`、当前材质关键字和用于测试的外部 GLES GLSL 变体。

正常链路由 `export_compiled_gles_variants` 从 `shaderPath` 调用 Unity 编译器，指定 GLES3x 平台并导出真实顶点/片元 GLSL；调用方不得把 ShaderLab/HLSL 源码交给 `malioc`。仅在诊断或回归测试中，才允许显式提供外部变体覆盖导出结果。

外部编译 GLES 变体格式：

```json
{
  "compiledGlesVariants": [
    {
      "name": "默认变体",
      "keywords": ["_ALPHATEST_ON"],
      "vertexGlsl": "实际导出的 GLES 顶点 GLSL",
      "fragmentGlsl": "实际导出的 GLES 片元 GLSL"
    }
  ]
}
```

`vertexGlsl` / `fragmentGlsl` 的单一顶层输入仅用于兼容旧调用。禁止将 ShaderLab 或 HLSL 源码伪装为 GLES GLSL。

## 用户交互：策略和 Mali 编译器

首次性能验收时，先要求用户选择性能策略；不得擅自猜测目标设备等级。

若选择 `custom`，必须收集：

- `maxFragmentEstimatedCost`
- `maxTextureSamples`
- `maxBranches`

可选收集：

- `maxLongestPathCycles`
- `maxWorkRegisters`

Mali Offline Compiler 路径由用户或已确认的项目配置显式提供为 `maliCompilerPath`。不得扫描全盘、猜测安装目录或将环境变量值视为已授权路径。路径必须指向可执行的 `malioc` 文件；无效或缺失时继续静态分析，并报告“未配置”。

## 执行流程

1. 确认 Shader 已通过本轮 Unity 编译与 Console 门禁；编译失败时返回主 Skill 处理，不产生基于失败源码的 Mali 结论。
2. 调用 `export_compiled_gles_variants` 异步 Job，并以 `shaderPath` 导出 Unity GLES3x 编译产物。若该 Job 返回 `failed` 或零变体，只记录导出诊断，继续静态分析和后续视觉验收。
3. 将导出的 `compiledGlesVariants` 原样传给 `analyze_shader_performance`；也可直接调用后者，由它在没有显式变体时自动执行同一 Unity 导出步骤。
4. 静态预估必须覆盖：纹理采样、高代价数学、动态控制流、透明/裁剪、视差/步进和关键字指令数量，并给出代码证据。
5. 如同时具备有效 `maliCompilerPath` 和真实已编译 GLES GLSL：按每个变体、每个目标 GPU、每个 Vertex/Fragment Stage 调用 `malioc`。
6. 解析并保留：工作/Uniform 寄存器、Stack 大小与 spilling、16 位算术比例、总/最短/最长路径的 Arithmetic/LoadStore/Varying/Texture 周期及 bottleneck。
7. 写入 revision 绑定的当前摘要，并将同一 JSON 归档至 `Artifacts/ShaderRuns/<runId>/performance/`。
8. 读取报告，形成供主 Skill 使用的性能结论和优化建议；不得直接改动 Shader。

## 评级规则

1. `collect_only` 固定评级为“未评级”。
2. 静态片元估算、纹理采样或分支超过预算：预警；超过预算 150%：高风险。
3. 若设置 `maxLongestPathCycles`：对每个成功的 Mali Stage 使用最长路径的最大周期分量；超过预算为预警，超过 150% 为高风险。
4. 若设置 `maxWorkRegisters`：超过预算为预警，超过 150% 为高风险。
5. 任意 `Stack spilling` 为高风险。
6. `malioc` 未配置、Unity GLES 导出不可用、GLES 变体不可用或单次 Mali 失败必须记录分析可用性/失败信息；它们绝不能阻断视觉验收。

## 输出与交接

返回以下内容：

- Shader 路径和 SHA-256 revision。
- 所选策略、实际生效预算和 Mali 目标。
- 静态指标和高成本模块。
- Mali 状态、逐 Stage 结果、`maliBudgetAssessment` 与违规列表。
- 摘要路径和运行归档路径。
- 结论：`信息`、`注意`、`预警`、`高风险` 或 `未评级`。
- 明确说明“性能结论仅预警，不会阻断视觉验收”。

Inspector 只显示与当前 Shader revision 完全相同的摘要；详情折叠区显示变体、GPU、阶段、状态、工作寄存器、最长路径周期和 Stack spilling 状态。不得展示旧 revision 结果。

主 Skill 收到结果后，无论评级为何，必须继续固定场景截图与 LLM 视觉验收；性能风险只作为后续 Shader 优化循环的输入。

## 验收标准

- `malioc` 路径未提供时，静态报告仍能写入，状态为“未配置”，视觉验收可继续。
- Unity 导出的真实 GLES 变体和有效 `malioc` 路径存在时，报告包含逐变体/目标/阶段的 Mali 结构化结果。
- 自定义最长路径和工作寄存器预算被写入报告并参与评级。
- Stack spilling 产生“高风险”预警，但不阻断视觉验收。
- 当前摘要与运行归档都存在，且摘要 revision 与当前 Shader 内容哈希一致。
- Inspector 不显示 revision 不匹配的旧性能结果。
