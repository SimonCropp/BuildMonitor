#!/usr/bin/env bash
# GoCD for the live tests: bash provision.sh [up] [provision] [logs] [down]. See ../lib.sh.
source "$(dirname "${BASH_SOURCE[0]}")/../lib.sh"

server=http://127.0.0.1:${BUILDMONITOR_GOCD_PORT:-8153}
user=buildmonitor
pipeline=buildmonitor-live
# What the provider sends: the unversioned type means each API's latest version.
latest=(-H 'Accept: application/vnd.go.cd+json')

# compose.yaml reads this. up fills it in.
export LIVE_GOCD_PASSWORDS=''

password() { tr -d '\r\n' < "$state/gocd.password"; }

up() {
  if [[ ! -s $state/gocd.password ]]; then
    random_hex 16 > "$state/gocd.password"
  fi
  mask "$(password)"
  # The password file plugin reads user={SHA}base64(sha1(password)).
  local hash
  hash=$(printf '%s' "$(password)" | openssl dgst -sha1 -binary | openssl base64 -A | tr -d '\r\n')
  LIVE_GOCD_PASSWORDS="$user={SHA}$hash"
  compose pull --quiet
  compose up --detach
}

provision() {
  [[ -s $state/gocd.password ]] || fail "run up first"
  mask "$(password)"
  basic=(--user "$user:$(password)")
  wait_for 300 "GoCD to be healthy" healthy

  # While security is off every caller is an administrator, so the first call switches it on.
  local v2=(-H 'Accept: application/vnd.go.cd.v2+json')
  case $(status "${v2[@]}" "$server/go/api/admin/security/auth_configs/$user") in
    404)
      printf '{"id":"%s","plugin_id":"cd.go.authentication.passwordfile","properties":[{"key":"PasswordFilePath","value":"/run/secrets/gocd_passwords"}]}' "$user" \
        | fetch "${v2[@]}" -H 'Content-Type: application/json' --data-binary @- \
            "$server/go/api/admin/security/auth_configs" > /dev/null
      say "security switched on"
      ;;
    401|403) ;;
    *) fail "unexpected answer while switching security on" ;;
  esac
  wait_for 120 "the password to sign in" signed_in

  local v11=(-H 'Accept: application/vnd.go.cd.v11+json')
  if [[ $(status "${basic[@]}" "${v11[@]}" "$server/go/api/admin/pipelines/$pipeline") == 404 ]]; then
    fetch "${basic[@]}" "${v11[@]}" -H 'Content-Type: application/json' --data-binary @- \
      "$server/go/api/admin/pipelines" < "$here/$pipeline.json" > /dev/null
    say "created $pipeline"
  fi

  wait_for 300 "the agent to register" registered
  if ! grep -q '"agent_config_state" *: *"Enabled"' <<< "$(agents)"; then
    printf '{"uuids":["%s"],"agent_config_state":"Enabled"}' "$uuid" \
      | fetch "${basic[@]}" -X PATCH -H 'Accept: application/vnd.go.cd.v7+json' -H 'Content-Type: application/json' \
          --data-binary @- "$server/go/api/agents" > /dev/null
    say "agent enabled"
  fi

  # An access token cannot create another, so the password does.
  token=$(printf '{"description":"%s"}' "$pipeline" \
    | fetch "${basic[@]}" -H 'Accept: application/vnd.go.cd.v1+json' -H 'Content-Type: application/json' \
        --data-binary @- "$server/go/api/current_user/access_tokens" | field token)
  [[ -n $token ]] || fail "no access token"
  mask "$token"
  bearer=(-H "Authorization: Bearer $token")

  # The first poll of the material normally starts the first run.
  if ! poll 150 has_run; then
    say "scheduling $pipeline"
    fetch "${bearer[@]}" "${latest[@]}" -H 'X-GoCD-Confirm: true' -H 'Content-Type: application/json' --data '{}' \
      "$server/go/api/pipelines/$pipeline/schedule" > /dev/null
  fi

  wait_for 480 "a failed $pipeline run" has_failed
  # The dashboard answers 202 until its cache is built, and discovery reads it.
  wait_for 180 "the dashboard to list $pipeline" dashboard_ready
  export_env \
    "BUILDMONITOR_GOCD_SERVER=$server" \
    "BUILDMONITOR_GOCD_USER=$user" \
    "BUILDMONITOR_GOCD_TOKEN=$token" \
    "BUILDMONITOR_GOCD_PIPELINE=$pipeline"
}

healthy() { [[ $(status "$server/go/api/v1/health") == 200 ]]; }

signed_in() { [[ $(status "${basic[@]}" "${latest[@]}" "$server/go/api/current_user") == 200 ]]; }

agents() { fetch "${basic[@]}" -H 'Accept: application/vnd.go.cd.v7+json' "$server/go/api/agents" 2> /dev/null; }

registered() {
  uuid=$(agents | field uuid)
  [[ -n $uuid ]]
}

history() { fetch "${bearer[@]}" "${latest[@]}" "$server/go/api/pipelines/$pipeline/history?page_size=10" 2> /dev/null; }

has_run() { grep -q '"counter"' <<< "$(history)"; }

has_failed() { history | tr -d '\r\n' | grep -q '"result" *: *"Failed"'; }

dashboard_ready() {
  local answer
  answer=$(curl -sS --max-time 30 -w '\n%{http_code}' "${bearer[@]}" "${latest[@]}" "$server/go/api/dashboard") || return 1
  [[ ${answer##*$'\n'} == 200 && $answer == *"\"$pipeline\""* ]]
}

main "$@"
