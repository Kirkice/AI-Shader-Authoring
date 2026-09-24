#if UNITY_EDITOR
using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMcp.Editor
{
    /// <summary>
    /// Unity-side WebSocket client used by the bundled UnityMCP stdio server.
    /// This replaces the former local HTTP bridge.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityMcpConnection
    {
        public enum ServiceState
        {
            Closed,
            WaitingForConnection,
            Connected
        }

        private const string DefaultServerHost = "127.0.0.1";
        private const int DefaultServerPort = 8080;
        private const string ServerHostPreferenceKey = "UnityMcp.ServerHost";
        private const string ServerPortPreferenceKey = "UnityMcp.ServerPort";
        private const int MaxLogEntries = 1000;
        private static readonly ConcurrentQueue<string> PendingMessages = new ConcurrentQueue<string>();
        private static readonly Queue<LogEntry> RecentLogs = new Queue<LogEntry>();
        private static readonly object SendLock = new object();

        private static ClientWebSocket webSocket;
        private static CancellationTokenSource cancellation;
        private static DateTime nextConnectAttemptUtc;
        private static DateTime nextStateSendUtc;
        private static bool connecting;
        private static bool isConnected;
        private static string lastErrorMessage = string.Empty;
        private static bool serviceEnabled = true;
        private static readonly string EditorInstanceId = GetOrCreateEditorInstanceId();
        private static readonly string ProjectPath = NormalizePath(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath);

        public static bool IsConnected => isConnected && webSocket != null && webSocket.State == WebSocketState.Open;
        public static string CurrentEditorInstanceId => EditorInstanceId;
        public static string CurrentProjectPath => ProjectPath;
        public static string ServerHost => EditorPrefs.GetString(ServerHostPreferenceKey, DefaultServerHost);
        public static int ServerPort => EditorPrefs.GetInt(ServerPortPreferenceKey, DefaultServerPort);
        public static bool IsServiceEnabled => serviceEnabled;
        public static ServiceState State => !serviceEnabled
            ? ServiceState.Closed
            : IsConnected
                ? ServiceState.Connected
                : ServiceState.WaitingForConnection;
        public static Uri ServerUri => BuildServerUri(ServerHost, ServerPort);
        public static string LastErrorMessage => lastErrorMessage;
        public static int LogCount => RecentLogs.Count;

        static UnityMcpConnection()
        {
            Application.logMessageReceived += HandleLogMessage;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Disconnect;
            EditorApplication.quitting += Disconnect;
            EditorApplication.delayCall += StartService;
        }

        public static void StartService()
        {
            serviceEnabled = true;
            lastErrorMessage = string.Empty;
            nextConnectAttemptUtc = DateTime.MinValue;
            ConnectToServer();
        }

        public static void StopService()
        {
            serviceEnabled = false;
            lastErrorMessage = string.Empty;
            Disconnect();
        }

        public static void RetryConnection()
        {
            StartService();
        }

        public static bool TryConfigureServerEndpoint(string host, string portText, out string errorMessage)
        {
            errorMessage = null;
            host = (host ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host) || host.IndexOf("://", StringComparison.Ordinal) >= 0 || host.IndexOf('/') >= 0 || host.IndexOf('?') >= 0 || host.IndexOf('#') >= 0)
            {
                errorMessage = "服务器地址只能填写主机名、IPv4 或 IPv6 地址，不要包含 ws://、路径或查询参数。";
                return false;
            }

            int port;
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                errorMessage = "端口必须是 1 到 65535 之间的整数。";
                return false;
            }

            try { BuildServerUri(host, port); }
            catch (Exception exception)
            {
                errorMessage = "服务器地址无效：" + exception.Message;
                return false;
            }

            EditorPrefs.SetString(ServerHostPreferenceKey, host);
            EditorPrefs.SetInt(ServerPortPreferenceKey, port);
            lastErrorMessage = string.Empty;
            Disconnect();
            if (serviceEnabled) StartService();
            return true;
        }

        public static void Disconnect()
        {
            isConnected = false;
            connecting = false;
            try { cancellation?.Cancel(); } catch { }
            try { webSocket?.Abort(); webSocket?.Dispose(); } catch { }
            cancellation?.Dispose();
            cancellation = null;
            webSocket = null;
        }

        /// <summary>Runs a read-only dashboard action locally and serializes its result for the Editor UI.</summary>
        public static string RunDashboardTool(string toolName, string commandCode = null)
        {
            switch (toolName)
            {
                case "get_editor_state":
                    return JsonSerializer.Serialize(GetEditorState(), new JsonSerializerOptions { WriteIndented = true });
                case "get_logs":
                    return JsonSerializer.Serialize(RecentLogs.ToArray(), new JsonSerializerOptions { WriteIndented = true });
                case "execute_editor_command":
                    if (string.IsNullOrWhiteSpace(commandCode))
                        throw new ArgumentException("请输入要执行的 C# 命令。", nameof(commandCode));
                    return JsonSerializer.Serialize(new { result = CSEditorHelper.ExecuteCommand(commandCode) }, new JsonSerializerOptions { WriteIndented = true });
                default:
                    throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "不支持的 Dashboard 工具。");
            }
        }

        private static void Update()
        {
            while (PendingMessages.TryDequeue(out var message))
                HandleMessageOnMainThread(message);

            if (serviceEnabled && !IsConnected && !connecting && DateTime.UtcNow >= nextConnectAttemptUtc)
                ConnectToServer();

            if (IsConnected && DateTime.UtcNow >= nextStateSendUtc)
            {
                nextStateSendUtc = DateTime.UtcNow.AddSeconds(1);
                SendEditorState();
            }
        }

        private static async void ConnectToServer()
        {
            if (!serviceEnabled || connecting || IsConnected) return;
            connecting = true;
            DisconnectSocketOnly();

            try
            {
                cancellation = new CancellationTokenSource();
                webSocket = new ClientWebSocket();
                await webSocket.ConnectAsync(ServerUri, cancellation.Token);
                if (!serviceEnabled)
                {
                    DisconnectSocketOnly();
                    return;
                }

                isConnected = true;
                lastErrorMessage = string.Empty;
                nextStateSendUtc = DateTime.MinValue;
                Send("hello", new
                {
                    protocolVersion = 2,
                    editorInstanceId = EditorInstanceId,
                    projectPath = ProjectPath,
                    projectName = Path.GetFileName(ProjectPath),
                    unityVersion = Application.unityVersion,
                    processId = System.Diagnostics.Process.GetCurrentProcess().Id
                });
                _ = ReceiveLoop(webSocket, cancellation.Token);
                Debug.Log("[Unity MCP] Connected to WebSocket server as " + EditorInstanceId + " for " + ProjectPath + ".");
            }
            catch (Exception exception)
            {
                isConnected = false;
                lastErrorMessage = exception.Message;
                nextConnectAttemptUtc = DateTime.UtcNow.AddSeconds(5);
            }
            finally
            {
                connecting = false;
            }
        }

        private static void DisconnectSocketOnly()
        {
            try { cancellation?.Cancel(); } catch { }
            try { webSocket?.Abort(); webSocket?.Dispose(); } catch { }
            cancellation?.Dispose();
            cancellation = null;
            webSocket = null;
            isConnected = false;
        }

        private static async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            var builder = new StringBuilder();
            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    builder.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            isConnected = false;
                            nextConnectAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                            return;
                        }
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    PendingMessages.Enqueue(builder.ToString());
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                isConnected = false;
                lastErrorMessage = exception.Message;
                nextConnectAttemptUtc = DateTime.UtcNow.AddSeconds(5);
            }
        }

        /// <summary>Returns a stable snapshot of the recent Unity console entries for structured diagnostics.</summary>
        public static object[] GetRecentLogSnapshot()
        {
            lock (RecentLogs)
            {
                return RecentLogs.ToArray();
            }
        }

        /// <summary>Serializes the recent Unity console snapshot so the Editor assembly stays free of JSON parsing concerns.</summary>
        public static string GetRecentLogSnapshotJson() => JsonSerializer.Serialize(GetRecentLogSnapshot());

        private static void HandleLogMessage(string message, string stackTrace, LogType type)
        {
            var entry = new LogEntry
            {
                message = message,
                stackTrace = stackTrace,
                logType = type.ToString(),
                timestamp = DateTime.UtcNow.ToString("o")
            };
            RecentLogs.Enqueue(entry);
            while (RecentLogs.Count > MaxLogEntries) RecentLogs.Dequeue();
            Send("log", entry);
        }

        private static void SendEditorState() => Send("editorState", GetEditorState());

        private static async void Send(string type, object data)
        {
            if (!IsConnected) return;
            var socket = webSocket;
            var token = cancellation?.Token ?? CancellationToken.None;
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, data }));
            try
            {
                lock (SendLock)
                {
                    if (socket == null || socket.State != WebSocketState.Open) return;
                    socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                isConnected = false;
                lastErrorMessage = exception.Message;
                nextConnectAttemptUtc = DateTime.UtcNow.AddSeconds(5);
            }
            await Task.CompletedTask;
        }

        private static void HandleMessageOnMainThread(string message)
        {
            try
            {
                using (var document = JsonDocument.Parse(message))
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement)) return;
                    switch (typeElement.GetString())
                    {
                    case "executeEditorCommand":
                        ExecuteEditorCommand(root.TryGetProperty("data", out var commandData) ? commandData.GetRawText() : "{}");
                        break;
                    case "executeStructuredTool":
                        ExecuteStructuredTool(root.TryGetProperty("data", out var structuredToolData) ? structuredToolData.GetRawText() : "{}");
                        break;
                    case "selectGameObject":
                        SelectGameObject(root.TryGetProperty("data", out var selectionData)
                            ? JsonSerializer.Deserialize<SelectionData>(selectionData.GetRawText())?.objectPath
                            : null);
                        break;
                    case "togglePlayMode":
                        EditorApplication.isPlaying = !EditorApplication.isPlaying;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Unity MCP] Unable to process server message: {exception}");
            }
        }

        private static void ExecuteEditorCommand(string commandData)
        {
            EditorCommandData command = null;
            var logs = new List<string>();
            var errors = new List<string>();
            var warnings = new List<string>();
            void Capture(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning) warnings.Add(message);
                else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stackTrace);
                else logs.Add(message);
            }

            Application.logMessageReceived += Capture;
            try
            {
                command = JsonSerializer.Deserialize<EditorCommandData>(commandData ?? "{}");
                if (string.IsNullOrWhiteSpace(command?.code)) throw new ArgumentException("The command payload does not contain C# code.");
                Debug.Log($"[Unity MCP] Executing command:\n{command.code}");
                var result = CSEditorHelper.ExecuteCommand(command.code);
                Send("commandResult", new
                {
                    requestId = command.requestId,
                    agentSessionId = command.agentSessionId,
                    result,
                    logs,
                    errors,
                    warnings,
                    executionSuccess = true,
                    errorDetails = (object)null
                });
            }
            catch (Exception exception)
            {
                var error = $"[Unity MCP] Failed to execute editor command: {exception.Message}\n{exception.StackTrace}";
                Debug.LogError(error);
                errors.Add(error);
                Send("commandResult", new { requestId = command?.requestId, agentSessionId = command?.agentSessionId, result = (object)null, logs, errors, warnings, executionSuccess = false, errorDetails = new { message = exception.Message, stackTrace = exception.StackTrace, type = exception.GetType().Name } });
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
        }

        private static void ExecuteStructuredTool(string toolData)
        {
            string requestId = null;
            string agentSessionId = null;
            try
            {
                using (var document = JsonDocument.Parse(toolData ?? "{}"))
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("toolName", out var toolNameElement) || toolNameElement.ValueKind != JsonValueKind.String)
                        throw new ArgumentException("The structured tool payload does not contain toolName.");
                    var toolName = toolNameElement.GetString();
                    requestId = root.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() : null;
                    agentSessionId = root.TryGetProperty("agentSessionId", out var agentSessionIdElement) ? agentSessionIdElement.GetString() : null;
                    var args = root.TryGetProperty("args", out var argsElement) ? argsElement : default;
                    var result = UnityMcpShaderTools.Handle(toolName, args);
                    Send("structuredToolResult", new { requestId, agentSessionId, toolName, result, executionSuccess = true });
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[Unity MCP] Structured tool failed: " + exception);
                Send("structuredToolResult", new
                {
                    requestId,
                    agentSessionId,
                    executionSuccess = false,
                    errorDetails = new { message = exception.Message, stackTrace = exception.StackTrace, type = exception.GetType().Name }
                });
            }
        }

        private static void SelectGameObject(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath)) return;
            var gameObject = GameObject.Find(objectPath);
            if (gameObject != null) Selection.activeGameObject = gameObject;
        }

        private static object GetEditorState()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var activeGameObjects = scene.IsValid()
                ? scene.GetRootGameObjects().Where(gameObject => gameObject.activeInHierarchy).Select(gameObject => gameObject.name).ToArray()
                : Array.Empty<string>();
            var selectedObjects = Selection.objects.Where(item => item != null).Select(item => item.name).ToArray();
            return new
            {
                editorInstanceId = EditorInstanceId,
                projectPath = ProjectPath,
                activeGameObjects,
                selectedObjects,
                playModeState = EditorApplication.isPlaying ? "Playing" : EditorApplication.isPaused ? "Paused" : "Stopped",
                sceneHierarchy = scene.IsValid() ? scene.GetRootGameObjects().Select(SerializeGameObject).ToArray() : Array.Empty<object>(),
                projectStructure = new
                {
                    scenes = EditorBuildSettings.scenes.Select(item => item.path).ToArray(),
                    prefabs = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath).ToArray(),
                    scripts = AssetDatabase.FindAssets("t:Script").Select(AssetDatabase.GUIDToAssetPath).ToArray()
                }
            };
        }

        private static object SerializeGameObject(GameObject gameObject)
        {
            return new
            {
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
                path = GetHierarchyPath(gameObject.transform),
                components = gameObject.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().Name).ToArray(),
                children = Enumerable.Range(0, gameObject.transform.childCount).Select(index => SerializeGameObject(gameObject.transform.GetChild(index).gameObject)).ToArray()
            };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            while (transform != null) { names.Push(transform.name); transform = transform.parent; }
            return string.Join("/", names);
        }

        private static Uri BuildServerUri(string host, int port)
        {
            var uriHost = host != null && host.IndexOf(':') >= 0 && !host.StartsWith("[", StringComparison.Ordinal)
                ? "[" + host + "]"
                : host;
            return new Uri("ws://" + uriHost + ":" + port + "/", UriKind.Absolute);
        }

        private static string GetOrCreateEditorInstanceId()
        {
            const string key = "UnityMcp.EditorInstanceId";
            var value = SessionState.GetString(key, string.Empty);
            if (!string.IsNullOrEmpty(value)) return value;
            value = Guid.NewGuid().ToString("N");
            SessionState.SetString(key, value);
            return value;
        }

        private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

        [Serializable] private sealed class EditorCommandData { public string code { get; set; } public string requestId { get; set; } public string agentSessionId { get; set; } }
        [Serializable] private sealed class SelectionData { public string objectPath { get; set; } }
        [Serializable] private sealed class LogEntry { public string message; public string stackTrace; public string logType; public string timestamp; }

        public static class CSEditorHelper
        {
            public static object ExecuteCommand(string code)
            {
                var wrappedCode = $@"
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
public static class UnityMcpCommandExecutor
{{
    public static object Execute()
    {{
        {code}
        return ""Success"";
    }}
}}";
                var options = new CompilerParameters { GenerateInMemory = true };
                options.ReferencedAssemblies.Add(typeof(UnityEngine.Object).Assembly.Location);
                options.ReferencedAssemblies.Add(typeof(UnityEditor.Editor).Assembly.Location);

                // 动态命令需要和 Editor 域共享完整的 UnityEngine 模块；仅引用旧的 UnityEngine 门面程序集会缺少 Scene、Material 等类型。
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                    .Where(candidate => candidate.GetName().Name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(candidate.Location)))
                {
                    options.ReferencedAssemblies.Add(assembly.Location);
                }

                // CodeDom 编译器不会自动补齐 LINQ 扩展方法所需的 System.Core 引用。
                var systemCoreAssembly = typeof(Enumerable).Assembly;
                if (!string.IsNullOrWhiteSpace(systemCoreAssembly.Location))
                    options.ReferencedAssemblies.Add(systemCoreAssembly.Location);

                // Unity 2021+ 的 Editor 程序集依赖 netstandard；运行时编译命令时必须显式传入其实际加载的程序集。
                var netStandardAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "netstandard", StringComparison.OrdinalIgnoreCase));
                if (netStandardAssembly != null && !string.IsNullOrWhiteSpace(netStandardAssembly.Location))
                    options.ReferencedAssemblies.Add(netStandardAssembly.Location);

                // 部分受控 MCP 命令需要调用性能工具；动态命令编译时也应解析项目已使用的 System.Text.Json。
                var systemTextJsonAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "System.Text.Json", StringComparison.OrdinalIgnoreCase));
                if (systemTextJsonAssembly != null && !string.IsNullOrWhiteSpace(systemTextJsonAssembly.Location))
                    options.ReferencedAssemblies.Add(systemTextJsonAssembly.Location);

                using (var provider = new CSharpCodeProvider())
                {
                    var results = provider.CompileAssemblyFromSource(options, wrappedCode);
                    if (results.Errors.HasErrors)
                    {
                        var errors = string.Join("\n", results.Errors.Cast<CompilerError>().Select(error => error.ErrorText));
                        throw new InvalidOperationException("Compilation failed:\n" + errors);
                    }
                    var method = results.CompiledAssembly.GetType("UnityMcpCommandExecutor")?.GetMethod("Execute");
                    try
                    {
                        return method?.Invoke(null, null);
                    }
                    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
                    {
                        throw exception.InnerException;
                    }
                }
            }
        }
    }
}
#endif
