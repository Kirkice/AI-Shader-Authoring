# Shader Authoring Agent 附录索引

本目录承载 [`shader-authoring-agent/SKILL.md`](../SKILL.md) 的详细协议。核心 Skill 保留主状态机、硬性门禁、委派边界和对外结果；本附录承载稳定的数据契约与判定细节。

- [`semantic-model.md`](./semantic-model.md)：MaterialIntent、RenderingSemanticGraph 和效果组合。
- [`baseline-and-capability.md`](./baseline-and-capability.md)：迁移基线、项目风格基线、新资产基线与能力解析。
- [`validation-and-evidence.md`](./validation-and-evidence.md)：验证 Gate、证据和验收判定。
- [`confirmation-and-plan.md`](./confirmation-and-plan.md)：Code Plan Revision 与用户确认记录。
- [`examples.md`](./examples.md)：标准数据投影示例。

## 兼容性原则

附录是对核心协议的分层展开，不改变原有支持范围、状态机顺序、工具白名单、写入授权、编译门禁、固定场景验收和性能非阻断规则。发生冲突时，以核心 Skill、Unity MCP 操作协议和真实 Editor/Mcp 实现为准。
