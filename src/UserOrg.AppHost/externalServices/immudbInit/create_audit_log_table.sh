#!/bin/sh
set -eu

# immugw answers every refusal - a bad login, an expired token, a rejected statement - with HTTP 500
# and a JSON body, so each step is judged by what came back rather than by curl's exit code.
# The Authorization header carries the raw token; a "Bearer " prefix makes immugw answer "Please login".

token_of() {
  sed -n 's/.*"token":"\([^"]*\)".*/\1/p'
}

login() {
  user=$(printf '%s' "$IMMUDB_USER" | base64 | tr -d '\n')
  password=$(printf '%s' "$IMMUDB_PASSWORD" | base64 | tr -d '\n')

  curl -sS -X POST "$IMMUGW_URL/login" \
    -d "{\"user\":\"$user\",\"password\":\"$password\"}" | token_of
}

# immugw accepts connections before immudb has finished opening its databases, and Aspire has no
# readiness signal to wait on, so the first step is retried instead of failing the whole chain.
attempt=1
login_token=$(login || true)
while [ -z "$login_token" ]; do
  if [ "$attempt" -ge 30 ]; then
    echo "immugw did not accept a login for $IMMUDB_USER after $attempt attempts"
    exit 1
  fi
  attempt=$((attempt + 1))
  sleep 2
  login_token=$(login || true)
done

database_token=$(curl -sS -H "Authorization: $login_token" "$IMMUGW_URL/db/use/$IMMUDB_DATABASE" | token_of)
[ -n "$database_token" ] || { echo "immugw did not open database $IMMUDB_DATABASE"; exit 1; }

# old_values, new_values and metadata are JSON, not VARCHAR: immudb validates the document on the way
# in, so a half-written one is refused instead of becoming an unparseable row in an append-only
# archive. The VARCHAR widths are byte widths and they mirror AuditSqlSealer's own constants exactly.
answer=$(curl -sS -X POST -H "Authorization: $database_token" "$IMMUGW_URL/db/$IMMUDB_DATABASE/sqlexec" -d '{
  "sql": "CREATE TABLE IF NOT EXISTS audit_log (id INTEGER AUTO_INCREMENT, time_stamp TIMESTAMP, module_name VARCHAR[64], version VARCHAR[32], user_id VARCHAR[128], activity VARCHAR[128], object_type VARCHAR[128], object_id VARCHAR[128], old_values JSON, new_values JSON, ip_address VARCHAR[45], status VARCHAR[16], correlation_id VARCHAR[64], metadata JSON, PRIMARY KEY id)"
}')

echo "$answer" | grep -q '"txs"' || { echo "immugw refused to create audit_log: $answer"; exit 1; }

echo "audit_log is ready in $IMMUDB_DATABASE"
