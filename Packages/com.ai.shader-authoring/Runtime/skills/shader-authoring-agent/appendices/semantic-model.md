# Semantic Model

## MaterialIntent

```text
MaterialIntent
  identity: requestId, originalRequirement, controlledSummary
  scope: category, workflow, targetDescription
  surface: baseColor, metallic, roughness, normal, ambientOcclusion, emission
  effects: orderedLayers[]
  alpha: blendMode, alphaClipEnabled, alphaSource, alphaClipThreshold
  textures: semantic, source, channel, requiredness
  assumptions: value, source, rationale, confidence
  constraints: supported, excluded, projectDependent
  acceptanceIntent: visibleTargets, testMatrixOverrides, expectedDebugChannels
```

## RenderingSemanticGraph

图是自然语言需求到 ShaderCodePlan 的唯一计算语义真相，不保存具体 HLSL、Include 或 Unity API。节点必须声明 `phase`、`reads`、`writes`、`preconditions`、`requiredCapabilities`、`fallbackBehavior`、`debugChannels` 和来源字段。

生命周期阶段为 `input → surface → lighting → emission → alpha → output`。节点只能读取当前及更早阶段，并只能写入其授权槽位。效果对同一槽位的多次写入必须经过唯一 Compose 节点；循环、冲突或缺少必要 Compose 语义时返回 `blocked`。

## 首期语义不变

基础 PBR、Metallic-Roughness、Alpha Clip、Alpha Blend、Dissolve、Pulse Emission、Fresnel Rim 的支持范围和原有排序规则保持不变。未支持的 VFX、后处理、折射、透明排序修正、深度预通道、Clear Coat、Anisotropy、Parallax 和程序噪声仍不得隐式实现。
