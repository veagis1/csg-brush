#!/usr/bin/env python3
# /// block
# name = "caveman-check-read"
# summary = "Check Caveman Read less boundaries, recovery and subprocess exit codes."
# effects = "exec"
# example = ["--tools", "Tools/Caveman"]
#
# [returns]
# keys = ["passed"]
#
# [params]
# tools = { type = "path", required = true, help = "Caveman directory with the wrappers and installed CLI" }
#
# [stamp]
# verified = "d82add0bf1df"
# ///
"""Exercise installed Read less in an isolated state directory."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def checked_run(arguments, env):
    return subprocess.run(arguments, env=env, capture_output=True, timeout=45)


def verify_read(tools, workspace):
    node = tools / ".runtime" / "node.exe"
    if not node.is_file():
        node = shutil.which("node.exe")
    if not node:
        raise FileNotFoundError("Node.js was not found in .runtime or PATH")
    cli = tools / "node_modules" / "@caveman-ai" / "cli" / "dist" / "index.js"
    state = workspace / "state"
    state.mkdir()
    env = dict(os.environ, CAVEMAN_HOME=str(state), DO_NOT_TRACK="1",
               CAVEMAN_ENGINE_BIN=str(tools / ".state" / "bin" / "caveman-engine.exe"))
    source = workspace / "fixture.log"
    error = b"[ERROR] expected 8, got 7\r\n"
    line = b"[INFO] Worker prepared the same test fixture with dependencies already loaded.\r\n"
    handle_pattern = re.compile(rb"recover: caveman retrieve (ccr_[a-f0-9]+)")
    results = {}
    failures = {}

    def record(name, passed, result, details=None):
        if passed:
            results[name] = True if details is None else details
            return
        failures[name] = {"exit": result.returncode,
                          "stderr": result.stderr.decode("utf-8", "replace").strip()[:250],
                          "stdout": result.stdout[:200].decode("utf-8", "replace").strip()}

    def read_log(data, *options, config_env=env):
        source.write_bytes(data)
        return checked_run([node, tools / "read-log.mjs", *options, source], config_env)

    def size_fixture(size):
        long_line = line[:-2] * 4 + b"\r\n"
        repetitions, remainder = divmod(size - len(error) - 2, len(long_line))
        return long_line * repetitions + b"x" * remainder + b"\r\n" + error

    for name, data in {"200-lines": line * 199 + error, "32768-bytes": size_fixture(32768)}.items():
        result = read_log(data)
        record(name, result.returncode == 0 and result.stdout == data, result)

    for name, data in {"201-lines": line * 200 + error, "32769-bytes": size_fixture(32769),
                       "1001-lines": line * 1000 + error}.items():
        result = read_log(data)
        handle = handle_pattern.search(result.stdout)
        record(name, result.returncode == 0 and handle is not None and error.strip() in result.stdout,
               result, {"input_bytes": len(data), "summary_bytes": len(result.stdout)})
        if name == "1001-lines":
            raw = read_log(data, "--raw")
            record("raw", raw.returncode == 0 and raw.stdout == data, raw)
        if name == "1001-lines" and handle is not None:
            retrieved = checked_run([node, cli, "retrieve", handle.group(1).decode("ascii")], env)
            expected_sha = hashlib.sha256(data).hexdigest()
            actual_sha = hashlib.sha256(retrieved.stdout).hexdigest()
            record("retrieve-sha256", retrieved.returncode == 0 and actual_sha == expected_sha,
                   retrieved, actual_sha)

    over_limit = read_log(b"x" * (8 * 1024 * 1024 + 1))
    record("over-8MiB", over_limit.returncode == 3 and b"8 " in over_limit.stderr, over_limit,
           over_limit.stderr.decode("utf-8").strip())

    disabled = workspace / "disabled-input"
    disabled.mkdir()
    (disabled / "cloud.json").write_text('{"modules":{"input":false}}', encoding="utf-8")
    disabled_result = read_log(line * 200 + error, config_env=dict(env, CAVEMAN_HOME=str(disabled)))
    record("disabled-input", disabled_result.returncode == 3 and b"Read less" in disabled_result.stderr,
           disabled_result, disabled_result.stderr.decode("utf-8").strip())

    process = checked_run([node, cli, "shrink", "--", node, "-e",
                           "console.log('fixture stdout');console.error('fixture stderr');process.exit(7)"], env)
    record("exit-7", process.returncode == 7 and b"fixture stdout" in process.stdout and
           b"fixture stderr" in process.stdout, process, {"exit": process.returncode, "merged": True})
    return results, failures


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tools", type=Path, required=True)
    args = parser.parse_args()
    tools = args.tools.resolve()
    try:
        with tempfile.TemporaryDirectory(prefix="check-read-", dir=tools / ".state") as workspace:
            results, failures = verify_read(tools, Path(workspace))
    except (OSError, subprocess.TimeoutExpired) as error:
        print(json.dumps({"error": str(error)}))
        return 1
    if failures:
        print(json.dumps({"error": failures, "passed": results}))
        return 1
    print(json.dumps({"passed": results}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
