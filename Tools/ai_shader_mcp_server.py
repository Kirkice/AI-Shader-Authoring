#!/usr/bin/env python3
"""stdio MCP server for the AI Shader Unity bridge and on-demand Console queries."""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

BRIDGE_BASE = os.environ.get("AI_SHADER_UNITY_BRIDGE", "http://127.0.0.1:8765").rstrip("/")
SERVER_INFO = {"name": "ai-shader-mcp", "version": "0.2.0"}


def tool(name, description, properties=None, required=None):
    return {"name": name, "description": description, "inputSchema": {"type": "object", "properties": properties or {}, "required": required or [], "additionalProperties": False}}


TOOLS = [
    tool("unity_get_console", "按需读取 Unity Console 缓存。", {"level": {"type": "string", "enum": ["all", "Log", "Warning", "Error", "Exception", "Assert", "errors"]}, "limit": {"type": "integer", "minimum": 1, "maximum": 500}, "search": {"type": "string", "maxLength": 300}, "includeStackTrace": {"type": "boolean"}}),
    tool("unity_get_errors", "读取 Unity Error、Exception、Assert。", {"limit": {"type": "integer", "minimum": 1, "maximum": 500}, "search": {"type": "string", "maxLength": 300}, "includeStackTrace": {"type": "boolean"}}),
    tool("unity_get_log_detail", "按日志 ID 读取完整堆栈。", {"id": {"type": "string"}}, ["id"]),
    tool("unity_get_status", "读取 Unity Editor 状态、版本、活动场景、编译状态和 Console 数量。"),
    tool("unity_clear_console", "清理 Unity Console MCP 内存缓存。"),
    tool("get_project_context", "读取用户确认的 Shader、Include、Pipeline 和生成路径。"),
    tool("write_generated_text", "只在 Assets 生成目录内写入文本。", {"path": {"type": "string"}, "content": {"type": "string"}}, ["path", "content"]),
    tool("refresh_assets", "刷新 Unity AssetDatabase。"),
    tool("create_validation_scene", "创建验证场景。"),
    tool("capture_validation_frame", "捕获验证截图和 Manifest。"),
]


def bridge_get(path):
    request = urllib.request.Request(BRIDGE_BASE + path, method="GET")
    with urllib.request.urlopen(request, timeout=10) as response:
        payload = json.loads(response.read().decode("utf-8"))
        if response.status >= 400:
            raise RuntimeError(payload.get("error", "Unity bridge HTTP error"))
        return payload


def bridge_post(method, params):
    body = json.dumps({"method": method, "params": json.dumps(params or {})}).encode("utf-8")
    request = urllib.request.Request(BRIDGE_BASE + "/mcp", data=body, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.URLError as exc:
        raise RuntimeError("Unity HTTP bridge unavailable: " + str(exc.reason))
    if not payload.get("success"):
        raise RuntimeError(payload.get("error", "Unity bridge request failed"))
    try:
        return json.loads(payload.get("result", "{}"))
    except json.JSONDecodeError:
        return {"value": payload.get("result", "")}


def call_tool(name, args):
    args = args or {}
    if name == "unity_get_console":
        query = {k: v for k, v in args.items() if v is not None}
        query_string = "?" + urllib.parse.urlencode({k: str(v).lower() if isinstance(v, bool) else v for k, v in query.items()}) if query else ""
        return bridge_get("/logs" + query_string)
    if name == "unity_get_errors":
        query = {"level": "errors", "limit": args.get("limit", 50), "includeStackTrace": str(args.get("includeStackTrace", True)).lower()}
        if args.get("search"): query["search"] = args["search"]
        return bridge_get("/logs?" + urllib.parse.urlencode(query))
    if name == "unity_get_log_detail": return bridge_get("/logs/" + urllib.parse.quote(args["id"], safe=""))
    if name == "unity_get_status": return bridge_get("/status")
    if name == "unity_clear_console": return bridge_post("clear_console_logs", {})
    return bridge_post(name, args)


def handle(message):
    method = message.get("method")
    request_id = message.get("id")
    if method == "initialize":
        return {"jsonrpc": "2.0", "id": request_id, "result": {"protocolVersion": message.get("params", {}).get("protocolVersion", "2024-11-05"), "capabilities": {"tools": {}}, "serverInfo": SERVER_INFO}}
    if method == "notifications/initialized": return None
    if method == "tools/list": return {"jsonrpc": "2.0", "id": request_id, "result": {"tools": TOOLS}}
    if method == "tools/call":
        try:
            params = message["params"]
            return {"jsonrpc": "2.0", "id": request_id, "result": {"content": [{"type": "text", "text": json.dumps(call_tool(params["name"], params.get("arguments")), ensure_ascii=False, indent=2)}]}}
        except Exception as exc:
            return {"jsonrpc": "2.0", "id": request_id, "result": {"isError": True, "content": [{"type": "text", "text": str(exc)}]}}
    if request_id is None: return None
    return {"jsonrpc": "2.0", "id": request_id, "error": {"code": -32601, "message": "Method not found: " + str(method)}}


for line in sys.stdin:
    if not line.strip(): continue
    try:
        response = handle(json.loads(line))
        if response is not None:
            sys.stdout.write(json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n")
            sys.stdout.flush()
    except Exception as exc:
        sys.stderr.write("[ai-shader-mcp] %s\n" % exc)
        sys.stderr.flush()
