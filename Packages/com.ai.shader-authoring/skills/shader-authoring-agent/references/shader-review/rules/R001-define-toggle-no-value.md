# R001 — `#define` 功能开关一律写裸定义

**级别**：🔴 高（会让人误判"开关关掉了"，实际是开着的）

> 📍 **生效范围（大前提）**：只对 `Assets/Shaders/ShaderTheseus/**` 与 `Assets/Shaders/ShaderLibrary/**` 下的 shader 生效；范围外不检查、不报告。


## 规则

```hlsl
// ✅ 新代码统一这么写（不需要加 1）
#define _MULTI_SPECULAR

// ❌ 禁止：0 看起来像"关闭"，但 defined() 判定下它等于"开启"
#define _MULTI_SPECULAR 0

// ❌ 新代码不要写 1（历史代码保持原状，不强改）
#define _MULTI_SPECULAR 1
```

## 为什么

1. **`0` 不等于关闭**。本工程所有开关宏都用 `#ifdef _XXX` / `#if defined(_XXX)` 判定，`#define _XXX 0` 依然"已定义"，分支照旧进去。写 `0` 的人**主观上想关闭**，客观上却是开启的 —— 这是最危险的一类误导，曾导致"高光模式开错了却查不出原因"的排查。
2. **裸定义 + `#if _XXX` 会编译报错**。所以只要统一用裸定义，就必须、也只能用 `#ifdef` / `#if defined()` 判定，从机制上堵死"按值判断"的写法：
   ```
   error: #if with no expression      // 裸定义展开为空，预处理器看到空表达式
   ```
   （而 `#define _XXX 0` + `#if _XXX` 是能编过的，会走 false 分支 —— 保留 `0` 就等于保留了这个歧义入口。）
3. **便于 grep**。`#define _MULTI_SPECULAR 0` 和 `#define _MULTI_SPECULAR 1` 混在一起时，没法用一条 grep 判断"有多少个开关是开的"；裸定义只有一个形态。

## 检测方法

```bash
# 高危：值为 0 的 define（逐个人工确认，排除 FUR_OFFSET 0.00 这类浮点参数）
grep -rnE '^[[:space:]]*#[[:space:]]*define[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]+0[[:space:]]*(//.*)?$' <目标>

# 提示：值为 1 的 define（新文件中出现则建议改成裸定义，存量不改）
grep -rnE '^[[:space:]]*#[[:space:]]*define[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]+1[[:space:]]*(//.*)?$' <目标>
```

判定顺序：

1. 命中 `值为 0` → 先看这个宏是不是**分层/强度类数值参数**（如 `FurMultiplierLayer 0`、`NoiseFadeLayer 0`），是则放行。
2. 不是数值参数、且该宏在代码里用 `defined()` 判定 → **报 🔴**：这个开关实际是开的。
3. 命中 `值为 1` 且是**新写文件** → 报 🟡 建议改成裸定义；存量文件不动。

## 误报提示

- `#define INV_PI 0.31830...`、`#define FUR_OFFSET 0.00`、`#define HALF_MIN_SQRT_SQRT 0.0883` 这类是**浮点数值**，不是 0，放行。
- 被注释掉的 `// #define CLOTH_LASER 0` 不生效，最多提一句清理，不报错。
- 头文件保护 `#define XXX_INCLUDED`、`#define SHADER_NEED_XXX` 本来就是裸定义，属于正确形态，不用改。

## 存量现状（2026-10 扫描 `Assets/Shaders`）

- 值为 `0`：48 处（其中真正的开关型误写极少，绝大多数是层参数/浮点常量）
- 值为 `1`：417 处
- 裸定义：849 处

**策略**：存量不强制批量改（改动面大、风险高），只在**新写或正在修改的文件**上执行本规则；存量中 `值为 0` 的开关型宏单独报出来给人确认。

## 真实案例（存量，仅作规则来源记录）

> ⚠️ 下面这个文件已在 `whitelist/legacy-paths.txt` 里，**按约定不修**。留着是为了说清楚这条规则怎么来的。

- `ShaderTheseus/Character/Theseus_Pbr_Anisotropic/Theseus_Pbr_Anisotropic_Silk_Flowmap.shader:87`
  ```hlsl
  // 先尝试单层高光能否满足需求
  #define _MULTI_SPECULAR 0
  ```
  全库 `_MULTI_SPECULAR` 共 30 处，29 处写 `1`，只有这处写 `0`。而判定是 `#if defined(_MULTI_SPECULAR)`，所以这处**是开启的**，注释写的意图与实际行为相反。
