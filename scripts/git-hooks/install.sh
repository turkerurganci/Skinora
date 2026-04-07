#!/usr/bin/env bash
# Skinora git hook installer
#
# Skinora git hook'larini scripts/git-hooks/ dizininden .git/hooks/'a kopyalar.
# T11 close-out (discipline-only branch protection) icin gerekli.
#
# Kullanim:
#   bash scripts/git-hooks/install.sh

set -e

HOOK_SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
GIT_DIR="$(git rev-parse --git-dir 2>/dev/null)"

if [ -z "$GIT_DIR" ]; then
  echo "ERROR: not inside a git repository" >&2
  exit 1
fi

HOOK_TARGET_DIR="$GIT_DIR/hooks"
mkdir -p "$HOOK_TARGET_DIR"

hooks_installed=0
for hook_file in "$HOOK_SOURCE_DIR"/*; do
  hook_name="$(basename "$hook_file")"

  # install.sh ve README'yi atla
  case "$hook_name" in
    install.sh|README*|*.md)
      continue
      ;;
  esac

  cp "$hook_file" "$HOOK_TARGET_DIR/$hook_name"
  chmod +x "$HOOK_TARGET_DIR/$hook_name"
  echo "Installed: $hook_name -> $HOOK_TARGET_DIR/$hook_name"
  hooks_installed=$((hooks_installed + 1))
done

if [ "$hooks_installed" -eq 0 ]; then
  echo "WARN: no hooks found in $HOOK_SOURCE_DIR" >&2
  exit 1
fi

echo ""
echo "Installed $hooks_installed hook(s) successfully."
echo ""
echo "Test pre-push hook:"
echo "  git push origin main      # should be blocked"
echo "  SKINORA_ALLOW_DIRECT_PUSH=1 git push origin main   # bypass (use sparingly)"
