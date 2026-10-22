#!/usr/bin/env bash
# =============================================================================
# Skinora — automatic SQL Server backups (T164, DEPLOY_RUNBOOK §N).
#
# SQL Server Express has no SQL Agent, so nothing inside the database server
# schedules a backup. This script is the scheduler. It runs as the long-lived
# compose service `skinora-db-backup` (image: the database's own image, which
# ships sqlcmd); `restart: unless-stopped` brings it back with Docker Desktop
# after the PC restarts, and the first tick catches up on whatever is due —
# the schedule is "the newest file is older than the interval", not a clock
# time, so a night the PC was off costs one late backup, never a skipped one.
#
# The database server writes the files (BACKUP ... TO DISK is a server-side
# path) into /var/opt/mssql/backup, a HOST folder bind-mounted into both the
# database and this container — the copies live outside the database volume
# and survive `down -v`. This container renames, verifies, prunes and lists.
#
# Commands (default: run):
#   run                    the scheduler loop (the compose service)
#   tick                   one pass of that loop (what run does every TICK s)
#   full [tag]             one full backup now (e.g. `full predeploy`)
#   log                    one log backup now
#   prune                  apply the retention now
#   list                   the backup files, oldest first
#   restore <target> [stopat]
#                          restore the newest full (the newest one at or before
#                          stopat) + every later log backup, in order, into a
#                          NEW database <target>; stopat = YYYY-MM-DDTHH:MM:SSZ
#                          (UTC) for a point-in-time restore. Never overwrites.
#                          Refuses a stopat later than the newest log backup
#                          (the restore would silently end earlier) and prints
#                          the point it reached.
#   swap <restored>        rename the live database to <db>_before_restore_<ts>
#                          and <restored> to <db>; the old copy is KEPT for the
#                          reconciliation (DEPLOY_RUNBOOK §N.6). Then a full.
#
# Environment (compose passes them; defaults are the T164 proposal — ⚠):
#   MSSQL_SA_PASSWORD               required (handed to sqlcmd as SQLCMDPASSWORD)
#   DB_BACKUP_SERVER                skinora-db
#   DB_BACKUP_DATABASE              Skinora
#   DB_BACKUP_FULL_INTERVAL_HOURS   24
#   DB_BACKUP_LOG_INTERVAL_MINUTES  15   (0 = no log backups; recovery SIMPLE)
#   DB_BACKUP_RETENTION_DAYS        14   (the newest 2 fulls are always kept)
#   DB_BACKUP_TICK_SECONDS          60
#   DB_BACKUP_DIR                   /var/opt/mssql/backup (same path in both containers)
# =============================================================================
set -uo pipefail

DIR="${DB_BACKUP_DIR:-/var/opt/mssql/backup}"
DB="${DB_BACKUP_DATABASE:-Skinora}"
SERVER="${DB_BACKUP_SERVER:-skinora-db}"
FULL_HOURS="${DB_BACKUP_FULL_INTERVAL_HOURS:-24}"
LOG_MINUTES="${DB_BACKUP_LOG_INTERVAL_MINUTES:-15}"
RETENTION_DAYS="${DB_BACKUP_RETENTION_DAYS:-14}"
TICK="${DB_BACKUP_TICK_SECONDS:-60}"
SQLCMD="${SQLCMD:-/opt/mssql-tools18/bin/sqlcmd}"
LOCK="${DB_BACKUP_LOCK:-/tmp/skinora-db-backup.lock}"
# Always kept, whatever their age: a stalled schedule must never prune away
# the last good backups.
MIN_KEEP_FULL=2
# A failed full is retried at most this often (disk full, verify failure ...);
# meanwhile the log backups keep running on the previous full's chain.
FULL_RETRY_MINUTES=60
FULL_FAILED_MARK="${LOCK}.full-failed"

log() { printf '%s db-backup %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
die() { log "FATAL $*"; exit 1; }
is_uint() { [[ "$1" =~ ^[0-9]+$ ]]; }

validate_config() {
  [[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || die "DB_BACKUP_DATABASE must match [A-Za-z0-9_]+ (got '$DB')"
  is_uint "$FULL_HOURS" && [ "$FULL_HOURS" -ge 1 ] || die "DB_BACKUP_FULL_INTERVAL_HOURS must be an integer >= 1"
  is_uint "$LOG_MINUTES" || die "DB_BACKUP_LOG_INTERVAL_MINUTES must be an integer >= 0"
  is_uint "$RETENTION_DAYS" && [ "$RETENTION_DAYS" -ge 1 ] || die "DB_BACKUP_RETENTION_DAYS must be an integer >= 1"
  is_uint "$TICK" && [ "$TICK" -ge 1 ] || die "DB_BACKUP_TICK_SECONDS must be an integer >= 1"
  if [ $((RETENTION_DAYS * 24)) -lt "$FULL_HOURS" ]; then
    die "DB_BACKUP_RETENTION_DAYS ($RETENTION_DAYS d) is shorter than DB_BACKUP_FULL_INTERVAL_HOURS ($FULL_HOURS h)"
  fi
  [ -n "${MSSQL_SA_PASSWORD:-}" ] || die "MSSQL_SA_PASSWORD is empty"
  [ -d "$DIR" ] && [ -w "$DIR" ] || die "backup directory $DIR is missing or not writable (host bind mount)"
  # sqlcmd reads the password from the environment, never from argv (ps).
  export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
  unset MSSQL_SA_PASSWORD
}

# One T-SQL batch; prints the result rows (no headers, '|' separated).
sql() {
  "$SQLCMD" -S "$SERVER" -U sa -C -b -h -1 -W -s '|' -l 15 -Q "SET NOCOUNT ON; $1" 2>&1
}

now_epoch() { date -u +%s; }

# Skinora_FULL_20261022T120000Z[_tag].bak -> epoch; empty when the name is not ours.
file_epoch() {
  local base ts
  base="$(basename "$1")"
  [[ "$base" =~ _(FULL|LOG)_([0-9]{8})T([0-9]{2})([0-9]{2})([0-9]{2})Z(_[a-z0-9-]+)?\.(bak|trn)$ ]] || return 1
  ts="${BASH_REMATCH[2]} ${BASH_REMATCH[3]}:${BASH_REMATCH[4]}:${BASH_REMATCH[5]}"
  date -u -d "${ts:0:4}-${ts:4:2}-${ts:6:2}${ts:8}" +%s
}

# Completed backup files of one kind, oldest first (the name sorts by time).
files_of() {
  local kind="$1" ext
  [ "$kind" = FULL ] && ext=bak || ext=trn
  find "$DIR" -maxdepth 1 -type f -name "${DB}_${kind}_*.${ext}" -printf '%f\n' 2>/dev/null | sort
}

newest_epoch() {
  local f
  f="$(files_of "$1" | tail -n 1)"
  [ -n "$f" ] && file_epoch "$f"
}

db_state() { sql "SELECT state_desc FROM sys.databases WHERE name = N'$DB';"; }

wanted_recovery() { [ "$LOG_MINUTES" -gt 0 ] && echo FULL || echo SIMPLE; }

ensure_recovery_model() {
  local current wanted
  wanted="$(wanted_recovery)"
  current="$(sql "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$DB';")" || return 1
  if [ "$current" != "$wanted" ]; then
    if sql "ALTER DATABASE [$DB] SET RECOVERY $wanted;" >/dev/null; then
      log "recovery model $current -> $wanted (log backups every ${LOG_MINUTES} min; 0 = none)"
      return 2   # a new log chain starts with the next full
    fi
    log "FAILED to set recovery model $wanted on $DB"
    return 1
  fi
  return 0
}

# do_backup FULL|LOG [tag] — writes <name>.inprogress, verifies it, renames it.
do_backup() {
  local kind="$1" tag="${2:-}" ts name tmp out stmt size
  if [ -n "$tag" ] && ! [[ "$tag" =~ ^[a-z0-9-]{1,32}$ ]]; then
    log "FAILED $kind backup: tag must match [a-z0-9-]{1,32}"
    return 1
  fi
  # Names are second-resolution: two backups of a kind in the same second
  # (a manual one next to the scheduler) would overwrite each other and break
  # the log chain - wait for a free second instead.
  while :; do
    ts="$(date -u +%Y%m%dT%H%M%SZ)"
    compgen -G "$DIR/${DB}_${kind}_${ts}*" >/dev/null || break
    sleep 1
  done
  if [ "$kind" = FULL ]; then
    name="${DB}_FULL_${ts}${tag:+_$tag}.bak"
    stmt="BACKUP DATABASE [$DB] TO DISK = N'$DIR/$name.inprogress' WITH CHECKSUM, INIT, NAME = N'$DB full $ts';"
  else
    name="${DB}_LOG_${ts}${tag:+_$tag}.trn"
    stmt="BACKUP LOG [$DB] TO DISK = N'$DIR/$name.inprogress' WITH CHECKSUM, INIT, NAME = N'$DB log $ts';"
  fi
  tmp="$DIR/$name.inprogress"
  if ! out="$(sql "$stmt")"; then
    rm -f "$tmp"
    log "FAILED $kind backup $name: $(echo "$out" | tr '\n' ' ')"
    # 4214: no current full backup (new database, recovery model change,
    # restore) — the caller takes a full.
    if echo "$out" | grep -q 'Msg 4214'; then return 3; fi
    return 1
  fi
  if ! out="$(sql "RESTORE VERIFYONLY FROM DISK = N'$tmp' WITH CHECKSUM;")"; then
    rm -f "$tmp"
    log "FAILED $kind backup $name: verify: $(echo "$out" | tr '\n' ' ')"
    return 1
  fi
  mv -n "$tmp" "$DIR/$name" && [ ! -e "$tmp" ] || { rm -f "$tmp"; log "FAILED $kind backup $name: rename"; return 1; }
  chmod 600 "$DIR/$name" 2>/dev/null || true
  size="$(stat -c %s "$DIR/$name" 2>/dev/null || echo '?')"
  log "OK $kind $name bytes=$size verified"
  [ "$kind" = FULL ] && rm -f "$FULL_FAILED_MARK"
  return 0
}

prune() {
  local cutoff fulls count keep_from oldest_kept f e removed=0
  cutoff=$(( $(now_epoch) - RETENTION_DAYS * 86400 ))
  mapfile -t fulls < <(files_of FULL)
  count=${#fulls[@]}
  [ "$count" -eq 0 ] && return 0
  # Index of the first full that stays: every full newer than the cutoff,
  # plus the newest one before it (a point in time just after the cutoff
  # restores from that full and its logs, so the window really is
  # RETENTION_DAYS long), and never fewer than MIN_KEEP_FULL.
  keep_from=$count
  for ((i = 0; i < count; i++)); do
    e="$(file_epoch "${fulls[$i]}")" || continue
    if [ "$e" -ge "$cutoff" ]; then keep_from=$i; break; fi
  done
  [ "$keep_from" -gt 0 ] && keep_from=$((keep_from - 1))
  if [ $((count - keep_from)) -lt "$MIN_KEEP_FULL" ]; then
    keep_from=$((count - MIN_KEEP_FULL)); [ "$keep_from" -lt 0 ] && keep_from=0
  fi
  for ((i = 0; i < keep_from; i++)); do
    rm -f "$DIR/${fulls[$i]}" && removed=$((removed + 1)) && log "pruned ${fulls[$i]}"
  done
  oldest_kept="$(file_epoch "${fulls[$keep_from]}")"
  # A log backup older than the oldest kept full restores nothing.
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    e="$(file_epoch "$f")" || continue
    if [ "$e" -lt "$oldest_kept" ]; then
      rm -f "$DIR/$f" && removed=$((removed + 1)) && log "pruned $f"
    fi
  done < <(files_of LOG)
  # Leftovers of a backup that died half-way.
  find "$DIR" -maxdepth 1 -type f -name '*.inprogress' -mmin +1440 -delete 2>/dev/null || true
  # Backup history in msdb shrinks with the files.
  sql "EXEC msdb.dbo.sp_delete_backuphistory @oldest_date = '$(date -u -d "@$cutoff" '+%Y-%m-%d %H:%M:%S')';" >/dev/null || true
  [ "$removed" -gt 0 ] && log "retention ${RETENTION_DAYS} d: removed $removed file(s), oldest kept full ${fulls[$keep_from]}"
  return 0
}

QUIET_UNTIL=0
quiet_log() {
  # Waiting states (server starting, database not created yet) are logged at
  # most every 10 minutes.
  local now; now=$(now_epoch)
  if [ "$now" -ge "$QUIET_UNTIL" ]; then log "$*"; QUIET_UNTIL=$((now + 600)); fi
}

tick() {
  local state now full_e log_e rc
  if ! state="$(db_state)"; then quiet_log "waiting: database server $SERVER not reachable"; return 0; fi
  if [ -z "$state" ]; then quiet_log "waiting: database $DB does not exist yet (migrations not applied)"; return 0; fi
  # RESTORING / RECOVERING / OFFLINE ... — a restore or swap is in progress.
  if [ "$state" != ONLINE ]; then quiet_log "waiting: database $DB is $state"; return 0; fi
  QUIET_UNTIL=0

  ensure_recovery_model; rc=$?
  [ "$rc" -eq 1 ] && return 0
  now=$(now_epoch)
  full_e="$(newest_epoch FULL || true)"
  if [ "$rc" -eq 2 ] || [ -z "$full_e" ] || [ $((now - full_e)) -ge $((FULL_HOURS * 3600)) ]; then
    if full_retry_due "$now"; then
      if do_backup FULL; then prune; return 0; fi
      echo "$now" > "$FULL_FAILED_MARK"
      log "full backup will be retried in ${FULL_RETRY_MINUTES} min; log backups continue on the previous full"
    fi
    # No full to chain on (new database, recovery model just changed): a log
    # backup would be refused (Msg 4214) - nothing more this tick.
    if [ "$rc" -eq 2 ] || [ -z "$full_e" ]; then return 0; fi
  fi
  [ "$LOG_MINUTES" -eq 0 ] && return 0
  log_e="$(newest_epoch LOG || true)"
  [ -z "$log_e" ] || [ "$log_e" -lt "$full_e" ] && log_e="$full_e"
  if [ $((now - log_e)) -ge $((LOG_MINUTES * 60)) ]; then
    do_backup LOG; rc=$?
    if [ "$rc" -eq 3 ] && full_retry_due "$now"; then
      if do_backup FULL; then prune; else echo "$now" > "$FULL_FAILED_MARK"; fi
    fi
  fi
  return 0
}

# A failed full is retried after FULL_RETRY_MINUTES, not on every tick: each
# attempt writes (and deletes) a whole backup - a full disk gets fuller - and
# the log backups must not wait behind it.
full_retry_due() {
  local failed
  [ -f "$FULL_FAILED_MARK" ] || return 0
  failed="$(cat "$FULL_FAILED_MARK" 2>/dev/null)"
  is_uint "$failed" || return 0
  [ $(( $1 - failed )) -ge $((FULL_RETRY_MINUTES * 60)) ]
}

locked() { exec 9>"$LOCK"; flock 9; "$@"; local rc=$?; flock -u 9; return $rc; }

cmd_run() {
  log "scheduler started: database $DB on $SERVER, full every ${FULL_HOURS} h, log every ${LOG_MINUTES} min (0 = none), retention ${RETENTION_DAYS} d (newest ${MIN_KEEP_FULL} fulls always kept), dir $DIR"
  trap 'log "stopping"; exit 0' TERM INT
  while true; do
    locked tick
    sleep "$TICK" & wait $!
  done
}

cmd_list() {
  local f
  for f in $(files_of FULL; files_of LOG); do
    printf '%s\t%s\n' "$f" "$(stat -c %s "$DIR/$f")"
  done | sort -t_ -k3
}

# The restore chain for an optional stopat: the newest full at or before it,
# then every log backup after that full; with stopat, up to and including the
# first log backup taken at or after it.
restore_chain() {
  local stopat_e="$1" full="" f e
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    e="$(file_epoch "$f")" || continue
    if [ -z "$stopat_e" ] || [ "$e" -le "$stopat_e" ]; then full="$f"; fi
  done < <(files_of FULL)
  [ -n "$full" ] || return 1
  echo "$full"
  local full_e; full_e="$(file_epoch "$full")"
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    e="$(file_epoch "$f")" || continue
    [ "$e" -le "$full_e" ] && continue
    echo "$f"
    if [ -n "$stopat_e" ] && [ "$e" -ge "$stopat_e" ]; then break; fi
  done < <(files_of LOG)
}

cmd_restore() {
  local target="${1:-}" stopat="${2:-}" stopat_e="" stopat_sql="" exists chain moves first=1 f out last last_e reached
  [[ "$target" =~ ^[A-Za-z0-9_]+$ ]] || die "usage: restore <target-db [A-Za-z0-9_]+> [stopat YYYY-MM-DDTHH:MM:SSZ]"
  [ "$target" != "$DB" ] || die "restore never overwrites $DB — restore into a new name, then swap"
  if [ -n "$stopat" ]; then
    [[ "$stopat" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] || die "stopat must be YYYY-MM-DDTHH:MM:SSZ (UTC)"
    stopat_e="$(date -u -d "${stopat/T/ }" +%s)" || die "bad stopat"
    stopat_sql=", STOPAT = N'${stopat:0:10} ${stopat:11:8}'"
  fi
  exists="$(sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$target';")" || die "server not reachable: $exists"
  [ "$exists" = 0 ] || die "database $target already exists"
  mapfile -t chain < <(restore_chain "$stopat_e")
  [ "${#chain[@]}" -gt 0 ] || die "no full backup${stopat:+ at or before $stopat} in $DIR"
  last="${chain[${#chain[@]}-1]}"
  last_e="$(file_epoch "$last")"
  reached="end of $last (taken $(date -u -d "@$last_e" +%Y-%m-%dT%H:%M:%SZ))"
  if [ -n "$stopat_e" ]; then
    # No backup at or after stopat: SQL Server would stop at the end of the
    # last log without a word, and the restored database would be up to one
    # log interval older than asked (DEPLOY_RUNBOOK §N.6).
    [ "$last_e" -ge "$stopat_e" ] || die "stopat $stopat is later than the newest backup ($last, taken $(date -u -d "@$last_e" +%Y-%m-%dT%H:%M:%SZ)) - the restore would silently end there. Take a log backup first (log; the live database must be readable), or restore without stopat to get the newest point the backups hold"
    reached="$stopat (STOPAT)"
  fi
  log "restore $target from: ${chain[*]}${stopat:+ (stopat $stopat UTC)}"
  # New physical files next to the server's defaults, named after the target and
  # the time: after a swap the live database keeps the files of the name it was
  # restored under, so a later restore into the same name must not collide.
  moves="$(sql "RESTORE FILELISTONLY FROM DISK = N'$DIR/${chain[0]}';" | awk -F'|' -v t="${target}_$(date -u +%Y%m%dT%H%M%SZ)" '
    NF > 3 { ext = ($3 == "L") ? "_log.ldf" : ((n[$3]++ == 0) ? ".mdf" : "_" n[$3] ".ndf");
             printf ", MOVE N'\''%s'\'' TO N'\''/var/opt/mssql/data/%s%s'\''", $1, t, ext }')"
  [ -n "$moves" ] || die "could not read the file list of ${chain[0]}"
  for f in "${chain[@]}"; do
    if [ "$first" = 1 ]; then
      out="$(sql "RESTORE DATABASE [$target] FROM DISK = N'$DIR/$f' WITH NORECOVERY, CHECKSUM$moves;")" || die "restore $f: $(echo "$out" | tr '\n' ' ') - $target is left RESTORING: DROP DATABASE [$target] before trying again"
      first=0
    else
      out="$(sql "RESTORE LOG [$target] FROM DISK = N'$DIR/$f' WITH NORECOVERY, CHECKSUM$stopat_sql;")" || die "restore $f: $(echo "$out" | tr '\n' ' ') - $target is left RESTORING: DROP DATABASE [$target] before trying again"
    fi
    log "applied $f"
  done
  out="$(sql "RESTORE DATABASE [$target] WITH RECOVERY;")" || die "recovery: $(echo "$out" | tr '\n' ' ') - $target is left RESTORING: DROP DATABASE [$target] before trying again"
  log "OK restored $target ($(( ${#chain[@]} )) file(s)) to the $reached; check it, then: swap $target"
}

cmd_swap() {
  local restored="${1:-}" ts old exists
  [[ "$restored" =~ ^[A-Za-z0-9_]+$ ]] && [ "$restored" != "$DB" ] || die "usage: swap <restored-db>"
  exists="$(sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$restored' AND state_desc = 'ONLINE';")" || die "server not reachable"
  [ "$exists" = 1 ] || die "database $restored is not ONLINE"
  ts="$(date -u +%Y%m%dT%H%M%SZ)"
  old="${DB}_before_restore_${ts}"
  exists="$(sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$DB';")"
  if [ "$exists" = 1 ]; then
    sql "ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE [$DB] MODIFY NAME = [$old]; ALTER DATABASE [$old] SET MULTI_USER;" >/dev/null \
      || die "could not rename $DB (is the backend stopped?)"
    log "renamed $DB -> $old (kept for the reconciliation; drop it when done)"
  fi
  sql "ALTER DATABASE [$restored] MODIFY NAME = [$DB];" >/dev/null || die "could not rename $restored -> $DB"
  log "renamed $restored -> $DB"
  ensure_recovery_model >/dev/null; do_backup FULL postrestore || die "post-restore full backup failed"
}

main() {
  local cmd="${1:-run}"; shift || true
  validate_config
  case "$cmd" in
    run) cmd_run ;;
    tick) locked tick ;;
    full) locked do_backup FULL "${1:-}" ;;
    log) locked do_backup LOG ;;
    prune) locked prune ;;
    list) cmd_list ;;
    restore) locked cmd_restore "$@" ;;
    swap) locked cmd_swap "$@" ;;
    *) die "unknown command '$cmd' (run | tick | full [tag] | log | prune | list | restore <target> [stopat] | swap <restored>)" ;;
  esac
}

main "$@"
