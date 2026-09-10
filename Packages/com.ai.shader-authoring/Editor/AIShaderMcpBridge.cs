#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace AIShader.Editor
{
    [InitializeOnLoad]
    public static class AIShaderMcpBridge
    {
        private static readonly object Sync = new object();
        private static readonly System.Collections.Concurrent.ConcurrentQueue<Request> Requests = new();
        private static HttpListener listener;
        private static Thread listenerThread;
        private static bool enabled;
        private const string Prefix = "http://127.0.0.1:8765/";
        private sealed class Request { public HttpListenerContext Context; public string Body; }
        [Serializable] private sealed class RpcRequest { public string method; public string @params; }
        [Serializable] private sealed class RpcResponse { public bool success; public string result; public string error; }
        [Serializable] private sealed class WriteTextParams { public string path; public string content; }
        [Serializable] private sealed class LogDetailParams { public string id; }

        static AIShaderMcpBridge()
        {
            EditorApplication.update += ProcessRequests;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.delayCall += TryStart;
        }

        public static void TryStart()
        {
            lock (Sync)
            {
                if (enabled) return;
                try
                {
                    listener = new HttpListener(); listener.Prefixes.Add(Prefix); listener.Start(); enabled = true;
                    listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "AIShaderMcpBridge" }; listenerThread.Start();
                    EditorPrefs.SetBool("AIShader.McpBridgeStarted", true);
                    Debug.Log($"AI Shader MCP bridge listening at {Prefix}mcp");
                }
                catch (Exception exception) { enabled = false; listener?.Close(); listener = null; Debug.LogError($"AI Shader MCP bridge failed to start: {exception.Message}"); }
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                enabled = false;
                try { listener?.Stop(); listener?.Close(); } catch { }
                listener = null;
                EditorPrefs.SetBool("AIShader.McpBridgeStarted", false);
            }
        }

        private static void ListenLoop()
        {
            while (enabled && listener != null)
            {
                try
                {
                    var context = listener.GetContext();
                    if (context.Request.HttpMethod == "GET")
                    {
                        HandleGet(context);
                        continue;
                    }
                    if (context.Request.HttpMethod != "POST" || context.Request.Url.AbsolutePath != "/mcp") { WriteResponse(context, 404, Error("Supported endpoints: GET /health, /status, /logs and POST /mcp.")); continue; }
                    using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                    Requests.Enqueue(new Request { Context = context, Body = reader.ReadToEnd() });
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private static void HandleGet(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (path == "/health") { WriteRaw(context, 200, "{\"connected\":true}"); return; }
            if (path == "/status")
            {
                WriteRaw(context, 200, JsonUtility.ToJson(new UnityStatus { unityVersion = Application.unityVersion, isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating, isPlaying = EditorApplication.isPlaying, activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path, consoleErrorCount = AIShaderConsoleService.ErrorCount }));
                return;
            }
            if (path == "/logs")
            {
                var query = context.Request.QueryString;
                var level = query["level"] ?? "all";
                var search = query["search"];
                var include = string.Equals(query["includeStackTrace"], "true", StringComparison.OrdinalIgnoreCase);
                var limit = int.TryParse(query["limit"], out var parsed) ? parsed : 100;
                WriteRaw(context, 200, JsonUtility.ToJson(AIShaderConsoleService.Query(level, search, include, limit).ToArray()));
                return;
            }
            if (path.StartsWith("/logs/", StringComparison.Ordinal))
            {
                var item = AIShaderConsoleService.GetById(path.Substring("/logs/".Length));
                if (item == null) { WriteRaw(context, 404, "{\"error\":\"Log entry not found\"}"); return; }
                WriteRaw(context, 200, JsonUtility.ToJson(item));
                return;
            }
            WriteRaw(context, 404, "{\"error\":\"Unknown endpoint\"}");
        }

        private static void WriteRaw(HttpListenerContext context, int status, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static void ProcessRequests()
        {
            while (Requests.TryDequeue(out var request))
            {
                RpcResponse response;
                try { var rpc = JsonUtility.FromJson<RpcRequest>(request.Body); response = Dispatch(rpc?.method, rpc?.@params); }
                catch (Exception exception) { response = Error(exception.Message); }
                WriteResponse(request.Context, response.success ? 200 : 400, response);
            }
        }

        private static RpcResponse Dispatch(string method, string rawParams)
        {
            switch (method)
            {
                case "ping": return Ok("{\"pong\":true}");
                case "get_unity_status": return Ok(JsonUtility.ToJson(new UnityStatus { unityVersion = Application.unityVersion, isCompiling = EditorApplication.isCompiling, isUpdating = EditorApplication.isUpdating, isPlaying = EditorApplication.isPlaying, activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path, consoleErrorCount = AIShaderConsoleService.ErrorCount }));
                case "clear_console_logs":
                case "unity_clear_console": AIShaderConsoleService.Clear(); return Ok("{\"cleared\":true}");
                case "get_console_logs":
                case "unity_get_console": return Ok(JsonUtility.ToJson(AIShaderConsoleService.Query("all", null, false, 200).ToArray()));
                case "get_console_errors":
                case "unity_get_errors": return Ok(JsonUtility.ToJson(AIShaderConsoleService.Query("errors", null, true, 200).ToArray()));
                case "get_console_warnings": return Ok(JsonUtility.ToJson(AIShaderConsoleService.Query("Warning", null, true, 200).ToArray()));
                case "unity_get_log_detail":
                    var detail = AIShaderConsoleService.GetById(JsonUtility.FromJson<LogDetailParams>(rawParams ?? "{}").id);
                    return detail == null ? Error("Log entry not found") : Ok(JsonUtility.ToJson(detail));
                case "refresh_assets": AIShaderCompilationBridge.RefreshAssets(); return Ok("{\"refreshed\":true}");
                case "create_validation_scene": AIShaderValidationSceneCreator.CreateValidationScene(); return Ok("{\"created\":true}");
                case "capture_validation_frame": return CaptureValidationFrame();
                case "get_project_context": return Ok(JsonUtility.ToJson(AIShaderProjectContext.Load()));
                case "write_generated_text": return WriteGeneratedText(rawParams);
                default: return Error($"Unknown MCP method: {method}");
            }
        }

        [Serializable] private sealed class UnityStatus { public string unityVersion; public bool isCompiling; public bool isUpdating; public bool isPlaying; public string activeScene; public int consoleErrorCount; }

        private static RpcResponse WriteGeneratedText(string rawParams)
        {
            var parameters = JsonUtility.FromJson<WriteTextParams>(rawParams ?? "{}");
            var context = AIShaderProjectContext.Load(); var path = (parameters?.path ?? string.Empty).Replace('\\', '/'); var root = context.generatedAssetsPath.TrimEnd('/');
            if (string.IsNullOrEmpty(path) || !(path == root || path.StartsWith(root + "/", StringComparison.Ordinal))) return Error($"Write path must be inside configured generated assets path: {root}");
            if (path.Contains("..")) return Error("Parent traversal is not allowed.");
            var absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)); File.WriteAllText(absolute, parameters.content ?? string.Empty, Encoding.UTF8); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return Ok(JsonUtility.ToJson(new WriteResult { path = path, written = true }));
        }
        private static RpcResponse CaptureValidationFrame()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<global::AIShader.AIShaderValidationController>();
            if (controller == null) return Error("No AIShaderValidationController found in active scene.");
            var material = controller.TargetRenderer != null ? controller.TargetRenderer.sharedMaterial : null;
            var shaderPath = material != null && material.shader != null ? AssetDatabase.GetAssetPath(material.shader) : string.Empty;
            var materialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            var iteration = $"mcp-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            var report = AIShaderCaptureManifestWriter.Capture(controller.ValidationCamera, iteration, "mcp", shaderPath, materialPath, controller.DebugChannel, controller);
            return Ok(JsonUtility.ToJson(new CaptureResult { report = report, iteration = iteration }));
        }

        [Serializable] private sealed class CaptureResult { public string report; public string iteration; }
        [Serializable] private sealed class WriteResult { public string path; public bool written; }
        private static RpcResponse Ok(string result) => new RpcResponse { success = true, result = result };
        private static RpcResponse Error(string error) => new RpcResponse { success = false, error = error };
        private static void WriteResponse(HttpListenerContext context, int status, RpcResponse response)
        {
            try { var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(response)); context.Response.StatusCode = status; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length; context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.OutputStream.Close(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
#endif
