#!/usr/bin/env bash
# Skinora git hook installer
#
# Hook'lari .git/hooks/'a kopyalamak yerine git'in core.hooksPath
# config'ini scripts/git-hooks/'a yonlendirir. Boylece:
#   - Hook'lar version-controlled (repo'da yasiyorlar)
#   - Edit'ler aninda etkili (kopyalama gereksiz)
#   - Stale .git/hooks/ kopyalari ile sync sorunu yok
#
# Kullanim (yeni clone sonrasi tek seferlik):
#   bash scripts/git-hooks/install.sh
#
# T11 close-out (discipline-only branch protection) icin gerekli.
# Validator R1 mitigasyonu — repo-level config ile onboarding tek komut.

set -e

GIT_DIR="$(git rev-parse --git-dir 2>/dev/null)"
if [ -z "$GIT_DIR" ]; then
  echo "ERROR: not inside a git repository" >&2
  exit 1
fi

REPO_ROOT="$(git rev-parse --show-toplevel)"
HOOKS_PATH="scripts/git-hooks"

# core.hooksPath set et
git config core.hooksPath "$HOOKS_PATH"
echo "Set core.hooksPath = $HOOKS_PATH"

# Hook dosyalarinin executable bit'ini garanti altina al
# (Windows'ta git update-index --chmod=+x ile commit'lendi ama klon sirasinda
#  baska Unix sistemlerinde dogru gelmesi icin chmod cekiyoruz)
for hook in "$REPO_ROOT/$HOOKS_PATH"/*; do
  hook_name="$(basename "$hook")"
  case "$hook_name" in
    install.sh|README*|*.md)
      continue
      ;;
  esac
  if [ -f "$hook" ]; then
    chmod +x "$hook"
    echo "Ensured executable: $hook_name"
  fi
done

echo ""
echo "Hooks installed via core.hooksPath."
echo ""
echo "Verify:"
echo "  git config core.hooksPath        # should print: $HOOKS_PATH"
echo ""
echo "Test pre-push hook:"
echo "  git push origin main             # should be blocked"
echo "  SKINORA_ALLOW_DIRECT_PUSH=1 git push origin main   # bypass (use sparingly)"
echo ""
echo "Disable (revert to default .git/hooks/):"
echo "  git config --unset core.hooksPath"
