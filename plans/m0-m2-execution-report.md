# M0–M2 执行报告

## 已完成

- M0：建立风险核验和回归基线；确认 Node 原先没有自动测试脚本、Unity Tests 目录没有 EditMode 测试程序集；记录“未验证”边界。
- M1：动态 C# 默认关闭；加入强制配对 token、challenge/HMAC、loopback 默认限制、2 MiB 消息限制、连接 epoch、请求 nonce 和重放拒绝。
- M2：加入 Unity-owned 授权登记表、提议/本地批准/撤销、run/plan/capability/path/revision/expiry/maxWrites 校验，以及临时文件原子替换。
- M2 收尾：副作用 Job 已纳入 capability、job type、路径与 revision 授权；单文件写入增加持久化事务记录和幂等键冲突检测。

## 验证结果

- Node TypeScript：`npm run build` 通过。
- Node 安全协议测试：`npm test` 通过，4/4。
- Patch 格式：`git diff --check` 通过（仅存在现有 CRLF→LF 提示）。
- Unity Editor：已创建 EditMode 测试程序集并尝试批处理执行。本机只有 Unity 2021.3.29f1，而项目版本为 Unity 6000.5.4f1；Package 解析因版本不兼容失败，故 Unity 6000 的 C# 编译、EditMode 与真实 WebSocket 集成保持“未验证”。

## 验收对照

| 里程碑 | 验收结果 | 说明 |
|---|---|---|
| M0 | 通过 | 基线文档和客观执行结果已记录。 |
| M1 | 条件通过 | 实现和 Node 自动测试完成；Unity 6000 编译/集成受本机版本阻塞。 |
| M2 | 条件通过 | 写入及副作用 Job 授权、事务和幂等实现完成；Unity 6000 AssetDatabase 行为受环境阻塞。 |

## 偏离与限制

- 配对 token 由环境变量或 Unity EditorPrefs 配置，不通过 MCP 返回；当前 Dashboard 未增加明文 token 输入框，以避免普通界面意外泄漏。
- 授权已覆盖当前结构化副作用 Job；`restore_shader_checkpoint` 在 Unity 工具分发中原本没有实现，未伪造一个无恢复语义的入口。
- 多文件事务尚未引入；当前写入工具一次只提交一个文件，事务边界即单文件。
