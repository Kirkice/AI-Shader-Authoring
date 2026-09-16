---
name: markup-shader-gui-authoring
description: 为 Unity Shader 的 Properties 块自动分析、设计、生成、修复和验证 MarkupShaderGUI 注释标记；将材质属性组织为可折叠分组、标题、开关与枚举，并安全写入 CustomEditor 声明。
---

# Markup ShaderGUI Authoring

## 使命

根据 Shader 的已有 `Properties` 属性、关键字、材质语义和用户意图，自动生成与维护 `MarkupShaderGUI` 的注释标记，使 Unity Inspector 获得结构化、可折叠、可复制分组的编辑界面。

本 Skill 只负责 Shader 的编辑器注释布局及 `CustomEditor "MarkupShaderGUI.MarkupShaderGUI"` 声明；不改变渲染算法、Pass、HLSL、属性默认值、关键字声明或渲染状态，除非用户明确要求其同步变更。

## 适用场景

- 用户要求为 Shader 添加、生成、整理或修复 MarkupShaderGUI 标记。
- 用户希望 AI 根据 PBR Shader 属性自动生成 Inspector 分组。
- 用户要求把旧式/无结构的 Properties 块转换为 MarkupShaderGUI。
- 用户要求检查现有标记是否能被 `MarkupShaderGUI` 正确解析。
- 用户要求补充 Shader 的 `CustomEditor` 声明。

## 不适用场景

- 修改 Shader 的光照模型、Pass、HLSL、混合模式、关键字矩阵或贴图采样逻辑。
- 新增未在 Shader 中声明的材质属性。
- 修改 `Assets/MarkupShaderGUI` 的解析器或 GUI 实现；此类需求应转为 GUI 功能开发任务。

## 权威实现依据

生成和校验时，必须以项目实现为准，不得按记忆臆测：

- 语法常量：[`ShaderMarkupConstants`](../../Assets/MarkupShaderGUI/Parsing/ShaderMarkupConstants.cs:10)。
- 解析与失败条件：[`ShaderMarkupParser.Parse()`](../../Assets/MarkupShaderGUI/Parsing/ShaderMarkupParser.cs:19)。
- Inspector 实际绘制行为：[`MarkupShaderGUI.OnGUI()`](../../Assets/MarkupShaderGUI/Gui/MarkupShaderGUI.cs:24)。
- 注释到数据模型的映射：[`ShaderMarkupModel`](../../Assets/MarkupShaderGUI/Parsing/ShaderMarkupModel.cs:20)。
- 接管资格判断：[`MarkupShaderGUIDataFactory.GetOrParse()`](../../Assets/MarkupShaderGUI/Gui/MarkupShaderGUIDataFactory.cs:16)。
- `CustomEditor` 写入规则：[`MarkupShaderGUISetup`](../../Assets/MarkupShaderGUI/Gui/MarkupShaderGUISetup.cs:12)。

如果上述实现与本文档存在冲突，始终以实现为准，并更新本 Skill。

## 不可违反的规则

1. 修改前必须完整读取目标 Shader；先识别 `Properties` 块、已有注释标记、`CustomEditor`、`#pragma shader_feature`/`multi_compile` 以及属性使用位置。
2. 只在 `Properties` 块中写入标记；不得把 `// # ...` 标记写入 HLSL、Pass 或 SubShader 其他位置。
3. 除非用户要求，保留既有属性名称、显示名、类型、默认值、属性顺序、关键字名与注释内容。
4. 只为真实存在于 `Properties` 块中的属性生成属性、开关或枚举 UI；不得虚构 `_Property` 或关键字。
5. 必须保持一个属性最多由一个组内声明、一个枚举或一个开关控制，避免 Inspector 上重复绘制。
6. 未被分组的属性会由 GUI 默认绘制；但只要目标是结构化 Inspector，应优先将用户可编辑属性归入合理分组。
7. `FeatureDes` 是可选的。缺少它不得阻止 GUI 接管；存在时仅显示顶部 Feature 说明。
8. 不得生成 `[Toggle]`、`[ToggleOff]` 或 `[KeywordEnum]` 原生属性标记；它们会被当前解析器判定为冲突。
9. 每次完成写入后，必须通过 Unity MCP 刷新并编译被修改的 Shader；若编辑器已连接，还必须读取目标 Shader 的诊断。
10. 解析或编译失败时，保留失败证据，最小化修复标记，不得通过删除 Properties 属性来规避问题。

## 工作流程

### 阶段 1：读取与分类

读取完整 Shader 后，建立以下清单：

```text
MarkupPlan
  shaderPath
  shaderName
  hasMarkupShaderGUICustomEditor
  existingMarkers
  properties[]
    name
    displayName
    type
    attributes
    defaultValue
    semantic
  keywords[]
  groups[]
  unsupportedOrAmbiguousItems[]
```

属性语义按以下优先级判断：

1. 用户明确指定的分组和名称。
2. 现有 Shader 注释、属性显示名、关键字名称。
3. PBR 习惯：表面、法线、遮蔽、发光、透明裁剪、菲涅尔、溶解等。
4. 无法可靠判断时，归入“其他参数 / Other”，不要自行猜测复杂业务语义。

### 阶段 2：设计 Inspector 布局

默认推荐分组顺序：

1. `基础设置_BaseSettings`：基础色、主贴图、金属度、粗糙度/光滑度。
2. `法线与遮蔽_NormalAndOcclusion`：法线、法线强度、AO。
3. `自发光_Emission`：自发光颜色、贴图、强度、开关。
4. `透明与裁剪_TransparencyAndClip`：Alpha、透明度、裁剪阈值。
5. `附加效果_AdditionalEffects`：Fresnel、Dissolve、特殊遮罩等。
6. `其他参数_OtherSettings`：无法从属性名或代码可靠归类的项目。

分组数量应服务于可读性：

- 只有 1–3 个互相关联属性时可使用一个组。
- 不要为单独一个普通 Float 属性创建无意义分组，除非用户明确要求。
- 大组可通过 `Label` 划分子区域。
- 支持关键字的独立功能优先用组级开关包裹其相关参数。

### 阶段 3：生成语法

#### 3.1 可选功能说明

```shader
// # FeatureDes:支持金属度工作流 PBR 与菲涅尔边缘光。
```

- 可写在 `Properties` 块任意位置。
- 只用于顶部说明；不得将它作为 GUI 是否接管的前提。
- 内容应说明用户可感知的材质能力，不要复述实现细节。

#### 3.2 分组（核心）

```shader
// # GroupStart:基础设置_BaseSettings
_BaseMap("基础贴图", 2D) = "white" {}
_BaseColor("基础色", Color) = (1, 1, 1, 1)
_Metallic("金属度", Range(0, 1)) = 0
// # GroupEnd
```

约束：

- 格式必须是 `// # GroupStart:中文显示名_EnglishIdentifier`。
- 中文名用于 Inspector 标题；英文标识用于折叠状态和组复制/粘贴键。
- `EnglishIdentifier` 使用 PascalCase，仅包含字母和数字；同一 Shader 内必须唯一，且**不能再包含 `_`**。当前解析器按 `_` 切分并只读取第二段。
- 每个 `GroupStart` 必须由一个 `GroupEnd` 结束；禁止嵌套组。
- `Label`、`Toggle`、`Enum` 必须处于组内；`Warning` 与 `FeatureDes` 可以位于组外。

#### 3.3 组内小标题

```shader
// # Label:表面参数
```

- 显示为粗体标题。
- 仅一个组当前只保留一个 `Label` 数据字段；因此应放在该组属性绘制之前。
- 需要多个子标题时，不要假设当前版本支持多个分段标题；应拆分为多个组，或等待 GUI 能力扩展。

#### 3.4 普通属性类型

组内普通属性会自动按 Properties 类型绘制：

| Property 类型 | GUI 控件 |
|---|---|
| `Color` | 颜色字段 |
| `Vector` | Vector 字段 |
| `Range(min,max)` | 滑条 |
| `Float` / `Int` | 数值字段 |
| `2D` / `Cube` | 贴图字段 |

对于 `2D` 贴图：未声明 `[NoScaleOffset]` 时显示 Tiling / Offset；声明后隐藏它们。

#### 3.5 数值属性开关

```shader
// # Toggle:Uniform:启用菲涅尔:_FresnelEnabled
_FresnelEnabled("启用菲涅尔", Float) = 1
```

- 格式：`// # Toggle:Uniform:显示名:属性名`。
- `属性名` 必须是在同一 `Properties` 块中存在的 Float/Range/Int 属性。
- 写入 `0` 或 `1` 到该 MaterialProperty。
- 适用于运行时通过 uniform 分支控制的功能。

#### 3.6 关键字开关

```shader
// # Toggle:KeyWords:启用菲涅尔:_FRESNEL_ON
```

- 格式：`// # Toggle:KeyWords:显示名:关键字名`。
- 不要求同名 Properties 属性；关键字必须在 Shader 中有真实对应的 pragma 或逻辑使用。
- GUI 直接调用 Material 的启用/禁用关键字接口。
- 关键字名必须与 Shader 声明的拼写和大小写一致。

#### 3.7 组级数值开关

```shader
// # GroupStart:边缘光_Fresnel
// # Toggle:GroupUniform:启用边缘光:_FresnelEnabled
_FresnelEnabled("启用边缘光", Float) = 1
_FresnelColor("边缘光颜色", Color) = (1, 1, 1, 1)
_FresnelPower("边缘光强度", Range(0, 8)) = 2
// # GroupEnd
```

- `GroupUniform` 的格式与 `Uniform` 一致。
- 当该值关闭时，组内其余参数不绘制。
- 开关属性本身仍需要真实存在于 Properties 块。

#### 3.8 组级关键字开关

```shader
// # GroupStart:边缘光_Fresnel
// # Toggle:GroupKeyWords:启用边缘光:_FRESNEL_ON
_FresnelColor("边缘光颜色", Color) = (1, 1, 1, 1)
_FresnelPower("边缘光强度", Range(0, 8)) = 2
// # GroupEnd
```

- `GroupKeyWords` 控制 Material keyword，关闭时隐藏组内其余参数。
- 只对确有真实 Shader keyword 的效果使用。

#### 3.9 枚举

```shader
// # Enum_SurfaceMode:表面模式:_SurfaceMode:不透明|裁剪=1|透明=2
_SurfaceMode("表面模式", Float) = 0
```

- 格式：`// # Enum_英文名:中文显示名:属性名:选项A|选项B=数值`。
- 默认按顺序映射 `0`、`1`、`2`，这是当前 GUI 完整支持的安全写法。
- 当前下拉框以属性数值作为选项下标读取；因此除非显式值仍等于其下标，否则**不要自动生成** `=数值` 的稀疏或重排映射（例如 `高=4`）。
- 枚举属性必须是实际 Properties 中的 Float/Range/Int 类型。
- `英文名` 用于描述，当前 GUI 显示的是中文显示名。

#### 3.10 警告（可选）

```shader
// # Warning:开启透明后请确认排序和深度预处理策略。
```

- 可选扩展：`// # Warning:文本:颜色名:秒数`。
- 当前 GUI 仅显示文本；颜色名和秒数虽会解析，但尚未影响绘制。

## CustomEditor 规则

最终 Shader 必须有且仅有一条以下声明：

```shader
CustomEditor "MarkupShaderGUI.MarkupShaderGUI"
```

- 应位于 Shader 最外层结束花括号之前。
- 已有其他 `CustomEditor` 时，先报告兼容性风险；只有用户确认替换，或原声明明确属于旧版 Markup/Unified GUI 时才替换。
- 不得在 SubShader、Pass 或 HLSL 块内写入该声明。

## 完整示例

```shader
Shader "Example/Lit Fresnel"
{
    Properties
    {
        // # FeatureDes:支持 PBR 表面参数与可开关的菲涅尔边缘光。

        // # GroupStart:基础设置_BaseSettings
        // # Label:表面参数
        _BaseMap("基础贴图", 2D) = "white" {}
        _BaseColor("基础色", Color) = (1, 1, 1, 1)
        _Metallic("金属度", Range(0, 1)) = 0
        _Smoothness("光滑度", Range(0, 1)) = 0.5
        // # GroupEnd

        // # GroupStart:边缘光_Fresnel
        // # Toggle:GroupKeyWords:启用边缘光:_FRESNEL_ON
        _FresnelColor("边缘光颜色", Color) = (1, 1, 1, 1)
        _FresnelPower("边缘光强度", Range(0, 8)) = 2
        // # GroupEnd
    }

    SubShader
    {
        // 原有渲染实现保持不变。
    }

    CustomEditor "MarkupShaderGUI.MarkupShaderGUI"
}
```

## 写入后校验清单

在刷新/编译前静态核对：

- [ ] 目标 Shader 仍保留全部原有 Properties 属性及其默认值。
- [ ] 每个 `GroupStart` 都有对应 `GroupEnd`，且没有嵌套。
- [ ] 所有组名均包含非空的中文段与英文标识段，二者由一个 `_` 分隔。
- [ ] 所有 `Label`、`Toggle`、`Enum` 位于分组内。
- [ ] 所有 `Uniform` / `GroupUniform` / `Enum` 指向真实 Properties 属性。
- [ ] 所有 `KeyWords` / `GroupKeyWords` 指向真实 Shader keyword。
- [ ] 没有 `[Toggle]`、`[ToggleOff]`、`[KeywordEnum]` 原生属性标记。
- [ ] 最终只存在一条正确的 `CustomEditor` 声明。

## Unity 验证流程

1. 刷新并编译修改后的 Shader。
2. 读取 Unity Console 中该 Shader 的新增错误与警告。
3. 若无错误，选中使用该 Shader 的 Material，人工或自动确认：
   - Inspector 被 `MarkupShaderGUI` 接管；
   - 分组标题可折叠；
   - 普通属性类型与原属性相符；
   - 组级开关隐藏/显示组内容；
   - Keyword 开关同步改变目标 keyword；
   - 枚举写入预期数值；
   - Render Queue 字段仍存在。
4. 失败时，先根据解析器的行号修复标记，再次编译；不得修改无关渲染代码。

## 输出规范

完成一次生成或修复后，必须报告：

```text
- 目标 Shader 路径与名称
- 新增/修改的分组、标题、开关、枚举及其绑定对象
- 是否新增或替换 CustomEditor 声明
- 未自动处理的歧义项及原因
- Unity 编译与 Console 诊断结果
```
