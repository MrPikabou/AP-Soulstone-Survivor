#!/usr/bin/env python3
"""Soulstone Survivors x Archipelago launcher.

A small desktop window so you do not have to juggle shell commands:

  * pick a seed and start the local Archipelago server
  * connect the client to a server with a slot name and password
  * launch the game, and watch the live bridge status

Run with any Python that has tkinter (the system Python is fine):
    python launcher.py
"""
from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import tkinter as tk
import urllib.parse
from tkinter import filedialog, messagebox, ttk

HERE = os.path.dirname(os.path.abspath(__file__))
ARCHIPELAGO = os.path.join(HERE, ".tools", "archipelago")
VENV_PYTHON = os.path.join(HERE, ".tools", "venv", "Scripts", "python.exe")
GAME_CANDIDATES = (
    r"C:\Program Files (x86)\Steam\steamapps\common\Soulstone Survivors",
    r"C:\Program Files\Steam\steamapps\common\Soulstone Survivors",
    r"D:\Steam\steamapps\common\Soulstone Survivors",
    r"D:\SteamLibrary\steamapps\common\Soulstone Survivors",
)


def find_game_dir() -> str:
    """SOULSTONE_DIR wins, then the usual Steam libraries (same as env.sh)."""
    override = os.environ.get("SOULSTONE_DIR")
    if override:
        return override
    for candidate in GAME_CANDIDATES:
        if os.path.isdir(candidate):
            return candidate
    return GAME_CANDIDATES[0]


GAME_DIR = find_game_dir()
GAME_EXE = os.path.join(GAME_DIR, "Soulstone Survivors.exe")
CONFIG_PATH = os.path.join(HERE, "launcher.json")
CLIENT_OUT = os.path.join(HERE, "launcher_client.log")
BRIDGE_PORT = 47821


# ---------------------------------------------------------------- bridge probe


def bridge_state(timeout: float = 3.0) -> dict | None:
    try:
        with socket.create_connection(("127.0.0.1", BRIDGE_PORT), timeout=timeout) as sock:
            sock.sendall(b"STATE\n")
            buffer = b""
            while b"\n" not in buffer:
                chunk = sock.recv(65536)
                if not chunk:
                    break
                buffer += chunk
    except OSError:
        return None

    line = buffer.decode("utf-8", errors="replace").strip()
    if not line.startswith("OK "):
        return {"ok": False, "error": line[4:] if line.startswith("ERR ") else line}
    try:
        return json.loads(urllib.parse.unquote(line[3:]))
    except ValueError:
        return None


# ------------------------------------------------------------------- the app


class Launcher(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Soulstone Survivors - Archipelago")
        self.geometry("760x520")
        self.minsize(640, 440)

        self.server_process: subprocess.Popen | None = None
        self.client_process: subprocess.Popen | None = None
        self.log_offsets: dict[str, int] = {}

        self.server_var = tk.StringVar(value="localhost:38281")
        self.slot_var = tk.StringVar(value="Survivor")
        self.password_var = tk.StringVar()
        self.seed_var = tk.StringVar(value=self._default_seed())
        self.status_var = tk.StringVar(value="Game: not detected")

        self._build()
        self._load_config()
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.after(400, self._tick_bridge)
        self.after(600, self._tick_logs)

    # ---- layout

    def _build(self) -> None:
        pad = {"padx": 6, "pady": 4}

        connection = ttk.LabelFrame(self, text="Archipelago server")
        connection.pack(fill="x", **pad)

        ttk.Label(connection, text="Address").grid(row=0, column=0, sticky="w", padx=6, pady=4)
        ttk.Entry(connection, textvariable=self.server_var, width=28).grid(row=0, column=1, sticky="w")
        ttk.Label(connection, text="Slot").grid(row=0, column=2, sticky="e", padx=(12, 4))
        ttk.Entry(connection, textvariable=self.slot_var, width=18).grid(row=0, column=3, sticky="w")
        ttk.Label(connection, text="Password").grid(row=1, column=0, sticky="w", padx=6, pady=4)
        ttk.Entry(connection, textvariable=self.password_var, show="*", width=28).grid(row=1, column=1, sticky="w")

        self.connect_button = ttk.Button(connection, text="Connect", command=self.toggle_client)
        self.connect_button.grid(row=0, column=4, rowspan=2, padx=8, pady=4, sticky="ns")

        seed_row = ttk.Frame(connection)
        seed_row.grid(row=2, column=0, columnspan=5, sticky="we", padx=6, pady=(0, 6))
        ttk.Label(seed_row, text="Seed").pack(side="left")
        ttk.Entry(seed_row, textvariable=self.seed_var).pack(side="left", fill="x", expand=True, padx=6)
        ttk.Button(seed_row, text="Browse", command=self.browse_seed).pack(side="left", padx=(0, 6))
        self.server_button = ttk.Button(seed_row, text="Start server", command=self.toggle_server)
        self.server_button.pack(side="left")

        actions = ttk.LabelFrame(self, text="Game")
        actions.pack(fill="x", **pad)
        ttk.Button(actions, text="Launch Soulstone Survivors", command=self.launch_game).pack(
            side="left", padx=6, pady=6
        )
        ttk.Label(actions, textvariable=self.status_var).pack(side="left", padx=10)

        log_frame = ttk.LabelFrame(self, text="Client log")
        log_frame.pack(fill="both", expand=True, **pad)
        self.log = tk.Text(log_frame, height=14, wrap="none", state="disabled", font=("Consolas", 9))
        scroll = ttk.Scrollbar(log_frame, command=self.log.yview)
        self.log.configure(yscrollcommand=scroll.set)
        self.log.pack(side="left", fill="both", expand=True)
        scroll.pack(side="right", fill="y")

        self.hint = ttk.Label(
            self,
            text="Pick save slot 1 in game - the plugin only talks to that slot.",
        )
        self.hint.pack(fill="x", padx=10, pady=(0, 6))

    # ---- helpers

    def _default_seed(self) -> str:
        output = os.path.join(HERE, "output")
        seeds = sorted(
            (os.path.join(output, name) for name in os.listdir(output) if name.endswith(".zip")),
            key=os.path.getmtime,
            reverse=True,
        ) if os.path.isdir(output) else []
        return seeds[0] if seeds else ""

    def _append(self, text: str) -> None:
        self.log.configure(state="normal")
        self.log.insert("end", text)
        self.log.see("end")
        self.log.configure(state="disabled")

    def _load_config(self) -> None:
        try:
            with open(CONFIG_PATH, encoding="utf-8") as handle:
                data = json.load(handle)
        except (OSError, ValueError):
            return
        self.server_var.set(data.get("server", self.server_var.get()))
        self.slot_var.set(data.get("slot", self.slot_var.get()))

    def _save_config(self) -> None:
        try:
            with open(CONFIG_PATH, "w", encoding="utf-8") as handle:
                json.dump({"server": self.server_var.get(), "slot": self.slot_var.get()}, handle, indent=1)
        except OSError:
            pass

    # ---- actions

    def browse_seed(self) -> None:
        path = filedialog.askopenfilename(
            title="Select an Archipelago seed", initialdir=os.path.join(HERE, "output"),
            filetypes=[("Archipelago seed", "*.zip"), ("All files", "*.*")],
        )
        if path:
            self.seed_var.set(path)

    def toggle_server(self) -> None:
        if self.server_process and self.server_process.poll() is None:
            self.server_process.terminate()
            self.server_process = None
            self.server_button.configure(text="Start server")
            self._append("[launcher] server stopped\n")
            return

        seed = self.seed_var.get().strip()
        if not seed or not os.path.exists(seed):
            messagebox.showwarning("No seed", "Pick an Archipelago seed (.zip) first.")
            return
        port = self.server_var.get().split(":")[-1] or "38281"
        self.server_process = subprocess.Popen(
            [sys.executable, os.path.join(HERE, "tools", "server_ctl.py"), seed, port],
            cwd=HERE,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1"),
        )
        self.server_button.configure(text="Stop server")
        self._append(f"[launcher] server starting on port {port}\n")

    def toggle_client(self) -> None:
        if self.client_process and self.client_process.poll() is None:
            self.client_process.terminate()
            self.client_process = None
            self.connect_button.configure(text="Connect")
            self._append("[launcher] client stopped\n")
            return

        if not os.path.exists(VENV_PYTHON):
            messagebox.showerror("Missing environment", f"Client interpreter not found:\n{VENV_PYTHON}")
            return

        command = [
            VENV_PYTHON, "-u", "-m", "worlds.soulstone_survivors.soulstone_client",
            "--connect", self.server_var.get().strip(),
            "--slot", self.slot_var.get().strip(),
            "--nogui",
        ]
        if self.password_var.get():
            command += ["--password", self.password_var.get()]

        out = open(CLIENT_OUT, "w", encoding="utf-8")
        self.client_process = subprocess.Popen(
            command,
            cwd=ARCHIPELAGO,
            stdin=subprocess.DEVNULL,
            stdout=out,
            stderr=subprocess.STDOUT,
            env=dict(os.environ, SKIP_REQUIREMENTS_UPDATE="1", PYTHONUNBUFFERED="1"),
        )
        self.log_offsets.clear()
        self.connect_button.configure(text="Disconnect")
        self._append(f"[launcher] connecting to {self.server_var.get()} as {self.slot_var.get()}\n")
        self._save_config()

    def launch_game(self) -> None:
        if not os.path.exists(GAME_EXE):
            messagebox.showerror("Game not found", f"Could not find:\n{GAME_EXE}")
            return
        subprocess.Popen([GAME_EXE], cwd=GAME_DIR)
        self._append("[launcher] game launched - pick save slot 1\n")

    # ---- polling

    def _tick_bridge(self) -> None:
        state = bridge_state()
        if state is None:
            self.status_var.set("Game: not running (or plugin not loaded)")
        elif not state.get("ok"):
            self.status_var.set(f"Game: {state.get('error', 'not ready')}")
        else:
            self.status_var.set(
                f"Game: slot {state.get('profile')} - "
                f"{len(state.get('characters', []))} characters, "
                f"{len(state.get('weapons', []))} weapons, "
                f"{len(state.get('achievements', []))} achievements tracked"
            )
        self.after(1500, self._tick_bridge)

    def _newest_client_log(self) -> str | None:
        log_dir = os.path.join(ARCHIPELAGO, "logs")
        try:
            names = [n for n in os.listdir(log_dir) if n.startswith("SoulstoneClient_")]
        except OSError:
            return None
        if not names:
            return None
        newest = max((os.path.join(log_dir, n) for n in names), key=os.path.getmtime)
        return newest

    def _pump_file(self, path: str | None) -> None:
        if not path:
            return
        offset = self.log_offsets.get(path, 0)
        try:
            with open(path, encoding="utf-8", errors="replace") as handle:
                handle.seek(offset)
                chunk = handle.read()
                self.log_offsets[path] = handle.tell()
        except OSError:
            return
        if chunk:
            self._append(chunk)

    def _tick_logs(self) -> None:
        newest = self._newest_client_log()
        if newest:
            self._pump_file(newest)
        self._pump_file(CLIENT_OUT)
        self.after(800, self._tick_logs)

    # ---- shutdown

    def _on_close(self) -> None:
        for process in (self.client_process, self.server_process):
            if process and process.poll() is None:
                process.terminate()
        self._save_config()
        self.destroy()


if __name__ == "__main__":
    Launcher().mainloop()
