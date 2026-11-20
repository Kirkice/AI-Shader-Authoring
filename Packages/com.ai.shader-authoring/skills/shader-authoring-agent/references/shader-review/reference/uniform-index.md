# Uniform 命名索引（Character Shader 功能模块 → 规范名）

> 数据来源：`Assets/Shaders/ShaderTheseus/Character` 下 **205 个 .shader** 的 `Properties` 块，
> 共 **9243 条属性声明 / 1067 个不同 uniform 名**（2026-10-29 统计）。
> 交叉校验源：`Assets/Editor/FTheseusShaderGUI/Data/FTheseus_Pbr_ShaderProperties.cs`（ShaderGUI 实际写入用的常量表）。

## 怎么用

1. 写新 shader 加功能 → **先在下面找到对应模块**，直接用「规范名」，不要自己起名。
2. 找不到完全对应的模块 → 用**最近的模块的前缀** + 现有词汇表里的词，别造新词。
3. 命名完跑一遍：`bash tools/naming-lint.sh <新shader>`，黑名单命中会直接报出来。
4. 拿不准就查本文件，不要凭记忆 —— 工程里同一个概念最多有 **4 种拼写**。

## 命名硬规则

| # | 规则 | 反例 | 正例 |
|---|---|---|---|
| 1 | 一律 `_` + **lowerCamelCase**；不允许全大写、不允许 PascalCase | `_AlbedoColor` `_SSSIntensity` | `_albedoColor` `_sssIntensity` |
| 2 | **禁止拼音** | `_Rongjie_Fanwei` `_DissolveColor_Power`(「拖尾」本意) | `_dissolveEdgeRange` |
| 3 | 禁止词中间加 `_` 分段（除 `_XXX_low` 这类既有特例） | `_Dissolve_Tex` `_Dissolve_Tiling_Speed` | `_dissolveTex` |
| 4 | 同一 shader 内风格必须统一；**不同后缀不许混用** | `_DissolveTex` + `_Dissolve_Tiling_Speed` 并存 | 统一 `_dissolveTex` |
| 5 | 第二套/第三套参数用后缀 `2` / `2nd` / `3`，不要用 `_01` `_02` | `_sunShift_01` `_sunShift_02` | `_sunShift` / `_sunShift2nd` |
| 6 | 贴图 Tiling/Offset 用 Unity 约定 `_xxx_ST`，不要自造 | `_Dissolve_TiSP` `_Tiling_Offset` `_DissolveTexTransform` | `_dissolveTex_ST` |
| 7 | 多通道贴图必须把通道语义写进 Display Name | `_DissolveTex("溶解纹理", 2D)` | `_dissolveTex("溶解纹理 (R:遮罩 G:纹路 B:扰动)", 2D)` |
| 8 | 0~1 的强度/范围用 `Range(...)`，纯开关/枚举用 `Float`/`int` | `_metallicMultiplier("金属度", Float)` | `_metallicMultiplier("金属度", Range(0.0,1.0))` |
| 9 | 同一语义只允许一个名字，**已存在就复用，不许新起同义词** | 已有 `_cutoff` 又新写 `_dissolveAmount` | 复用 `_cutoff` |
| 10 | 新增 uniform 前先 grep 全库，确认不是又造了一个同义名 | — | `grep -rn "_xxx" Assets/Shaders/ShaderTheseus/Character` |

> 规则 1 的注意点：存量 `Theseus_Pbr_Crystal` / `Theseus_Crystal` / `Theseus_Water_Characters` / `Theseus_FireEye`
> 整族用 PascalCase（94%~100%），**新 shader 不沿用**，但也不要为了统一去改它们（白名单，既往不咎）。

---

## 模块索引

### M01 基础表面 / RMO —— 出现 **133** 个文件

最核心的一块，几乎每个 PBR 角色 shader 都有。

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_albedoMap` | 2D | `"white"{}` | 基础色贴图（`[Tex][NoScaleOffset]`） | 25 |
| `_albedoColor` | Color | `(1,1,1,1)` | 基础色染色（`[HDR]`，与贴图 `.w` 遮罩相乘） | 123 |
| `_materialParamsMap` | 2D | `"white"{}` | **RMO 合并图**：`x=粗糙度 y=金属度 z=AO w=AlbedoColor遮罩` | 35 |
| `_metallicMultiplier` | Range(0,1) | — | 金属度倍率（× `RMO.y`） | 117 |
| `_roughnessMultiplier` | Range(0,1) | — | 粗糙度倍率（× `RMO.x`） | 119 |
| `_normalMap` | 2D | `"bump"{}` | 切线空间法线（`[Tex][NoScaleOffset]`） | 27 |
| `_NormalIntensity` | Range(0,2) | — | 法线强度（**注意：本名首字母大写，为存量高频写法**） | 20 |
| `_detailNormalMap` | 2D | `"bump"{}` | 细节法线 | 1 |
| `_detailNormalIntensity` | Range | — | 细节法线强度 | 3 |
| `_emissiveMap` | 2D | `"white"{}` | 自发光贴图 | 27 |
| `_emissiveColor` | Color | — | 自发光染色（`[HDR]`） | 126 |
| `_emissiveBreathe` | Vector | `(0,0,0,0)` | 自发光呼吸：`xyz=幅度 w=速度` | 38 |

**禁用变体**：`_AlbedoMap` `_AlbedoColor` `_MaterialParamsMap` `_MetallicMultiplier` `_RoughnessMultiplier` `_NormalMap` `_EmissiveMap` `_EmissiveColor` `_EmissiveBreathe` `_metallic` `_metallicMultiplierSimple` `_Metal_Intensity` `_Metal_Rough_Skin`(通道图，另议)

---

### M02 光照标定 / 环境 BRDF —— 出现 **167** 个文件

这一组是**全项目共享的标定参数**，改它们等于改所有角色。真正声明在
`ShaderLibrary/Pipeline/ShadingCommon.hlsl`（**受 P001 保护，改动必须 @朱瀚**）。

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_localDiffuseGI` | Vector | `(1,1,1,1)` | 本地漫反射 GI 强度缩放，取 `.xyz` | 167 |
| `_indirectSpecularIntensityScale` | Vector | `(1,1,1,1)` | 间接光高光：`xyz=强度缩放`（平方后乘），`w>0` 时额外用漫反射 GI 去饱和 | 162 |
| `_DfgTexture` | 2D | `"black"{}` | BRDF DFG LUT（`R=scale G=bias`） | 167 |
| `_SpecularOcclusionLut3D` | 2D | — | 高光遮挡 LUT（cone BRDF 积分） | 167 |
| `_ACESLutTex` | 2D | `"white"{}` | ACES 色调映射 LUT | 191 |
| `_specularAlphaMode` | float | `1` | 高光是否混入 alpha（`>0` 时 `alpha += specularLuminance`，用于透明高光） | 115 |

**禁用变体**：`_LocalDiffuseGI` `_localDiffuseGi` `_IndirectSpecularIntensityScale` `_DfgTex` `_SpecularOcclusionLut3d`

---

### M03 阴影与遮蔽 —— 出现 **144** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_shadowStrengthMap` | 2D | `"white"{}` | 阴影强度遮罩，取 **`.rgb` 三通道分别对应三方向阴影** | 108 |
| `_shadowStrength` | Range(0,3) | — | 阴影整体强度 | 120 |
| `_shadowColor` | Color | `(0,0,0,0)` | 阴影染色 | 121 |
| `_occlusionScale` | Range(0,1) | — | AO 强度倍率（× `RMO.z`） | 124 |

**禁用变体**：`_ShadowStrengthMap` `_ShadowStrength` `_ShadowColor` `_OcclusionScale`

---

### M04 渲染状态（ShaderGUI 驱动）—— 出现 **111** 个文件

由 `FTheseus_Pbr_ShaderProperties.cs` 的 `k_*` 常量直接写入，**名字改了 GUI 就失效**。

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_renderingMode` | Float/int | `0` | 渲染模式枚举（Opaque/Cutout/Transparent…） | 111 |
| `_cutoff` | Range(0,1) | — | Alpha 裁剪阈值（GUI 显示为 AlphaCut） | 112 |
| `_cull` | Float | `2.0` | 剔除模式（0=Off 1=Front 2=Back） | 87 |
| `_srcblend` | Float | `1.0` | 源混合 RGB（One） | 66 |
| `_dstblend` | Float | `0.0` | 目标混合 RGB（Zero） | 66 |
| `_srcblendalpha` | Float | `1.0` | 源混合 Alpha | 66 |
| `_dstblendalpha` | Float | `0.0` | 目标混合 Alpha | 66 |
| `_zwrite` | Float | `1.0` | 深度写入 | 89 |
| `_alphatomask` | Float | — | AlphaToCoverage | 106 |

**禁用变体**：`_Cull` `_Cutoff` `_ZWrite`(=Pascal 变体，C# 表里用的正是 `_ZWrite`，**但 shader 侧高频是 `_zwrite`，两者都存在** — 新增请用 `_zwrite`，除非你复用 GUI 常量)
> ⚠️ `_cutoff` 语义冲突：部分 shader 用它做**溶解进度**（Display Name 写「溶解」），
> 这属于已有滥用。新 shader 的溶解进度**必须用 `_dissolveAmount` 这类明确名字**，不要再借 `_cutoff`。

---

### M05 流光 FlowLight —— 出现 **118** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_FlowLightTex` | 2D | `"white"{}` | 流光图：`RGB=颜色 A=遮罩` | 97 |
| `_FlowLightMask` | 2D | `"white"{}` | 流光遮罩：`RGB` 分通道；部分 shader `R=上层 G=下层` | 65 |
| `_FlowLightColor` | Color | — | 流光染色 | 104 |
| `_FlowLightFactory` | Vector | `(1,0,0,0)` | **`x=强度` `yz=UV流速`** | 103 |
| `_UseFlowLight2U` | float | `0` | 是否用 UV2 做流光 | 113 |
| `_FlowLightTex_ST` | Vector | — | 自动生成，勿手写 Properties | 204 |

**禁用变体（同一功能 4 种开关拼写！）**：
`_USE_FLOW_LIGHT`(63) / `_USE_FLOW_LIGHT_ON`(17) / `_USE_FLOWLIGHT`(3) / `_USE_FLOW_LIGHT2`(1)
→ 新 shader 统一用 **`_USE_FLOW_LIGHT`**。
另：`_FlowLightUpTex` / `_FlowLightDownTex` / `_FlowLightMap` / `_FlowLightTwoTex` 是双层/旧版实现的重复造词，新代码不要再用。

---

### M06 镭射 Laser —— 出现 **61** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_LaserMask` | 2D | `"white"{}` | **`R=镭射遮罩` `G=Ramp 索引`** | 42 |
| `_LaserRamp` | 2D | `"black"{}` | 镭射渐变色带（按 `G` 通道索引纵向采样） | 50 |
| `_LaserRampIntensity` | Float | `1` | 镭射强度 | 52 |
| `_LaserColor` | Color | — | 镭射染色 | 52 |

**禁用变体**：`_laserColor` `_laserIntensity` `_LaserIntensity` `_LaserTex`/`_LaserCubemap`（旧实现）
**开关**：`_LASER_ON`(41) 优于 `_USE_LASER_ON`(2)

---

### M07 闪点 Glitter —— 出现 **44** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_GlitterMap` | 2D | — | 闪点采样图 | 16 |
| `_GlitteryControl` | Vector | — | **`x=uv1 Tiling` `y=uv2 Tiling` `z=对比度` `w=颜色强度`** | 16 |
| `_GlitterySPColor` | Color | — | 闪点颜色 | 16 |
| `_GlitterIntensity` | Range(0,10) | — | 闪点强度 | 28 |
| `_GlitterContrast` | Range(0,50) | — | 闪点对比度 | 28 |
| `_GlitterScale` | Range(0,100) | — | 闪点缩放 | 28 |
| `_Glitter_Offset` | Range(0,1) | — | 闪点相对流光的偏移率 | 16 |
| `_LGMaskedGlitter` | Float | — | 闪点只在流光区域显示 | 1 |

**禁用变体**：`_glitterMap` `_glitteryControl` `_glitterySPColor` `_GlitterIntensity`(旧式全大驼峰无前缀，与新 `_Glitter*` 混用需谨慎)
**开关（4 种拼写）**：`_USE_GLITTERY`(17) / `_USE_GLITTER`(5) / `_USE_GLITTER_ON`(2) / `_LG_ON`(4) → 统一 **`_USE_GLITTERY`**

---

### M08 溶解 Dissolve —— 出现 **147** 个文件，也是**命名最乱的重灾区**

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_dissolveTex` | 2D | `"white"{}` | **`R=遮罩` `G=纹路/第二套` `B=方向扰动`** | 35（现名 `_DissolveTex`） |
| `_dissolveTex_ST` | Vector | — | Tiling/Offset | 112 |
| `_dissolveAmount` | Range(0,1) | — | 溶解进度（新代码用这个；存量多用 `_cutoff`） | 0（**待立**） |
| `_dissolveDirSpeed` | Vector | `(1,0,0,0)` | **`x=方向` `y=?` `zw=UV流速`** | 27 |
| `_dissolveEdgeRange` | Range(0.2,10) | — | 边缘过渡范围 | 18 |
| `_dissolveEdgeShrink` | Float | `6` | 边缘压缩 | 19 |
| `_dissolveEdgeColor` | Color | — | 边缘颜色 | 27 |
| `_dissolveEdgeHard` | Range(0,0.5) | — | 边缘硬度 | 9 |
| `_UseDissolve2U` | Float | `0` | 是否用 UV2 做溶解 | 22 |

**禁用变体（重点）**：
- 下划线分段式：`_Dissolve_Tex`(6) / `_Dissolve_TiSP`(3) / `_Dissolve_TiSp`(1) / `_Dissolve_Tiling_Speed`(3) / `_Dissolve_Dir`(1) / `_DissolveNoise_TiSp`(1)
- 拼音式：`_Rongjie_Color_Fanwei`(3) / `_Rongjie_Fanwei`(2) / `_Rongjie_saoguang_liangbian`(1) / `_Rongjie_Tiling_Speed`(1)
- 拼写错误：`_Dissovle_Tiling_Offset`
- 同义重复：`_DissolveValue` / `_DissolveThreshold` / `_albedoDissolveThreshold` / `_DissolveRange` / `_DissolveRangeSize` / `_DissolveShrink` / `_DissolveEdgeCompression`
- 开关（4 种）：`_DISSOLVE_ON`(3) / `_USE_DISSOLVE`(3) / `_ENABLE_DISSOLVE`(2) / `_SWITCH_DISSOLVE_MODE_ON`(1)

---

### M09 各向异性 Anisotropic / 丝绸高光 —— 出现 **42** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_anisotropicMap` | 2D | `"white"{}` | **`.xy` = specularShift（法线扰动的各向异性方向）** | 32 |
| `_anisotropicMultiplier` | Range(0,1) | — | 主高光各向异性强度 | 30 |
| `_anisotropicMultiplier2nd` | Range(0,1) | — | 次高光各向异性强度 | 20 |
| `_sunShift` | float | `1` | 主高光切线扭曲量 | 34 |
| `_sunShiftOffset` | float | `1` | 主高光切线偏移 | 35 |
| `_sunShift2nd` | float | `1` | 次高光扭曲（配合 `_MULTI_SPECULAR`） | 25 |
| `_sunShiftOffset2nd` | float | `1` | 次高光偏移 | 25 |
| `_anisoUse2U` | float | — | 各向异性贴图用 UV2 | 2 |
| `_directSpecularColor` | Color | — | 主直射高光颜色 | 127 |
| `_directSpecularColor2nd` | Color | — | 次直射高光颜色 | 25 |

**禁用变体**：`_AnisotropicMap` `_AnisotropicTex` `_SunShift` `_SunShiftOffset` `_sunShift_01` `_sunShift_02` `_SunShift2nd` `_AnisotropicMultiplier` `_AnisotropicMultiplier2nd`
**同义重复**：`_AnisotropicStrength_1` `_SilkAnisotropy` `_AnisotropyIntensity` `_AnisotropyRange` `_AnisotropyDistort` `_AnisotropyOffset` `_MinorAnisotropy*` / `_Anisotropy*`（一整套平行命名，新代码禁止再用）
> 🔴 该组对应 **R002**（切线符号乘两次）的 42 个命中文件，改写时不要连 bug 一起抄。

---

### M10 3S / 次表面散射 —— 出现 **30** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_sssColorBase` | Color | `(1,1,1,1)` | 3S 基础色（正面） | 19 |
| `_sssColorBack` | Color | `(1,1,1,1)` | 3S 背面透光色 | 19 |
| `_sssColorOcc` | Color | `(1,1,1,1)` | 3S 遮蔽处颜色 | 19 |
| `_sssIntensity` | Range(0,3) | — | 3S 强度 | 19 |
| `_sssShadowColor` | Color | — | SSS 阴影色 | 3 |
| `_sssShadowStrength` | Range | — | SSS 阴影强度 | 3 |
| `_skinMap` | 2D | — | 皮肤遮罩（`B` 通道常用于 3S） | 19 |

**禁用变体**：`_SSSColorBase` `_SSSColorBack` `_SSSColorOcc` `_SSSIntensity` `_newSSSIntensity` `_SSSColor`
**开关**：`_SKIN_SSSS_ON`

---

### M11 菲涅尔 / 边缘光 Fresnel · Rim —— 出现 **62** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_FresnelColor` | Color | — | 菲涅尔颜色 | 44 |
| `_FresnelPower` | Range(0,10) | — | 菲涅尔收束范围（指数） | 27 |
| `_FresnelScale` | Float | `0.5` | 菲涅尔强度/阈值 | 22 |
| `_FresnelVector` | Vector | — | 手绘菲涅尔打包参数（具体通道见各 shader Display Name） | 12 |
| `_FresnelRotation` | Float | — | 菲涅尔方向旋转 | 14 |
| `_RimLightColor` | Color | — | 边缘光颜色 | 16 |
| `_RimLightPower` | Range(0,32) | — | 边缘光范围 | 17 |
| `_RimLightRotation` | Float | — | 边缘光旋转 | 14 |
| `_outlineColor` | Color | — | 描边颜色 | 7 |
| `_outlineWidth` | Range/Float | — | 描边宽度 | 5 |

**禁用变体（本组最碎）**：
`_fresnelColor` `_fresnelCol` `_fresnelPower` `_fresnelPow` `_fresnelInt` `_FresnelSacle`(拼错) `_FresnelSacle` `_FresnelRange`/`_fresnelRange`
`_Rim_Color` `_RimColor` `_Rim_Intensity` `_RimIntensity` `_RimPower` `_Rim_Ramp` `_RimRamp` `_Rim_Tex` `_RimTex`
`_OutlineWidth` `_OutlineColor`
> 菲涅尔 vs 边缘光：两者在工程里是**不同模块**（菲涅尔=视角相关遮罩，边缘光=独立补的一圈光）。
> 新代码不要用 `_Fresnel*` 去实现边缘光，也不要反过来。

---

### M12 补光 / 散射 Sanshe · MendsLight —— 出现 **24** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_Sanshe_color` | Color | `(0.5,0.5,0.5,1)` | 补光 1 颜色 | 20 |
| `_Sanshe_Fw` | Range(0.001,10) | — | 补光 1 范围 | 20 |
| `_Sanshe_Power` | Float | `0` | 补光 1 强度 | 20 |
| `_Sanshe_X` / `_Sanshe_Y` | Range(-1,1) | — | 补光 1 屏幕方向偏移 | 20 |
| `_Sanshe2_*` | 同上 | — | 补光 2 同构参数 | 20 |
| `_SansheMask` | 2D | `"white"{}` | 补光遮罩 | 12 |
| `_MendsLightDirection` | Vector | `(1,0,1,0)` | 补光方向 | 17 |
| `_MendsLightFallOff` | Range(0,1) | — | 补光衰减 | 17 |
| `_MendsLightColor` | Color | — | 补光颜色 | 17 |
| `_MendsLightDirection2` / `_MendsLightFallOff2` | — | — | 第二套同构参数 | 17 |

> ⚠️ **拼音遗留**：`_Sanshe_*` 是「散射」的拼音，`_Fw` 是「范围」的拼音首字母。
> 因为已有 20 个文件 + C# 常量表（`k_Sanshe_colorPropName` 等）在用，**作为例外保留**，
> 但新代码不得再扩散拼音命名（不许出现 `_Yanse` `_Qiangdu` 之类）。
> 两套命名（`_Sanshe_*` 与 `_MendsLight_*`）语义重叠，新 shader 二选一 —— 优先 `_MendsLight*`（英文、可读）。

**开关**：`_DIRECT_SANSHE`(12) / `_SANSHE_ON`(8) / `_USE_MENDS_LIGHT`(11) → 用 `_MendsLight*` 参数时配 `_USE_MENDS_LIGHT`

---

### M13 环境反射 CubeMap —— 出现 **23** 个文件

| 规范名 | 类型 | 默认 | 语义 | 文件数 |
|---|---|---|---|---|
| `_CubeMap` | Cube | `"white"{}` | 环境反射 cubemap | 17 |
| `_CubeMapMip` | Range(1,7) | — | 采样 Mip 等级 | 16 |
| `_CubeMapIntensity` | Range(0,5) | — | 反射强度 | 16 |
| `_CubeMapMask` | 2D | — | 反射遮罩 | 1 |

**禁用变体**：`_Cubemap` `_cubeMap` `_cubeMapMip` `_cubeMapIntensity` `_Cube_Intensity` `_CubeIntensity` `_CubeColor`

---

### M14 顶点/其它通用

| 规范名 | 类型 | 语义 | 文件数 |
|---|---|---|---|
| `_VertexEffect` | Float | 顶点特效总开关 | — |
| `_VertexWaveSpeed` / `_VertexWaveHeight` | Float | 顶点波动 | — |
| `_VisibilityBake` | Float | 可见性烘焙开关 | — |

---

## 关键字（shader_feature / multi_compile）命名

工程现状**也是乱的**，同一个开关最多 4 种拼写：

| 功能 | 现有关键字（出现次数） | 规范 |
|---|---|---|
| 流光 | `_USE_FLOW_LIGHT`(63) / `_USE_FLOW_LIGHT_ON`(17) / `_USE_FLOWLIGHT`(3) / `_USE_FLOW_LIGHT2`(1) | `_USE_FLOW_LIGHT` |
| 闪点 | `_USE_GLITTERY`(17) / `_USE_GLITTER`(5) / `_LG_ON`(4) / `_USE_GLITTER_ON`(2) | `_USE_GLITTERY` |
| 镭射 | `_LASER_ON`(41) / `_USE_LASER_ON`(2) | `_LASER_ON` |
| 溶解 | `_DISSOLVE_ON`(3) / `_USE_DISSOLVE`(3) / `_ENABLE_DISSOLVE`(2) / `_SWITCH_DISSOLVE_MODE_ON`(1) | `_DISSOLVE_ON` |
| 补光 | `_USE_MENDS_LIGHT`(11) / `_DIRECT_SANSHE`(12) / `_SANSHE_ON`(8) | 按参数组选：`_MendsLight*` → `_USE_MENDS_LIGHT` |
| 双流层 | `_USE_DOUBLE_FLOWLIGHT`(7) | `_USE_DOUBLE_FLOWLIGHT` |
| Alpha Clip | `_ALPHA_CLIP`(62) | `_ALPHA_CLIP` |
| 自发光呼吸 | `_EMISSIVE_BREATHE`(29) | `_EMISSIVE_BREATHE` |
| 画质档 | `_RENDER_QUALITY_LOW`(195) | `_RENDER_QUALITY_LOW` |
| 光照模式 | `MODE_UNITY`(189) / `MODE_CUSTOM`(189) | `MODE_*` |
| 调试 | `DEBUGMODE`(232) | `DEBUGMODE` |

**命名规则**：

1. 功能开关 = `_USE_<MODULE>` 全大写下划线（`_USE_FLOW_LIGHT`、`_USE_GLITTERY`）
2. 已有短形式且占主导的保持（`_LASER_ON`、`_DISSOLVE_ON`、`_ALPHA_CLIP`）
3. 禁止 `_ENABLE_` / `_SWITCH_` / `_XXX_ON_ON` 之类新造前缀
4. 与属性同名的开关（`_USE_FLOW_LIGHT` 同时又出现在 Properties 里，30 处）**不要学** ——
   开关只做 keyword，走 `[Toggle(_USE_FLOW_LIGHT)]` 或 `[Toggle]` + 属性名分开
5. 宏定义写法遵守 **R001**：裸定义，不写 `0`/`1`

---

## 贴图 Tiling / Offset 约定

| 写法 | 文件数 | 结论 |
|---|---|---|
| `_xxx_ST`（Unity 自动生成） | `_FlowLightTex_ST`(204) `_DissolveTex_ST`(112) `_anisotropicMap_ST`(110) `_LaserRamp_ST`(99) … | ✅ **规范** |
| `_xxx_TiSP` / `_xxx_TiSp` | 18 / 12 | ❌ 旧实现，勿用 |
| `_xxx_Tiling_Speed` / `_xxx_Tiling_Offset` | 26 / 32 | ❌ 中文式命名，勿用 |
| `_xxxTransform` / `_xxxScale` | 各 1 | ❌ |

> `_xxx_ST` 由 Unity 在声明 `_xxx` 贴图时自动生成（`TRANSFORM_TEX`），
> **不要在 Properties 里手写**，也不要在 HLSL 里重复声明。

## 采样器约定

统一 `SAMPLER(sampler_<贴图名去下划线>)`：
```hlsl
TEXTURE2D(_dissolveTex);  SAMPLER(sampler_dissolveTex);
```
存量里 `sampler__DissolveTex`（双下划线）、`sampler_DissolveTexture` 之类的错配属于历史问题。

---

## 现状治理清单

| 问题 | 数量 | 处置 |
|---|---|---|
| 大小写重复的 uniform 对 | **61 对** | 新代码用 `reference/variant-blacklist.txt` 里的「规范名」 |
| 拼音命名 | 8 个（`_Sanshe_*` 6 + `_Rongjie_*` 4，含重叠） | `_Sanshe_*` 例外保留；`_Rongjie_*` 禁止沿用 |
| 同一开关多拼写 | 6 组，最多 4 种 | 见「关键字命名」表 |
| 溶解模块同义名 | 20+ 个 | 新代码收敛到 8 个规范名 |
| 各向异性平行命名体系 | 2 套（`_Aniso*` / `_Silk*` / `_Anisotropy*`） | 统一用 `_anisotropic*` + `_sunShift*` |

> 存量一律不改（白名单）。本文件只管**新代码**。

## 重新生成统计

```bash
# 重新统计 Character 目录的 Properties（需要 python）
bash tools/scan-uniformes.sh "H:/MLBBTest/Assets/Shaders/ShaderTheseus/Character"
```
