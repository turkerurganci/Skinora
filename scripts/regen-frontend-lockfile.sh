#!/usr/bin/env bash
#
# Regenerate frontend/package-lock.json with a cross-platform optional-dep
# set (required for CI Linux runners to find @parcel/watcher bindings).
#
# When a Windows dev runs `npm install` locally, the resulting lockfile
# omits `@parcel/watcher-linux-x64-glibc` and related Linux-only optional
# bindings. On CI (ubuntu-latest) `npm ci` is strict and fails to resolve
# `next build` → "No prebuild of @parcel/watcher found".
#
# This script regenerates the lockfile inside a Linux container so the
# lockfile contains all Linux bindings, without bind-mounting the repo
# (Windows Docker Desktop bind mounts are too slow for a 400-pkg install).
#
# Usage:
#   bash scripts/regen-frontend-lockfile.sh
#
# Post-run: commit frontend/package-lock.json as-is. Do NOT run
# `npm install` on Windows afterwards — it will drift the lockfile.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/frontend"
CONTAINER_NAME="skinora-frontend-regen-$$"

if [ ! -f "$FRONTEND_DIR/package.json" ]; then
  echo "ERROR: $FRONTEND_DIR/package.json not found" >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  echo "ERROR: Docker is not running" >&2
  exit 1
fi

cleanup() {
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Starting idle Linux container ($CONTAINER_NAME)"
docker run -d --name "$CONTAINER_NAME" node:20-slim sleep 600 >/dev/null

echo "==> Copying package.json into container"
MSYS_NO_PATHCONV=1 docker exec "$CONTAINER_NAME" mkdir -p /work
MSYS_NO_PATHCONV=1 docker cp "$FRONTEND_DIR/package.json" "$CONTAINER_NAME:/work/package.json"

echo "==> Running 'npm install' inside container (this may take ~5 min)"
MSYS_NO_PATHCONV=1 docker exec -w /work "$CONTAINER_NAME" \
  npm install --no-audit --no-fund

echo "==> Verifying lockfile contains linux-x64-glibc binding"
if ! MSYS_NO_PATHCONV=1 docker exec -w /work "$CONTAINER_NAME" \
     grep -q '@parcel/watcher-linux-x64-glibc' package-lock.json; then
  echo "ERROR: regenerated lockfile is missing linux-x64-glibc binding" >&2
  exit 1
fi

echo "==> Copying regenerated package-lock.json back to host"
MSYS_NO_PATHCONV=1 docker cp "$CONTAINER_NAME:/work/package-lock.json" \
  "$FRONTEND_DIR/package-lock.json"

echo ""
echo "OK: frontend/package-lock.json regenerated with cross-platform bindings."
echo "Commit the updated lockfile. Do NOT run 'npm install' on Windows afterwards."
