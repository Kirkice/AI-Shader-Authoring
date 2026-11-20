# R002 — 切线符号不能在顶点和片元各乘一次

**级别**：🔴 高（负缩放/镜像物体上各向异性高光方向反了，正常物体上完全看不出来）

> 📍 **生效范围（大前提）**：只对 `Assets/Shaders/ShaderTheseus/**` 与 `Assets/Shaders/ShaderLibrary/**` 下的 shader 生效；范围外不检查、不报告。


## 规则

符号来源只有一个：`GetOddNegativeScale()`（等价于 `unity_WorldTransformParams.w` 的符号）。**整个 Pass 内只能乘一次**，而且"乘了几次"要跟着数据一起走。

| 顶点怎么写 `tangentWS.w` | 片元怎么用 | 结论 |
|---|---|---|
| `tangentOS.w * GetOddNegativeScale()`（或 `GetVertexNormalInputs`，其内部已乘） | 直接用 `tangentWS.w` | ✅ 正确 |
| `tangentOS.w * GetOddNegativeScale()` | 又乘 `unity_WorldTransformParams.w` 的符号 | ❌ **本规则命中** |
| `tangentOS.w`（原样，不乘） | 片元乘一次 `GetOddNegativeScale()` | ✅ 正确（Unity 经典写法） |
| `tangentOS.w`（原样） | 片元也不再乘 | ❌ 漏乘，镜像物体 bitangent 反向 |

**判定标准是"符号乘了几次"，不是"写了哪一行"。** 两次都写在顶点里也算命中（见下方 `Theseus_Pbr_Anisotropic.shader:282`）。

## 错误写法（真实代码）

`Theseus_Pbr_Anisotropic_Silk_Flowmap.shader`

```hlsl
// ── 顶点阶段：已经把符号乘进去了 ──
real sign = input.tangentOS.w * GetOddNegativeScale();
output.tangentWS = half4(normalInput.tangentWS.xyz, sign);      // tangentWS.w 已是 ±1

// ── 片元阶段：又乘了一次 ──
float tangentSign = (unity_WorldTransformParams.w >= 0.0) ? 1.0 : -1.0;
tangentSign = tangentSign * input.tangentWS.w;                  // ← 第二次乘
...
o.BitangentAnisotropic = cross(o.NormalWS.xyz, flowTangent) * tangentSign;
```

数学上：

```
tangentWS.w   = tangentOS.w * oddScale          (oddScale = sign(unity_WorldTransformParams.w) = ±1)
tangentSign   = sign(WTP.w) * tangentWS.w
              = sign(WTP.w) * tangentOS.w * oddScale
              = tangentOS.w                      ← oddScale 被自己抵消掉了
```

**结果**：负缩放（镜像）物体的矫正凭空消失，bitangent 手性取反 → 各向异性高光/丝绸方向在镜像物体上左右翻。正常（非镜像）物体上 `oddScale = 1`，'乘一次'和'乘两次'结果相同，所以**平时看不出问题，只在镜像/负缩放的挂件、左右对称部件、镜像身体上暴露**。

## 正确写法

```hlsl
// 顶点已有 sign = tangentOS.w * GetOddNegativeScale()
// 片元直接取，不要再乘 unity_WorldTransformParams
float tangentSign = input.tangentWS.w;      // 已经是 ±1
```

依据：工程内 `ShaderLibrary/Core/Packing.hlsl:624`、`Core/SpaceTransforms.hlsl:321` 的注释就写着 ——
`float sgn = tangentWS.w;  // should be either +1 or -1`，即 `tangentWS.w` 本应**已经包含** odd negative scale 的矫正，直接当符号用。

## 检测方法

自动：`bash tools/scan.sh <目标>` 的 **R002** 区块。

手工（判断依据，比 grep 更可靠）——找这一对连招：

```hlsl
float tangentSign = (unity_WorldTransformParams.w >= 0.0) ? 1.0 : -1.0;
tangentSign = tangentSign * <xxx>.tangentWS.w;
```

再确认文件里顶点侧确实已经把符号预乘进 `tangentWS.w`：
- 手写 `sign = input.tangentOS.w * GetOddNegativeScale();` → `tangentWS = half4(..., sign)`
- 或调用了 `GetVertexNormalInputs()`（`ShaderLibrary/Pipeline/Core.hlsl:141` 内部已乘）

```bash
# 精确特征（两行连招）
grep -rn -A3 -E 'tangentSign[[:space:]]*=[[:space:]]*\([[:space:]]*unity_WorldTransformParams\.w' <目标> \
  | grep -E 'tangentSign[[:space:]]*=[[:space:]]*tangentSign[[:space:]]*\*.*tangentWS\.w'
```

## 误报提示

- 顶点**只**乘一次（`cross(...) * v.tangentOS.w * unity_WorldTransformParams.w`）是**正确**的，因为那里的 `tangent.w` 是原始值，没有预乘。
- `CreateTangentToWorld(normal, tangent.xyz, tangentWS.w)` 这种传参：`CreateTangentToWorld` 内部会再乘一次 `GetOddNegativeScale()`（`SpaceTransforms.hlsl:199`），所以传进去的必须是**原始** `tangent.w`。工程里 `BattleHeroPass.hlsl:49` 就是 `OUT.tangentWS.w = IN.tangent.w;`（原样）→ 自洽。**若某处把这个惯例和"预乘"惯例混用，同样是本规则命中**，需要按整条链路确认。
- `ParallaxMapping.hlsl:13` 的注释 `we do not need to multiple GetOddNegativeScale() here, as it is done in vertex shader` 是同一约定，别在那里再乘。

## 存量命中清单（已在白名单，**既往不咎，不要修**）

2026-10-29 扫描 `Assets/Shaders`：**42 个文件**，全部集中在各向异性/头发（Anisotropic / Hair）族。

> ⚠️ 这 42 个文件全部已在 `whitelist/legacy-paths.txt` 中。它们已上线稳定运行，**任何"修复"都属于新增线上风险，不要动**。
> 列出它们的目的：① 记录规则来源；② 提醒 **新建变体时不要照抄这段代码**（新文件不在白名单，必须按正确写法写）。

> 用户最初提到的 `Theseus_Pbr_Anisotropic_Silk_FlowGlitter` 在当前工程里已找不到同名文件（可能已改名或删除），同族的 `Silk_Flowmap`、`MultiSpecular_Silk`、`MultiSpecular_Silk_Dissolve` 均已命中。

```
FTheseus/FTheseus_Pbr_Hair(Anisotropic).shader

ShaderTheseus/Character/Theseus_NoACES/
  Theseus_Pbr_Anisotropic_MultiSpecular_2U.shader

ShaderTheseus/Character/Theseus_Pbr_Anisotropic/
  Pbr_Anisotropic_Pass.hlsl
  Pbr_Anisotropic_Pass_76Only.hlsl
  PCSSAO_Pbr_Anisotropic(MultiSpecular).shader
  PCSSAO_Pbr_Anisotropic(MultiSpecular)_Fman.shader
  PCSSAO_Pbr_Anisotropic(MultiSpecular)_Worf.shader
  PCSSAO_Pbr_Anisotropic_RimLight.shader
  Theseus_Pbr_Anisotropic.shader
  Theseus_Pbr_Anisotropic(MultiSpecular).shader
  Theseus_Pbr_Anisotropic_ChangeCol.shader
  Theseus_Pbr_Anisotropic_Clip.shader
  Theseus_Pbr_Anisotropic_Dissolve.shader
  Theseus_Pbr_Anisotropic_DoubleFlowLight_Glitter.shader
  Theseus_Pbr_Anisotropic_DoubleFlowLight_Glitter_DetailNormal.shader
  Theseus_Pbr_Anisotropic_FlowChangeColor.shader
  Theseus_Pbr_Anisotropic_FlowLight_Glitter_ButtonColorChang_Dissolve.shader
  Theseus_Pbr_Anisotropic_FlowLight_Glitter_ColorChang.shader
  Theseus_Pbr_Anisotropic_GradientFlowHair.shader
  Theseus_Pbr_Anisotropic_GradientFlowHair_ColorChang.shader
  Theseus_Pbr_Anisotropic_GradientFlowHair_Simple.shader
  Theseus_Pbr_Anisotropic_HairColorChang.shader
  Theseus_Pbr_Anisotropic_MendsLight.shader
  Theseus_Pbr_Anisotropic_MultiSpecular_AnisotropicMask.shader
  Theseus_Pbr_Anisotropic_MultiSpecular_MendLight.shader
  Theseus_Pbr_Anisotropic_MultiSpecular_Silk.shader
  Theseus_Pbr_Anisotropic_MultiSpecular_Silk_Dissolve.shader
  Theseus_Pbr_Anisotropic_RasterCard.shader
  Theseus_Pbr_Anisotropic_RimLight.shader
  Theseus_Pbr_Anisotropic_Silk_Flowmap.shader
  Theseus_Pbr_Anisotropic_SwapUV.shader
  Theseus_Pbr_Anisotropic_Transparent.shader
  Theseus_Pbr_Hair.shader
  Theseus_Pbr_Hair_FlowLight_Glitter_ColorChange.shader
  Theseus_Pbr_Hair_SF.shader
  Theseus_Pbr_Hair_SF_Clip.shader

ShaderTheseus/Character/Theseus_Pbr_Hair_WitdhControl/
  Theseus_PBR_Hair_WidthControl.shader
  Theseus_PBR_Hair_WidthControl_Blend.shader
  Theseus_PBR_Hair_WidthControl_Blend_Dissolve.shader
  Theseus_PBR_Hair_WidthControl_Dissolve.shader
  Theseus_PBR_Hair_WidthControl_V2.shader

ShaderTheseus/Character/Theseus_Pbr_Initial/
  Theseus_Pbr_Anisotropic(MultiSpecular)_Initial.shader
```

由于是复制传播（同一段代码在几十个变体里复制），很容易为了"和原文件保持一致"继续复制这个 bug。

- **存量文件（上面这 42 个）**：不改。
- **未来新建的变体**：必须写成 `float tangentSign = input.tangentWS.w;`，不要抄存量写法。新文件不在白名单里，`tools/scan.sh` 会把它查出来。

## 验证方法

1. 拿一个**负缩放**（Scale 里某个轴为 -1，或父节点镜像）的角色部件，各向异性高光方向在修复前后应不同。
2. 或者临时把 `unity_WorldTransformParams.w` 相关项可视化：`tangentSign` 在修复前对负缩放物体恒等于 `tangentOS.w`（丢掉矫正），修复后等于 `tangentOS.w * oddScale`。
