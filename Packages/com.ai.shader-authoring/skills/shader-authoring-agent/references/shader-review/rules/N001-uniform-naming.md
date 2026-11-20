# N001 · Uniform 命名必须查表，禁止自造同义名

| 项 | 值 |
|---|---|
| ID | N001 |
| 类型 | 代码类（命名规范） |
| 严重级别 | 🟡 中（不报错，但会持续制造维护成本） |
| 适用对象 | **新增** shader 的 `Properties` 与 HLSL 声明；存量白名单文件不检查 |
| 规范表 | [`reference/uniform-index.md`](../reference/uniform-index.md) |
| 自动检测 | `bash tools/naming-lint.sh <文件或目录>` |


> 📍 **生效范围（大前提）**：只对 `Assets/Shaders/ShaderTheseus/**` 与 `Assets/Shaders/ShaderLibrary/**` 下的 shader 生效；范围外不检查、不报告。

## 规则

新增 uniform（`Properties` 里的属性、HLSL 里的 `uniform`/`TEXTURE2D`/`CBUFFER` 成员）时：

1. **先查规范表**：`reference/uniform-index.md` 按功能模块列出 14 个模块的规范名、类型、默认值、通道语义。
2. **表里有 → 直接用那个名字**，不改大小写、不加前缀、不换同义词。
3. **表里没有 → 用最近的模块前缀 + 现有词汇**，然后在 PR / 报告里说明「新增概念」。
4. **绝不新增同一功能的第二种写法**。工程里同一个概念最多已经存在 4 种拼写（如流光的 `_USE_FLOW_LIGHT` / `_USE_FLOW_LIGHT_ON` / `_USE_FLOWLIGHT` / `_USE_FLOW_LIGHT2`），再来一种就是第 5 种。

## 为什么（真实代价）

- **ShaderGUI 会静默失效**：`Assets/Editor/FTheseusShaderGUI/Data/FTheseus_Pbr_ShaderProperties.cs` 里是**硬编码字符串常量**（`"_albedoColor"`、`"_cutoff"`…）。shader 里名字不一致 → GUI 面板上调节不生效，且**不报错**，只能靠肉眼比对。
- **材质球数值不继承**：用 `_AlbedoColor` 的旧材质切到新 shader（写 `_albedoColor`）会全部丢值。
- **美术/TA 认知负担**：同一个「AO 强度」在不同 shader 叫 `_occlusionScale` / `_OcclusionScale` / `_AOStrength`，调参时反复试错。

存量统计（`Character` 目录 205 个 shader / 1067 个唯一名）：

| 现象 | 数量 |
|---|---|
| 仅大小写不同的重复 | **60 组 / 61 个非规范写法** |
| 溶解模块同义名 | 20+ 个（`_DissolveValue` / `_DissolveThreshold` / `_DissolveRange` / `_DissolveShrink` …） |
| 同一开关的不同关键字 | 6 组，最多 4 种拼写 |
| 拼音命名 | `_Sanshe_*`(例外保留) / `_Rongjie_*`(禁止) |

## 检出方法

```bash
bash tools/naming-lint.sh <新shader>            # 只查非白名单文件
bash tools/naming-lint.sh <目录> --legacy       # 连存量一起查（审计用，不阻断）
```

检查项：

| 编号 | 检查 | 级别 |
|---|---|---|
| R1 | 命中 `reference/variant-blacklist.txt`（大小写变体） | ❌ 错误 |
| R1b | 命中 `reference/typos.txt`（拼写错误 / 同义名） | ❌ 错误 |
| R2 | 拼音命名（`_Rongjie_*` / `_Yanse` / `_Qiangdu` …，`_Sanshe_*` 例外） | ❌ 错误 |
| R3 | 词中下划线（`_Dissolve_Tex` / `_Dissolve_Tiling_Speed` / `_xxx_TiSP`） | ⚠️ 提示 |
| R4 | 属性名全大写（Toggle keyword 名除外） | ⚠️ 提示 |
| R5 | Pascal 开头且不在规范名集合里（`_BrandNewParam` → `_brandNewParam`） | ⚠️ 提示 |
| R6 | **同一文件内同一功能出现两种写法**（`_AlbedoColor` + `_albedoColor`） | ❌ 错误 |
| R7 | keyword 用了 `_ENABLE_*` / `_SWITCH_*`，或该功能已有规范关键字 | ❌ / ⚠️ |

## 误报提示

- `_Sanshe_*` / `_Sanshe2_*` / `_LG_*` / `_AO_*` **不是误报而是既有例外**：C# 常量表（`k_Sanshe_colorPropName` 等）和 20 个 shader 在用，动了就断。新代码不要跟着扩散拼音命名。
- `_SpecularOcclusionLut3D` / `_DfgTexture` / `_ACESLutTex` / `_NormalIntensity` / `_UseFlowLight2U` / `_FlowLightTex` 等**本身就是规范名**（在 `canonical-names.txt` 里），Pascal 开头不算问题 —— 不要为了「统一小写」去改它们。
- 全大写属性名如果是 `[Toggle(_XXX)]` 绑定的 keyword 名（`_DISSOLVE_ON`、`_LASER_ON`、`_ALPHA_CLIP`），是合法写法。
- 存量族（`Theseus_Pbr_Crystal` / `Theseus_Crystal` / `Theseus_Water_Characters` / `Theseus_FireEye`）整族 Pascal 是历史事实，**白名单内不改**，新 shader 不沿用即可。

## 正例 / 反例

```hlsl
// ❌ 反例：三种写法混用 + 拼音 + 自造 Tiling
_AlbedoColor("基础色", Color) = (1,1,1,1)
_Rongjie_Fanwei("溶解范围", Float) = 1
_Dissolve_Tex("溶解图", 2D) = "white"{}
_Dissolve_Tiling_Speed("Tiling", Vector) = (0,0,0,0)

// ✅ 正例
_albedoColor("基础色", Color) = (1,1,1,1)
_dissolveEdgeRange("溶解边缘范围", Range(0.2, 10.0)) = 1
_dissolveTex("溶解纹理 (R:遮罩 G:纹路 B:方向扰动)", 2D) = "white"{}
// Tiling/Offset 不手写，Unity 自动生成 _dissolveTex_ST
```

```hlsl
// ❌ 反例
#pragma shader_feature _ENABLE_DISSOLVE
#pragma shader_feature _USE_DISSOLVE
// ✅ 正例（本项目规范）
#pragma shader_feature _DISSOLVE_ON
```

## 相关

- **P001**：如果新增的 uniform 要落到 `Assets/Shaders/ShaderLibrary/**`（比如加进 `ShadingCommon.hlsl`），**先 @朱瀚 审批**。
- **R001**：`#define` 开关的写法（裸定义）。
- 数据候选刷新：`bash tools/scan-uniformes.sh H:/MLBBTest/Assets/Shaders/ShaderTheseus/Character --update`。只更新 `reference/variant-candidates.txt`；`canonical-names.txt`、`variant-blacklist.txt`、`typos.txt` 均须人工确认，不会自动覆盖。
