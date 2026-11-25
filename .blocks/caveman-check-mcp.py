#!/usr/bin/env python3
# /// block
# name = "caveman-check-mcp"
# summary = "Check Caveman MCP stdio and exact log recovery with an offline fixture."
# effects = "exec"
# example = ["--tools", "Tools/Caveman"]
#
# [returns]
# keys = ["passed"]
#
# [params]
# tools = { type = "path", required = true, help = "Caveman directory with installed binaries" }
#
# [stamp]
# verified = "0f876e910523"
# ///
"""Check exact MCP log recovery without altering the persistent recovery store."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib.mcp_stdio import connect, text


def verify_recovery(tools, env, state):
    node = tools / ".runtime" / "node.exe"
    if not node.is_file():
        node = shutil.which("node.exe")
    if not node:
        raise FileNotFoundError("Node.js was not found in .runtime or PATH")
    cli = tools / "node_modules" / "@caveman-ai" / "cli" / "dist" / "index.js"
    fixture = ("[INFO] Worker prepared the same fixture with dependencies loaded.\n" * 1000 +
               "[ERROR] expected 8, got 7\n").encode("utf-8")
    source = state / "fixture.log"
    source.write_bytes(fixture)
    summary = subprocess.run([node, tools / "read-log.mjs", source], env=env,
                             capture_output=True, timeout=35, check=True)
    match = re.search(rb"ccr_[a-f0-9]+", summary.stdout)
    if not match:
        raise RuntimeError("Read less did not return a recovery handle")
    handle = match.group().decode("ascii")
    cli_result = subprocess.run([node, cli, "retrieve", handle], env=env,
                                capture_output=True, timeout=35, check=True)
    with connect([tools / ".state" / "bin" / "caveman-mcp.exe"], env) as server:
        info, registered = server.initialize()
        recovered = server.call("caveman_retrieve", {"recovery_handle": handle})
        actual = text(recovered).encode("utf-8")
        if fixture != cli_result.stdout or fixture != actual:
            raise RuntimeError("recovery did not match the fixture byte for byte")
        return {"server": info, "tools": list(registered), "sha256": hashlib.sha256(actual).hexdigest()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tools", type=Path, required=True)
    args = parser.parse_args()
    tools = args.tools.resolve()
    try:
        with tempfile.TemporaryDirectory(prefix="check-mcp-", dir=tools / ".state") as directory:
            state = Path(directory)
            env = dict(os.environ, CAVEMAN_HOME=str(state), DO_NOT_TRACK="1",
                       CAVEMAN_ENGINE_BIN=str(tools / ".state" / "bin" / "caveman-engine.exe"))
            result = verify_recovery(tools, env, state)
    except (RuntimeError, OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    print(json.dumps({"passed": {"recovery": result}}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
