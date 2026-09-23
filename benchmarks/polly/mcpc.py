# ponytail: minimal MCP stdio client (newline-delimited JSON-RPC), enough to script tool calls.
import json, subprocess, sys, time, os
class Mcp:
    def __init__(self, args, cwd=None):
        self.p = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=open(os.devnull, "w"), cwd=cwd, text=True, encoding="utf-8")
        self.id = 0
        self.req("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                "clientInfo": {"name": "probe", "version": "0"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})
    def send(self, m): self.p.stdin.write(json.dumps(m) + "\n"); self.p.stdin.flush()
    def req(self, method, params=None):
        self.id += 1; self.send({"jsonrpc": "2.0", "id": self.id, "method": method, "params": params or {}})
        while True:
            line = self.p.stdout.readline()
            if not line: raise RuntimeError("server exited")
            m = json.loads(line)
            if m.get("id") == self.id: return m
    def call(self, name, **args):
        t = time.perf_counter(); r = self.req("tools/call", {"name": name, "arguments": args})
        dt = time.perf_counter() - t
        if "error" in r: return dt, "ERROR: " + json.dumps(r["error"])
        c = r["result"]; txt = "\n".join(x.get("text", "") for x in c.get("content", []))
        return dt, ("[isError] " if c.get("isError") else "") + txt
    def close(self): self.p.stdin.close(); self.p.wait(10)

def server_cmd(here):
    """dnx command for the server under test: DOTNETDEVMCP (default the published 0.3.0), plus ./feed if you packed one locally."""
    import os
    cmd = ["dotnet", "dnx", os.environ.get("DOTNETDEVMCP", "DotNetDevMCP@0.3.0"), "--yes"]
    feed = os.path.join(here, "feed")
    return cmd + (["--add-source", feed] if os.path.isdir(feed) else [])
