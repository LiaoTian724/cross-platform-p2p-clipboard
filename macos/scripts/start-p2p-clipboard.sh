#!/bin/bash

BASE="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$BASE/logs"
BIN="$BASE/bin/p2p-clipboard"
LOG="$BASE/logs/p2p-clipboard.log"
ERRLOG="$BASE/logs/p2p-clipboard-error.log"

if pgrep -f "$BIN" >/dev/null; then
    echo "[$(date)] p2p-clipboard is already running." >> "$LOG"
    exit 0
fi

echo "[$(date)] Waiting for NetBird..." >> "$LOG"

until ifconfig | grep -q "100.64.25.71"; do
    sleep 2
done

echo "[$(date)] NetBird ready." >> "$LOG"

nohup "$BIN" \
    --listen 100.64.25.71:55516 \
    --connect 100.64.79.70:55515 \
    12D3KooWLF8kaRr1Gg84RFAiDhLnwHk23rM9ASkuyYcqqiBZU46r \
    --no-mdns \
    >> "$LOG" 2>> "$ERRLOG" &

echo "[$(date)] p2p-clipboard started." >> "$LOG"