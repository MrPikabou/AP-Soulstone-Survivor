#!/usr/bin/env bash
# Starts the Archipelago server and the Soulstone Survivors client.
# Usage: bash play.sh [path/to/multidata.zip]
set -euo pipefail
source "$(dirname "$0")/env.sh"

MULTIDATA="${1:-}"
if [ -z "$MULTIDATA" ]; then
  MULTIDATA="$(ls -t "$AP_ROOT"/output/AP_*.zip 2>/dev/null | head -1 || true)"
fi
if [ -z "$MULTIDATA" ] || [ ! -f "$MULTIDATA" ]; then
  echo "No multidata found. Generate a seed first, or pass the zip path." >&2
  exit 1
fi

PORT="${AP_PORT:-38281}"
SLOT="${AP_SLOT:-Survivor}"

cd "$AP_ROOT"
rm -f server_cmds.txt client_cmds.txt
touch server_cmds.txt client_cmds.txt

echo "server: $MULTIDATA on port $PORT"
python tools/server_ctl.py "$MULTIDATA" "$PORT" > server_stdout.log 2>&1 &
SERVER_WRAPPER=$!

for _ in $(seq 1 40); do
  if grep -q "server listening on 0.0.0.0" server.log 2>/dev/null; then break; fi
  sleep 1
done

echo "client: slot $SLOT"
SKIP_REQUIREMENTS_UPDATE=1 python tools/feed.py client_cmds.txt \
  "$AP_ROOT/.tools/archipelago" \
  "$AP_ROOT/.tools/venv/Scripts/python.exe" \
  -u -m worlds.soulstone_survivors.soulstone_client --connect "localhost:$PORT" --slot "$SLOT" --nogui \
  > client.log 2>&1 &
CLIENT_WRAPPER=$!

echo "server pid $SERVER_WRAPPER, client pid $CLIENT_WRAPPER"
echo "tail -f $AP_ROOT/client.log  to follow the client"
echo "commands: echo '/check Clear SurvivorsDesert' >> $AP_ROOT/client_cmds.txt"

wait $CLIENT_WRAPPER
kill $SERVER_WRAPPER 2>/dev/null || true
