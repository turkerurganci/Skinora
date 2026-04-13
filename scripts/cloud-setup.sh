#!/usr/bin/env bash
# Skinora — Cloud session setup script
#
# claude.ai/code cloud environment'ında session başlamadan önce çalışır.
# Environment oluştururken:
#   1. Network access → "Full" (Trusted mod .NET indirmesini 403 ile engeller)
#   2. Setup script alanına şu satırı gir:
#      bash scripts/cloud-setup.sh
#
# Ne yapar:
#   1. .NET 9.0 SDK kurulumu (yoksa)
#   2. Git hook'ları aktif eder (savunma katmanları)
#
# Lokal PC'de çalıştırmak gereksiz ama zararsız.

set -e

echo "=== Skinora cloud-setup ==="

# -------------------------------------------------------------------
# 1. .NET 9.0 SDK
# -------------------------------------------------------------------
if command -v dotnet &>/dev/null && dotnet --version 2>/dev/null | grep -q '^9\.'; then
  echo ".NET 9.0 zaten kurulu: $(dotnet --version)"
else
  echo ".NET 9.0 kuruluyor..."
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
  echo ".NET kuruldu: $(dotnet --version)"
fi

# -------------------------------------------------------------------
# 2. Git hooks (4 savunma katmanı)
# -------------------------------------------------------------------
if [ -f scripts/git-hooks/install.sh ]; then
  echo "Git hook'ları kuruluyor..."
  bash scripts/git-hooks/install.sh
else
  echo "UYARI: scripts/git-hooks/install.sh bulunamadı — hook'lar kurulmadı"
fi

echo "=== cloud-setup tamamlandı ==="
