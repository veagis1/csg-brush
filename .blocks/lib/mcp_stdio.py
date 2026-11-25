"""Small JSON-RPC stdio client shared by the project MCP probes."""
from contextlib import contextmanager
import json
from pathlib import Path
import queue
import subprocess
import tempfile
from threading import Thread


def drain_stdout(stream, responses):
    for line in stream:
        responses.put(line)
    responses.put(None)


class Client:
    def __init__(self, process):
        self.process = process
        self.responses = queue.Queue()
        self.counter = 0
        self.thread = Thread(target=drain_stdout, args=(process.stdout, self.responses), daemon=True)
        self.thread.start()

    def exchange(self, method, params=None, notification=False):
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        if not notification:
            self.counter += 1
            message["id"] = self.counter
        self.process.stdin.write(json.dumps(message).encode("utf-8") + b"\n")
        self.process.stdin.flush()
        if notification:
            return None
        try:
            response = self.responses.get(timeout=45)
        except queue.Empty as error:
            raise RuntimeError(f"{method}: timed out waiting for the server") from error
        if response is None:
            raise RuntimeError(f"{method}: server closed stdout (exit {self.process.poll()})")
        data = json.loads(response)
        if data.get("id") != self.counter or "result" not in data:
            raise RuntimeError(f"{method}: {data}")
        return data["result"]

    def initialize(self):
        init = self.exchange("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                                            "clientInfo": {"name": "game1-smoke", "version": "1"}})
        self.exchange("notifications/initialized", notification=True)
        tools = self.exchange("tools/list")["tools"]
        return init["serverInfo"], {tool["name"]: tool["inputSchema"] for tool in tools}

    def call(self, name, arguments):
        result = self.exchange("tools/call", {"name": name, "arguments": arguments})
        if result.get("isError"):
            raise RuntimeError(f"{name}: {text(result)[:1000]}")
        return result


@contextmanager
def connect(command, env):
    with tempfile.TemporaryFile() as errors:
        with subprocess.Popen(command, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=errors) as process:
            client = Client(process)
            try:
                yield client
            except (RuntimeError, OSError, ValueError, KeyError) as error:
                errors.seek(0)
                diagnostics = errors.read().decode("utf-8", "replace").strip()[-1200:]
                raise RuntimeError(f"{error} | stderr: {diagnostics}") from error
            finally:
                process.stdin.close()
                try:
                    code = process.wait(timeout=15)
                except subprocess.TimeoutExpired as error:
                    process.kill()
                    process.wait()
                    raise RuntimeError(f"{Path(command[0]).name} failed to exit on stdin EOF") from error
                client.thread.join(timeout=2)
            if code != 0:
                errors.seek(0)
                diagnostics = errors.read().decode("utf-8", "replace").strip()[-1200:]
                raise RuntimeError(f"{Path(command[0]).name} exited with {code} | stderr: {diagnostics}")


def text(result):
    return "\n".join(item["text"] for item in result["content"] if item["type"] == "text")
