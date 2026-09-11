import { spawn } from 'node:child_process';

const server = spawn(process.execPath, ['./build/index.js'], {
  cwd: process.cwd(),
  stdio: ['pipe', 'pipe', 'pipe']
});

let nextId = 1;
const pending = new Map();
let buffer = '';

server.stdout.on('data', (chunk) => {
  buffer += chunk.toString();
  let newline;
  while ((newline = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, newline).trim();
    buffer = buffer.slice(newline + 1);
    if (!line) continue;
    const message = JSON.parse(line);
    const resolve = pending.get(message.id);
    if (resolve) {
      pending.delete(message.id);
      resolve(message);
    }
  }
});
server.stderr.on('data', (chunk) => process.stderr.write(chunk));
server.on('exit', (code) => {
  for (const reject of pending.values()) reject(new Error(`MCP server exited with code ${code}`));
});

function request(method, params) {
  const id = nextId++;
  server.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  return new Promise((resolve, reject) => {
    pending.set(id, (message) => message.error ? reject(new Error(JSON.stringify(message.error))) : resolve(message.result));
    setTimeout(() => {
      if (pending.delete(id)) reject(new Error(`Timed out waiting for ${method}`));
    }, 45000);
  });
}

const shader = `Shader "AIShader/Generated/TransparentFresnelPBR"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.08, 0.36, 0.72, 0.45)
        _Metallic("Metallic", Range(0,1)) = 0.15
        _Roughness("Roughness", Range(0.04,1)) = 0.28
        [HDR] _RimColor("Fresnel Rim Color", Color) = (0.08, 0.8, 1.5, 1)
        _RimIntensity("Fresnel Rim Intensity", Range(0,10)) = 2.5
        _RimPower("Fresnel Rim Power", Range(0.5,8)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Name "ForwardTransparentFresnel"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _Metallic; half _Roughness;
                half4 _RimColor; half _RimIntensity; half _RimPower;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = SafeNormalize(input.normalWS);
                half3 viewDirWS = SafeNormalize(GetWorldSpaceNormalizeViewDir(input.positionWS));
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = _BaseColor.rgb * (1.0h - _Metallic) * (0.15h + ndotl * mainLight.color);
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half3 rimEmission = _RimColor.rgb * (fresnel * _RimIntensity);
                return half4(diffuse + rimEmission, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}`;

const shaderPath = 'Assets/AIShader/Generated/TransparentFresnelPBR.shader';
const materialPath = 'Assets/AIShader/Generated/TransparentFresnelPBR.mat';
const scenePath = 'Assets/AIShader/Generated/TransparentFresnelPBRValidation.unity';
const operationContext = { runId: 'transparent-fresnel-pbr-smoke-test', skill: 'manual-mcp-smoke-test', codePlanId: 'transparent-fresnel-pbr-v2' };
const codePlan = { codePlanId: operationContext.codePlanId, allowedFiles: [shaderPath, materialPath, scenePath] };

const createMaterialCode = `
var shaderPath = "${shaderPath}";
var materialPath = "${materialPath}";
var scenePath = "${scenePath}";
var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
if (shader == null) throw new System.InvalidOperationException("Generated Shader failed to import.");
var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
if (existing != null) AssetDatabase.DeleteAsset(materialPath);
var material = new Material(shader) { name = "TransparentFresnelPBR" };
material.SetColor("_BaseColor", new Color(0.08f, 0.36f, 0.72f, 0.45f));
material.SetFloat("_Metallic", 0.15f);
material.SetFloat("_Roughness", 0.28f);
material.SetColor("_RimColor", new Color(0.08f, 0.8f, 1.5f, 1f));
material.SetFloat("_RimIntensity", 2.5f);
material.SetFloat("_RimPower", 3.0f);
AssetDatabase.CreateAsset(material, materialPath);
var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
sphere.name = "Transparent Fresnel PBR Sphere";
sphere.GetComponent<Renderer>().sharedMaterial = material;
var lightObject = new GameObject("Key Light");
var light = lightObject.AddComponent<Light>();
light.type = LightType.Directional;
light.intensity = 1.2f;
lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
var cameraObject = new GameObject("Main Camera");
var camera = cameraObject.AddComponent<Camera>();
cameraObject.tag = "MainCamera";
camera.transform.position = new Vector3(0f, 0f, -3f);
camera.transform.LookAt(sphere.transform.position);
EditorSceneManager.SaveScene(scene, scenePath);
AssetDatabase.SaveAssets();
return new { shaderPath, materialPath, scenePath, shaderName = shader.name, materialName = material.name, sphereName = sphere.name };`;

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

try {
  await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'manual-pbr-smoke-test', version: '1.0.0' }
  });
  // Unity retries its WebSocket connection at a five-second interval after a server restart.
  await delay(7000);
  const revisionResult = await request('tools/call', {
    name: 'get_asset_revision',
    arguments: { assetPaths: [shaderPath] }
  });
  const revisionPayload = JSON.parse(revisionResult.content[0].text);
  const baseRevision = revisionPayload.assets[0].revision;
  const writeResult = await request('tools/call', {
    name: 'write_generated_text_asset',
    arguments: {
      operationContext,
      codePlan,
      asset: { path: shaderPath, contentUtf8: shader, baseRevision, createPolicy: 'create_or_update' }
    }
  });
  console.log(JSON.stringify({ writeResult }, null, 2));
  const materialResult = await request('tools/call', {
    name: 'execute_editor_command',
    arguments: { code: createMaterialCode }
  });
  const materialPayload = JSON.parse(materialResult.content[0].text);
  console.log(JSON.stringify({ materialResult }, null, 2));
  if (materialPayload.result?.executionSuccess !== true) {
    throw new Error(materialPayload.result?.errorDetails?.message ?? 'Material creation command failed.');
  }
  server.kill('SIGINT');
} catch (error) {
  console.error(error);
  server.kill('SIGINT');
  process.exitCode = 1;
}
