#!/usr/bin/env bash
# One-time server setup: Docker, Docker Compose plugin, 2 GB swap.
# Safe to run many times — each step is skipped when already done.
# Usage (on the server): sudo bash setup-server.sh
set -euo pipefail

if ! command -v docker >/dev/null 2>&1; then
  echo ">> Installing Docker"
  if command -v dnf >/dev/null 2>&1; then
    dnf install -y docker
  else
    apt-get update
    apt-get install -y docker.io
  fi
fi
systemctl enable --now docker

if ! docker compose version >/dev/null 2>&1; then
  echo ">> Installing Docker Compose plugin"
  mkdir -p /usr/local/lib/docker/cli-plugins
  curl -fsSL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m)" \
    -o /usr/local/lib/docker/cli-plugins/docker-compose
  chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
fi

# Extra memory so the .NET build does not crash on a small instance.
if [ ! -f /swapfile ]; then
  echo ">> Creating 2 GB swap"
  dd if=/dev/zero of=/swapfile bs=1M count=2048
  chmod 600 /swapfile
  mkswap /swapfile
  echo '/swapfile swap swap defaults 0 0' >> /etc/fstab
fi
swapon --show | grep -q /swapfile || swapon /swapfile

# Lets the login user run "docker" without sudo (takes effect at next login).
LOGIN_USER="${SUDO_USER:-ec2-user}"
usermod -aG docker "$LOGIN_USER" || true

echo ">> Server setup OK"
