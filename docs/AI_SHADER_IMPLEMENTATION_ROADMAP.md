# AI Shader 生成系统达成路径

> 方案：B——渲染知识驱动、项目结构适配、LLM 生成代码，并通过 Unity 场景、球体材质和截图反馈形成闭环。
>
> 当前工程基线：Unity `6000.5.4f1`、URP `17.5.0`。当前工程为新建 URP 工程，已有 `Assets/Scenes/SampleScene.unity` 与 `Assets/Settings` 下的 URP 配置，但暂未建立 AI Shader 专用代码与测试资产。

---

## 1. 目标与非目标

### 1.1 目标

系统能够让用户用自然语言提出材质需求，LLM 完成以下工作：

1. 分析用户需求，提取材质模型、光照组成、表面属性和特殊效果；
2. 读取当前 Unity 工程中已有 Shader、HLSL Include、材质、URP 配置和命名约定；
3. 抽象出当前项目的 `ProjectShaderProfile`；
4. 将材质需求转换为与具体代码无关的渲染语义图；
5. 根据项目能力将渲染语义图翻译为当前工程风格的 Shader/HLSL；
6. 自动创建验证场景、球体 Mesh、灯光、相机和测试材质；
7. 逐阶段编译、截图、分析和修正；
8. 在直接光照、间接光照、Combine、特殊效果等阶段分别确认，降低定位成本；
9. 保存每一轮的代码、截图、诊断、决策和回滚点。

### 1.2 非目标

第一阶段不追求：

- 让 LLM 任意修改 `Packages` 或 `ProjectSettings`；
- 一次生成完美的完整 Shader；
- 仅根据截图判断所有物理正确性；
- 自动解决所有 URP 版本、渲染路径和平台差异；
- 直接支持复杂后处理、Renderer Feature、Ray Tracing 和运行时动态编译。

第一阶段的重点是建立一个可靠的 **Shader 编程与验证循环**。

---

## 2. 总体架构

```text
自然语言需求
    ↓
Requirement Parser
    ↓
Material Intent
    ↓
Rendering Semantic Graph
    ↓
Project Shader Analyzer
    ↓
Project Shader Profile
    ↓
Capability Resolver
    ↓
Shader Code Plan
    ↓
LLM Code Generation
    ↓
Static Validation
    ↓
Unity AssetDatabase Refresh / Shader Compilation
    ↓
Validation Scene + Sphere Mesh
    ↓
Deterministic Screenshot Capture
    ↓
Image / Console / Numeric Analysis
    ↓
Checkpoint Decision
    ├── pass：进入下一个阶段
    ├── revise：保留现场，回到当前阶段
    └── blocked：记录缺失能力或人工决策
```

系统要明确区分四类对象：

| 对象 | 作用 |
|---|---|
| `MaterialIntent` | 用户想要什么效果 |
| `RenderingSemanticGraph` | 需要哪些渲染计算，以及它们如何连接 |
| `ProjectShaderProfile` | 当前工程如何提供顶点、法线、光源、阴影、间接光等能力 |
| `ShaderCodePlan` | 本轮具体修改哪些文件、函数、Pass 和输入输出 |

LLM 不应跳过中间对象直接生成最终 Shader。

---

## 3. 核心设计原则

### 3.1 算法语义与项目代码解耦

知识库描述：

- PBR 的能量组成；
- 漫反射和镜面反射的计算；
- NDF、Geometry、Fresnel；
- 直接光、间接漫反射、间接高光和自发光；
- 坐标空间、归一化、精度、能量守恒和边界条件；
- 常见实现变体与失败案例。

当前项目 Profile 描述：

- 顶点/片元入口；
- `Attributes`、`Varyings`、`InputData`、`SurfaceData` 等结构；
- 世界空间位置、法线、切线、视线方向的来源；
- 主光源和附加光源的获取方式；
- 阴影、距离衰减、环境光、反射探针和 BRDF LUT 的接口；
- Include 组织、命名、缩进、精度和宏约定；
- 必须保留的 Pass。

LLM 依据二者生成适配代码。

### 3.2 分阶段实现，不一次生成完整效果

每个阶段只引入一组新的语义节点，并且具有独立的输入、输出、截图和验收标准。

推荐顺序：

```text
Stage 0：工程与参考基线
Stage 1：几何、坐标空间、材质输入
Stage 2：直接漫反射
Stage 3：直接镜面反射
Stage 4：直接光完整组合
Stage 5：间接漫反射
Stage 6：间接镜面反射
Stage 7：间接光完整组合
Stage 8：自发光
Stage 9：最终 Combine 与颜色输出
Stage 10：阴影、附加光、法线贴图和高级特性
```

### 3.3 截图是反馈信号，不是唯一真值

截图用于发现：

- 整体是否变黑、过曝、反向、断层或出现异常色带；
- 高光位置是否随相机/灯光正确移动；
- 粗糙度变化是否符合预期；
- 间接光是否存在明显接缝或错误颜色；
- Combine 是否重复加光、丢失分量或错误乘色。

但截图不能单独证明公式正确。因此验证必须同时使用：

1. Unity Console 和编译结果；
2. Shader 静态检查；
3. 固定场景下的截图差异；
4. 可选的 Debug 输出通道；
5. 参数扫描和边界测试；
6. 与 Unity Lit 或离线参考结果的对照。

---

## 4. 知识库建设路径

知识库建议采用结构化条目，而不是只保存长篇文档或代码片段。

每个渲染知识条目至少包含：

```json
{
  "id": "brdf.fresnel.schlick",
  "category": "BRDF",
  "purpose": "计算视角相关的菲涅尔反射率",
  "inputs": {
    "f0": "float3",
    "cosTheta": "float"
  },
  "output": "float3",
  "formula": "F0 + (1 - F0) * pow(1 - saturate(cosTheta), 5)",
  "preconditions": [
    "输入向量必须归一化",
    "cosTheta 必须位于 [0,1]"
  ],
  "coordinateSpace": "无关，但输入方向必须在同一空间",
  "dependencies": [],
  "variants": ["schlick", "roughness-aware-schlick"],
  "commonMistakes": [
    "使用未归一化的 N、V、L",
    "把 roughness 直接当作 cosTheta",
    "遗漏 saturate"
  ],
  "testCases": [
    {"name": "正视角", "expected": "接近 F0"},
    {"name": "掠射角", "expected": "接近 1"}
  ]
}
```

第一批知识条目：

### 4.1 基础数据

- 坐标空间与空间转换；
- 法线变换与非均匀缩放；
- `N`、`V`、`L`、`H`、`NdotL`、`NdotV`；
- 归一化、`saturate`、epsilon 和精度。

### 4.2 PBR 直接光

- Lambert / Burley 漫反射；
- Metallic 工作流与 `F0`；
- GGX NDF；
- Smith Geometry；
- Schlick Fresnel；
- Cook-Torrance 镜面反射；
- 直接光颜色、衰减和阴影的乘法关系。

### 4.3 PBR 间接光

- SH / ambient irradiance；
- 反射方向与环境采样；
- 预过滤环境图；
- BRDF LUT；
- Occlusion；
- 间接光的粗糙度处理与能力降级。

### 4.4 验证与错误案例

- 漫反射重复乘 `NdotL`；
- 高光分母为零；
- 法线在切线空间/世界空间混用；
- Gamma/Linear 混用；
- 间接光被重复加到直接光；
- 自发光被错误乘以阴影；
- HDR 值被过早 `saturate`；
- 粗糙度与光滑度方向反了。

---

## 5. 项目结构分析与 ProjectShaderProfile

LLM 开始写代码前必须先完成工程侦察。分析范围优先为 `Assets`，其次为只读参考的 URP `Packages`，不修改 Package 内容。

### 5.1 分析步骤

1. 读取 Unity 和 URP 版本；
2. 读取当前 Render Pipeline Asset、Renderer Asset 和渲染路径；
3. 枚举 `Assets` 中的 `.shader`、`.hlsl`、`.shadergraph`、`.cs`；
4. 找到可编译、实际被场景使用的 Shader；
5. 识别 Pass、LightMode、入口函数和 Include；
6. 识别数据结构与插值器；
7. 识别光照、阴影、环境光和反射接口；
8. 记录工程命名、文件布局、精度和注释风格；
9. 输出 Profile，并标记每项能力的证据来源和置信度。

### 5.2 Profile 示例

```json
{
  "unityVersion": "6000.5.4f1",
  "urpVersion": "17.5.0",
  "renderPath": "Forward",
  "shaderConventions": {
    "worldNormal": "InputData.normalWS",
    "viewDirectionWS": "GetWorldSpaceNormalizeViewDir(positionWS)",
    "mainLight": "GetMainLight(shadowCoord)",
    "indirectDiffuse": "SampleSH(normalWS)"
  },
  "capabilities": {
    "mainLight": true,
    "additionalLights": true,
    "shadowSampling": true,
    "indirectDiffuse": true,
    "indirectSpecular": "unknown",
    "reflectionProbe": "unknown",
    "brdfLut": "unknown"
  },
  "requiredPasses": [
    "UniversalForward",
    "ShadowCaster",
    "DepthOnly",
    "DepthNormals"
  ],
  "evidence": [
    {
      "capability": "mainLight",
      "source": "Assets/.../Example.hlsl:...",
      "confidence": 0.92
    }
  ]
}
```

“unknown” 必须保留，不能让 LLM 用猜测填充。未知能力需要通过代码搜索、Unity API 查询或小型实验确认。

---

## 6. 验证场景与球体测试台

需要建立独立的、可重复生成的 `AIShaderValidation` 场景，不直接污染用户当前工作场景。

建议目录：

```text
Assets/AIShader/
├── Runtime/
├── Editor/
├── Shaders/
├── Knowledge/
├── Profiles/
├── Generated/
├── Validation/
│   ├── Scenes/
│   ├── Materials/
│   ├── Captures/
│   └── Reports/
└── Tests/
```

### 6.1 场景固定内容

- 一个带高质量法线和 UV 的 Sphere Mesh；
- 一个主方向光，可调整方向、颜色和强度；
- 至少一个辅助填充光或可开关的附加光；
- 中性背景和可控环境光；
- 主相机固定位置；
- 可选的灰色平面，用于观察投影和接触关系；
- 参考材质球，例如 URP Lit；
- 目标生成材质球；
- Debug UI 或全局控制器；
- 固定分辨率、HDR、曝光和后处理设置。

### 6.2 为什么必须使用球体

球体能同时暴露：

- 法线是否正确；
- 高光方向是否正确；
- 粗糙度是否起效；
- 菲涅尔是否随视角变化；
- 间接光是否连续；
- 切线空间法线是否正确；
- Combine 是否发生能量异常。

### 6.3 场景必须可参数化

验证控制器至少支持：

```text
SetMaterial(shader/material)
SetBaseColor(color)
SetMetallic(value)
SetRoughness(value)
SetEmission(color, intensity)
SetMainLight(direction, color, intensity)
SetEnvironmentColor(color)
SetCamera(position, rotation)
SetDebugChannel(channel)
Capture(label)
```

场景生成和截图必须可重复，避免 LLM 每轮手动拖拽导致不可比较。

---

## 7. 分阶段 Loop 设计

每个阶段使用同一个标准循环：

```text
定义阶段目标
  ↓
读取当前代码和 Profile
  ↓
制定最小修改计划
  ↓
生成或修改代码
  ↓
静态检查
  ↓
Unity 刷新与编译
  ↓
配置验证场景
  ↓
截取基准图、目标图和 Debug 图
  ↓
读取 Console / 分析截图 / 对比参考
  ↓
输出阶段报告
  ↓
通过则锁定 checkpoint
  ↓
失败则只修改当前阶段允许的代码
```

### 7.1 阶段状态

```text
PENDING      尚未开始
IMPLEMENTING 正在修改
COMPILING    等待 Unity 编译
ANALYZING    正在分析截图和诊断
PASSED       达成验收标准
REVISING     当前阶段修正
BLOCKED      缺少项目能力或需要人工确认
```

### 7.2 每轮 Loop 的硬限制

- 每轮只允许一个明确的主假设；
- 每轮优先修改一个函数或一个数据链路；
- 不允许同时修改直接光、间接光和 Combine；
- 编译错误必须先修复，不能继续视觉判断；
- 截图必须记录场景参数、材质参数和代码版本；
- 连续多轮无改善时，回退到最近 checkpoint；
- 任何“通过”都必须有证据，而不是仅由 LLM 自我判断。

---

## 8. PBR 分阶段验收标准

### Stage 0：工程与参考基线

**目标**：确认项目可编译、验证场景可生成、截图链路可用。

验收：

- Unity Console 无新增编译错误；
- 验证场景可打开；
- Sphere、Camera、Light、Reference Material 都存在；
- 能生成带时间戳或迭代 ID 的 PNG；
- 同样输入重复截图结果基本一致。

### Stage 1：几何与材质输入

**目标**：只验证 Base Color、顶点位置、法线、视线方向和材质输入。

建议 Debug 通道：

```text
WorldNormal
ViewDirection
BaseColor
Metallic
Roughness
UV
```

验收：

- 法线球形分布连续；
- 视线方向变化符合相机移动；
- Base Color 不受光照影响时颜色正确；
- Metallic/Roughness 参数读取无反转或丢失。

### Stage 2：直接漫反射

**目标**：只输出直接漫反射，不接高光、间接光和自发光。

```text
Output = DirectDiffuse
```

验收：

- `NdotL <= 0` 的区域不产生正向直接漫反射；
- 灯光方向改变时明暗边界正确移动；
- 灯光强度和颜色线性响应；
- 与参考 Lambert 结果一致；
- 关闭主光后目标输出接近黑色。

### Stage 3：直接镜面反射

**目标**：单独验证 D、F、G 和高光合成。

```text
Output = DirectSpecular
```

验收：

- 高光随灯光和相机方向移动；
- Roughness 增大时高光变宽、峰值降低；
- 金属度变化影响 F0 和高光颜色；
- 不出现 NaN、Inf、黑色断点或异常闪烁；
- 正视角与掠射角趋势符合 Fresnel。

建议分别输出：

```text
NDF
Fresnel
GeometryTerm
NoL
NoV
SpecularBRDF
```

### Stage 4：直接光完整组合

**目标**：确认直接漫反射和直接高光之间没有重复乘法或遗漏衰减。

```text
Output = DirectDiffuse + DirectSpecular
```

验收：

- 与 Stage 2、Stage 3 的分量加和一致；
- 阴影或衰减只影响应受影响的光照分量；
- Metallic 接近 1 时漫反射按预期降低；
- HDR 输出没有被过早 saturate；
- 关闭其中一个分量时另一个分量保持一致。

### Stage 5：间接漫反射

**目标**：单独验证环境漫反射或 SH/探针输入。

```text
Output = IndirectDiffuse
```

验收：

- 关闭所有直接光后，球体仍能得到预期环境响应；
- 改变环境颜色时响应正确；
- 法线朝向改变时环境漫反射连续变化；
- Occlusion 只按设计影响间接光；
- 与项目实际可用的环境光接口一致。

若项目没有可靠的间接漫反射能力，必须显式记录降级：

```text
IndirectDiffuse = fallbackAmbientColor
```

不得伪造为完整 IBL。

### Stage 6：间接镜面反射

**目标**：单独验证反射方向、环境采样、粗糙度和 BRDF LUT。

```text
Output = IndirectSpecular
```

验收：

- 反射方向随法线和视线变化正确；
- Roughness 影响环境反射模糊程度或采样级别；
- 反射探针/环境图不存在时有明确降级；
- 不把直接光接口误用为间接高光；
- 若缺少 BRDF LUT，报告中明确能力缺失和替代方案。

### Stage 7：间接光完整组合

```text
Output = IndirectDiffuse + IndirectSpecular
```

验收：

- 两个间接分量可以独立开关；
- 不与直接光重复；
- Metallic、Roughness、Occlusion 的作用位置正确；
- 环境光强度变化不会改变直接光分量。

### Stage 8：自发光

```text
Output = Emission
```

验收：

- 无灯光时仍有自发光；
- 自发光不受阴影衰减；
- 强度为 0 时不影响其他分量；
- HDR 强度不会被过早截断；
- 后处理 Bloom 是否存在要在报告中单独标明。

### Stage 9：最终 Combine

```text
Output = Direct + Indirect + Emission
```

验收：

- 单独输出各分量后，最终输出与预期组合一致；
- 分量不会重复加法；
- 所有颜色空间和曝光处理位置正确；
- Debug 开关可以逐一关闭分量；
- 与参考材质在基准参数下具有可解释的差异。

### Stage 10：扩展能力

在基础闭环稳定后再加入：

- 法线贴图；
- 阴影；
- Additional Lights；
- Clear Coat；
- Anisotropy；
- Detail Map；
- Parallax；
- Alpha Clipping；
- Transparent；
- Fresnel/Rim Light；
- Dissolve 和风格化效果。

每个扩展都必须成为独立阶段，不要一次加入多个未经验证的能力。

---

## 9. 截图与图像分析协议

### 9.1 每次截图必须伴随 Manifest

```json
{
  "iteration": "pbr-stage-03-try-02",
  "stage": "direct-specular",
  "scene": "Assets/AIShader/Validation/Scenes/PBRValidation.unity",
  "shader": "Assets/AIShader/Generated/PBR.generated.shader",
  "material": "Assets/AIShader/Validation/Materials/Target.mat",
  "camera": {
    "position": [0, 0, -4],
    "rotation": [0, 0, 0]
  },
  "light": {
    "direction": [0.3, -0.4, -0.8],
    "intensity": 1.0
  },
  "parameters": {
    "metallic": 0.8,
    "roughness": 0.25
  },
  "debugChannel": "SpecularBRDF",
  "consoleErrors": 0
}
```

### 9.2 每个阶段至少保存三张图

1. `reference.png`：参考材质或参考实现；
2. `target.png`：当前生成 Shader；
3. `debug.png`：当前阶段的单项 Debug 输出。

必要时保存：

- 灯光关闭图；
- 环境光关闭图；
- 分量开关图；
- 参数扫描图；
- 相机移动前后对比图。

### 9.3 采用固定测试矩阵

不要只看一个视角。最低测试矩阵：

```text
Camera：正面、偏左、偏右、掠射角
Light：正面、侧面、背面
Material：metallic 0/0.5/1，roughness 0.05/0.5/0.95
Lighting：直接光开关、间接光开关、自发光开关
```

截图分析结果要区分：

```text
编译失败
场景配置失败
接口能力缺失
结构/数据错误
公式错误
参数错误
颜色空间/曝光错误
视觉偏差但可接受
```

---

## 10. MCP 工具边界

MCP 需要提供高层、可审计的工具，不让 LLM 直接获得无限文件和进程权限。

### 10.1 工程分析

```text
get_project_info()
get_urp_configuration()
list_shader_assets(root)
read_shader_asset(path)
extract_shader_structure(path)
build_project_shader_profile()
```

### 10.2 代码与资产

```text
create_or_update_shader(path, content, baseRevision)
create_or_update_hlsl(path, content, baseRevision)
create_material(path, shaderPath)
set_material_properties(path, values)
```

`baseRevision` 用于防止 LLM 覆盖其他轮次的修改。

### 10.3 验证场景

```text
create_validation_scene(config)
configure_validation_scene(config)
assign_validation_material(materialPath)
set_debug_channel(channel)
set_lighting_config(config)
set_camera_config(config)
```

### 10.4 编译和截图

```text
refresh_assets()
wait_for_compilation(timeout)
get_console_entries(filter)
capture_validation_frame(label, config)
get_validation_report(iteration)
rollback_to_checkpoint(id)
```

### 10.5 安全边界

- 默认只允许写入 `Assets/AIShader/`；
- 禁止修改 `Packages/`；
- 禁止修改 `ProjectSettings/`，除非后续提供专门审批工具；
- 每次写入前检查路径、扩展名和大小；
- 保存旧版本和哈希；
- 编译失败时保留失败现场，不立即覆盖；
- 场景操作限制在专用验证场景；
- 所有截图、报告和变更都写入迭代目录。

---

## 11. Skill 的职责与 Loop 提示词结构

建议建立一个 `shader-authoring-agent` Skill。它不直接保存某个工程的固定 Shader，而是规定工作协议。

### 11.1 Skill 必须要求的行为

```text
1. 先分析项目，不得凭记忆假设接口。
2. 先生成 ProjectShaderProfile，再写代码。
3. 先生成 RenderingSemanticGraph，再生成 ShaderCodePlan。
4. 每次只实现一个阶段。
5. 实现前先定义本阶段输出和验收标准。
6. 编译错误优先于视觉分析。
7. 每轮必须截图并保存 Manifest。
8. 通过必须有 Console、静态检查和截图证据。
9. 失败时只修改当前阶段范围。
10. 连续失败时回滚并报告具体阻塞条件。
```

### 11.2 每阶段建议的 LLM 输出格式

```text
## Stage Plan
- stage:
- objective:
- semantic nodes:
- expected inputs:
- expected outputs:
- project APIs to reuse:
- files allowed to change:
- forbidden changes:
- acceptance criteria:

## Implementation
- changed files:
- key functions:
- assumptions:

## Validation
- compile result:
- console result:
- captures:
- debug channel:
- comparison result:

## Decision
- PASS / REVISE / BLOCKED
- evidence:
- next action:
```

---

## 12. 版本、Checkpoint 和回滚

每个阶段通过后创建不可变 Checkpoint：

```text
checkpoint/
├── source-hash.json
├── ProjectShaderProfile.json
├── RenderingSemanticGraph.json
├── ShaderCodePlan.json
├── generated-files/
├── scene-config.json
├── captures/
└── validation-report.json
```

建议 checkpoint：

```text
pbr-stage-00-baseline
pbr-stage-01-surface-input
pbr-stage-02-direct-diffuse
pbr-stage-03-direct-specular
pbr-stage-04-direct-combine
pbr-stage-05-indirect-diffuse
pbr-stage-06-indirect-specular
pbr-stage-07-indirect-combine
pbr-stage-08-emission
pbr-stage-09-final-combine
```

回滚必须同时恢复：

- Shader/HLSL 源码；
- Material 参数；
- 验证场景配置；
- Semantic Graph 和 Profile；
- 截图基线和报告引用。

只恢复 Shader 而不恢复场景，会导致后续对比失真。

---

## 13. 实现分期

### Phase A：验证基础设施

目标：先不做 AI 生成，建立确定性的验证台。

交付：

- `PBRValidation.unity`；
- Sphere、Camera、主光、环境光、参考材质；
- 参数化场景控制器；
- 固定相机和灯光配置；
- 截图工具；
- Screenshot Manifest；
- Debug Channel 机制。

### Phase B：项目结构分析器

交付：

- Shader/HLSL 文件枚举；
- Pass 和 Include 提取；
- 入口函数识别；
- 光照接口候选识别；
- `ProjectShaderProfile` JSON；
- 证据和置信度输出。

### Phase C：知识库与语义图

交付：

- PBR 基础知识条目；
- 节点输入输出 Schema；
- 依赖、冲突和降级规则；
- `RenderingSemanticGraph` 校验器；
- PBR 分阶段图模板。

### Phase D：单阶段代码生成

先只实现 Stage 2：直接漫反射。

交付：

- `ShaderCodePlan`；
- 受限文件修改；
- 代码生成；
- 静态检查；
- Unity 编译；
- 截图；
- PASS/REVISE/BLOCKED 判定。

### Phase E：完整 PBR Loop

按 Stage 3 至 Stage 9 扩展，要求每阶段都有独立 checkpoint。

### Phase F：MCP 封装

把已验证的基础设施和操作能力封装为 MCP，不要先设计过度通用的工具。

### Phase G：Skill 封装

将 Phase B 至 Phase E 的协议、约束和输出格式整理成 Skill。

### Phase H：NPR 与效果扩展

在 PBR Loop 稳定后，复用：

- 项目分析；
- 语义图；
- 验证场景；
- 分阶段 Loop；
- 截图和回滚。

只替换 NPR 的语义知识与验收场景。

---

## 14. 第一条端到端验收用例

用户输入：

> 在当前 URP 工程中创建一个带金属高光和轻微自发光的 PBR 材质。请先分析当前工程的 Shader 结构，在专用球体场景中分阶段实现：先完成直接漫反射，再完成直接高光，然后加入间接光，最后加入自发光并合成。每个阶段都编译并截图，失败时只修改当前阶段。

成功条件：

1. Agent 生成 `ProjectShaderProfile`；
2. Agent 生成分阶段 `RenderingSemanticGraph`；
3. Agent 创建独立验证场景和 Sphere；
4. Stage 2 通过后创建 checkpoint；
5. Stage 3 通过后创建 checkpoint；
6. Stage 5/6 能识别项目是否真的支持间接光；
7. Stage 8 不把自发光乘进阴影；
8. Stage 9 能用 Debug 开关验证 Combine；
9. 所有阶段有截图、Manifest、Console 结果和报告；
10. 任意阶段失败可以回滚并重试。

---

## 15. 关键风险与应对

| 风险 | 应对 |
|---|---|
| LLM 误判已有 Shader 结构 | Profile 必须有证据来源和置信度，未知能力不得猜测 |
| 代码能编译但视觉错误 | 分量 Debug、固定测试矩阵、参考材质对比 |
| 同时改动太多导致无法定位 | 每轮一个阶段、一个主假设、限制修改文件 |
| URP API 版本变化 | 以当前工程和 Packages 只读源码为准，锁定版本 |
| 间接光能力不存在 | Capability Resolver 输出降级方案，不伪造完整 IBL |
| 截图不稳定 | 固定场景、相机、灯光、分辨率、曝光和后处理 |
| 修改破坏用户工程 | 专用目录、版本哈希、checkpoint 和回滚 |
| 视觉评估过度依赖主观判断 | 结合公式测试、Debug 输出和参考材质 |
| 失败循环无限持续 | 每阶段设置最大尝试次数，超限进入 BLOCKED |

---

## 16. 推荐的 MVP 顺序

```text
1. 创建 AIShader/Validation 基础目录
2. 实现可重复的球体验证场景
3. 实现截图和 Manifest
4. 实现 Debug Channel
5. 实现 ProjectShaderProfile 生成
6. 建立 PBR 直接漫反射知识条目
7. 手动或半自动跑通 Stage 2
8. 将 Stage 2 封装成 Loop
9. 依次实现直接高光、间接光、自发光和 Combine
10. 加入 checkpoint/rollback
11. 封装 MCP 工具
12. 封装 Skill
13. 扩展 NPR
```

---

## 17. 当前开发状态

### 已完成：验证基础设施第一批

当前已迁移为本地 Unity Package：

```text
Packages/com.ai.shader-authoring/package.json
Packages/com.ai.shader-authoring/Runtime/AIShaderAuthoring.Runtime.asmdef
Packages/com.ai.shader-authoring/Runtime/Validation/AIShaderDebugChannel.cs
Packages/com.ai.shader-authoring/Runtime/Validation/AIShaderValidationController.cs
Packages/com.ai.shader-authoring/Runtime/ProjectProfile/AIShaderProjectShaderProfile.cs
Packages/com.ai.shader-authoring/Editor/AIShaderAuthoring.Editor.asmdef
Packages/com.ai.shader-authoring/Editor/Compilation/AIShaderCompilationBridge.cs
Packages/com.ai.shader-authoring/Editor/Mcp/UnityMcpConnection.cs
Packages/com.ai.shader-authoring/Editor/Mcp/UnityMcpWindow.cs
Packages/com.ai.shader-authoring/Editor/ProjectContext/AIShaderProjectContext.cs
Packages/com.ai.shader-authoring/Editor/ProjectContext/AIShaderProjectContextWindow.cs
Packages/com.ai.shader-authoring/Editor/ProjectContext/AIShaderProjectContextPrompt.cs
Packages/com.ai.shader-authoring/Editor/Validation/AIShaderValidationSceneCreator.cs
Packages/com.ai.shader-authoring/Editor/Validation/AIShaderCaptureManifest.cs
Packages/com.ai.shader-authoring/Editor/Validation/AIShaderValidationCaptureMenu.cs
```

已实现的能力：

- `AIShaderDebugChannel`：定义 Final、WorldNormal、NoL、NDF、Fresnel、DirectDiffuse、DirectSpecular、IndirectDiffuse、IndirectSpecular、Emission、Combine 等诊断通道；
- `AIShaderValidationController`：统一控制目标 Renderer、参考 Renderer、主方向光、验证相机、材质参数和 Debug 通道；
- `AIShaderValidationSceneCreator`：通过 Unity 菜单 `AI Shader/Create Validation Scene` 创建独立验证场景；
- 验证场景包含 Camera、Directional Light、Target Sphere、Reference Sphere 和 Neutral Ground；
- `AIShaderCaptureManifest`：提供 Editor 截图和 Manifest 写入能力，记录场景、材质、Shader、相机、灯光、材质参数、Debug 通道和迭代信息；
- 截图采用 LDR `RGBA32` PNG，避免 HDR Half 纹理直接编码 PNG 的兼容性问题；
- 验证参数通过 `MaterialPropertyBlock` 写入 Renderer，不再直接修改共享材质资产；
- 迭代 ID 使用严格白名单清理，并拒绝覆盖已有截图和 Manifest；
- 默认写入范围位于 `Assets/AIShader/Validation/`；该目录只保存用户工程中的验证场景、材质、截图和报告，不保存 Package 源码。
- Package 源码位于 `Packages/com.ai.shader-authoring/`，并通过 `Packages/manifest.json` 以本地 `file:` 依赖接入。

### 本轮修复

根据代码审查已修复：

- `RGBAHalf`/`EncodeToPNG` 捕获不兼容：改为 `ARGB32` RenderTexture 和 `RGBA32` Texture2D；
- 修复 Unity `Texture2D` 不实现 `System.IDisposable` 的编译错误，改为显式 `DestroyImmediate` 释放临时纹理；
- `sharedMaterial` 资产污染：目标 Renderer 改用 `MaterialPropertyBlock`；
- Reference Sphere 不再被目标材质参数覆盖；
- Manifest 补充灯光和材质参数；
- iteration 改为字母、数字、`-`、`_` 白名单，并禁止覆盖已有文件；
- 项目路径改由 `Application.dataPath` 推导；
- 场景创建前增加未保存场景确认，保存后检查返回值；
- URP/Lit 缺失时显式报错，不再回退到 Built-in Standard。

### 本轮 Shader 生成

用户已确认：

```text
分析路径：H:\\tmp\\URP-AI\\Packages
生成路径：Assets/AIShader
```

已读取并参考：

```text
Packages/com.unity.render-pipelines.universal@e38be786c41e/Shaders/Lit.shader
Packages/com.unity.render-pipelines.universal@e38be786c41e/ShaderLibrary/Lighting.hlsl
Packages/com.unity.render-pipelines.universal@e38be786c41e/ShaderLibrary/Core.hlsl
```

已生成：

```text
Assets/AIShader/SimplePBR.shader
Assets/AIShader/README.md
ProjectSettings/AIShaderProjectContext.json
```

`SimplePBR.shader` 当前包含：

- Base Color、Metallic、Roughness；
- GGX NDF、Schlick Fresnel、Smith Geometry；
- Direct Diffuse、Direct Specular；
- SH Indirect Diffuse；
- Emission；
- Debug Channel；
- 一个 `UniversalForward` Pass。

当前明确限制：这只是第一版验证 Shader，尚未实现 `ShadowCaster`、`DepthOnly`、`DepthNormals`、`Meta` 等完整 URP Pass，也尚未由 Unity Editor 编译确认。

### 当前未完成

- Package 迁移后的 Assembly Definition 和 Unity Package 依赖尚未由 Unity Editor 实际编译确认；
- Shader 生成资产目录已加入工程上下文配置：默认 `Assets/AIShader/Generated`；本轮用户指定为 `Assets/AIShader`；
- 已生成第一版 `Assets/AIShader/SimplePBR.shader`，基于用户指定的 `Packages` 路径进行读取和适配；
- 尚未生成验证场景资产；
- URP/Lit 的 `_EMISSION` 关键字和自发光属性仍需在 Unity 实际材质中确认；
- 尚未接入 PNG 图像差异分析；
- 尚未实现 ProjectShaderProfile 分析器；
- 尚未实现生成 Shader 和 PBR Stage 2；
- 原 MCP Console/HTTP Bridge、受限文件写入、Shader 验证和截图工具已移除；
- 已切换到 `Tools/unity-mcp-server` 的 stdio MCP Server + WebSocket 实现；
- Unity 端通过 `UnityMcpConnection.cs` 主动连接 `ws://localhost:8080`，发送编辑器状态和日志，并接收编辑器命令；
- 已保留 Unity MCP Dashboard，并将其状态、工具目录和连接操作适配为 WebSocket 模式；
- 当前 MCP 工具为 `get_editor_state`、`execute_editor_command` 与 `get_logs`；
- Unity Editor 启动和 MCP 端到端验证仍待执行；
- Checkpoint 和自动 Loop 尚未实现。

### UnityMCP WebSocket（已切换，待 Unity 验证）

Package 使用：

```text
Packages/com.ai.shader-authoring/Editor/Mcp/UnityMcpConnection.cs
Tools/unity-mcp-server/src/index.ts
```

通信链路：

```text
MCP client ⇄ stdio server ⇄ ws://localhost:8080 ⇄ Unity Editor
```

已提供的 MCP 工具：

```text
get_editor_state
execute_editor_command
get_logs
```

Unity 插件每秒发送编辑器状态，转发日志，并在主线程上处理服务端的编辑器命令。`execute_editor_command` 会动态编译并执行任意 C#，因此只能连接可信的本地 MCP 客户端。

### 工程路径确认流程

LLM 不应自行猜测工程 Shader 或管线文件的位置。用户需要先打开：

```text
AI Shader/Project Context
```

并提供：

```text
Shader Paths       当前项目 Shader 文件或目录
Include Paths      HLSL Include 文件或目录
Pipeline Paths     URP Pipeline Asset、Renderer Asset 和相关配置
Reference Paths    用户指定的参考 Shader、Lighting Include 或示例文件
```

这些路径和 Shader 生成目录保存在：

```text
ProjectSettings/AIShaderProjectContext.json
```

其中 `generatedAssetsPath` 必须位于 `Assets/` 下。用户明确指定时使用用户目录；用户不指定时默认创建并使用：

```text
Assets/AIShader/Generated
```

生成的 Shader、HLSL、Material 和相关资产属于用户工程资产，不写入 Package 源码目录。启动时如果尚未填写分析路径，Package 会在 Console 提醒用户。后续 ProjectShaderProfile Analyzer 只读取用户确认的路径，并在生成 Profile 时保留路径证据。

### 当前验证步骤

在 Unity Editor 完成 Package 编译后：

1. 先执行 `AI Shader/Project Context`，填写并保存 Shader 与管线路径；
2. 执行 `AI Shader/Create Validation Scene`；
3. 打开 `Assets/AIShader/Validation/Scenes/PBRValidation.unity`；
4. 确认 Target Sphere、Reference Sphere、Neutral Ground、Validation Camera 和 Main Directional Light 存在；
5. 在 Inspector 中确认 `AIShaderValidationController` 引用完整；
6. 从 Editor 调用截图工具，确认 `Assets/AIShader/Validation/Captures/` 产生 PNG；
7. 确认 `Assets/AIShader/Validation/Reports/` 产生对应 Manifest JSON；
8. 再开始 Stage 1 的 Shader Debug 输出实现。

最优先的工程成果不是“第一个能生成的 Shader”，而是：

> **一个可重复、可分阶段、可截图、可诊断、可回滚的 Shader 验证闭环。**

一旦这个闭环成立，PBR、NPR 和后续特效只是在知识库、语义图和验收规则上扩展，而不是重新设计整套基础设施。
