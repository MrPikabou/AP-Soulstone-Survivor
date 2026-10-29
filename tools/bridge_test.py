#!/usr/bin/env python3
"""Tiny bridge probe: python bridge_test.py STATE"""
import socket
import sys
import urllib.parse

PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 47821


def send(request: str, port: int = PORT, timeout: float = 20.0) -> str:
    with socket.create_connection(("127.0.0.1", port), timeout=timeout) as sock:
        sock.sendall((request + "\n").encode("utf-8"))
        sock.settimeout(timeout)
        chunks = []
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            chunks.append(chunk)
            if b"\n" in chunk:
                break
        return b"".join(chunks).decode("utf-8").strip()


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "PING"
    reply = send(command)
    if reply.startswith("OK "):
        print(urllib.parse.unquote(reply[3:]))
    else:
        print(reply)
