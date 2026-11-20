# 仓库参考驱动的 Shader 编写路线

## 适用范围与路线边界

主 Skill 支持角色、场景和特效 Shader 的编写；用途不是光照模型。默认使用本路线，不强制转换为 PBR。只有用户明确要求 PBR、物理材质或明确同意采用 PBR 时，进入主文档 Step 2–5 的 MaterialIntent / RenderingSemanticGraph 专项路线；含糊的“金属”“受光”不能自动当作明确选择，应最小澄清。

特效 Shader 编写不等于授权搭建粒子系统、游戏逻辑、模拟或完整 VFX 资产；若这些配置是依赖，记录所需输入并请求用户授权/提供。后处理仍不在本次支持范围。

## R1. 类型确认与检索

1. 确保知识库 fresh 且目录分类经过用户确认。用户已明确“特效/场景/角色”时记录原文，无需重复询问；未明确则询问类型，可同时询问参考 Shader 与目标目录。PBR 用户也需确定用途类型或明确允许跨分类搜索。
2. 创建 ShaderTaskIntent：taskId、originalRequirement、domain（character/scene/vfx/明确的 mixed）、carrier、route（reference/pbr）、targetDirectory、providedAssets、requestedFeatures、referencePreferences、unknowns、userDomainConfirmation。
3. 在已确认对应分类内检索名称、功能标签、实现模式和关联材质；优先当前工程、当前管线与实际载体兼容的样本，包内资产作为明确标记的候选。通用库依适用性补充，不混淆库与材质模板。
4. 展示少量候选的路径、Shader 名称、功能证据、匹配点、缺口及选择理由，在编码前的功能确认中让用户确认最终参考。跨分类参考必须显式说明理由与差异，并获用户确认；没有匹配项时列缺口并澄清，不擅自回退角色 PBR。
5. 任务内不全仓库重扫，不静默改变目录分类；新发现记录增量请求。

## R2. 实时核验与实现契约

完整读取候选源文件及必要 Include，通过 Unity 状态、资产结构与 revision 核验实际接口。名称仅用于发现，功能必须读源码确认。

```text
ReferenceShaderProfile
  knowledgeBaseVersion
  confirmedScopeVersion
  taskIntent
  selectedBaseline: path, shaderName, revision, owner, domain, selectionEvidence
  sourceExamples
  materialPropertiesAndTextureChannels
  keywordsAndIncludes
  passesAndRenderStates
  vertexInputsAndVaryings
  vertexAndFragmentAnchors
  lightingAndColorComposition
  alphaOutputContract
  customEditorAndGuiContract
  requiredCapabilities
  verifiedCapabilities
  missingAssets
  unknownsAndConflicts
  validationRequirements
```

能力要求由实际需求和基线推导：不受光特效不要求主光、BRDF、世界法线；烘焙材质依其实现核验 UV/Lightmap 采样解码；粒子依其实现核验顶点色、顶点流与 Custom Data。未用能力标记 not_required，不能伪称降级。任何模型都不凭记忆编造项目接口。

## R3. 参考派生计划与共享流程接入

建立 ReferenceShaderPlan：intent、baselineRevision、保留契约、requestedChanges、allowedFiles、forbiddenFiles、anchors、plannedChanges、dependencies、renderState、invariants、generatedMaterialProperties、shaderGuiAuthoring、validationPlan、rollbackPlan，以及 ReferenceShaderParityManifest。

- 参考 Shader 确实存在的属性、通道、关键字、工作流、Pass、颜色/Alpha 契约和迁移语义完整保留；未存在的 PBR 输入不添加。
- 每个差异列出需求来源、精确修改锚点与风险。删除或替换已有能力必须用户明确批准，不得静默精简。
- 输出契约整体记录 RGB/Alpha 含义、是否预乘、Blend、ZWrite/ZTest/Cull、Queue 与裁剪语义，不能把全部特效改成固定 SrcAlpha 混合。
- 已知历史错误按 Review 处理：不能因参考继承而复制到新文件，也不能回头修改白名单原件。契约与规则冲突先报告并等待方案确认。
- 本计划映射到主文档 ShaderCodePlan 的通用字段后，接入 Step 6.4 → 6.5 → 7 → 7.5 → 7.6 → 8。Step 6 中单阶段、白名单、revision、回滚、GUI 约束继续有效。
- Step 6.5 的确认来源在本路线为 ShaderTaskIntent、ReferenceShaderProfile 与 ReferenceShaderPlan，而非虚构 MaterialIntent/RenderingSemanticGraph。展示所选参考、功能和属性表，全部确认后编码。
- GUI 委派使用实际 Properties/Keywords；原 materialIntent 字段传 taskIntent 并标注 intentKind=reference，renderingSemanticGraph 标记 not_applicable，不构造伪 PBR 图。若真实工具契约不支持需报告并阻断该自动化步骤，不能虚称已调用。
- 输出报告用 Reference Profile / Reference Plan 替代 PBR 专属 Intent/Graph 小节，其余授权、编译、Review、证据和用户验收记录不变。

## R4. 按基线与载体验证

主文档 Step 8 的静态/编译/Console/适用 GUI/Review 用户确认门禁全部保留。PBR 专属 Stage 0–10、固定 Alpha Blend 和固定 Sphere 夹具不强加给非 PBR 路线。

1. 按实际功能制定验证矩阵：基线对照、参数低/高响应、颜色/混合/裁剪、UV/时间动画，以及所需的 Lightmap、顶点色、粒子流或其他能力。没有使用的项明确 not_required。
2. 现有 MCP 的固定 Sphere + Test.mat 只在确实能覆盖所需输入时使用。不能假定传入自定义 scenePath 能改变工具固定行为，也不能把球体颜色响应视为粒子、蒙皮或 Lightmap 验证通过。
3. 不足时列出所需验证场景、相机、Renderer/ParticleSystem、输入与材质夹具，向用户确认已存在的专用夹具和可用工具支持。不得自动改写原工具或擅自创建/保存场景、材质资产；没有安全工具与合适夹具则 BLOCKED，并说明源码已完成但对应运行验收未完成。
4. 所有捕获必须验证真实目标可见、目标材质绑定生效、目标参数/区域像素有响应，并人工复核；背景变化不能代替目标变化。每次记录 Shader revision、测试参数、截图与结论。
5. 任何临时状态必须快照且在成功、失败、取消时恢复，不保存测试材质或场景；保持原禁止创建验证专用 .mat 的限制。
6. 编译错误最多三轮修复、Review 用户批准修复后复审、最新版本确认进入验收及最终用户接受全部照常执行。任何修复写入都使旧 Review 接受失效。

本路线不是新增粒子运行时、烘焙工具或验证工具的实现。技能可指导相应 Shader 编写，但证据或工具缺失时必须如实 BLOCKED，不能用范围扩大宣称所有效果均可验收。
