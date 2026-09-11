/**
 * Manual Step 7 "constrained execution" Console gate.
 *
 * Spawns the built Node MCP server, then:
 *   1. forces a refresh + shader compile for the generated assets (asynchronous job),
 *   2. polls the job until it settles,
 *   3. reads Unity console diagnostics scoped to the generated assets.
 *
 * Exit code 1 means the generated Shader (or peers) still report Console errors.
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

const shaderPath = 'Assets/AIShader/Generated/TransparentFresnelPBR.shader';
const materialPath = 'Assets/AIShader/Generated/TransparentFresnelPBR.mat';
const scenePath = 'Assets/AIShader/Generated/TransparentFresnelPBRValidation.unity';
const assetPaths = [shaderPath, materialPath, scenePath];
const operationContext = { runId: 'transparent-fresnel-pbr-smoke-test', skill: 'manual-console-check', codePlanId: 'transparent-fresnel-pbr-v2' };

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function callTool(name, args) {
  const result = await request('tools/call', { name, arguments: args });
  const payload = result.content?.[0]?.text;
  return payload ? JSON.parse(payload) : result;
}

/**
 * A Unity domain reload keeps the TCP socket open while the editor stops processing messages,
 * so a call can time out even though the bridge reports "connected". Retry those transient
 * failures instead of treating them as real diagnostics failures.
 */
function isTransient(error) {
  const message = error?.message ?? '';
  return message.includes('did not acknowledge within 30 seconds') || message.includes('not connected') || message.includes('disconnected');
}

async function callToolWithRetry(name, args, attempts = 5) {
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      return await callTool(name, args);
    } catch (error) {
      lastError = error;
      if (!isTransient(error)) throw error;
      console.error(`[console-check] ${name} attempt ${attempt} failed transiently: ${error.message}`);
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

/**
 * An edited editor assembly triggers a Unity domain reload, which drops the WebSocket until Unity's
 * five-second retry loop succeeds. Poll a cheap read tool until the bridge is actually usable.
 */
async function waitForUnity(timeoutMs = 300000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  let attempts = 0;
  while (Date.now() < deadline) {
    try {
      await callTool('get_editor_state', {});
      if (attempts > 0) console.error(`[console-check] Unity bridge recovered after ${attempts} attempts.`);
      return;
    } catch (error) {
      lastError = error;
      attempts += 1;
      if (attempts % 10 === 0) console.error(`[console-check] still waiting for Unity Editor (attempt ${attempts})...`);
      // Asset reimports and domain reloads can block the editor main thread for a while.
      await delay(3000);
    }
  }
  throw new Error(`Unity Editor did not connect: ${lastError?.message ?? 'unknown'}`);
}

try {
  await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'manual-console-check', version: '1.0.0' }
  });
  // Unity retries its WebSocket connection at a five-second interval after a server restart.
  await delay(5000);
  await waitForUnity();

  const since = new Date().toISOString();
  // Unity jobs live in memory, so an editor domain reload can drop a job mid-flight. Re-issue once.
  let compileJob;
  for (let attempt = 1; attempt <= 2; attempt += 1) {
    const compileStart = await callToolWithRetry('refresh_and_compile_assets', { operationContext, assetPaths });
    console.log(JSON.stringify({ compileStart }, null, 2));
    try {
      compileJob = await pollJob(compileStart.jobId);
      break;
    } catch (error) {
      if (attempt === 2 || !String(error.message).includes('Unknown jobId')) throw error;
      console.error('[console-check] Job lost to a domain reload; re-issuing compile.');
    }
  }
  console.log(JSON.stringify({ compileJob }, null, 2));

  const probePath = process.env.CONSOLE_CHECK_PATH;
  const diagnostics = await callToolWithRetry('get_console_diagnostics', {
    operationContext,
    assetPaths: probePath ? [probePath] : assetPaths,
    includeWarnings: true,
    since
  });
  console.log(JSON.stringify({ diagnostics }, null, 2));

  if ((diagnostics.errorCount ?? 0) > 0) {
    process.exitCode = 1;
  }
  server.kill('SIGINT');
} catch (error) {
  console.error(error);
  server.kill('SIGINT');
  process.exitCode = 1;
}
