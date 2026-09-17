# Sourced by <provider>/provision.sh. Needs bash 4+, docker compose v2, curl, openssl and awk.
# GitHub's Linux runner has them all, and so does Git Bash on Windows. It deliberately avoids jq,
# because Git Bash has no jq.
#
#   bash <provider>/provision.sh [up] [provision] [logs] [down]    (default: up provision)
#
# up         pull the images and start the containers
# provision  wait for the server, then create a user, a token and the buildmonitor-live job.
#            Make sure the job has one failed run, then write the BUILDMONITOR_* settings to
#            .state/<provider>.env, and to $GITHUB_ENV in a workflow
# logs       print the containers' recent output
# down       remove the containers, their volumes and the generated state
set -euo pipefail
# The images run as uid 1000, and must be able to read the secrets written here.
umask 022

here=$(cd "$(dirname "${BASH_SOURCE[1]}")" && pwd)
provider=$(basename "$here")
state=$(cd "$here/.." && pwd)/.state
mkdir -p "$state"

say() { printf '[%s %s] %s\n' "$provider" "$(date +%H:%M:%S)" "$*" >&2; }
fail() { say "error: $*"; exit 1; }

# MSYS_NO_PATHCONV keeps Git Bash from rewriting container paths such as /run/secrets. It is set
# for docker alone, because curl needs /dev/null rewritten.
compose() { (cd "$here" && MSYS_NO_PATHCONV=1 docker compose "$@"); }

# poll SECONDS COMMAND...: succeeds once COMMAND does, fails once SECONDS have passed.
poll() {
  local limit=$1 start=$SECONDS
  shift
  until "$@"; do
    (( SECONDS - start < limit )) || return 1
    sleep 5
  done
}

wait_for() {
  local limit=$1 what=$2
  shift 2
  say "waiting for $what"
  poll "$limit" "$@" || fail "no $what after ${limit}s"
  say "$what: done"
}

# The status code alone, 000 when nothing answered.
status() { curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$@" || true; }

fetch() { curl -sS --fail-with-body --max-time 60 "$@"; }

# field NAME < json: the first value of "NAME", a string, number or boolean, or nothing.
field() {
  tr -d '\r\n' | awk -v key="\"$1\"" '{
    i = index($0, key); if (!i) exit
    rest = substr($0, i + length(key)); sub(/^[ \t]*:[ \t]*"?/, "", rest)
    match(rest, /^[^",}]*/); print substr(rest, 1, RLENGTH) }'
}

random_hex() { openssl rand -hex "$1" | tr -d '\r\n'; }

# A value masked before it is used can never reach a public workflow log.
mask() { [[ -z ${GITHUB_ACTIONS:-} ]] || echo "::add-mask::$1"; }

export_env() {
  local file=$state/$provider.env pair
  : > "$file"
  for pair in "$@"; do
    printf '%s\n' "$pair" >> "$file"
    if [[ -n ${GITHUB_ENV:-} ]]; then
      printf '%s\n' "$pair" >> "$GITHUB_ENV"
    fi
  done
  say "settings written to $file"
}

# Only to the log, where a workflow's masks apply: an uploaded file is never masked.
logs() {
  compose ps --all || true
  compose logs --no-color --timestamps --tail 400 || true
}

down() {
  compose down --volumes --remove-orphans
  rm -f "$state/$provider".*
}

main() {
  (( $# )) || set -- up provision
  local step
  for step in "$@"; do
    case $step in
      up|provision|logs|down) "$step" ;;
      *) fail "unknown step $step: expected up, provision, logs or down" ;;
    esac
  done
}
