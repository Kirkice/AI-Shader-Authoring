#!/usr/bin/env node
import { randomUUID } from 'node:crypto';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from '@modelcontextprotocol/sdk/types.js';
import { WebSocketServer, WebSocket } from 'ws';

interface UnityEditorIdentity {
  editorInstanceId: string;
  projectPath: string;
  projectName: string;
  unityVersion: string;
  processId: number;
  protocolVersion: number;
}

interface UnityEditorState {
  activeGameObjects: string[];
  selectedObjects: string[];
  playModeState: string;
  sceneHierarchy: any;
  projectStructure: {
    [key: string]: string[];
  };
}

interface LogEntry {
  message: string;
  stackTrace: string;
  logType: string;
  timestamp: string;
}

interface UnityEditorClient {
  socket: WebSocket;
  identity: UnityEditorIdentity | null;
  editorState: UnityEditorState;
  logBuffer: LogEntry[];
}

interface PendingUnityRequest {
  resolve: (value: any) => void;
  reject: (reason?: any) => void;
  editorInstanceId: string;
}

class UnityMCPServer {
  private server: Server;
  private wsServer: WebSocketServer;
  private readonly websocketHost = process.env.UNITY_MCP_WS_HOST || '127.0.0.1';
  private readonly websocketPort = this.readWebSocketPort(process.env.UNITY_MCP_WS_PORT);
  private readonly agentSessionId = process.env.UNITY_MCP_AGENT_SESSION_ID || randomUUID();
  private targetEditorInstanceId = process.env.UNITY_MCP_TARGET_EDITOR_ID || '';
  private readonly targetProjectPath = this.normalizePath(process.env.UNITY_MCP_TARGET_PROJECT_PATH || '');
  private readonly editorClients = new Map<WebSocket, UnityEditorClient>();
  private readonly pendingUnityRequests = new Map<string, PendingUnityRequest>();
  private editorState: UnityEditorState = {
    activeGameObjects: [],
    selectedObjects: [],
    playModeState: 'Stopped',
    sceneHierarchy: {},
    projectStructure: {}
  };

  private logBuffer: LogEntry[] = [];
  private readonly maxLogBufferSize = 1000;
  
  private commandStartTime: number | null = null;

  constructor() {
    // Initialize MCP Server
    this.server = new Server(
      {
        name: 'unity-mcp-server',
        version: '0.1.0',
      },
      {
        capabilities: {
          tools: {},
        },
      }
    );

    // Initialize WebSocket Server for Unity communication.
    this.wsServer = new WebSocketServer({ host: this.websocketHost, port: this.websocketPort });
    this.setupWebSocket();
    this.setupTools();

    // Error handling
    this.server.onerror = (error) => console.error('[MCP Error]', error);
    process.on('SIGINT', async () => {
      await this.cleanup();
      process.exit(0);
    });
  }

  private setupWebSocket() {
    console.error(`[Unity MCP] WebSocket server starting on ${this.websocketHost}:${this.websocketPort}`);
    
    this.wsServer.on('listening', () => {
      console.error(`[Unity MCP] WebSocket server is listening on ${this.websocketHost}:${this.websocketPort}`);
    });

    this.wsServer.on('error', (error) => {
      console.error('[Unity MCP] WebSocket server error:', error);
    });

    this.wsServer.on('connection', (ws: WebSocket) => {
      const client: UnityEditorClient = {
        socket: ws,
        identity: null,
        editorState: this.emptyEditorState(),
        logBuffer: []
      };
      this.editorClients.set(ws, client);
      console.error('[Unity MCP] Unity Editor socket connected; awaiting hello identity.');

      ws.on('message', (data: Buffer) => {
        try {
          const message = JSON.parse(data.toString());
          this.handleUnityMessage(client, message);
        } catch (error) {
          console.error('[Unity MCP] Error handling message:', error);
        }
      });

      ws.on('error', (error) => console.error('[Unity MCP] WebSocket error:', error));
      ws.on('close', () => this.handleEditorDisconnected(client));
    });
  }

  private readWebSocketPort(value: string | undefined): number {
    if (!value) return 8080;
    const port = Number(value);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      throw new Error('UNITY_MCP_WS_PORT must be an integer between 1 and 65535.');
    }
    return port;
  }

  private handleUnityMessage(client: UnityEditorClient, message: any) {
    switch (message.type) {
      case 'hello':
        this.registerEditorIdentity(client, message.data);
        break;
      case 'editorState':
        // Create a simplified version of the state
        const filteredData: UnityEditorState = {
          activeGameObjects: message.data.activeGameObjects || [],
          selectedObjects: message.data.selectedObjects || [],
          playModeState: message.data.playModeState || 'Stopped',
          sceneHierarchy: message.data.sceneHierarchy || {},
          projectStructure: {}
        };

        // Filter project structure to only include user files
        if (message.data.projectStructure) {
          Object.keys(message.data.projectStructure).forEach(key => {
            if (Array.isArray(message.data.projectStructure[key])) {
              filteredData.projectStructure[key] = (message.data.projectStructure[key] as string[]).filter(
                (path: string) => !path.startsWith('Packages/')
              );
            }
          });
        }

        client.editorState = filteredData;
        if (this.isSelectedClient(client)) this.editorState = filteredData;
        break;
      
      case 'commandResult':
      case 'structuredToolResult':
        this.resolveUnityRequest(client, message.data);
        break;

      case 'log':
        this.handleLogMessage(client, message.data);
        break;
      
      default:
        console.error('[Unity MCP] Unknown message type:', message.type);
    }
  }

  private emptyEditorState(): UnityEditorState {
    return { activeGameObjects: [], selectedObjects: [], playModeState: 'Stopped', sceneHierarchy: {}, projectStructure: {} };
  }

  private normalizePath(path: string): string {
    return path.replace(/\\/g, '/').replace(/\/$/, '');
  }

  private registerEditorIdentity(client: UnityEditorClient, data: any): void {
    const editorInstanceId = typeof data?.editorInstanceId === 'string' ? data.editorInstanceId : '';
    const projectPath = this.normalizePath(typeof data?.projectPath === 'string' ? data.projectPath : '');
    if (!editorInstanceId || !projectPath) {
      console.error('[Unity MCP] Ignoring Unity socket without editorInstanceId/projectPath hello payload.');
      client.socket.close(1008, 'hello requires editor identity');
      return;
    }
    client.identity = {
      editorInstanceId,
      projectPath,
      projectName: typeof data.projectName === 'string' ? data.projectName : '',
      unityVersion: typeof data.unityVersion === 'string' ? data.unityVersion : '',
      processId: Number(data.processId) || 0,
      protocolVersion: Number(data.protocolVersion) || 1
    };
    console.error(`[Unity MCP] Registered Editor ${editorInstanceId} for ${projectPath}.`);
  }

  private resolveTargetEditor(): UnityEditorClient {
    const candidates = Array.from(this.editorClients.values()).filter(client =>
      client.identity !== null
      && client.socket.readyState === WebSocket.OPEN
      && (!this.targetEditorInstanceId || client.identity.editorInstanceId === this.targetEditorInstanceId)
      && (!this.targetProjectPath || client.identity.projectPath === this.targetProjectPath));
    if (candidates.length === 1) return candidates[0];
    const available = Array.from(this.editorClients.values())
      .filter(client => client.identity !== null)
      .map(client => `${client.identity!.editorInstanceId} (${client.identity!.projectPath})`)
      .join(', ') || 'none';
    const reason = candidates.length === 0
      ? `No Unity Editor matches this Agent binding. Set UNITY_MCP_TARGET_EDITOR_ID or UNITY_MCP_TARGET_PROJECT_PATH. Available: ${available}`
      : `Agent binding is ambiguous; ${candidates.length} Unity Editors match. Set UNITY_MCP_TARGET_EDITOR_ID. Matches: ${available}`;
    throw new McpError(ErrorCode.InternalError, reason);
  }

  private isSelectedClient(client: UnityEditorClient): boolean {
    try { return this.resolveTargetEditor() === client; } catch { return false; }
  }

  private resolveUnityRequest(client: UnityEditorClient, data: any): void {
    const requestId = typeof data?.requestId === 'string' ? data.requestId : '';
    const pending = requestId ? this.pendingUnityRequests.get(requestId) : undefined;
    if (!pending) {
      console.error(`[Unity MCP] Ignored unmatched Unity response requestId=${requestId || '<missing>'}.`);
      return;
    }
    if (client.identity?.editorInstanceId !== pending.editorInstanceId) {
      pending.reject(new Error('Unity response came from an Editor other than the bound target.'));
    } else {
      pending.resolve(data);
    }
    this.pendingUnityRequests.delete(requestId);
  }

  private handleEditorDisconnected(client: UnityEditorClient): void {
    this.editorClients.delete(client.socket);
    const identity = client.identity?.editorInstanceId ?? '<unidentified>';
    console.error(`[Unity MCP] Unity Editor disconnected: ${identity}`);
    for (const [requestId, pending] of this.pendingUnityRequests) {
      if (pending.editorInstanceId === client.identity?.editorInstanceId) {
        pending.reject(new Error(`Bound Unity Editor ${identity} disconnected while request ${requestId} was executing.`));
        this.pendingUnityRequests.delete(requestId);
      }
    }
  }

  private setupTools() {
    // List available tools with comprehensive documentation
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [
        {
          name: 'get_editor_state',
          description: 'Retrieve the current state of the Unity Editor, including active GameObjects, selection state, play mode status, scene hierarchy, and project structure. This tool provides a comprehensive snapshot of the editor\'s current context.',
          category: 'Editor State',
          tags: ['unity', 'editor', 'state', 'hierarchy', 'project'],
          inputSchema: {
            type: 'object',
            properties: {
              format: {
                type: 'string',
                enum: ['Raw', 'scripts only', 'no scripts'],
                description: 'Specify the output format:\n- Raw: Complete editor state including all available data\n- scripts only: Returns only the list of script files in the project\n- no scripts: Returns everything except script-related information',
                default: 'Raw'
              }
            },
            additionalProperties: false
          },
          returns: {
            type: 'object',
            description: 'Returns a JSON object containing the requested editor state information',
            format: 'The response format varies based on the format parameter:\n- Raw: Full UnityEditorState object\n- scripts only: Array of script file paths\n- no scripts: UnityEditorState minus script-related fields'
          },
          examples: [
            {
              description: 'Get complete editor state',
              input: {},
              output: '{ "activeGameObjects": ["Main Camera", "Directional Light"], ... }'
            },
            {
              description: 'Get only script files',
              input: { format: 'scripts only' },
              output: '["Assets/Scripts/Player.cs", "Assets/Scripts/Enemy.cs"]'
            }
          ]
        },
        {
          name: 'execute_editor_command',
          description: 'Execute arbitrary C# code within the Unity Editor context. This powerful tool allows for direct manipulation of the Unity Editor, GameObjects, components, and project assets using the Unity Editor API.',
          category: 'Editor Control',
          tags: ['unity', 'editor', 'command', 'c#', 'scripting'],
          inputSchema: {
            type: 'object',
            properties: {
              code: {
                type: 'string',
                description: 'C# code to execute in the Unity Editor context. The code has access to all UnityEditor and UnityEngine APIs.',
                minLength: 1,
                examples: [
                  'Selection.activeGameObject.transform.position = Vector3.zero;',
                  'EditorApplication.isPlaying = !EditorApplication.isPlaying;'
                ]
              }
            },
            required: ['code'],
            additionalProperties: false
          },
          returns: {
            type: 'object',
            description: 'Returns the execution result and any logs generated during execution',
            format: 'JSON object containing "result" and "logs" fields'
          },
          errorHandling: {
            description: 'Common error scenarios and their handling:',
            scenarios: [
              {
                error: 'Compilation error',
                handling: 'Returns compilation error details in logs'
              },
              {
                error: 'Runtime exception',
                handling: 'Returns exception details and stack trace'
              },
              {
                error: 'Timeout',
                handling: 'Command execution timeout after 5 seconds'
              }
            ]
          },
          examples: [
            {
              description: 'Center selected object',
              input: {
                code: 'var selected = Selection.activeGameObject; if(selected != null) { selected.transform.position = Vector3.zero; }'
              },
              output: '{ "result": true, "logs": ["[UnityMCP] Command executed successfully"] }'
            }
          ]
        },
        {
          name: 'get_logs',
          description: 'Retrieve and filter Unity Editor logs with comprehensive filtering options. This tool provides access to editor logs, console messages, warnings, errors, and exceptions with powerful filtering capabilities.',
          category: 'Debugging',
          tags: ['unity', 'editor', 'logs', 'debugging', 'console'],
          inputSchema: {
            type: 'object',
            properties: {
              types: {
                type: 'array',
                items: {
                  type: 'string',
                  enum: ['Log', 'Warning', 'Error', 'Exception'],
                  description: 'Log entry types to include'
                },
                description: 'Filter logs by type. If not specified, all types are included.',
                examples: [['Error', 'Exception'], ['Log', 'Warning']]
              },
              count: {
                type: 'number',
                description: 'Maximum number of log entries to return',
                minimum: 1,
                maximum: 1000,
                default: 100
              },
              fields: {
                type: 'array',
                items: {
                  type: 'string',
                  enum: ['message', 'stackTrace', 'logType', 'timestamp']
                },
                description: 'Specify which fields to include in the output. If not specified, all fields are included.',
                examples: [['message', 'logType'], ['message', 'stackTrace', 'timestamp']]
              },
              messageContains: {
                type: 'string',
                description: 'Filter logs to only include entries where the message contains this string (case-sensitive)',
                minLength: 1
              },
              stackTraceContains: {
                type: 'string',
                description: 'Filter logs to only include entries where the stack trace contains this string (case-sensitive)',
                minLength: 1
              },
              timestampAfter: {
                type: 'string',
                description: 'Filter logs after this ISO timestamp (inclusive)',
                format: 'date-time',
                example: '2024-01-14T00:00:00Z'
              },
              timestampBefore: {
                type: 'string',
                description: 'Filter logs before this ISO timestamp (inclusive)',
                format: 'date-time',
                example: '2024-01-14T23:59:59Z'
              }
            },
            additionalProperties: false
          },
          returns: {
            type: 'array',
            description: 'Returns an array of log entries matching the specified filters',
            format: 'Array of objects containing requested log entry fields'
          },
          examples: [
            {
              description: 'Get recent error logs',
              input: {
                types: ['Error', 'Exception'],
                count: 10,
                fields: ['message', 'timestamp']
              },
              output: '[{"message": "NullReferenceException", "timestamp": "2024-01-14T12:00:00Z"}, ...]'
            },
            {
              description: 'Search logs for specific message',
              input: {
                messageContains: 'Player',
                fields: ['message', 'logType']
              },
              output: '[{"message": "Player position updated", "logType": "Log"}, ...]'
            }
          ]
        },
        ...this.getStructuredTools(),
      ],
    }));

    // Handle tool calls with enhanced validation and error handling
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      // Resolve this Agent's explicitly bound Unity Editor before executing any tool.
      const targetEditor = this.resolveTargetEditor();

      const { name, arguments: args } = request.params;

      // Validate tool exists with helpful error message.
      const availableTools = ['get_editor_state', 'execute_editor_command', 'get_logs', ...this.getStructuredToolNames()];
      if (!availableTools.includes(name)) {
        throw new McpError(
          ErrorCode.MethodNotFound,
          `Unknown tool: ${name}. Available tools are: ${availableTools.join(', ')}`
        );
      }

      // Structured operations use a typed Unity-side allow-list and never compile arbitrary C#.
      if (this.getStructuredToolNames().includes(name)) {
        return this.callStructuredTool(name, args ?? {});
      }

      // Validate arguments based on tool schemas
      switch (name) {
        case 'get_editor_state': {
          // Validate format parameter
          const validFormats = ['Raw', 'scripts only', 'no scripts'];
          const format = args?.format as string || 'Raw';
          
          if (args?.format && !validFormats.includes(format)) {
            throw new McpError(
              ErrorCode.InvalidParams,
              `Invalid format: "${format}". Valid formats are: ${validFormats.join(', ')}`
            );
          }

          let responseData: any;

          try {
            switch (format) {
              case 'Raw':
                responseData = this.editorState;
                break;
              case 'scripts only':
                responseData = this.editorState.projectStructure.scripts || [];
                break;
              case 'no scripts': {
                const { projectStructure, ...stateWithoutScripts } = {...this.editorState};
                const { scripts, ...otherStructure } = {...projectStructure};
                responseData = {
                  ...stateWithoutScripts,
                  projectStructure: otherStructure
                };
                break;
              }
            }

            return {
              content: [{
                type: 'text',
                text: JSON.stringify(responseData, null, 2)
              }]
            };
          } catch (error) {
            throw new McpError(
              ErrorCode.InternalError,
              `Failed to process editor state: ${error instanceof Error ? error.message : 'Unknown error'}`
            );
          }
        }

        case 'execute_editor_command': {
          // Validate code parameter
          if (!args?.code) {
            throw new McpError(
              ErrorCode.InvalidParams,
              'The code parameter is required'
            );
          }
          
          if (typeof args.code !== 'string') {
            throw new McpError(
              ErrorCode.InvalidParams,
              'The code parameter must be a string'
            );
          }

          if (args.code.trim().length === 0) {
            throw new McpError(
              ErrorCode.InvalidParams,
              'The code parameter cannot be empty'
            );
          }

          try {
            // Clear previous logs and set command start time
            const startLogIndex = this.logBuffer.length;
            this.commandStartTime = Date.now();

            const requestId = randomUUID();
            try {
              const resultPromise = new Promise<any>((resolve, reject) => {
                this.pendingUnityRequests.set(requestId, { resolve, reject, editorInstanceId: targetEditor.identity!.editorInstanceId });
              });
              targetEditor.socket.send(JSON.stringify({
                type: 'executeEditorCommand',
                data: { requestId, agentSessionId: this.agentSessionId, code: args.code },
              }));

              // Wait for result with enhanced timeout handling
              const timeoutMs = 5000;
              const result = await Promise.race([
                resultPromise,
                new Promise((_, reject) =>
                  setTimeout(() => reject(new Error(
                    `Command execution timed out after ${timeoutMs/1000} seconds. This may indicate a long-running operation or an issue with the Unity Editor.`
                  )), timeoutMs)
                )
              ]);

            const commandResult = result as {
              executionSuccess?: boolean;
              errorDetails?: { message?: string };
              errors?: string[];
            };
            if (!commandResult.executionSuccess) {
              throw new Error(
                commandResult.errorDetails?.message
                ?? commandResult.errors?.join('\n')
                ?? 'Unity Editor command failed.'
              );
            }

            // Get logs that occurred during command execution
            const commandLogs = this.logBuffer
              .slice(startLogIndex)
              .filter(log => (log.message ?? '').includes('[UnityMCP]'));

            // Calculate execution time
            const executionTime = Date.now() - this.commandStartTime;

              return {
                content: [
                  {
                    type: 'text',
                    text: JSON.stringify({
                      result,
                      logs: commandLogs,
                      executionTime: `${executionTime}ms`,
                      status: 'success'
                    }, null, 2),
                  },
                ],
              };
            } finally {
              this.pendingUnityRequests.delete(requestId);
            }
          } catch (error) {
            // Enhanced error handling with specific error types
            if (error instanceof Error) {
              if (error.message.includes('timed out')) {
                throw new McpError(
                  ErrorCode.InternalError,
                  error.message
                );
              }
              
              // Check for common Unity-specific errors
              if (error.message.includes('NullReferenceException')) {
                throw new McpError(
                  ErrorCode.InvalidParams,
                  'The code attempted to access a null object. Please check that all GameObject references exist.'
                );
              }

              if (error.message.includes('CompileError')) {
                throw new McpError(
                  ErrorCode.InvalidParams,
                  'C# compilation error. Please check the syntax of your code.'
                );
              }
            }

            // Generic error fallback
            throw new McpError(
              ErrorCode.InternalError,
              `Failed to execute command: ${error instanceof Error ? error.message : 'Unknown error'}`
            );
          }
        }

        case 'get_logs': {
          const options = {
            types: args?.types as string[] | undefined,
            count: args?.count as number || 100,
            fields: args?.fields as string[] | undefined,
            messageContains: args?.messageContains as string | undefined,
            stackTraceContains: args?.stackTraceContains as string | undefined,
            timestampAfter: args?.timestampAfter as string | undefined,
            timestampBefore: args?.timestampBefore as string | undefined
          };
          
          const logs = this.filterLogs(options);
          return {
            content: [
              {
                type: 'text',
                text: JSON.stringify(logs, null, 2),
              },
            ],
          };
        }

        default:
          throw new McpError(
            ErrorCode.MethodNotFound,
            `Unknown tool: ${name}`
          );
      }
    });
  }

  private getStructuredToolNames(): string[] {
    return [
      'run_unity_job', 'get_unity_job', 'cancel_unity_job',
      'get_shader_knowledge_base_status', 'build_shader_knowledge_base', 'query_shader_knowledge_base',
      'inspect_shader_structure', 'get_asset_revision', 'write_generated_text_asset',
      'refresh_and_compile_assets', 'ensure_validation_scene', 'capture_validation',
      'create_shader_checkpoint', 'restore_shader_checkpoint', 'get_console_diagnostics',
      'export_compiled_gles_variants', 'analyze_shader_performance'
    ];
  }

  private getStructuredTools(): any[] {
    const jobContext = {
      type: 'object',
      properties: {
        operationContext: {
          type: 'object',
          description: 'Audit context containing the non-empty runId, skill, codePlanId, and optional authorizationGrantId.',
          properties: {
            runId: { type: 'string', minLength: 1 },
            skill: { type: 'string', minLength: 1 },
            codePlanId: { type: 'string', minLength: 1 },
            authorizationGrantId: { type: 'string', minLength: 1 }
          },
          required: ['runId', 'skill', 'codePlanId'],
          additionalProperties: false
        },
        idempotencyKey: { type: 'string', minLength: 1 }
      },
      additionalProperties: true
    };
    return [
      { name: 'run_unity_job', description: 'Start an allow-listed asynchronous Unity job. Never accepts C# source.', category: 'Shader Jobs', inputSchema: { ...jobContext, properties: { ...jobContext.properties, jobType: { type: 'string', enum: ['build_shader_knowledge_base', 'refresh_and_compile_assets', 'ensure_validation_scene', 'capture_validation', 'create_shader_checkpoint'] }, args: { type: 'object' } }, required: ['jobType'] } },
      { name: 'get_unity_job', description: 'Read status and artifacts for an asynchronous Unity job.', category: 'Shader Jobs', inputSchema: { type: 'object', properties: { jobId: { type: 'string' } }, required: ['jobId'], additionalProperties: false } },
      { name: 'cancel_unity_job', description: 'Cancel a queued structured Unity job.', category: 'Shader Jobs', inputSchema: { type: 'object', properties: { jobId: { type: 'string' }, reason: { type: 'string' } }, required: ['jobId'], additionalProperties: false } },
      { name: 'get_shader_knowledge_base_status', description: 'Read persistent project Shader Knowledge Base freshness and version.', category: 'Shader Knowledge', inputSchema: { type: 'object', properties: { expectedSchemaVersion: { type: 'string' } }, additionalProperties: false } },
      { name: 'build_shader_knowledge_base', description: 'Queue a full or incremental project Shader Knowledge Base build.', category: 'Shader Knowledge', inputSchema: { ...jobContext, properties: { ...jobContext.properties, mode: { type: 'string', enum: ['full', 'incremental'] }, reason: { type: 'string' } }, required: ['mode'] } },
      { name: 'query_shader_knowledge_base', description: 'Retrieve persisted Shader examples, function cards and capabilities.', category: 'Shader Knowledge', inputSchema: { type: 'object', properties: { knowledgeBaseVersion: { type: 'string' }, query: { type: 'object' } }, additionalProperties: true } },
      { name: 'inspect_shader_structure', description: 'Read a Shader asset into structured properties, passes, entries, includes and render states.', category: 'Shader Analysis', inputSchema: { type: 'object', properties: { assetPath: { type: 'string' }, expectedRevision: { type: 'string' } }, required: ['assetPath'], additionalProperties: true } },
      { name: 'get_asset_revision', description: 'Read content revisions for project-relative assets.', category: 'Shader Analysis', inputSchema: { type: 'object', properties: { assetPaths: { type: 'array', items: { type: 'string' } } }, required: ['assetPaths'], additionalProperties: false } },
      {
        name: 'write_generated_text_asset',
        description: 'Write one revision-protected generated text asset under Assets/AIShader/Generated only.',
        category: 'Shader Assets',
        inputSchema: {
          type: 'object',
          properties: {
            operationContext: {
              type: 'object',
              description: 'Audit context for the authorized Shader write. codePlanId must match codePlan.codePlanId.',
              properties: {
                runId: { type: 'string', minLength: 1 },
                skill: { type: 'string', minLength: 1 },
                codePlanId: { type: 'string', minLength: 1 },
                authorizationGrantId: { type: 'string', minLength: 1 }
              },
              required: ['runId', 'skill', 'codePlanId'],
              additionalProperties: false
            },
            idempotencyKey: { type: 'string', minLength: 1 },
            asset: {
              type: 'object',
              properties: {
                path: { type: 'string', minLength: 1 },
                contentUtf8: { type: 'string' },
                baseRevision: { type: 'string', minLength: 1 },
                createPolicy: { type: 'string', enum: ['create_only', 'update_only', 'create_or_update'] }
              },
              required: ['path', 'contentUtf8', 'baseRevision'],
              additionalProperties: false
            },
            codePlan: {
              type: 'object',
              description: 'Authorized write plan. codePlanId must match operationContext.codePlanId and allowedFiles must contain the exact target path.',
              properties: {
                codePlanId: { type: 'string', minLength: 1 },
                allowedFiles: { type: 'array', minItems: 1, items: { type: 'string', minLength: 1 } },
                baselineRevision: { type: 'string' },
                objective: { type: 'string' }
              },
              required: ['codePlanId', 'allowedFiles'],
              additionalProperties: true
            }
          },
          required: ['operationContext', 'idempotencyKey', 'asset', 'codePlan'],
          additionalProperties: false
        }
      },
      { name: 'refresh_and_compile_assets', description: 'Queue refresh and import for specified assets, returning structured compile evidence.', category: 'Shader Validation', inputSchema: { ...jobContext, properties: { ...jobContext.properties, assetPaths: { type: 'array', items: { type: 'string' } } }, required: ['assetPaths'] } },
      {
        name: 'ensure_validation_scene',
        description: 'Queue isolated deterministic validation-session setup. The scene, camera, target Renderer, and Material paths are required.',
        category: 'Shader Validation',
        inputSchema: {
          ...jobContext,
          properties: {
            ...jobContext.properties,
            validationProfile: {
              type: 'object',
              properties: {
                scenePath: { type: 'string', minLength: 1 },
                cameraPath: { type: 'string', minLength: 1 },
                width: { type: 'integer', minimum: 64, maximum: 4096 },
                height: { type: 'integer', minimum: 64, maximum: 4096 },
                minAverageLuminance: { type: 'number', minimum: 0, maximum: 1 },
                minNonBackgroundRatio: { type: 'number', minimum: 0, maximum: 1 }
              },
              required: ['scenePath', 'cameraPath'],
              additionalProperties: false
            },
            target: {
              type: 'object',
              properties: {
                objectPath: { type: 'string', minLength: 1 },
                materialPath: { type: 'string', minLength: 1 }
              },
              required: ['objectPath', 'materialPath'],
              additionalProperties: false
            }
          },
          required: ['operationContext', 'idempotencyKey', 'validationProfile', 'target']
        }
      },
      {
        name: 'capture_validation',
        description: 'Queue deterministic validation capture for an existing validation session.',
        category: 'Shader Validation',
        inputSchema: {
          ...jobContext,
          properties: {
            ...jobContext.properties,
            validationSessionId: { type: 'string', minLength: 1 },
            captures: {
              type: 'array',
              minItems: 1,
              items: {
                type: 'object',
                properties: {
                  captureName: { type: 'string', minLength: 1 },
                  bindingMode: { type: 'string', enum: ['generated_material', 'preserve_original'] }
                },
                required: ['captureName', 'bindingMode'],
                additionalProperties: false
              }
            }
          },
          required: ['operationContext', 'idempotencyKey', 'validationSessionId', 'captures']
        }
      },
      { name: 'create_shader_checkpoint', description: 'Queue an immutable Shader run checkpoint manifest.', category: 'Shader Validation', inputSchema: { ...jobContext, properties: { ...jobContext.properties, decision: { type: 'string', enum: ['pass', 'revise', 'blocked'] }, assetRevisions: { type: 'array' } }, required: ['decision', 'assetRevisions'] } },
      { name: 'restore_shader_checkpoint', description: 'Request structured restoration of a generated-assets checkpoint.', category: 'Shader Assets', inputSchema: { ...jobContext, properties: { ...jobContext.properties, checkpointId: { type: 'string' } }, required: ['checkpointId'] } },
      { name: 'get_console_diagnostics', description: 'Read Unity Console errors and warnings for generated Shader assets, including authoritative shader compiler findings for every platform. Use this after every constrained-execution write in Step 7.', category: 'Shader Validation', inputSchema: { type: 'object', properties: { assetPaths: { type: 'array', items: { type: 'string' }, description: 'Optional project-relative asset paths to scope diagnostics to; omit to scan the whole buffered console.' }, includeWarnings: { type: 'boolean', description: 'Include Warning-severity entries. Defaults to false so only errors block validation.' }, since: { type: 'string', description: 'ISO-8601 cursor (typically the write/job timestamp). Console entries at or before this instant are ignored so stale errors cannot pin a fixed Shader as failed.' }, operationContext: { type: 'object' } }, additionalProperties: true } },
      { name: 'export_compiled_gles_variants', description: 'Export actual Unity-compiled GLES3x GLSL vertex and fragment variants for a Shader.', category: 'Shader Performance', inputSchema: { ...jobContext, properties: { ...jobContext.properties, shaderPath: { type: 'string' } }, required: ['shaderPath'] } },
      { name: 'analyze_shader_performance', description: 'Analyze static Shader cost and optional Mali Offline Compiler metrics. Advisory only; never blocks visual validation.', category: 'Shader Performance', inputSchema: { ...jobContext, properties: { ...jobContext.properties, shaderPath: { type: 'string' }, policy: { type: 'string', enum: ['low_android', 'medium_android', 'high_android', 'all_android', 'custom', 'collect_only'] }, maliTargets: { type: 'array', items: { type: 'string' } }, maliCompilerPath: { type: 'string' } }, required: ['shaderPath', 'policy'] } }
    ];
  }

  private async callStructuredTool(name: string, args: unknown) {
    const targetEditor = this.resolveTargetEditor();
    const requestId = randomUUID();
    const resultPromise = new Promise<any>((resolve, reject) => {
      this.pendingUnityRequests.set(requestId, { resolve, reject, editorInstanceId: targetEditor.identity!.editorInstanceId });
    });
    try {
      targetEditor.socket.send(JSON.stringify({
        type: 'executeStructuredTool',
        data: { requestId, agentSessionId: this.agentSessionId, toolName: name, args }
      }));
      const result = await Promise.race([
        resultPromise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('Structured Unity tool did not acknowledge within 30 seconds.')), 30000))
      ]);
      if (!result?.executionSuccess) {
        throw new McpError(ErrorCode.InternalError, result?.errorDetails?.message ?? 'Structured Unity tool failed.');
      }
      return { content: [{ type: 'text', text: JSON.stringify(result.result, null, 2) }] };
    } finally {
      this.pendingUnityRequests.delete(requestId);
    }
  }

  private handleLogMessage(client: UnityEditorClient, logEntry: LogEntry) {
    client.logBuffer.push(logEntry);
    if (client.logBuffer.length > this.maxLogBufferSize) client.logBuffer.shift();
    if (this.isSelectedClient(client)) {
      this.logBuffer = client.logBuffer;
    }
  }

  private filterLogs(options: {
    types?: string[],
    count?: number,
    fields?: string[],
    messageContains?: string,
    stackTraceContains?: string,
    timestampAfter?: string,
    timestampBefore?: string
  }): any[] {
    const {
      types,
      count = 100,
      fields,
      messageContains,
      stackTraceContains,
      timestampAfter,
      timestampBefore
    } = options;

    // First apply all filters
    let filteredLogs = this.logBuffer
      .filter(log => {
        // Type filter
        if (types && !types.includes(log.logType)) return false;
        
        // Message content filter
        if (messageContains && !(log.message ?? '').includes(messageContains)) return false;
        
        // Stack trace content filter
        if (stackTraceContains && !(log.stackTrace ?? '').includes(stackTraceContains)) return false;
        
        // Timestamp filters
        if (timestampAfter && new Date(log.timestamp) < new Date(timestampAfter)) return false;
        if (timestampBefore && new Date(log.timestamp) > new Date(timestampBefore)) return false;
        
        return true;
      });

    // Then apply count limit
    filteredLogs = filteredLogs.slice(-count);

    // Finally apply field selection if specified
    if (fields?.length) {
      return filteredLogs.map(log => {
        const selectedFields: Partial<LogEntry> = {};
        fields.forEach(field => {
          if (field in log && (field === 'message' || field === 'stackTrace' ||
              field === 'logType' || field === 'timestamp')) {
            selectedFields[field as keyof LogEntry] = log[field as keyof LogEntry];
          }
        });
        return selectedFields;
      });
    }

    return filteredLogs;
  }

  private async cleanup() {
    for (const client of this.editorClients.values()) {
      client.socket.close();
    }
    this.wsServer.close();
    await this.server.close();
  }

  async run() {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error('Unity MCP server running on stdio');
    
    // Wait for WebSocket server to be ready
    await new Promise<void>((resolve) => {
      this.wsServer.once('listening', () => {
        console.error('[Unity MCP] WebSocket server is ready on port 8080');
        resolve();
      });
    });
  }
}

const server = new UnityMCPServer();
server.run().catch(console.error);
