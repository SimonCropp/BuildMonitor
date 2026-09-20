#!/usr/bin/env bash
# Jenkins for the live tests: bash provision.sh [up] [provision] [logs] [down]. See ../lib.sh.
source "$(dirname "${BASH_SOURCE[0]}")/../lib.sh"

server=http://127.0.0.1:${BUILDMONITOR_JENKINS_PORT:-8080}
user=buildmonitor
job=buildmonitor-live
secret=$state/jenkins.secret

# compose.yaml reads these. up fills them in.
export LIVE_JENKINS_SECRET='' LIVE_JENKINS_INIT=''

setting() { sed -n "s/^$1=//p" "$secret" | tr -d '\r'; }

up() {
  # The token is fixed here, before Jenkins starts, so it is masked before it is ever used.
  # buildmonitor.groovy registers it.
  if [[ ! -s $secret ]]; then
    printf 'user=%s\npassword=%s\ntoken=11%s\n' "$user" "$(random_hex 16)" "$(random_hex 16)" > "$secret"
  fi
  mask "$(setting password)"
  mask "$(setting token)"
  LIVE_JENKINS_SECRET=$(< "$secret")
  LIVE_JENKINS_INIT=$(< "$here/buildmonitor.groovy")
  compose pull --quiet
  compose up --detach
}

provision() {
  [[ -s $secret ]] || fail "run up first"
  mask "$(setting token)"
  auth=(--user "$user:$(setting token)")
  wait_for 300 "Jenkins to accept the token" signed_in
  if [[ $(status "${auth[@]}" "$server/job/$job/api/json") != 200 ]]; then
    fetch "${auth[@]}" -H 'Content-Type: application/xml' --data-binary @- \
      "$server/createItem?name=$job" < "$here/$job.xml" > /dev/null
    say "created $job"
  fi

  if [[ $(status "${auth[@]}" "$server/job/$job/lastFailedBuild/api/json") != 200 ]]; then
    fetch "${auth[@]}" -X POST "$server/job/$job/build" > /dev/null
    say "started $job"
  fi

  wait_for 300 "a failed $job build" failed
  export_env \
    "BUILDMONITOR_JENKINS_SERVER=$server" \
    "BUILDMONITOR_JENKINS_USER=$user" \
    "BUILDMONITOR_JENKINS_TOKEN=$(setting token)" \
    "BUILDMONITOR_JENKINS_PIPELINE=$job" \
    "BUILDMONITOR_JENKINS_ARTIFACT=marker.txt"
}

signed_in() { [[ $(status "${auth[@]}" "$server/me/api/json?tree=id") == 200 ]]; }

# lastFailedBuild only ever points at a finished build.
failed() { [[ $(status "${auth[@]}" "$server/job/$job/lastFailedBuild/api/json?tree=number") == 200 ]]; }

main "$@"
