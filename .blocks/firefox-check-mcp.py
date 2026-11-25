#!/usr/bin/env python3
# /// block
# name = "firefox-check-mcp"
# summary = "Verify headless Firefox MCP navigation, snapshot and shutdown with an offline fixture."
# effects = "exec"
# example = ["--tools", "Development~/Tools/Firefox", "--heading", "Firefox fixture"]
#
# [returns]
# keys = ["passed"]
#
# [params]
# tools = { type = "path", required = true, help = "Firefox MCP directory with installed dependencies" }
# heading = { type = "str", required = true, help = "Heading to generate in the offline page" }
#
# [stamp]
# verified = "3582cd1bc654"
# ///
"""Test the project's Firefox MCP wrapper with a separate profile and a data URL."""
import argparse
from html import escape
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
from urllib.parse import quote

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib.mcp_stdio import connect, text


def check_firefox(tools, heading):
    state = tools / ".state"
    if not (state / "bin" / "geckodriver.exe").is_file():
        raise FileNotFoundError("Install project-local geckodriver before running the offline check")
    command = ["cmd.exe", "/d", "/c", "call", tools / "mcp.cmd"]
    url = "data:text/html;charset=utf-8," + quote(f"<h1>{escape(heading)}</h1>")
    session = Path(tempfile.mkdtemp(prefix="check-firefox-", dir=state))
    try:
        env = dict(os.environ, FIREFOX_MCP_SESSION_DIR=str(session))
        with connect(command, env) as server:
            info, tools_available = server.initialize()
            server.call("navigate_page", {"url": url})
            snapshot = text(server.call("take_snapshot", {"maxLines": 20}))
            if heading not in snapshot:
                raise RuntimeError(f"Snapshot omitted {heading!r}: {snapshot[:400]}")
        return {"server": info, "tools": list(tools_available), "heading": heading, "eof": True}
    finally:
        remove_session(session, state)


def remove_session(session, state):
    if session.resolve().parent != state.resolve() or not session.name.startswith("check-firefox-"):
        raise RuntimeError(f"Unexpected temporary profile path: {session}")
    for attempt in range(20):
        try:
            shutil.rmtree(session)
            return
        except FileNotFoundError:
            if not session.exists():
                return
            raise
        except PermissionError:
            if attempt == 19:
                raise
            time.sleep(0.25)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tools", type=Path, required=True)
    parser.add_argument("--heading", required=True)
    args = parser.parse_args()
    try:
        result = check_firefox(args.tools.resolve(), args.heading)
    except (RuntimeError, OSError, ValueError, KeyError) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    print(json.dumps({"passed": result}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
