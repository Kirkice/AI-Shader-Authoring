# P001 — 改 `Assets/Shaders/ShaderLibrary/` 下任何文件必须在群里 @朱瀚

**级别**：⛔ 硬性流程要求（不是代码质量建议，是**修改 / 提交 / 合入的前置条件**）

## 规则

任何新增、修改、删除、移动或重命名，只要涉及：

```text
Assets/Shaders/ShaderLibrary/**
```

都必须先在**群里 @朱瀚(Kirk)** 说明改什么、为什么改，得到确认后才能动手修改、提交或合入。

> 这条规则按**目录路径**判断，不再只盯 `ShadingModels.hlsl`。目录内所有文件和子目录都适用，包括 `.hlsl`、`.shader`、`.mat`、`.meta` 以及未来出现的其他文件类型。

## 触发范围

当前目录结构：

```text
Assets/Shaders/ShaderLibrary/
├── Core/
├── Pipeline/
└── ...（未来新增的文件和子目录也自动适用）
```

2026-10-29 盘点：目录内有 44 个非 `.meta` 文件（39 `.hlsl`、4 `.shader`、1 `.mat`）。**数量仅供背景参考，不作为匹配条件。**

### 会触发

- 修改 `Assets/Shaders/ShaderLibrary/Pipeline/ShadingModels.hlsl`
- 修改 `Assets/Shaders/ShaderLibrary/Core/SpaceTransforms.hlsl`
- 在目录下新增、删除、移动或重命名文件
- 只改 `.meta` 也触发
- 把文件移入或移出这个目录：旧路径或新路径任一个命中即触发

### 不会触发

- `Assets/Shaders/FTheseus/ShadingModels.hlsl`
- `Assets/Shaders/ShaderTheseus/.../PanBase_ShadingModels.hlsl`
- `Assets/Shaders/ShaderTheseus/.../WidthControl_ShadingModels.hlsl`
- 只修改一个 `#include "Assets/Shaders/ShaderLibrary/..."` 的外部 shader，但没有改 `ShaderLibrary/` 目录里的文件

> 目录外文件即使也叫 `ShadingModels.hlsl`，**不属于 P001 的强制触发范围**。需要其他审批时另建流程规则，不借用本规则扩大解释。

## 判定标准

本次改动集里的任一路径满足下面前缀即触发：

```text
Assets/Shaders/ShaderLibrary/
```

必须同时检查已跟踪改动和未跟踪文件：

```bash
# 从 Git 仓库根目录执行；涵盖暂存、未暂存、未跟踪、删除/重命名
{
  git diff --name-only
  git diff --cached --name-only
  git ls-files --others --exclude-standard
} | tr '\\' '/' | grep -E '(^|/)Assets/Shaders/ShaderLibrary/'
```

> `git diff --name-only` 单独使用会漏掉暂存区和未跟踪文件，所以不能只跑这一条。

## 动作

1. **停止修改**；不要先改完再补报备
2. 整理改动意图、影响面和验证计划，生成待发群消息草稿
3. 把草稿和目标群给用户确认；不要擅自选群或发送
4. 用户在群里 @朱瀚，并得到确认后，才继续修改 / 提交 / 合入
5. 报告中状态必须写为以下之一，不能留空：
   - `⛔ 待群里 @朱瀚确认`
   - `✅ 已确认（时间 / 结论 / 消息链接，如有）`

## 联系人

| 项 | 值 |
|---|---|
| 姓名 | 朱瀚(Kirk) |
| open_id | `ou_e2f1ba04362e32328c6b236b72351b10` |
| 邮箱 | kirkzhu@moonton.com |
| 部门 | 研发中心-研发技术中台-研发效能-技术支持 |

需要重新确认联系人时：

```bash
lark-cli contact +search-user --query "朱瀚" --as user
```

## 待发群消息模板

```text
<at user_id="ou_e2f1ba04362e32328c6b236b72351b10">朱瀚</at>
【ShaderLibrary 改动报备】

· 文件：Assets/Shaders/ShaderLibrary/<具体相对路径>
· 操作：<新增 / 修改 / 删除 / 移动 / 重命名>
· 改什么：<具体到函数、宏、结构体或资源>
· 为什么：<效果问题 / bug / 新需求，附现象或截图>
· 影响面：<受影响的 shader / 角色 / 平台 / Pass / 变体>
· 验证方式：<测试场景、正负缩放、平台、性能或截图对比>
· 计划合入时间：<日期>

请帮忙确认这个方向是否可行，谢谢。
```

发送命令（`oc_xxx` 换成用户确认过的目标群）：

```bash
lark-cli im +messages-send --chat-id oc_xxx \
  --text '<at user_id="ou_e2f1ba04362e32328c6b236b72351b10">朱瀚</at> 【ShaderLibrary 改动报备】...'
```

> 发送前必须让用户确认消息正文和目标群；不得擅自选择群或发送。

## 扫描器行为

`tools/scan.sh` 会在扫描目标本身位于 `Assets/Shaders/ShaderLibrary/` 时打印 P001 ⛔ 提示。它能处理目录内的所有文件类型，不受 shader 扩展名过滤限制。

但扫描一个很大的父目录（例如整个 `Assets/`）只能说明范围里包含 `ShaderLibrary`，**不能证明这些文件真的发生了修改**。最终必须以上面的 Git 改动集命令为准。

## 为什么要有这条

`ShaderLibrary/` 是工程公共 shader 基础库，包含空间变换、公共 Core、管线数据结构、光照和着色模型等底层实现。其改动可能跨角色、材质、Pass 和平台扩散；单个文件看似局部，实际影响面往往远大于调用处。

## 变更记录

- 2026-10-29：建立规则，最初只覆盖 `*ShadingModels.hlsl`。
- 2026-10-29：按团队要求拓宽为 `Assets/Shaders/ShaderLibrary/**` 全目录；目录外同名文件不再由 P001 强制触发。
