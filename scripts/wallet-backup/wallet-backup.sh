#!/usr/bin/env bash
# =============================================================================
# T163 — offline backup of the wallet secrets (DEPLOY_RUNBOOK §L).
#
#   bash scripts/wallet-backup/wallet-backup.sh backup  <secrets-dir> <out-dir> --label testnet|mainnet
#   bash scripts/wallet-backup/wallet-backup.sh verify  <archive.tar.gpg>
#   bash scripts/wallet-backup/wallet-backup.sh restore <archive.tar.gpg> <secrets-dir> --label testnet|mainnet [--force]
#
# What it backs up: the five signing secret files of the two networks —
# tron_hd_mnemonic.txt, tron_hot_wallet_private_key.txt,
# ethereum_hd_mnemonic.txt, ethereum_hot_wallet_private_key.txt,
# ethereum_fuel_wallet_private_key.txt — whichever are present and non-empty.
#
# How: tar → GnuPG symmetric encryption (AES-256, S2K SHA-512 with the maximum
# iteration count, integrity-protected), one passphrase. The archive holds a
# MANIFEST.json: SHA-256 of every file and the PUBLIC addresses derived from
# them (5 deposit addresses per seed, the hot and fuel wallets). Every command
# re-derives the addresses and compares them with the manifest.
#
# Never shows a secret: no `set -x`, the passphrase is read with echo off (or
# from --passphrase-file for drills/tests) and reaches gpg through a file
# descriptor, never argv; plaintext is only ever in a private temp directory
# that is shredded on exit, also when the run is interrupted (Ctrl+C, a
# closed window, kill). gpg runs with a throwaway GNUPGHOME — nothing is
# cached in, or written to, the operator's own keyring.
#
# Needs: bash, tar, gpg (all in Git for Windows), sha256sum, node + the
# sidecars' ethers (`npm ci` in sidecar-ethereum once). No network.
# =============================================================================
set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST_TOOL="$SCRIPT_DIR/wallet-manifest.mjs"
SECRET_NAMES=(
  tron_hd_mnemonic.txt
  tron_hot_wallet_private_key.txt
  ethereum_hd_mnemonic.txt
  ethereum_hot_wallet_private_key.txt
  ethereum_fuel_wallet_private_key.txt
)
MIN_PASSPHRASE_LENGTH=16
GPG_CIPHER=(--cipher-algo AES256 --s2k-mode 3 --s2k-digest-algo SHA512 --s2k-count 65011712 --compress-algo none)

die() { printf 'wallet-backup: %s\n' "$*" >&2; exit 1; }
say() { printf '%s\n' "$*"; }

WORK=""
cleanup() {
  # Not interruptible: a second signal (or one sent to the whole process
  # group, as `timeout` and a closed terminal do) must not stop the shredding
  # half way. Ignored signals are inherited by shred/rm too.
  trap '' HUP INT TERM
  [ -n "$WORK" ] || return 0
  if [ -d "$WORK/gnupg" ]; then
    gpgconf --homedir "$WORK/gnupg" --kill gpg-agent >/dev/null 2>&1 || true
  fi
  # Best effort on NTFS/SSD (DEPLOY_RUNBOOK §L.8); then remove.
  find "$WORK" -type f -exec shred -u -z {} + 2>/dev/null || true
  rm -rf "$WORK"
  WORK=""
}
# bash runs the EXIT trap on a normal exit and on `die`, but NOT when a signal
# kills it: without the three traps below a closed terminal window (HUP) or a
# `kill` (TERM) would leave the plaintext in $WORK. Each turns the signal into
# an exit (128 + signal number), which then runs cleanup.
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

new_workdir() {
  WORK="$(mktemp -d "${TMPDIR:-/tmp}/skinora-wallet-backup.XXXXXX")"
  chmod 700 "$WORK"
  mkdir -m 700 "$WORK/gnupg" "$WORK/plain"
  export GNUPGHOME="$WORK/gnupg"
}

require_tools() {
  local tool
  for tool in tar gpg gpgconf sha256sum node shred; do
    command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required (DEPLOY_RUNBOOK §L.2)."
  done
}

PASSPHRASE_FILE=""
LABEL=""
FORCE=0
POSITIONAL=()
parse_args() {
  while [ $# -gt 0 ]; do
    case "$1" in
      --label) [ $# -ge 2 ] || die "--label needs a value"; LABEL="$2"; shift 2 ;;
      --passphrase-file) [ $# -ge 2 ] || die "--passphrase-file needs a path"; PASSPHRASE_FILE="$2"; shift 2 ;;
      --force) FORCE=1; shift ;;
      -h|--help) sed -n '2,28p' "$0"; exit 0 ;;
      --*) die "unknown option $1" ;;
      *) POSITIONAL+=("$1"); shift ;;
    esac
  done
}

require_label() {
  case "$LABEL" in
    testnet|mainnet) ;;
    *) die "--label testnet|mainnet is required (a mainnet archive must never be mistaken for a test one)." ;;
  esac
}

# Sets PASS. $1 = "new" asks twice and enforces the length floor.
read_passphrase() {
  local again
  if [ -n "$PASSPHRASE_FILE" ]; then
    [ -f "$PASSPHRASE_FILE" ] || die "passphrase file not found."
    IFS= read -r PASS < "$PASSPHRASE_FILE" || true
  else
    [ -r /dev/tty ] || die "no terminal to read the passphrase from."
    IFS= read -rs -p "Backup passphrase: " PASS < /dev/tty; printf '\n' > /dev/tty
    if [ "${1:-}" = "new" ]; then
      IFS= read -rs -p "Repeat passphrase: " again < /dev/tty; printf '\n' > /dev/tty
      [ "$PASS" = "$again" ] || die "the two passphrases differ."
    fi
  fi
  [ -n "$PASS" ] || die "empty passphrase."
  if [ "${1:-}" = "new" ] && [ "${#PASS}" -lt "$MIN_PASSPHRASE_LENGTH" ]; then
    die "passphrase shorter than $MIN_PASSPHRASE_LENGTH characters (DEPLOY_RUNBOOK §L.3: 6 random words)."
  fi
}

gpg_base() {
  gpg --batch --quiet --no-tty --pinentry-mode loopback --no-symkey-cache --passphrase-fd 3 "$@"
}

# Decrypts $1 into $WORK/plain and checks it against its own manifest.
decrypt_and_check() {
  local archive="$1" status
  [ -f "$archive" ] || die "archive not found: $archive"
  set +e
  gpg_base --decrypt "$archive" 3< <(printf '%s' "$PASS") 2>"$WORK/gpg.err" | tar -xf - -C "$WORK/plain" 2>"$WORK/tar.err"
  status=("${PIPESTATUS[@]}")
  set -e
  if [ "${status[0]}" -ne 0 ]; then
    die "decryption failed — wrong passphrase or a damaged/altered archive (gpg exit ${status[0]}: $(tail -n 1 "$WORK/gpg.err"))."
  fi
  [ "${status[1]}" -eq 0 ] || die "the decrypted archive is not a valid tar."
  [ -f "$WORK/plain/MANIFEST.json" ] || die "archive has no MANIFEST.json."
  node "$MANIFEST_TOOL" check "$WORK/plain" "$WORK/plain/MANIFEST.json" > "$WORK/addresses.json" \
    || die "archive content does not match its manifest."
}

manifest_field() {
  node -e 'const m=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8"));const v=process.argv[2].split(".").reduce((o,k)=>o?.[k],m);process.stdout.write(Array.isArray(v)?v.map(x=>x.name??x).join(" "):String(v??""))' "$1" "$2"
}

print_addresses() {
  node -e '
    const a = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
    for (const [net, v] of Object.entries(a)) {
      console.log(`  ${net.toUpperCase()}`);
      if (v.seed) for (const d of v.deposits) console.log(`    deposit #${d.index}  ${d.address}`);
      else console.log("    deposit seed  (none)");
      console.log(`    hot wallet   ${v.hotWallet ?? "(none)"}`);
      if ("fuelWallet" in v) console.log(`    fuel wallet  ${v.fuelWallet ?? "(none)"}`);
    }' "$1"
}

cmd_backup() {
  [ ${#POSITIONAL[@]} -eq 2 ] || die "usage: backup <secrets-dir> <out-dir> --label testnet|mainnet"
  require_label
  local src="${POSITIONAL[0]}" out="${POSITIONAL[1]}" stamp archive files=() name
  [ -d "$src" ] || die "secrets directory not found: $src"
  mkdir -p "$out"
  for name in "${SECRET_NAMES[@]}"; do
    if [ -d "$src/$name" ]; then die "$src/$name is a directory (Docker creates one for a missing secret) — fix it first."; fi
    if [ -s "$src/$name" ] && grep -q '[^[:space:]]' "$src/$name"; then files+=("$name"); fi
  done
  [ ${#files[@]} -gt 0 ] || die "no wallet secret found in $src."

  new_workdir
  node "$MANIFEST_TOOL" manifest "$src" "$LABEL" > "$WORK/MANIFEST.json" || die "could not build the manifest."
  read_passphrase new

  stamp="$(date -u +%Y%m%d-%H%M%SZ)"
  archive="$out/skinora-wallets-$LABEL-$stamp.tar.gpg"
  [ ! -e "$archive" ] || die "$archive already exists."
  # Plaintext never touches the disk here: tar streams straight into gpg.
  tar -cf - -C "$WORK" MANIFEST.json -C "$src" "${files[@]}" \
    | gpg_base --symmetric "${GPG_CIPHER[@]}" --output "$archive" 3< <(printf '%s' "$PASS") \
    || die "encryption failed."
  (cd "$out" && sha256sum "$(basename "$archive")" > "$(basename "$archive").sha256")

  # Read it back with a SECOND entry of the passphrase (or the same file in a
  # drill): proves the archive opens and that the passphrase is known.
  PASS=""
  if [ -z "$PASSPHRASE_FILE" ]; then say "Verifying the archive — enter the passphrase again."; fi
  read_passphrase
  decrypt_and_check "$archive"
  node "$MANIFEST_TOOL" addresses "$src" > "$WORK/source.json"
  cmp -s "$WORK/addresses.json" "$WORK/source.json" || die "archive addresses differ from the source directory."

  say ""
  say "Backup OK  [$LABEL]"
  say "  archive  $archive"
  say "  sha256   $(cut -d' ' -f1 < "$archive.sha256")"
  say "  files    ${files[*]}"
  say "  missing  $(manifest_field "$WORK/plain/MANIFEST.json" missing)"
  say "Addresses (compare them with .env / the admin panel; write them on the label):"
  print_addresses "$WORK/addresses.json"
}

cmd_verify() {
  [ ${#POSITIONAL[@]} -eq 1 ] || die "usage: verify <archive.tar.gpg>"
  local archive="${POSITIONAL[0]}"
  new_workdir
  read_passphrase
  decrypt_and_check "$archive"
  say "Archive OK  [$(manifest_field "$WORK/plain/MANIFEST.json" label)]  created $(manifest_field "$WORK/plain/MANIFEST.json" createdAt)"
  say "  files    $(manifest_field "$WORK/plain/MANIFEST.json" files)"
  say "  sha256   $(sha256sum "$archive" | cut -d' ' -f1)"
  print_addresses "$WORK/addresses.json"
}

cmd_restore() {
  [ ${#POSITIONAL[@]} -eq 2 ] || die "usage: restore <archive.tar.gpg> <secrets-dir> --label testnet|mainnet [--force]"
  require_label
  local archive="${POSITIONAL[0]}" target="${POSITIONAL[1]}" name archived_label restored=()
  new_workdir
  read_passphrase
  decrypt_and_check "$archive"
  archived_label="$(manifest_field "$WORK/plain/MANIFEST.json" label)"
  [ "$archived_label" = "$LABEL" ] || die "the archive is a $archived_label backup, not $LABEL — refusing to restore it here."

  mkdir -p "$target"
  # Check every target first, then write: a refusal leaves the directory untouched.
  for name in $(manifest_field "$WORK/plain/MANIFEST.json" files); do
    if [ -d "$target/$name" ]; then
      [ -z "$(ls -A "$target/$name")" ] || die "$target/$name is a non-empty directory — remove it by hand."
    elif [ -s "$target/$name" ] && ! cmp -s "$WORK/plain/$name" "$target/$name" && [ "$FORCE" -ne 1 ]; then
      die "$target/$name exists with different content — refusing to overwrite (use --force only after backing it up)."
    fi
  done
  for name in $(manifest_field "$WORK/plain/MANIFEST.json" files); do
    if [ -d "$target/$name" ]; then
      rmdir "$target/$name"
      say "  removed the empty directory Docker left at $target/$name"
    fi
    cp "$WORK/plain/$name" "$target/$name"
    chmod 600 "$target/$name"
    restored+=("$name")
  done

  # The restored directory must derive exactly what the backup recorded.
  node "$MANIFEST_TOOL" addresses "$target" > "$WORK/restored.json"
  node -e '
    const fs = require("fs");
    const [restored, archived] = process.argv.slice(1).map((p) => JSON.parse(fs.readFileSync(p, "utf8")));
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    for (const net of Object.keys(archived)) {
      const a = archived[net], r = restored[net];
      if (a.seed && !same(a.deposits, r.deposits)) process.exit(1);
      for (const wallet of ["hotWallet", "fuelWallet"]) if (a[wallet] && a[wallet] !== r[wallet]) process.exit(1);
    }' "$WORK/restored.json" "$WORK/addresses.json"     || die "the restored directory does not derive the archived addresses."

  say ""
  say "Restore OK  [$LABEL] → $target"
  say "  restored ${restored[*]}"
  say "Addresses (the services must report these after restart — DEPLOY_RUNBOOK §L.6):"
  print_addresses "$WORK/addresses.json"
}

main() {
  [ $# -ge 1 ] || die "usage: wallet-backup.sh backup|verify|restore ... (--help)"
  local command="$1"; shift
  parse_args "$@"
  require_tools
  case "$command" in
    backup) cmd_backup ;;
    verify) cmd_verify ;;
    restore) cmd_restore ;;
    -h|--help) sed -n '2,28p' "$0" ;;
    *) die "unknown command $command" ;;
  esac
}

main "$@"
