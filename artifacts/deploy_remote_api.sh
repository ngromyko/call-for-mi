#!/usr/bin/env bash
set -euo pipefail

APP="/home/skynet/call-for-mi"
PIDFILE="$APP/api/call-for-mi.pid"

echo "===> Stopping running process..."
if [ -f "$PIDFILE" ]; then
  PID=$(cat "$PIDFILE" || true)
  if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
    echo "Killing PID $PID..."
    kill "$PID"
    for i in {1..10}; do
      if ! kill -0 "$PID" 2>/dev/null; then
        break
      fi
      sleep 1
    done
    if kill -0 "$PID" 2>/dev/null; then
      echo "Force killing PID $PID..."
      kill -9 "$PID"
    fi
  fi
fi

pkill -f "$APP/api/CallForMe.Api" || true

echo "===> Backing up current api directory..."
rm -rf "$APP/api.prev"
if [ -d "$APP/api" ]; then
  mv "$APP/api" "$APP/api.prev"
fi

echo "===> Creating new api directory..."
mkdir -p "$APP/api"

echo "===> Extracting tarball..."
tar -xzf "$APP/deploy-api.tar.gz" -C "$APP/api"
chmod +x "$APP/api/CallForMe.Api"

echo "===> Restoring data directory and appsettings..."
if [ -d "$APP/api.prev/data" ]; then
  cp -r "$APP/api.prev/data" "$APP/api/"
fi
if [ -f "$APP/api.prev/appsettings.Local.json" ]; then
  cp "$APP/api.prev/appsettings.Local.json" "$APP/api/"
fi

echo "===> Restarting API..."
chmod +x "$APP/start-api.sh"
"$APP/start-api.sh"

echo "===> Deployment completed successfully!"
