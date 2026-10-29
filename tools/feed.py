#!/usr/bin/env python3
"""Runs a command whose stdin is fed from a text file that can be appended to.

usage: python feed.py <feed-file> <cwd> <command...>
"""
import os
import subprocess
import sys
import time

feed_file = os.path.abspath(sys.argv[1])
cwd = sys.argv[2]
command = sys.argv[3:]

if not os.path.exists(feed_file):
    open(feed_file, "w").close()

process = subprocess.Popen(command, cwd=cwd, stdin=subprocess.PIPE)
sent = 0

try:
    while process.poll() is None:
        with open(feed_file, encoding="utf-8") as handle:
            pending = [line.rstrip("\n") for line in handle if line.strip()]
        while sent < len(pending):
            process.stdin.write((pending[sent] + "\n").encode("utf-8"))
            process.stdin.flush()
            sent += 1
        time.sleep(0.4)
finally:
    process.terminate()
