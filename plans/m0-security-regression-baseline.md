# M0 风险核验与回归基线

> 状态：实现中。本文档只记录可复现的核验项和实际执行结果；未执行的项目不得标记为通过。

## Node MCP Server 基线

| 编号 | 核验项 | 期望 | 当前结果 |
|---|---|---|---|
| N-001 | TypeScript 构建 | `npm run build` 成功且无错误 | 通过（2026-09-28，M1/M2 修改后复验） |
| N-002 | 默认工具列表 | 动态 C# 不应作为默认工具暴露 | 已实现；仅显式诊断环境变量开启 |
| N-003 | 未绑定 Editor 调用 | 调用被拒绝，不猜测目标 | 已由代码检查确认 |
| N-004 | 多 Editor 匹配 | 调用被拒绝，不按最后连接选择 | 已由代码检查确认 |
| N-005 | 断线中的请求 | pending 请求失败，不自动重放 | 已实现 connection epoch、nonce 去重和断线失败 |
| N-006 | 结构化请求超时 | 超时后清理 pending 请求 | 已由代码检查确认 |
| N-007 | 连接认证 | 未配对连接不能注册 Editor | 已实现 challenge/HMAC；Unity 集成待验证 |
| N-008 | Node 安全单测 | loopback、HMAC、replay cache、速率限制均通过 | 通过：4/4（2026-09-28） |

## Unity Editor 基线

| 编号 | 核验项 | 期望 | 当前结果 |
|---|---|---|---|
| U-001 | Editor 程序集存在 | Unity MCP Editor 程序集可加载 | 待 Unity Editor 验证 |
| U-002 | hello 身份 | Editor ID 与项目路径必须存在且匹配 | 已由代码检查确认 |
| U-003 | 未注册消息 | 未完成注册前不执行副作用消息 | 已实现；Unity 集成待验证 |
| U-004 | 动态 C# | 默认拒绝 `executeEditorCommand` | 已实现；Unity 编译待验证 |
| U-005 | 结构化写入路径 | 仅允许项目相对生成目录 | 已由代码检查确认，需补充联接/原子写入验证（M2） |
| U-006 | 结构化写入授权 | 请求自带的 `allowedFiles` 不能扩大权限 | 已实现 Unity-owned grant registry；Unity 集成待验证 |
| U-007 | revision 冲突 | 基线 revision 不一致时不写入 | 已由代码检查确认，需 Unity EditMode 验证 |
| U-008 | 写入中断恢复 | 不留下半文件或导入中间态 | 已实现临时文件 + replace/move；Unity 集成待验证 |

## M0 证据规则

1. Node 构建和静态协议检查可以在本机执行。
2. Unity EditMode、AssetDatabase 和真实 WebSocket 集成测试必须在 Unity Editor 中执行；本机未运行时标记为“未验证”。
3. 每次验证记录时间、命令或操作、结果和失败信息。
4. M1/M2 修改后重新执行 N-001、N-003、N-004、N-005，以及可用的 Unity 验证项。

## 执行记录

### 2026-09-28

- 已完成：检查 Node 包结构，未发现自动化测试脚本；现有脚本为手工 smoke/validation 工具。
- 已完成：检查 Unity `Tests/`，当前仅包含测试场景和材质，没有 EditMode 测试程序集。
- 通过：Node `npm run build`，M1/M2 修改后 TypeScript 编译无错误。
- 通过：Node `npm test`，4 项安全协议单测全部通过。
- 已建立：Unity EditMode 安全测试程序集，覆盖路径穿越、文件类型、runId 路径片段与动态 C# 默认策略。
- 阻塞：本机仅安装 Unity 2021.3.29f1，工程要求 Unity 6000.5.4f1；批处理验证因 Package 版本不兼容失败，证据见 `Artifacts/TestResults/m0-m2-unity.log`。因此 Unity 6000 编译、EditMode 和 WebSocket 集成仍标记“未验证”。
