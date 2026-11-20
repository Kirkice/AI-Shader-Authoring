
# Shader 历史问题检查（MLBB / Theseus）

团队开发过程中真实踩过的坑，固化成检查项。**规则会持续追加**。

---

## ⛔ 大前提：生效范围

本 skill **只对下面两个目录下的 shader 起作用**，其余目录一概不管：

```
H:/MLBBTest/Assets/Shaders/ShaderTheseus/**
H:/MLBBTest/Assets/Shaders/ShaderLibrary/**
```

| 情况 | 行为 |
|---|---|
| 目标在 `ShaderTheseus/` 或 `ShaderLibrary/` 内 | ✅ 正常检查 |
| 目标在范围外（`FTheseus/`、`BattleShaders/`、`UI/`、第三方插件…） | ⛔ **不检查、不报告、不给修改建议** |
| 目标是生效目录的**父级/祖先目录**（如 `Assets/Shaders`、`Assets`） | ✅ 自动过滤，只检查范围内文件，并报告"范围外跳过 N 个" |
| 目标全部在范围外 | ⛔ 直接告知"不在生效范围内"，然后结束 |

- 范围定义唯一来源：`tools/_scope.sh` 的 `SR_SCOPE_ROOTS`。需要扩范围就改那里，不要在别处硬编码。
- `tools/scan.sh` 与 `tools/naming-lint.sh` 输出头部都会打印生效范围 + "范围外跳过"计数。
- 范围外的 shader 即使查出同类问题（比如 R002），也**不提、不改、不开 issue** —— 那不是本 skill 的职责范围。
- `reference/uniform-index.md` 的统计口径是范围内的 `Character` 目录，**不宣称代表全项目**。

---

## ⚠️ 第一原则：存量既往不咎

`whitelist/legacy-paths.txt` 里列的是**两个生效范围内当前已存在、已上线稳定运行**的 shader（当前 480 个）。

对白名单内的文件：

- ❌ 不检查、不报告、**不修改**
- ❌ 不要"顺手修一下" —— 它们带着已知的历史问题（如 R002 的 42 个文件），但改动 = 新增线上风险，收益远小于风险
- ✅ 唯一允许的动作：**提示使用者"这是存量文件，按约定不改"**

只有**新增文件**（不在白名单里）才走下面的完整检查和指导。

> 判断方式：`bash tools/scan.sh` 默认就会跳过白名单，输出里会写"白名单跳过（存量）: N"。

---

## 使用时机

- 用户给的路径在生效范围外 → **先说明"不在本 skill 生效范围内"，然后停**
- 新写了一个 shader，准备提交前 → 走「新 Shader 写作指导」自检
- 要给 shader 的 uniform / 属性 / 贴图命名 → 查 `reference/uniform-index.md`，再跑 `tools/naming-lint.sh`
- 改了别人的 shader → 先确认它在不在白名单（在 → 只报"存量，不改"）
- 用户说"帮我 review 一下这个 shader" → 走「工作流程」
- 要动 `Assets/Shaders/ShaderLibrary/` 下任何文件 → 先走 **P001 审批**，再谈代码

## 工作流程

0. **确定目标文件 + 判生效范围**：用户给了路径就用；没给就问。目标不在 `ShaderTheseus/`、`ShaderLibrary/` 下 → 直接说明不检查并结束；父级目标只保留范围内文件。
1. **查硬性流程项**（见下「流程规则」）：范围内改动是否涉及 `Assets/Shaders/ShaderLibrary/**`？涉及 → 立刻提醒用户群里 @朱瀚，**这一项没确认就不要先改完再补报备**。
2. **跑扫描**：
   ```bash
   bash <skill目录>/tools/scan.sh <目标>
   ```
   读取输出头部的「生效范围 / 范围外跳过 / 待检查 / 白名单跳过」字段。若"目标文件（待检查）: 0"，说明全是存量 → 直接回一句"这些是白名单存量文件，按约定不改"就结束。
3. **逐条读 `rules/`**，对扫描出的候选**人工读上下文确认**，剔除误报。每条规则末尾都有"误报提示"，必须先看。
3.5 **命名检查**（新文件必做）：
   ```bash
   bash <skill目录>/tools/naming-lint.sh <目标>
   ```
   要新加 uniform 时，先查 `reference/uniform-index.md` 的模块表，别自己起名。
4. **输出报告**（格式见下，含审批状态栏）。
5. 需要修改时**先说明改法并征求同意**。特别注意：同类代码往往在几十个文件里复制过，改动面可能远超预期 —— 但**白名单内的文件一律排除在改动范围之外**。

---

## 新 Shader 写作指导（提交前自检清单）

写新 shader 时逐条对照，全部满足再提交：

- [ ] ⛔ **新文件是不是在 `ShaderTheseus/**` 或 `ShaderLibrary/**` 下？** 不在 → 本 skill 不管（大前提）
- [ ] ⛔ **改动有没有碰到 `Assets/Shaders/ShaderLibrary/**`？** 碰到 → 群里 @朱瀚 确认了吗？（P001）
- [ ] **功能开关用裸定义**：`#define _XXX`，不加 `1`，绝不写 `0`
- [ ] **只按"是否定义"判断**：`#ifdef _XXX` / `#if defined(_XXX)`；绝不写 `#if _XXX`（裸定义 + `#if` 会编译报错）
- [ ] **宏定义位置**：写在所有 `#include` **之前**、且写在使用它的那个 Pass 的 `HLSLPROGRAM` 内部
- [ ] **切线符号只乘一次**：
      - 顶点用 `GetVertexNormalInputs()`（内部已乘 `GetOddNegativeScale`），或手写 `sign = tangentOS.w * GetOddNegativeScale()` 存进 `tangentWS.w`
      - 片元 **直接** `float tangentSign = input.tangentWS.w;`，不要再乘 `unity_WorldTransformParams.w`
      - 反之，若顶点传的是**原始** `tangent.w`，则片元必须乘一次 —— 二选一，不能都不做也不能都做
- [ ] **从已有 shader 复制改造时额外注意**：白名单里的存量 shader 本身就带着 R002 的问题，**复制 = 继承 bug**。新文件不在白名单里，会被查出来，必须照着上面的正确写法修掉，不要"和原文件保持一致"
- [ ] **uniform 命名查过表**（N001）：新加的属性/贴图/参数名在 `reference/uniform-index.md` 里有规范名就用规范名；没有就沿用同模块的词汇，不自造同义词、不用拼音、不改大小写
- [ ] **没有同一功能的第二种写法**：同一文件内不许 `_AlbedoColor` 和 `_albedoColor` 并存
- [ ] **贴图 Tiling/Offset 不手写**：用 Unity 自动生成的 `_xxx_ST`，不要自造 `_xxx_TiSP` / `_xxx_Tiling_Speed`
- [ ] **开关只做 keyword**：`#pragma shader_feature _USE_XXX`，不要在 Properties 里再声明一个同名 `_USE_XXX` 属性（存量有 30 处这么干的，不要学）
- [ ] **跑一遍扫描**：`bash tools/scan.sh <新文件>` ，确保 R001 / R002 全部 ✅
- [ ] **跑一遍命名检查**：`bash tools/naming-lint.sh <新文件>` ，确保无 ❌ 错误

---

## 规则索引

| ID | 规则 | 严重级别 | 详见 |
|---|---|---|---|
| R001 | `#define` 功能开关一律写裸定义，禁止写 `0`，新代码不写 `1` | 🔴 高 | `rules/R001-define-toggle-no-value.md` |
| R002 | 切线符号（`GetOddNegativeScale` / `unity_WorldTransformParams.w`）不能顶点和片元各乘一次 | 🔴 高 | `rules/R002-tangent-sign-double-multiply.md` |
| N001 | Uniform 命名必须查表，禁止自造同义名 / 拼音 / 大小写变体 | 🟡 中 | `rules/N001-uniform-naming.md` |

> 新增规则：复制 `rules/_TEMPLATE.md` → 命名 `Rxxx-短横线描述.md` → 补全 → 上表加一行 → `CHANGELOG.md` 记一条 → 如需自动检测，在 `tools/scan.sh` 加区块。

## 流程规则（合入前置条件，不是代码建议）

| ID | 规则 | 要求 | 详见 |
|---|---|---|---|
| P001 | `Assets/Shaders/ShaderLibrary/**` 下任何文件发生新增、修改、删除、移动或重命名 | ⛔ **必须先在群里 @朱瀚(Kirk) 确认**，才能修改 / 提交 / 合入 | `rules/P001-shaderlibrary-approval.md` |

触发边界：严格按目录前缀 `Assets/Shaders/ShaderLibrary/` 判断，包括 `Core/`、`Pipeline/`、所有扩展名和未来新增子目录。目录外的同名 `ShadingModels.hlsl` 不由 P001 触发。

自检（从 Git 根目录运行，兼顾暂存、未暂存和未跟踪文件）：

```bash
{ git diff --name-only; git diff --cached --name-only; git ls-files --others --exclude-standard; } \
  | tr '\\' '/' | grep -E '(^|/)Assets/Shaders/ShaderLibrary/'
```

## 输出格式

```markdown
## Shader 检查报告

| 项 | 数量 |
|---|---|
| 生效范围 | ✅ 范围内 / ⛔ **不在范围（不检查）** |
| 目标文件 | 12 |
| ⛔ 审批状态 | 不涉及 / **待 @朱瀚 确认** / 已确认 |
| 白名单跳过（存量，不改） | 10 |
| 待检查新文件 | 2 |
| 命中问题 | 1 |

### 🟡 白名单说明
- `A/B.shader`、`C/D.shader` … 属存量文件，按约定不检查、不修改

### 🔴 R00X <规则标题>
| 文件:行 | 代码 | 问题 |
|---|---|---|
| `E/F.shader:87` | `#define _MULTI_SPECULAR 0` | 0 仍被 `defined()` 视为开启，语义误导 |

**修复建议**：<一句话 + 替换后的代码>

### ⚪ 已确认无问题
- R001：0 处
- R002：0 处

### ⛔ 流程项（P001）
- 本次改动涉及 `Assets/Shaders/ShaderLibrary/Core/SpaceTransforms.hlsl`
- **状态：待群里 @朱瀚(Kirk) 确认** —— 未确认前请勿修改 / 提交 / 合入
- 待发消息草稿：（见 `rules/P001-shaderlibrary-approval.md` 模板）

### 📌 备注
- <误报、需用户确认、影响面提醒>
```

命名检查单独输出（`naming-lint.sh`）：

```
==============================================
 Uniform 命名检查
 目标 : 2 个 shader
==============================================
  ❌ [X/New.shader] _AlbedoColor  →  规范名 _albedoColor（48 个文件在用）
  ❌ [X/New.shader] _Rongjie_Fanwei  →  禁止拼音命名
  ⚠️  [X/New.shader] _Dissolve_Tex  →  词中下划线，建议写成一个词
----------------------------------------------
 ❌ 错误 2   ⚠️  提示 1
 规范表 : .../reference/uniform-index.md
```

---

## 目录结构

```
shader-review/
├── SKILL.md                          ← 本文件：原则 / 流程 / 写作指导 / 规则索引
├── CHANGELOG.md                      ← 规则追加记录
├── whitelist/
│   └── legacy-paths.txt              ← 存量白名单（仅生效范围内，相对 Assets/ 的完整路径）
├── reference/
│   ├── uniform-index.md              ← ★ Uniform 命名索引（14 个功能模块 + 规范名 + 禁用变体）
│   ├── canonical-names.txt           ← 人工认可的规范名（自动刷新不会覆盖）
│   ├── variant-blacklist.txt         ← 人工确认的非规范拼写 -> 规范名
│   ├── typos.txt                     ← 手工维护的拼写错误 / 同义名
│   └── variant-candidates.txt        ← 自动扫描候选，需人工确认
├── rules/
│   ├── R001-define-toggle-no-value.md
│   ├── R002-tangent-sign-double-multiply.md
│   ├── N001-uniform-naming.md           ← uniform 命名规范
│   ├── P001-shaderlibrary-approval.md   ← ShaderLibrary 全目录硬性流程：@朱瀚
│   └── _TEMPLATE.md
└── tools/
    ├── _scope.sh                     ← ★ 生效范围定义（ShaderTheseus / ShaderLibrary）
    ├── scan.sh                       ← 扫描（先按范围过滤，再跳过白名单）
    ├── naming-lint.sh                ← uniform 命名检查（默认跳过白名单）
    ├── scan-uniformes.sh             ← 统计 Properties，--update 仅刷新候选
    ├── gen_whitelist.sh              ← 仅生成生效范围内白名单
    └── self-test.sh                  ← 临时目录回归测试
```

## 维护

**白名单**

```bash
# 有新 shader 通过审查并正式上线后，重新生成（仅纳入两个生效范围）
bash tools/gen_whitelist.sh /h/MLBBTest/Assets

# 先看差异再决定
bash tools/gen_whitelist.sh /h/MLBBTest/Assets --dry
```

- ❌ **新写的 shader 不要手工加进白名单**（否则它就绕过了全部规则）
- ✅ 白名单条目 = 相对 `Assets/` 的**完整路径精确匹配**；不按 basename 放行，换机器/工程根仍有效
- ✅ 任何文件（包括全库唯一文件名）都必须路径完全一致才算存量
- 真正需要人工豁免某个新文件时，追加在文件末尾并写明原因和时间

**生效范围**

```bash
# 唯一来源
$EDITOR tools/_scope.sh        # SR_SCOPE_ROOTS 数组
```

- 默认只有 `Shaders/ShaderTheseus` 与 `Shaders/ShaderLibrary` 两项
- 扩大范围时只改这一处，`scan.sh` 与 `naming-lint.sh` 自动跟随
- 新加一项后记得同步更新本文件「大前提」表格和 `CHANGELOG.md`

**规则**：见上「规则索引」下的说明。

---

## 项目背景事实（判断依据，勿凭记忆改）

- 本工程 `_MULTI_SPECULAR` / `SHADINGMODELID_*` 等开关**全部用 `#ifdef` / `#if defined()` 判定**，没有一处 `#if <宏>` 按值判断。所以"裸定义"与"定义成 1"行为等价，而"定义成 0"同样是**开启**。
- 全库**没有** `#undef _MULTI_SPECULAR`。
- HLSL 的 `#include` 是纯文本展开：宏只对**后面**的代码生效。定义写在 `#include` 之后 = 不生效。
- 每个 Pass 的预处理器独立：写在 `SubShader` 内、`HLSLPROGRAM` 外的 `#define` 不会进入 Pass。
- tangent 符号存在**两种合法惯例**（见 R002 表格），判定标准是"符号被乘了几次"，不是"写了哪一行"。
- 存量现状（2026-10）：`#define X 0` 48 处、`#define X 1` 417 处、裸定义 849 处；R002 命中 42 个文件 —— **全部属白名单存量，不修**。
- `Assets/Shaders/ShaderLibrary/` 是全项目公共 shader 基础库；当前含 44 个非 `.meta` 文件。其中 `Pipeline/ShadingModels.hlsl` 被 204 个 shader 直接 `#include`，但 P001 覆盖的是**整个目录**，不是只有这个文件。
- `Assets/Editor/FTheseusShaderGUI/Data/FTheseus_Pbr_ShaderProperties.cs` 是 ShaderGUI 的 **uniform 名硬编码表**。shader 里名字和它对不上 → 面板调节**静默失效**、不报错。所以「规范名」以「全库最高频写法 + 该文件」两者共同为准。
- Character 目录现状（2026-10）：205 个 `.shader`、9243 条属性声明、1067 个唯一 uniform、**60 组仅大小写不同的重复**、6 个功能开关各有最多 4 种关键字拼写。全部属白名单存量，不修。

## 排查经验

- "宏改了没效果" 三大原因：① 定义在 `#include` 之后；② 效果被乘了 0/黑色参数（如 `SpecularColor2` 为黑）；③ 写在了别的 Pass。
- 定位宏问题最快的方法：先把宏状态画到屏幕上（顶点条/整屏色），别靠肉眼猜效果差异。可参考工程内 `_DefineProbe` 目录的做法。
