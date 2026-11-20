
# Unified ShaderGUI Authoring

## 使命

根据 Shader 已有的 `Properties`、关键字、属性用途和用户意图，维护项目 `UnifiedShaderGUI` 使用的注释与受支持 ShaderLab attributes，使材质 Inspector 获得结构化分组、开关、枚举、VectorSplit、Ramp、DataTip、混合模式预设和组复制/粘贴能力。

默认只允许修改：

- `Properties` 块中的 `// # ...` 元数据；
- 已有 Property 前的受支持 ShaderLab attribute；
- Shader 顶层的 `CustomEditor "UnifiedShaderGUI.UnifiedShaderGUI"` 声明。

除非用户明确要求，不得修改 Pass、HLSL、渲染算法、属性名称、类型、默认值、关键字声明、Blend、ZWrite、Cull 或其他渲染状态。

## 适用与排除范围

适用于为现有 Shader 添加、整理或修复 UnifiedShaderGUI 元数据、接入或恢复 Unified CustomEditor，以及验证 Inspector 解析和绘制行为。

不适用于新增渲染功能、虚构 Property/Keyword、修改 `UnifiedGUI` 或 `CodeGen` 的 C# 实现，或决定特效和后处理表现本身。

## 权威实现依据

生成与校验必须服从项目实际实现；本文与实现冲突时以实现为准：

- 元数据解析：[`ShaderParser.ParseShaderProperties()`](../../../../../../Assets/Editor/Theseus/ShaderGUI/CodeGen/ShaderParser.cs:10)
- 元数据模型：[`ShaderDataStructures.cs`](../../../../../../Assets/Editor/Theseus/ShaderGUI/CodeGen/ShaderDataStructures.cs)
- Inspector 入口：[`UnifiedShaderGUI.OnGUI()`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUI.cs:52)
- 缓存与动态解析：[`UnifiedShaderGUIParser.GetOrParse()`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUIParser.cs:17)
- VectorSplit：[`UnifiedShaderGUIVectorSplit`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUIVectorSplit.cs)
- Ramp：[`UnifiedShaderGUIRamp`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUIRamp.cs)
- BlendPreset：[`UnifiedShaderGUICommonBlendModePreset`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUICommonBlendModePreset.cs)
- CustomEditor 安全写入与恢复：[`UnifiedShaderGUISetup`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUISetup.cs:10)
- 缓存失效范围：[`UnifiedShaderGUIAssetPostprocessor`](../../../../../../Assets/Editor/Theseus/ShaderGUI/UnifiedGUI/UnifiedShaderGUICache.cs:8)

## 不可违反的规则

1. 修改前完整读取目标 Shader，识别 `Properties`、现有元数据、attributes、顶层 `CustomEditor`、关键字声明和属性实际使用位置。
2. `// # ...` 元数据只能位于 `Properties` 块；不得写入 SubShader、Pass 或 HLSL。
3. 默认保留属性名称、显示名、类型、默认值、顺序、已有注释及关键字拼写。
4. Uniform、Enum、VectorSplit、Ramp、BlendPreset 必须绑定真实且类型兼容的 Property；Keyword Toggle 必须有真实 `shader_feature`、`multi_compile` 或使用证据。
5. 一个属性不得被普通 Property、Enum、Toggle 或 VectorSplit 重复接管；VectorSplit 中同一通道不得重复接管。
6. `[HideInInspector]` 属性不得作为普通可见控件加入分组；只有受其他控件驱动时才可保留，并说明依据。
7. 禁止新增 `[Toggle]`、`[ToggleOff]`、`[KeywordEnum(...)]`；解析器会将其判为不兼容并让 Inspector 回退默认绘制。
8. 不同 Shader 的材质不得进行 Unified 自定义多选编辑；同 Shader 多选必须保留 Mixed Value 与 Undo 语义。
9. 不得静默覆盖未知业务 `CustomEditor`。接入和恢复都会修改源码、不能 Ctrl+Z，必须确认并使用 UnifiedGUI 的备份/恢复机制。
10. 解析或编译失败时必须保留证据，只最小化修复元数据；不得删除 Property 规避问题。

## 读取与设计流程

### 阶段 1：建立计划

```text
UnifiedGUIPlan
  shaderPath
  shaderName
  existingCustomEditor
  inAutoInvalidationScope
  featureDescription
  warnings[]
  properties[]: name, displayName, type, attributes, defaultValue, hidden, semantic, assignedControl
  keywords[]: name, declarationEvidence, usageEvidence
  groups[]: displayName, id, label, groupToggle, properties, toggles, enums, vectorSplits
  rampProperties[]
  blendPresetProperties[]
  dataTipProperties[]
  unsupportedOrAmbiguousItems[]
```

语义判断顺序为：用户明确指定、已有注释和代码使用、属性/关键字命名与常见 PBR 语义、最后归入“其他参数”并报告歧义。布局应优先按基础设置、表面/法线/遮蔽、自发光、透明/裁剪/混合、独立效果和其他参数组织；不要为单个普通数值创建无意义分组。

### 阶段 2：最小差异写入

只改目标元数据行，不重排完整 Shader，也不得改变编码、BOM 或换行。接入 CustomEditor 时优先使用 Unity 菜单 `Assets/Use Unified ShaderGUI`，而不是用正则替换嵌套 Shader 文本。

### 阶段 3：静态检查、编译与验收

先完成本 Skill 的静态清单，再刷新 Unity 并读取目标 Shader 的编译和 Console 诊断。若 Unity Provider/MCP 可用，必须等待编译完成；若不能运行 Unity，只能报告“已完成静态校验”。

## UnifiedGUI 元数据语法

### FeatureDes、Group 与 Label

```shader
// # FeatureDes:支持金属度工作流与可开关的菲涅尔边缘光。
// # GroupStart:基础设置_BaseSettings
// # Label:表面参数
_BaseMap("基础贴图", 2D) = "white" {}
// # GroupEnd
```

- `FeatureDes` 可选；缺失时仍会接管 Inspector，但显示提示。
- Group 格式为 `// # GroupStart:显示名_GroupId`；解析器按第一个 `_` 切分，两个部分均不能为空，GroupId 在同一 Shader 内必须唯一。
- 每个 GroupStart 必须对应 GroupEnd，禁止嵌套。
- Label、Toggle、Enum、VectorSplit 必须在组内；每个组只有一个 Label 字段，需要多个区段时拆分组。

### Toggle

```shader
// # Toggle:Uniform:启用菲涅尔:_FresnelEnabled
_FresnelEnabled("启用菲涅尔", Float) = 1

// # Toggle:KeyWords:启用菲涅尔:_FRESNEL_ON

// # Toggle:GroupUniform:启用边缘光:_FresnelEnabled
// # Toggle:GroupKeyWords:启用边缘光:_FRESNEL_ON
```

`Uniform` 与 `GroupUniform` 绑定真实 Float、Range 或 Int；`KeyWords` 与 `GroupKeyWords` 绑定真实关键字。组级开关关闭后保留组内容但禁用绘制，不得删除布局。

### Enum

```shader
// # Enum_SurfaceMode:表面模式:_SurfaceMode:不透明=0|裁剪=1|透明=2
_SurfaceMode("表面模式", Float) = 0
```

格式为 `// # Enum_英文标识:显示名:Property名:选项列表`。未显式给值时依次使用 `0、1、2...`；允许显式数值。目标 Property 必须为可数值写回的 Float、Range 或 Int。

### VectorSplit

```shader
// # VectorSplit:_Params.x:Range(0,1):强度!_Params.y:Float:速度!Toggle:Uniform:启用:_Params.z!Enum_Mode:模式:_Params.w:关闭=0|开启=1
_Params("参数", Vector) = (0,0,0,0)
```

VectorSplit 必须在组内，子项以 `!` 分隔并保留其原始顺序。普通项支持 `Int`、`Float`、`Range(min,max)`、`Vector2`、`Vector3`；通道只能为 `x/y/z/w`，并且类型的通道数量必须匹配。子项 Toggle 仅支持 `Uniform`。所有引用必须为真实 Vector Property，写回只能覆盖声明通道。

### Ramp、BlendPreset 与 DataTip

```shader
[Ramp] _ColorRamp("颜色渐变", 2D) = "white" {}
[CommonBlendModePreset] _BlendPreset("混合模式", Float) = 0
[CustomDataTip] _CustomData("自定义数据@tip:X：强度；Y：速度；Z：遮罩；W：预留", Vector) = (0,0,0,0)
```

- `[Ramp]` 只用于可见 `2D` Property；渐变由 UnifiedGUI 烘焙为唯一 PNG，不得覆盖已有 PNG。
- `[CommonBlendModePreset]` 需要真实 `_SrcBlend` 和 `_DstBlend`；不得因此新增或重命名 Blend Property。支持 Blend、Add、正片叠底、滤色；旧 Add `(One, One)` 只能由用户明确迁移。
- 新写入统一使用 `[CustomDataTip]`；可兼容旧拼写 `[CustumDataTip]`。提示分隔符为区分大小写的 `@tip:`。
- 可保留 `[NoScaleOffset]`、`[HideInInspector]` 等不冲突属性，但仍以实际实现为准。

## CustomEditor 接入与缓存

最终目标 Shader 顶层只能保留一条：

```shader
CustomEditor "UnifiedShaderGUI.UnifiedShaderGUI"
```

该声明必须位于 Shader 最外层结束花括号之前，不得出现在 SubShader、Pass 或 HLSL 内。`Assets/Use Unified ShaderGUI` 会备份原始字节至 `Library/UnifiedShaderGUIBackups` 并保留编码、BOM 与换行；恢复使用 `Assets/Restore Unified ShaderGUI Backup`，恢复前也会先备份当前文件。

自动缓存失效仅覆盖位于 `Assets/Shaders/Theseus/`、文件名以 `Theseus_` 开头、扩展名为 `.shader` 的文件。范围外 Shader 修改元数据后，必须通过重新打开 Unity、显式失效或重载来验证，不能依赖 Postprocessor。

## 静态校验清单

- [ ] 保留原 Property 名称、类型、默认值和顺序，除非用户明确授权修改。
- [ ] 全部 `// # ...` 元数据位于 `Properties`。
- [ ] GroupStart/GroupEnd 成对、无嵌套，显示名与 GroupId 非空且 GroupId 唯一。
- [ ] Label、Toggle、Enum、VectorSplit 均位于 Group 内。
- [ ] Uniform、GroupUniform、Enum 指向真实兼容 Property；关键字 Toggle 有真实证据。
- [ ] 一个属性或 Vector 通道未被多种控件重复接管。
- [ ] Ramp 只用于可见 2D；BlendPreset 具备 `_SrcBlend`/`_DstBlend`；DataTip 使用 `@tip:`。
- [ ] 不含 `[Toggle]`、`[ToggleOff]`、`[KeywordEnum]`。
- [ ] 仅有一条顶层 Unified CustomEditor，或已记录未知业务 CustomEditor 冲突。

## Inspector 验收

刷新并编译后，打开使用目标 Shader 的 Material，确认：

1. Inspector 被 UnifiedShaderGUI 接管，FeatureDes 或缺失提示正确；
2. 分组折叠、普通 Property、Enum、Toggle、组级 Toggle 与 Render Queue 正确；
3. VectorSplit 顺序正确且只写声明通道；
4. Ramp、CustomDataTip 和 CommonBlendModePreset 在适用时符合实际行为；
5. 同 Shader 多材质保持 Mixed Value、Undo 和关键字数量预检；
6. 不同 Shader 多选显示警告并安全回退默认 GUI；
7. 组复制/粘贴不能跨 Shader，并在应用前进行关键字数量预检。

## 输出规范

完成后必须报告目标 Shader 路径和名称、修改的 Group/Label/Toggle/Enum/VectorSplit、Ramp/DataTip/BlendPreset 绑定、CustomEditor 处理与备份位置、未处理歧义、静态检查结果，以及 Unity 编译、Console 和 Inspector 验收结果；未执行的验证必须明确标注。
