#!/usr/bin/env bash
# TeamCity for the live tests: bash provision.sh [up] [provision] [logs] [down]. See ../lib.sh.
source "$(dirname "${BASH_SOURCE[0]}")/../lib.sh"

server=http://127.0.0.1:${BUILDMONITOR_TEAMCITY_PORT:-8111}
user=buildmonitor
project=BuildMonitorLive
config=BuildMonitorLive_Sandbox
agent=buildmonitor-agent
json=(-H 'Accept: application/json' -H 'Content-Type: application/json')

up() {
  compose pull --quiet
  compose up --detach
}

provision() {
  wait_for 600 "TeamCity to answer" answering
  # The server prints a one-off super user token, used with an empty user name.
  wait_for 180 "the super user token" read_super
  mask "$super"
  root=(--user ":$super")
  wait_for 600 "TeamCity to start" started

  # Roles are only assigned once per project permissions are on.
  local settings
  settings=$(fetch "${root[@]}" "${json[@]}" "$server/app/rest/server/authSettings")
  if ! grep -q '"perProjectPermissions" *: *true' <<< "$settings"; then
    sed -E 's/("perProjectPermissions" *: *)false/\1true/' <<< "$settings" \
      | fetch "${root[@]}" "${json[@]}" -X PUT --data-binary @- "$server/app/rest/server/authSettings" > /dev/null
  fi

  # A new password every time, since only its token outlives this step.
  local password
  password=$(random_hex 16)
  mask "$password"
  if [[ $(status "${root[@]}" "$server/app/rest/users/username:$user") == 404 ]]; then
    printf '{"username":"%s","password":"%s","roles":{"role":[{"roleId":"SYSTEM_ADMIN","scope":"g"}]}}' "$user" "$password" \
      | fetch "${root[@]}" "${json[@]}" --data-binary @- "$server/app/rest/users" > /dev/null
    say "created $user"
  else
    printf '%s' "$password" \
      | fetch "${root[@]}" -X PUT -H 'Content-Type: text/plain' --data-binary @- \
          "$server/app/rest/users/username:$user/password" > /dev/null
  fi

  # A token's value is only shown when it is created, so an existing one is replaced.
  local basic=(--user "$user:$password")
  status "${basic[@]}" -X DELETE "$server/app/rest/users/current/tokens/buildmonitor-live" > /dev/null
  token=$(printf '{"name":"buildmonitor-live"}' \
    | fetch "${basic[@]}" "${json[@]}" --data-binary @- "$server/app/rest/users/current/tokens" | field value)
  [[ -n $token ]] || fail "no access token"
  mask "$token"
  bearer=(-H "Authorization: Bearer $token")

  if [[ $(status "${bearer[@]}" "$server/app/rest/projects/id:$project") == 404 ]]; then
    post /app/rest/projects "{\"id\":\"$project\",\"name\":\"BuildMonitor Live\",\"parentProject\":{\"id\":\"_Root\"}}" > /dev/null
    say "created $project"
  fi

  if [[ $(status "${bearer[@]}" "$server/app/rest/buildTypes/id:$config") == 404 ]]; then
    post /app/rest/buildTypes "{\"id\":\"$config\",\"name\":\"buildmonitor-live\",\"project\":{\"id\":\"$project\"}}" > /dev/null
    post "/app/rest/buildTypes/id:$config/steps" \
      '{"name":"Fail","type":"simpleRunner","properties":{"property":[{"name":"script.content","value":"echo \"BuildMonitor live test\"\nsleep 60\nexit 1"},{"name":"use.custom.script","value":"true"}]}}' > /dev/null
    printf true \
      | fetch "${bearer[@]}" -X PUT -H 'Content-Type: text/plain' -H 'Accept: text/plain' --data-binary @- \
          "$server/app/rest/buildTypes/id:$config/settings/shouldFailBuildOnBadExitCode" > /dev/null
    say "created $config"
  fi

  wait_for 300 "the agent to register" agent_listed
  if ! grep -q '"authorized" *: *true' <<< "$(agent_json)"; then
    printf true \
      | fetch "${bearer[@]}" -X PUT -H 'Content-Type: text/plain' -H 'Accept: text/plain' --data-binary @- \
          "$server/app/rest/agents/name:$agent/authorized" > /dev/null
    say "agent authorized"
  fi

  # A new agent restarts once to take the server's plugins.
  wait_for 600 "the agent to be ready" agent_ready

  local failed="/app/rest/builds?locator=buildType:(id:$config),state:finished,status:FAILURE,count:1&fields=count"
  if ! grep -q '"count" *: *1' <<< "$(fetch "${bearer[@]}" "${json[@]}" "$server$failed")"; then
    build=$(post /app/rest/buildQueue "{\"buildType\":{\"id\":\"$config\"}}" | field id)
    [[ -n $build ]] || fail "no build queued"
    say "queued build $build"
    wait_for 600 "build $build to finish" finished
    grep -q '"status" *: *"FAILURE"' <<< "$(fetch "${bearer[@]}" "${json[@]}" "$server/app/rest/builds/id:$build?fields=status")" ||
      fail "build $build did not fail"
  fi

  export_env \
    "BUILDMONITOR_TEAMCITY_SERVER=$server" \
    "BUILDMONITOR_TEAMCITY_TOKEN=$token" \
    "BUILDMONITOR_TEAMCITY_SCOPE_PROJECT=$project" \
    "BUILDMONITOR_TEAMCITY_PIPELINE=$config"
}

# The web app is up once REST asks for credentials.
answering() {
  case $(status "$server/app/rest/server/version") in
    200|401) return 0 ;;
    *) return 1 ;;
  esac
}

read_super() {
  super=$(compose logs --no-color server 2> /dev/null \
    | sed -n 's/.*Super user authentication token: \([0-9]*\).*/\1/p' \
    | tail -n 1)
  [[ -n $super ]]
}

started() { [[ $(status "${root[@]}" "$server/app/rest/server") == 200 ]]; }

post() { printf '%s' "$2" | fetch "${bearer[@]}" "${json[@]}" --data-binary @- "$server$1"; }

agent_json() {
  fetch "${bearer[@]}" "${json[@]}" \
    "$server/app/rest/agents?locator=authorized:any,name:$agent&fields=agent(name,authorized,connected,enabled,uptodate)" 2> /dev/null
}

agent_listed() { grep -q "\"name\" *: *\"$agent\"" <<< "$(agent_json)"; }

agent_ready() {
  local answer flag
  answer=$(agent_json) || return 1
  for flag in authorized connected enabled uptodate; do
    grep -q "\"$flag\" *: *true" <<< "$answer" || return 1
  done
}

finished() {
  grep -q '"state" *: *"finished"' \
    <<< "$(fetch "${bearer[@]}" "${json[@]}" "$server/app/rest/builds/id:$build?fields=state" 2> /dev/null)"
}

main "$@"
