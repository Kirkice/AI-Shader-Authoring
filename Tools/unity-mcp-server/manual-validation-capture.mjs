/**
 * Manual Step 8 validation capture.
 *
 * Uses the user-provided scene as a read-only fixture. Unity opens it additively,
 * binds the generated material in memory, captures the configured camera, restores
 * the original material, and closes the scene without saving.
 */
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
    const settle = pending.get(message.id);
    if (settle) {
      pending.delete(message.id);
      settle(message);
    }
  }
});
server.stderr.on('data', (chunk) => process.stderr.write(chunk));
server.on('exit', (code) => {
  for (const reject of pending.values()) reject(new Error(`MCP server exited with code ${code}`));
});

function request(method, params, timeoutMs = 60000) {
  const id = nextId++;
  server.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  return new Promise((resolve, reject) => {
    pending.set(id, (message) => message.error ? reject(new Error(JSON.stringify(message.error))) : resolve(message.result));
    setTimeout(() => {
      if (pending.delete(id)) reject(new Error(`Timed out waiting for ${method}`));
    }, timeoutMs);
  });
}

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function callTool(name, args) {
  const result = await request('tools/call', { name, arguments: args });
  const payload = result.content?.[0]?.text;
  return payload ? JSON.parse(payload) : result;
}

function isTransient(error) {
  const message = error?.message ?? '';
  return message.includes('did not acknowledge within 30 seconds') || message.includes('not connected') || message.includes('disconnected');
}

async function waitForUnity(timeoutMs = 300000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      await callTool('get_editor_state', {});
      return;
    } catch (error) {
      lastError = error;
      await delay(3000);
    }
  }
  throw new Error(`Unity Editor did not connect: ${lastError?.message ?? 'unknown'}`);
}

async function callToolWithRetry(name, args, attempts = 5) {
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      return await callTool(name, args);
    } catch (error) {
      lastError = error;
      if (!isTransient(error)) throw error;
      console.error(`[validation-capture] ${name} attempt ${attempt} failed transiently: ${error.message}`);
      await delay(3000);
      await waitForUnity();
    }
  }
  throw lastError;
}

async function pollJob(jobId) {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    const job = await callToolWithRetry('get_unity_job', { jobId });
    if (job.status === 'succeeded' || job.status === 'failed' || job.status === 'cancelled') return job;
    await delay(500);
  }
  throw new Error(`Job ${jobId} did not settle in time.`);
}

const operationContext = {
  runId: 'transparent-fresnel-pbr-step8',
  skill: 'shader-authoring-agent',
  operation: 'validate'
};
const validationProfile = {
  scenePath: 'Packages/com.ai.shader-authoring/Tests/AI Shader Authoring.unity',
  cameraPath: 'Main Camera',
  width: 1024,
  height: 1024,
  minAverageLuminance: 0.01,
  minNonBackgroundRatio: 0.02
};
const target = {
  objectPath: 'Sphere',
  materialPath: 'Assets/AIShader/Generated/TransparentFresnelPBR.mat'
};

try {
  await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'manual-validation-capture', version: '1.0.0' }
  });
  await delay(5000);
  await waitForUnity();

  const sessionStart = await callToolWithRetry('ensure_validation_scene', {
    operationContext,
    validationProfile,
    target
  });
  console.log(JSON.stringify({ sessionStart }, null, 2));
  const sessionJob = await pollJob(sessionStart.jobId);
  console.log(JSON.stringify({ sessionJob }, null, 2));
  if (sessionJob.status !== 'succeeded') throw new Error(sessionJob.error ?? 'Validation-session creation failed.');

  const sessionResult = sessionJob.result;
  const validationSessionId = sessionResult?.validationSessionId;
  if (!validationSessionId) throw new Error('Validation-session job did not return a validationSessionId.');
  const captureStart = await callToolWithRetry('capture_validation', {
    operationContext,
    validationSessionId,
    captures: [{ name: 'material-validation', cameraPath: validationProfile.cameraPath }]
  });
  console.log(JSON.stringify({ captureStart }, null, 2));
  const captureJob = await pollJob(captureStart.jobId);
  console.log(JSON.stringify({ captureJob }, null, 2));
  if (captureJob.status !== 'succeeded') throw new Error(captureJob.error ?? 'Validation capture failed.');

  const captureResult = captureJob.result;
  if (captureResult?.decision !== 'pass') process.exitCode = 1;
  server.kill('SIGINT');
} catch (error) {
  console.error(error);
  server.kill('SIGINT');
  process.exitCode = 1;
}
