#!/usr/bin/env python3
"""Runs the Archipelago MultiServer with a file-driven console.

Useful in a sandbox where the server's stdin is not interactive: append a line
to server_cmds.txt and it is forwarded to the server console (prefix commands
with '/', e.g. "/send Survivor Character:Necromancer").
"""
import os
import queue
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
AP_ROOT = os.path.dirname(HERE)
ARCHIPELAGO = os.path.join(AP_ROOT, ".tools", "archipelago")
PYTHON = os.path.join(AP_ROOT, ".tools", "venv", "Scripts", "python.exe")
COMMANDS = os.path.join(AP_ROOT, "server_cmds.txt")

multidata = sys.argv[1]
port = sys.argv[2] if len(sys.argv) > 2 else "38281"

env = dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1", PYTHONUNBUFFERED="1")

process = subprocess.Popen(
    [PYTHON, "-u", "MultiServer.py", multidata, "--port", port],
    cwd=ARCHIPELAGO,
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.STDOUT,
    env=env,
    text=True,
    bufsize=1,
)

lines: "queue.Queue[str]" = queue.Queue()


def reader() -> None:
    for line in process.stdout:
        lines.put(line)


threading.Thread(target=reader, daemon=True).start()

log = open(os.path.join(AP_ROOT, "server.log"), "w", encoding="utf-8", buffering=1)
forwarded = 0

try:
    while process.poll() is None:
        try:
            line = lines.get(timeout=0.2)
        except queue.Empty:
            line = None
        if line:
            log.write(line)
            sys.stdout.write(line)

        if os.path.exists(COMMANDS):
            with open(COMMANDS, encoding="utf-8") as handle:
                pending = [l.rstrip("\n") for l in handle if l.strip()]
            while forwarded < len(pending):
                command = pending[forwarded]
                forwarded += 1
                if process.stdin:
                    process.stdin.write(command + "\n")
                    process.stdin.flush()
                log.write(f"[ctl] {command}\n")
finally:
    process.terminate()
