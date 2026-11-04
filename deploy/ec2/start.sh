#!/usr/bin/env bash
# Build the app image, start Postgres + migrations + app, then wait for /health.
# Usage (on the server): sudo bash ~/notification/deploy/ec2/start.sh
set -euo pipefail

cd "$(dirname "$0")"
if [ ! -f .env ]; then
  echo "ERROR: deploy/ec2/.env is missing. Copy .env.example to .env and fill it in." >&2
  exit 1
fi

echo ">> Building image (first time takes 5-10 minutes)"
docker build -t routego/notification:latest ../..

echo ">> Starting containers"
docker compose up -d

echo ">> Waiting for the app to be healthy"
for _ in $(seq 1 30); do
  if curl -fs http://localhost:5007/health >/dev/null 2>&1; then
    docker compose ps -a
    echo ">> DEPLOY OK — app is healthy on port 5007"
    exit 0
  fi
  sleep 2
done

echo "ERROR: app did not become healthy. Last logs:" >&2
docker compose ps -a
docker compose logs --tail 50 notification
exit 1
