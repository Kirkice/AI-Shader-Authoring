# CHANGELOG

规则追加记录。新增规则时在**最上面**加一条。

## v1.6.0 — 2026-10-29

**可靠性修复（基于完整 skill review）**

- 修复 `naming-lint.sh` 漏检 `[HDR]` / `[Toggle]` / 多 Attribute 和内联 `Properties { ... }`。
- N001 自动检测扩展到 `.hlsl/.cginc/.hlslinc` 的 `TEXTURE*`、`SAMPLER`、普通 uniform 与 CBUFFER 成员。
- 删除危险的 basename 白名单 fallback；现在只按相对 `Assets/` 的完整路径精确放行。
- 修复从 `Assets/Shaders` 等 cwd 传相对路径时被误判范围外。
- P001 统一用 `_scope.sh` 判断 ShaderLibrary 本身、内部文件及祖先目录。
- `gen_whitelist.sh` 只收两个生效范围，白名单由 2223 条收敛为 480 条；不再让 1743 个范围外文件影响判定。
- `scan-uniformes.sh --update` 只允许 Character 基准目录，并只生成 `variant-candidates.txt`；不再按最高频自动覆盖人工认可的 canonical/blacklist/typos。
- TSV 读取去除 CRLF，统一脚本和 reference 文本为 LF，新增 `.gitattributes`。
- 新增 `tools/self-test.sh`，覆盖 Attribute/内联/HLSL、范围外拒绝、basename 绕过和相对路径四类回归。
- `SKILL.md` 升级至 v1.6.0，修正执行顺序、父级目录措辞、输出字段及维护说明。

## v1.5.0 — 2026-10-29

**新增大前提：生效范围（只对两个目录下的 shader 起作用）**

- 生效范围：
  - `H:/MLBBTest/Assets/Shaders/ShaderTheseus/**`
  - `H:/MLBBTest/Assets/Shaders/ShaderLibrary/**`
- 范围外（`FTheseus/`、`BattleShaders/`、`UI/`、第三方插件…）：**不检查、不报告、不给修改建议**
- 新增 `tools/_scope.sh`：范围定义的**唯一来源**（`SR_SCOPE_ROOTS` 数组 + `sr_in_scope` / `sr_filter_in_scope` / `sr_target_relevant` 函数）
- `tools/scan.sh`：文件在扩展名下集前先过范围，新增「生效范围 / 范围外跳过」两行头部；目标全在范围外时直接退出并说明原因
- `tools/naming-lint.sh`：同样先过滤范围，并单独报告“范围外跳过 N 个”
- 混合目标（如扫 `Assets/Shaders` 或整个 `Assets`）：自动只取范围内文件，并报「范围外跳过 1581 个」（实测）
- `SKILL.md`：新增「## ⛔ 大前提：生效范围」表格与行为约定，放在「第一原则」之前；工作流程第 1 步加范围判定；自检清单首项加范围项；报告格式加「生效范围」行；目录结构加 `_scope.sh`；维护节加范围修改说明；frontmatter 版本 1.5.0
- `rules/R001` / `R002` / `N001`：开头均加一行「📍 生效范围（大前提）」声明；`P001` 本身就在范围内，无需重复


## v1.4.0 — 2026-10-29

**新增：N001 uniform 命名规范 + 功能模块索引（Character 目录全量统计）**

- 扫描对象：`Assets/Shaders/ShaderTheseus/Character` 下 **205 个 `.shader`**
  - 9243 条属性声明 / **1067 个唯一 uniform**
  - 发现 **60 组仅大小写不同的重复**（共 61 个非规范写法），最严重的单组出现 123 次
- 新增 `reference/uniform-index.md` —— **14 个功能模块 → 规范名索引**，每模块含：规范名 / 类型 / 默认值 / 通道语义 / 文件数 / 禁用变体
  - M01 基础表面 RMO（`_albedoColor` / `_materialParamsMap` 等，133 文件）
  - M02 光照标定（`_localDiffuseGI` 167 / `_indirectSpecularIntensityScale` 162 / `_DfgTexture` / `_SpecularOcclusionLut3D` / `_ACESLutTex`）
  - M03 阴影与遮蔽（`_shadowStrengthMap` rgb 三通道阴影 108 文件）
  - M04 渲染状态（ShaderGUI 硬编码驱动的 9 个，**改名即 GUI 静默失效**）
  - M05 流光 FlowLight（4 种开关拼写）
  - M06 镭射 Laser（`_LaserMask` R=遮罩 G=Ramp 索引）
  - M07 闪点 Glitter（`_GlitteryControl` xy=Tiling z=对比度 w=强度）
  - M08 溶解 Dissolve（**命名最乱**：20+ 同义名、拼音 `_Rongjie_*`、拼写错误 `_Dissovle`）
  - M09 各向异性（`_sunShift*` / `_anisotropicMap`.xy=specularShift，即 R002 那 42 个文件）
  - M10 3S、M11 菲涅尔/边缘光、M12 补光 Sanshe/MendsLight、M13 环境反射、M14 顶点
  - 另含：**关键字命名表**（6 组同一开关多拼写）、**Tiling/Offset 约定**（`_xxx_ST` vs 自造 `_TiSP`/`_Tiling_Speed`）、**采样器约定**
- 新增 `rules/N001-uniform-naming.md`：10 条硬规则 + 7 项自动检测 + 误报例外（`_Sanshe_*`、本身就是规范名的 Pascal 族）
- 新增 `reference/canonical-names.txt`（1006 条已认可规范名，抑制误报）
- 新增 `reference/variant-blacklist.txt`（61 条：非规范写法 → 规范名）
- 新增 `reference/typos.txt`（手工维护：拼写错误 `_FresnelSacle` / `_Dissovle_Tiling_Offset`、同义名 `_DissolveThreshold`→`_dissolveAmount` 等）
- 新增 `tools/naming-lint.sh`：解析 Properties 逐条比对，默认**跳过白名单存量**（存量既往不咎），`--legacy` 才审计；退出码 1 表示有错误
- 新增 `tools/scan-uniformes.sh`：重新统计 Properties 并 `--update` 刷新上述两个数据文件
- 关键发现：`Assets/Editor/FTheseusShaderGUI/Data/FTheseus_Pbr_ShaderProperties.cs` 是 GUI 的**字符串常量表**，
  shader 里 uniform 名和它不一致 → 面板调节**静默失效且不报错**。这是「必须统一命名」最硬的理由。
- `SKILL.md`：版本 1.4.0；自检清单加 5 项命名项；规则索引加 N001；目录结构加 `reference/` 与两个新工具；输出格式加命名检查样例


## v1.3.0 — 2026-10-29

**P001 范围拓宽：从单文件族改为 ShaderLibrary 全目录**

- 旧边界：任何 `*ShadingModels.hlsl`
- 新边界：`Assets/Shaders/ShaderLibrary/**` 下**所有文件和子目录**
- 操作类型：新增、修改、删除、移动、重命名均触发；`.hlsl/.shader/.mat/.meta` 及未来扩展名均适用
- 目录外同名文件（例如 `FTheseus/ShadingModels.hlsl`、角色私有 `*_ShadingModels.hlsl`）不再由 P001 强制触发
- 规则文档重命名为 `rules/P001-shaderlibrary-approval.md`
- 扫描器改为扩展名过滤之前按目录边界判断，避免漏掉 `.mat` / `.meta`
- Git 自检同时覆盖未暂存、已暂存和未跟踪文件，避免只用 `git diff` 漏报
- 当前目录盘点：44 个非 `.meta` 文件（39 `.hlsl`、4 `.shader`、1 `.mat`）

## v1.2.0 — 2026-10-29

**新增：流程规则 P001（硬性合入前置条件；此版本范围后来已被 v1.3.0 替代）**

- 改动涉及任何 `*ShadingModels.hlsl` → **必须先在群里 @朱瀚(Kirk) 说明改什么/为什么，确认后才能改 / 提交 / 合入**
- 触发文件（4 个）：
  - `Shaders/ShaderLibrary/Pipeline/ShadingModels.hlsl` ← 被 **204 个** shader 直接 include，全项目光照公共底座
  - `Shaders/FTheseus/ShadingModels.hlsl`
  - `Theseus_Pan/PanBase_ShadingModels.hlsl`（10 处引用）
  - `Theseus_Pbr_Hair_WitdhControl/WidthControl_ShadingModels.hlsl`（7 处引用）
- 联系人：朱瀚(Kirk) `ou_e2f1ba04362e32328c6b236b72351b10`（研发中心-研发技术中台-研发效能-技术支持）
- 当时规则文档名为 `P001-shadingmodels-approval.md`（v1.3.0 已更名为 `P001-shaderlibrary-approval.md`）：含触发范围、动作、待发群消息模板、lark-cli 发送命令
- `tools/scan.sh`：范围里出现 `*ShadingModel*` 时打 ⛔ 区块（放在白名单空判断之前，因为要改的正是存量文件）；同时提示用 `git diff --name-only | grep -i shadingmodel` 自己复核 diff
- `SKILL.md`：新增「流程规则」表；工作流程加第 0 步（先查审批项）；自检清单首项；报告格式加「⛔ 审批状态」行和「⛔ 流程项」区块

## v1.1.0 — 2026-10-29

**新增：存量白名单机制（既往不咎）**

- `whitelist/legacy-paths.txt`：当前工程全部 2223 个 shader 文件（`.shader/.hlsl/.cginc/.compute`，相对 `Assets/` 路径）
- 白名单内文件：**不检查、不报告、不修改** —— 均已上线稳定，改动即风险
- `tools/scan.sh`：默认跳过白名单，`--legacy` 才做存量审计；输出头部显示"目标文件 / 白名单跳过"计数
- `tools/gen_whitelist.sh`：重新生成白名单，支持 `--dry` 看差异
- 匹配规则：路径精确匹配；文件名匹配仅在**全库唯一**时生效（`Core.hlsl`/`Common.hlsl` 等 58 个重名文件必须路径一致）
- SKILL.md 增加「新 Shader 写作指导（提交前自检清单）」，明确：从存量 shader 复制改造 = 继承 R002 的 bug，新文件必须按正确写法修掉

## v1.0.0 — 2026-10-29

初始版本。

- **R001** `#define` 功能开关一律写裸定义，禁止写 `0`，新代码不写 `1`
  - 来源：`Theseus_Pbr_Anisotropic_Silk_Flowmap.shader:87` 的 `#define _MULTI_SPECULAR 0`。
    判定用的是 `#if defined(...)`，所以写 `0` 实际是"开启"，与注释意图相反，排查很久。

- **R002** 切线符号不能在顶点和片元各乘一次（负缩放矫正被抵消）
  - 来源：`Theseus_Pbr_Anisotropic_Silk_Flowmap`（及 `Silk_FlowGlitter`）
    顶点 `sign = tangentOS.w * GetOddNegativeScale()` 存进 `tangentWS.w`，
    片元又 `tangentSign = sign(unity_WorldTransformParams.w) * tangentWS.w`，
    `oddScale` 自我抵消 → 镜像物体上各向异性方向错误。
  - 首次扫描发现 **42 个文件** 均有此问题（复制传播）→ 已全部纳入白名单，**不修**。

## 待办 / 想加的规则

- [ ] 变体爆炸检查（`multi_compile` 数量超过阈值）
- [ ] `#if` 宏写在 `#include` 之后导致不生效（宏顺序检查）
- [ ] `SAMPLE_TEXTURE2D` 与 `sampler_` 命名不匹配
- [ ] 顶点阶段采样贴图（本工程部分 shader 有意为之，需明确豁免条件）
- [ ] N001 延伸：**语义同义名检测**（如 `_DissolveThreshold` 与 `_dissolveAmount` 语义相同但字面不同，目前只靠手工 `typos.txt`）
- [ ] N001 延伸：**C# ↔ shader 交叉校验**（ShaderGUI 常量表里写的名字，在目标 shader 里是否真的存在）
- [ ] 把 `Theseus_Pbr_Cloud` 里 `_shadowStrengthMap` 的用法（rgb 三通道阴影）补进 M03 模块说明的代码依据
- [ ] 生效范围是否需要加第三个目录（目前只列了 ShaderTheseus / ShaderLibrary）
