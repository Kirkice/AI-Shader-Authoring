# 知识库扫描范围与分类确认协议

## 强制顺序

只读候选发现 → 展示分类确认单 → 用户调整 → 再展示完整确认单 → 用户明确确认 → 正式构建知识库 → 覆盖率检查与发布。

同意“创建知识库”不等于确认目录分类。未确认之前只允许只读枚举、读取 Shader 名称及为解释歧义所需的小范围源码/引用核验，不构建正式 corpus、函数卡片或能力目录，不发布版本、不更新 current.json。可保存明确标记 unconfirmed 的发现草稿，但它不是正式知识库且不能用于代码生成。

## 发现范围与方法

1. 获取真实工程根、Unity/包环境，枚举可访问的 Assets/ 与 Packages/ 中的 Shader、HLSL、CGInclude、ShaderGraph 及关联材质候选；包来源单独标记，不因 manifest 中声明包就假定文件存在。
2. 对目录名、文件名、ShaderLab 的 `Shader "..."` 名称做大小写不敏感的包含匹配。初始候选词：
   - vfx：Effect、Particle、Fx、VFX。
   - scene：Scene。
   - character：Character、Hero、Role。
   - shader_library：ShaderLibrary；结合 Include 引用关系提出其他共享库候选。
   词表是建议，可由用户补充工程词汇。短词 Fx 可能误匹配，多个分类可同时命中，不使用隐含优先级消除冲突。
3. 名称只用于召回，不证明功能或着色模型。功能推断须区分 name_hint 与 source_verified。不能仅凭名字断言支持溶解、Lightmap、PBR 或粒子 Custom Data。
4. 为每个候选目录列出实际文件数量、owner、命中字段/关键词和少量代表 Shader（通常 3–5 个或实际数量）。代表文件按不同子目录、名称特征选取，给出理由；不是只展示前几个重名文件。
5. 代表项必须是真实存在的完整工程路径 + 实际 Shader 名称。无法解析时标记 unknown，ShaderGraph 无法可靠读取 Shader 名称时不得编造。共享库展示真实 Include 路径和可核验引用例子，不要求每个目录都有 ShaderLab Shader。
6. 列出混合目录、重复命中、未分类目录及建议排除项。父子目录去重计数，分类可多标签，但查询时必须说明标签来源。排除项优先；父目录确认不自动覆盖未来新建子目录，记录当次目录清单快照。

## 用户确认单（全部使用实际扫描结果填充）

```text
## 知识库扫描分类待确认（发现版本 {discoveryVersion}）
- 工程：{真实工程根}
- 枚举范围：{Assets/、可访问 Packages/，不可访问部分及原因}
- 当前只做候选发现，尚未生成正式知识库。

### 特效 Shader
| 候选目录 | owner | 文件数 | 代表文件完整路径 / Shader 名称 | 分类依据 | 状态 |
| --- | --- | --- | --- | --- | --- |
| {真实路径} | project/package | {数量} | {实际代表项} | {命中字段及关键词} | 待确认 |

### 场景 Shader
{同上；没有候选时明确写“未发现，请指定目录或确认本类缺失”}

### 角色 Shader
{同上}

### ShaderLibrary 通用库
{同上，代表项改为真实库文件及 Include 证据}

### 歧义、共享、未分类与排除
{逐项列路径、原因和用户可选择的归属/混合/共享/排除方式}

请确认各分类路径与代表 Shader 是否正确。可增删目录、调整归属、指定参考样本或排除项；确认最终扫描范围后才开始正式构建知识库。
```

用户只确认部分时，不猜测其余项；提交收敛后的完整范围继续等待。无该类目录可由用户明确确认 absent，不要求为了建库虚构四类样本。混合目录需确认目录/文件级归属或明确保留 mixed；unknown 可保留但默认不能成为该类型的自动参考。

## 持久化契约

在 Manifest、domain-map.json 与 discovery-confirmation.json 关联记录：

```text
schemaVersion: domain_aware_v1
projectIdentity
scopeVersion
scopeFingerprint
candidateDirectorySnapshot
shaderDomainMap
  characterDirectories[]
  sceneDirectories[]
  vfxDirectories[]
  sharedLibraryDirectories[]
  mixedDirectories[]
  unknownDirectories[]
  excludedDirectories[]
  fileOverrides[]
  ownerAndPackageBoundaries
  classificationEvidence
  representativeShaders[]
userConfirmation
  status: confirmed
  discoveryVersion
  confirmedScopeFingerprint
  confirmedAt
  userDecisionEvidence
  correctionsAndExclusions
```

明确区分 directory_confirmation（用户确认用途与范围）和 source_evidence（源码核验功能）；两者不能互相替代。正式构建仅在 confirmedScopeFingerprint 与当前范围相符时继续。用户批准的 ShaderLibrary 外部依赖需要在范围中明确列为 dependency_library，不能借 Include 递归静默扩大扫描；发现未批准依赖时先列缺口与范围补充请求。

## 复用与增量确认

- 首次、旧知识库无目录确认契约或范围指纹不匹配：先发现与确认，不得凭旧 fresh 直接放行。
- 新目录、新包来源、范围扩大、目录移动/删除、代表分类改变、排除项变化：展示受影响项和完整合并后的范围，确认后刷新相关分区。
- 已确认目录清单内普通源码修改或新文件，且无分类歧义及新增目录：可按已有用户同意的刷新请求增量处理，不重复询问全部目录。新分类冲突仍需确认。
- 无相关变化且确认有效：复用已发布版本，无需再次全量扫描。
- 构建中范围变化：停止发布，返回 blocked 并重新确认；失败不得覆盖最后成功版本。
- 用户确认分类不证明知识库功能正确。后续仍须源码证据、覆盖率检查、任务级实时核验与 Review。
