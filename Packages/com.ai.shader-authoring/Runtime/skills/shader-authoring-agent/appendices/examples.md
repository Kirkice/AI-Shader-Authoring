# Shader Authoring Examples

## CapabilityResolution 示例

```text
result: degraded
nodes:
  - nodeId: indirect_specular
    requested: true
    resolution: fallback
    reasonCode: REFLECTION_PROBE_UNCONFIRMED
    fallback: zero_indirect_specular
  - nodeId: dissolve
    requested: false
    resolution: omitted
    reasonCode: NOT_REQUESTED
summary: selected=8, fallback=1, omitted=1, unsupported=0, blocked=0
```

## ScopeConfirmationRecord 示例

```text
planId: plan_2026_001
planRevision: 2
planDigest: sha256:...
userDecision: confirmed
unresolvedItems: []
authorization:
  writeAllowed: true
  allowedFiles: [Assets/AIShader/Generated/Example.shader]
```

示例只说明数据投影，不改变主 Skill 的功能范围和执行顺序。
