# AI Shader Generated Assets

本目录由用户指定为 Shader 生成目录。

当前生成：

- `SimplePBR.shader`：基于当前工程 Packages 中 URP 17.5 ShaderLibrary 的简单 PBR Forward Shader。

参考路径：

- `Packages/com.unity.render-pipelines.universal@e38be786c41e/Shaders/Lit.shader`
- `Packages/com.unity.render-pipelines.universal@e38be786c41e/ShaderLibrary/Lighting.hlsl`
- `Packages/com.unity.render-pipelines.universal@e38be786c41e/ShaderLibrary/Core.hlsl`

当前实现包含：

- Base Color
- Metallic
- Roughness
- GGX NDF
- Schlick Fresnel
- Smith Geometry
- Direct Diffuse
- Direct Specular
- SH Indirect Diffuse
- Emission
- Debug Channel

这是第一版验证用 Shader，暂时只包含 Forward Pass，尚未实现 ShadowCaster、DepthOnly、DepthNormals、Meta 和完整 URP Lit 的全部关键词变体。
