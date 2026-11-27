#!/bin/sh
# RepairHub PostgreSQL backups. Runs as the "backup" service of deploy/docker-compose.yml.
#
#   rh-backup schedule                          daemon (default command): daily backups at BACKUP_SCHEDULE
#   rh-backup run                               take one backup now, then apply retention
#   rh-backup list                              list backups, newest first
#   rh-backup verify FILE                       check the checksum and that the archive is fully readable
#   rh-backup restore FILE --target DB          show what a restore would do (changes nothing)
#   rh-backup restore FILE --target DB --confirm
#                                               DB = the production database name: replace it (the application
#                                               must be stopped; the old database is kept, renamed);
#                                               DB = any new name: restore next to production, for inspection
#   rh-backup health                            container health check
#
# The database connection comes from the libpq environment (PGHOST, PGDATABASE, PGUSER, PGPASSWORD); nothing in
# this script prints it. Backups are pg_dump custom-format archives: one consistent snapshot (MVCC), taken without
# blocking the application's reads or writes.

set -u
umask 077 # backups contain customer data: readable by the owner only

DIR=/backups
PREFIX=repairhub
SCHEDULE=${BACKUP_SCHEDULE:-03:00}
KEEP_DAYS=${BACKUP_KEEP_DAYS:-7}
KEEP_MIN=${BACKUP_KEEP_MIN:-3}
MAX_AGE_HOURS=${BACKUP_MAX_AGE_HOURS:-26}
STATUS=$DIR/.last-run
LOCK=/tmp/rh-backup.lock
ERR=/tmp/rh-backup.err
TOC=/tmp/rh-backup.toc

log() { printf '%s %s\n' "$(date +%Y-%m-%dT%H:%M:%S%z)" "$*"; }
err() { log "ERROR: $*" >&2; }

# Logs the stderr of a failed PostgreSQL tool (messages only: libpq never includes the password).
log_tool_errors() {
	[ -s "$ERR" ] || return 0
	while IFS= read -r line; do err "  $line"; done <"$ERR"
}

is_uint() { case $1 in '' | *[!0-9]*) return 1 ;; esac }

valid_db_name() { echo "$1" | grep -Eq '^[a-z_][a-z0-9_]{0,62}$'; }

check_config() {
	rc=0
	is_uint "$KEEP_DAYS" || { err "BACKUP_KEEP_DAYS must be a whole number of days, got '$KEEP_DAYS'"; rc=1; }
	if ! is_uint "$KEEP_MIN" || [ "$KEEP_MIN" -lt 1 ]; then err "BACKUP_KEEP_MIN must be 1 or more, got '$KEEP_MIN'"; rc=1; fi
	if ! is_uint "$MAX_AGE_HOURS" || [ "$MAX_AGE_HOURS" -lt 1 ]; then err "BACKUP_MAX_AGE_HOURS must be 1 or more, got '$MAX_AGE_HOURS'"; rc=1; fi
	[ -n "$(echo "$SCHEDULE" | tr -d ', ')" ] || { err "BACKUP_SCHEDULE is empty"; rc=1; }
	for t in $(echo "$SCHEDULE" | tr ',' ' '); do
		echo "$t" | grep -Eq '^([01][0-9]|2[0-3]):[0-5][0-9]$' || { err "BACKUP_SCHEDULE: '$t' is not a time like 03:00"; rc=1; }
	done
	if [ ! -d "$DIR" ] || [ ! -w "$DIR" ]; then err "$DIR is not a writable directory (check BACKUP_DIR and its owner)"; rc=1; fi
	return $rc
}

# Backups that completed and were verified (the checksum is written last), newest first.
valid_backups() {
	for f in $(ls -1 "$DIR"/$PREFIX-*.dump 2>/dev/null | sort -r); do
		[ -f "$f.sha256" ] && echo "$f"
	done
}

# repairhub-20261127T030000Z.dump -> seconds since the epoch (UTC). Fails for any other name.
name_epoch() {
	ts=$(basename "$1" .dump | sed -nE "s/^$PREFIX-([0-9]{4})([0-9]{2})([0-9]{2})T([0-9]{2})([0-9]{2})([0-9]{2})Z\$/\\1-\\2-\\3 \\4:\\5:\\6/p")
	[ -n "$ts" ] && date -u -d "$ts" +%s
}

resolve() { case $1 in /*) echo "$1" ;; *) echo "$DIR/$1" ;; esac }

# Archive is non-empty, its table of contents is readable, every data block decompresses, and it holds tables.
# Sets TABLES.
check_archive() {
	f=$1
	[ -s "$f" ] || { err "$(basename "$f"): file is missing or empty"; return 1; }
	if ! pg_restore --list "$f" >"$TOC" 2>"$ERR"; then err "$(basename "$f"): not a readable pg_dump archive"; log_tool_errors; return 1; fi
	if ! pg_restore --file=/dev/null "$f" 2>"$ERR"; then err "$(basename "$f"): archive is damaged"; log_tool_errors; return 1; fi
	TABLES=$(grep -c ' TABLE DATA ' "$TOC")
	[ "$TABLES" -gt 0 ] || { err "$(basename "$f"): archive contains no table data"; return 1; }
}

mark() { printf '%s %s\n' "$1" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >"$STATUS" 2>/dev/null; }

backup_once() {
	started=$(date +%s)
	name=$PREFIX-$(date -u +%Y%m%dT%H%M%SZ).dump
	final=$DIR/$name
	part=$final.partial
	log "backup started: database '$PGDATABASE' -> $name"

	# --lock-wait-timeout: if a migration holds an exclusive table lock, give up after 2 minutes instead of
	# queueing behind it (the next scheduled run tries again).
	if ! pg_dump --format=custom --no-password --lock-wait-timeout=120000 --file="$part" 2>"$ERR"; then
		err "backup FAILED: pg_dump exited with an error"
		log_tool_errors
		rm -f "$part"
		mark failed
		return 1
	fi
	if ! check_archive "$part"; then
		err "backup FAILED: verification of the new archive failed"
		rm -f "$part"
		mark failed
		return 1
	fi
	hash=$(sha256sum "$part" | cut -d ' ' -f 1)
	if ! printf '%s  %s\n' "$hash" "$name" >"$final.sha256.partial" || ! mv "$part" "$final" || ! mv "$final.sha256.partial" "$final.sha256"; then
		err "backup FAILED: could not write $final (disk full or permissions?)"
		rm -f "$part" "$final" "$final.sha256.partial"
		mark failed
		return 1
	fi
	mark ok
	log "backup OK: $name, $(wc -c <"$final" | tr -d ' ') bytes, $TABLES tables, $(($(date +%s) - started)) s"
}

# Runs only after a successful backup, so the newest backup is always a fresh valid one. The newest KEEP_MIN
# backups are never deleted, whatever their age.
retention() {
	cutoff=$(($(date +%s) - KEEP_DAYS * 86400))
	n=0
	for f in $(valid_backups); do
		n=$((n + 1))
		[ "$n" -le "$KEEP_MIN" ] && continue
		e=$(name_epoch "$f") || continue
		if [ "$e" -lt "$cutoff" ]; then
			rm -f "$f" "$f.sha256" && log "retention: deleted $(basename "$f") (older than $KEEP_DAYS days)"
		fi
	done
	remove_leftovers
}

# Partial files of a run that was interrupted (container killed during pg_dump).
remove_leftovers() {
	find "$DIR" -maxdepth 1 -name "$PREFIX-*.partial" -mmin +60 -exec rm -f {} \; 2>/dev/null
	return 0
}

backup_and_retention() { backup_once && retention; }

# One backup or restore at a time (also between the scheduler and a manual "docker compose exec backup ...").
locked() {
	(
		flock -n 9 || { err "another backup or restore is running"; exit 1; }
		"$@"
	) 9>"$LOCK"
}

# Seconds until the next scheduled time today or tomorrow (local time, TZ).
next_delay() {
	set -- $(date '+%H %M %S')
	now=$((${1#0} * 3600 + ${2#0} * 60 + ${3#0}))
	best=86400
	for t in $(echo "$SCHEDULE" | tr ',' ' '); do
		h=${t%%:*}
		m=${t#*:}
		d=$(((${h#0} * 3600 + ${m#0} * 60 - now + 86400) % 86400))
		[ "$d" -eq 0 ] && d=86400
		[ "$d" -lt "$best" ] && best=$d
	done
	echo "$best"
}

newest_age_hours() {
	newest=$(valid_backups | head -n 1)
	[ -n "$newest" ] || return 1
	e=$(name_epoch "$newest") || return 1
	echo $((($(date +%s) - e) / 3600))
}

schedule() {
	check_config || exit 1
	trap 'log "scheduler stopping"; exit 0' TERM INT
	log "scheduler started: daily at $SCHEDULE (${TZ:-UTC}), keep $KEEP_DAYS days and at least $KEEP_MIN backups"
	remove_leftovers
	# Catch up after downtime (server off at backup time, first deployment).
	age=$(newest_age_hours) || age=
	if [ -z "$age" ] || [ "$age" -ge "$MAX_AGE_HOURS" ]; then
		log "no backup in the last $MAX_AGE_HOURS hours: taking one now"
		locked backup_and_retention
	fi
	while :; do
		d=$(next_delay)
		log "next backup at $(date -d "@$(($(date +%s) + d))" '+%Y-%m-%d %H:%M')"
		sleep "$d" &
		wait $!
		locked backup_and_retention
	done
}

health() {
	age=$(newest_age_hours) || { echo "no backup yet"; return 1; }
	[ "$age" -lt "$MAX_AGE_HOURS" ] || { echo "newest backup is $age hours old"; return 1; }
	case $(cat "$STATUS" 2>/dev/null) in failed*) echo "last backup attempt failed: see docker compose logs backup"; return 1 ;; esac
	echo "ok"
}

list() {
	found=0
	for f in $(valid_backups); do
		found=1
		local_time=$(date -d "@$(name_epoch "$f")" '+%Y-%m-%d %H:%M')
		printf '%s  %s  %12s bytes\n' "$(basename "$f")" "$local_time" "$(wc -c <"$f" | tr -d ' ')"
	done
	[ "$found" -eq 1 ] || echo "no backups in $DIR"
	[ -f "$STATUS" ] && echo "last run: $(cat "$STATUS")"
	return 0
}

verify() {
	[ $# -eq 1 ] || { err "usage: rh-backup verify FILE"; return 2; }
	f=$(resolve "$1")
	[ -f "$f" ] || { err "no such file: $f"; return 1; }
	if [ -f "$f.sha256" ]; then
		(cd "$(dirname "$f")" && sha256sum -c "$(basename "$f").sha256" >/dev/null 2>&1) || { err "$(basename "$f"): checksum mismatch, the file was changed or damaged"; return 1; }
	else
		log "warning: $(basename "$f").sha256 not found, checksum not checked"
	fi
	check_archive "$f" || return 1
	log "verify OK: $(basename "$f"), $TABLES tables"
}

psql_admin() { psql --no-password --no-psqlrc -v ON_ERROR_STOP=1 -d postgres -qtA "$@"; }

restore() {
	file=
	target=
	confirm=0
	while [ $# -gt 0 ]; do
		case $1 in
		--target)
			[ $# -ge 2 ] || { err "--target needs a database name"; return 2; }
			target=$2
			shift 2
			;;
		--confirm) confirm=1; shift ;;
		-*) err "unknown option $1"; return 2 ;;
		*) file=$1; shift ;;
		esac
	done
	if [ -z "$file" ] || [ -z "$target" ]; then
		err "usage: rh-backup restore FILE --target DB [--confirm]"
		err "  --target $PGDATABASE          replace the production database (stop the application first)"
		err "  --target ${PGDATABASE}_restore  restore into a new database next to production, for inspection"
		return 2
	fi
	valid_db_name "$target" || { err "invalid database name '$target' (lowercase letters, digits, _)"; return 2; }
	f=$(resolve "$file")
	verify "$f" || return 1

	work=${target}_restoring
	stamp=$(date -u +%Y%m%d%H%M%S)
	old=${target}_before_restore_$stamp
	if [ "$target" = "$PGDATABASE" ]; then
		valid_db_name "$old" || { err "database name too long for a backup copy name"; return 1; }
		log "plan: restore $(basename "$f") into a new database, then swap it in as '$target'."
		log "      The current '$target' is kept as '$old' (drop it yourself once everything is checked)."
	else
		exists=$(psql_admin -c "SELECT 1 FROM pg_database WHERE datname = '$target'") || { err "cannot connect to PostgreSQL"; return 1; }
		[ -z "$exists" ] || { err "database '$target' already exists; choose another name"; return 1; }
		log "plan: restore $(basename "$f") into the new database '$target'. Production ('$PGDATABASE') is not touched."
	fi
	if [ "$confirm" -ne 1 ]; then
		log "nothing changed. To do it, run the same command with --confirm"
		return 2
	fi

	if [ "$target" = "$PGDATABASE" ]; then
		busy=$(psql_admin -c "SELECT count(*) FROM pg_stat_activity WHERE datname = '$target' AND pid <> pg_backend_pid()") || { err "cannot connect to PostgreSQL"; return 1; }
		if [ "$busy" -ne 0 ]; then
			err "$busy open connection(s) to '$target'. Stop the application first: docker compose stop web api"
			return 1
		fi
	fi

	psql_admin -c "DROP DATABASE IF EXISTS \"$work\"" -c "CREATE DATABASE \"$work\"" 2>"$ERR" || { err "could not create database '$work'"; log_tool_errors; return 1; }
	log "restoring $(basename "$f") into '$work'..."
	if ! pg_restore --no-password --exit-on-error --single-transaction --no-owner --no-privileges -d "$work" "$f" 2>"$ERR"; then
		err "restore FAILED; nothing was replaced"
		log_tool_errors
		psql_admin -c "DROP DATABASE IF EXISTS \"$work\"" >/dev/null 2>&1
		return 1
	fi

	if [ "$target" = "$PGDATABASE" ]; then
		# Both renames in one statement string = one transaction: either both happen or neither.
		if ! psql_admin -c "ALTER DATABASE \"$target\" RENAME TO \"$old\"; ALTER DATABASE \"$work\" RENAME TO \"$target\";" 2>"$ERR"; then
			err "could not swap the databases (did the application reconnect?); '$target' is unchanged, the restored copy is '$work'"
			log_tool_errors
			return 1
		fi
		log "restore OK: '$target' now contains $(basename "$f"). Previous database kept as '$old'."
		log "next: docker compose up -d   (starts the application; pending migrations are applied first)"
	else
		psql_admin -c "ALTER DATABASE \"$work\" RENAME TO \"$target\"" 2>"$ERR" || { err "could not rename '$work'"; log_tool_errors; return 1; }
		log "restore OK: '$target' contains $(basename "$f"). Production was not touched."
	fi
}

cmd=${1:-}
[ $# -gt 0 ] && shift
case $cmd in
schedule) schedule ;;
run) check_config && locked backup_and_retention ;;
list) list ;;
verify) verify "$@" ;;
restore) locked restore "$@" ;;
health) health ;;
*)
	echo "usage: rh-backup schedule | run | list | verify FILE | restore FILE --target DB [--confirm] | health" >&2
	exit 2
	;;
esac
