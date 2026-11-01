#!/usr/bin/env bash
# Smoke test for a running Unified Service Scheduler API (for example `docker compose up -d`).
#
#   scripts/smoke-test.sh                      # API at http://localhost:5080, booking date = today + 7 days
#   BASE_URL=http://localhost:5080 DATE=2030-01-15 scripts/smoke-test.sh
#
# Needs: bash, curl, jq. Prints PASS/FAIL per step; exit code 1 if any step fails.
# It books on DATE and cancels what it booked, so it can be run again.
set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5080}"
CUSTOMER_ID=1
MAX_EXTRA_BOOKINGS=10

for tool in curl jq; do
  command -v "$tool" >/dev/null || { echo "This script needs '$tool'." >&2; exit 2; }
done

if [[ -z "${DATE:-}" ]]; then
  DATE="$(date -u -d '+7 days' +%F 2>/dev/null || date -u -v+7d +%F)"
fi

FAILURES=0
STATUS=""
BODY=""

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; FAILURES=$((FAILURES + 1)); }

# call METHOD PATH [JSON_BODY] [IDEMPOTENCY_KEY]  -> sets STATUS and BODY
call() {
  local method="$1" path="$2" data="${3:-}" key="${4:-}"
  local args=(-sS -X "$method" -H "Accept: application/json" -w $'\n%{http_code}')
  [[ -n "$data" ]] && args+=(-H "Content-Type: application/json" --data "$data")
  [[ -n "$key" ]] && args+=(-H "Idempotency-Key: $key")
  local out
  if ! out="$(curl "${args[@]}" "$BASE_URL$path" 2>&1)"; then
    STATUS="000"; BODY="$out"; return
  fi
  STATUS="${out##*$'\n'}"
  BODY="${out%$'\n'*}"
}

new_key() { cat /proc/sys/kernel/random/uuid 2>/dev/null || uuidgen 2>/dev/null || echo "smoke-$RANDOM-$RANDOM-$(date +%s)"; }

echo "Smoke test against $BASE_URL (booking date $DATE)"

# 1. Health
call GET /health/ready
[[ "$STATUS" == 200 ]] && pass "health/ready is 200" || fail "health/ready returned $STATUS"

# 2. Dealerships
call GET /api/dealerships
DEALERSHIP_ID="$(jq -r '.[0].id // empty' <<<"$BODY" 2>/dev/null)"
if [[ "$STATUS" == 200 && -n "$DEALERSHIP_ID" ]]; then
  pass "list dealerships ($(jq length <<<"$BODY") found, using id $DEALERSHIP_ID)"
else
  fail "list dealerships returned $STATUS"; echo "Cannot continue."; exit 1
fi

# Service type and vehicle for the booking.
call GET "/api/service-types?dealershipId=$DEALERSHIP_ID"
SERVICE_TYPE_ID="$(jq -r '.[0].id // empty' <<<"$BODY" 2>/dev/null)"
call GET "/api/customers/$CUSTOMER_ID/vehicles"
VEHICLE_ID="$(jq -r '.[0].id // empty' <<<"$BODY" 2>/dev/null)"
if [[ -z "$SERVICE_TYPE_ID" || -z "$VEHICLE_ID" ]]; then
  fail "load service type / vehicle (serviceTypeId='$SERVICE_TYPE_ID', vehicleId='$VEHICLE_ID')"; exit 1
fi

# 3. Availability
call GET "/api/dealerships/$DEALERSHIP_ID/availability?serviceTypeId=$SERVICE_TYPE_ID&date=$DATE"
START="$(jq -r '.slots[0].startUtc // empty' <<<"$BODY" 2>/dev/null)"
if [[ "$STATUS" == 200 && -n "$START" ]]; then
  pass "availability ($(jq '.slots | length' <<<"$BODY") free start times, using $START)"
else
  fail "availability returned $STATUS with no free slot"; exit 1
fi

REQUEST="$(jq -nc --argjson c "$CUSTOMER_ID" --argjson v "$VEHICLE_ID" --argjson d "$DEALERSHIP_ID" \
  --argjson s "$SERVICE_TYPE_ID" --arg t "$START" \
  '{customerId:$c, vehicleId:$v, dealershipId:$d, serviceTypeId:$s, startTime:$t}')"

# 4. Book
KEY="$(new_key)"
call POST /api/appointments "$REQUEST" "$KEY"
APPOINTMENT_ID="$(jq -r '.id // empty' <<<"$BODY" 2>/dev/null)"
if [[ "$STATUS" == 201 && -n "$APPOINTMENT_ID" ]]; then
  pass "book (201, appointment $APPOINTMENT_ID, technician $(jq -r .technician.name <<<"$BODY"), bay $(jq -r .serviceBay.name <<<"$BODY"))"
else
  fail "book returned $STATUS: $BODY"; exit 1
fi

# 5. Replay the same key + same body -> same appointment, no new booking
call POST /api/appointments "$REQUEST" "$KEY"
REPLAY_ID="$(jq -r '.id // empty' <<<"$BODY" 2>/dev/null)"
if [[ "$STATUS" == 201 && "$REPLAY_ID" == "$APPOINTMENT_ID" ]]; then
  pass "replay same Idempotency-Key (201, same appointment $REPLAY_ID)"
else
  fail "replay returned $STATUS with id '$REPLAY_ID' (expected 201 and $APPOINTMENT_ID)"
fi

# 6. Same slot, new keys -> 409 NO_AVAILABILITY once every technician/bay pair is taken
EXTRA_IDS=()
GOT_409=""
for ((i = 1; i <= MAX_EXTRA_BOOKINGS; i++)); do
  call POST /api/appointments "$REQUEST" "$(new_key)"
  if [[ "$STATUS" == 201 ]]; then
    EXTRA_IDS+=("$(jq -r .id <<<"$BODY")")   # another free pair existed; keep going
  else
    GOT_409="$STATUS"; break
  fi
done
CODE="$(jq -r '.code // empty' <<<"$BODY" 2>/dev/null)"
if [[ "$GOT_409" == 409 && "$CODE" == NO_AVAILABILITY ]]; then
  pass "same slot with a new key is 409 NO_AVAILABILITY (after ${#EXTRA_IDS[@]} extra booking(s); suggested: $(jq -c '.suggestedStartTimes' <<<"$BODY"))"
else
  fail "same slot with a new key returned ${GOT_409:-201 x$MAX_EXTRA_BOOKINGS} code '$CODE' (expected 409 NO_AVAILABILITY)"
fi

# 7. Cancel
call POST "/api/appointments/$APPOINTMENT_ID/cancel"
if [[ "$STATUS" == 200 && "$(jq -r .status <<<"$BODY")" == Cancelled ]]; then
  pass "cancel appointment $APPOINTMENT_ID (200, Cancelled)"
else
  fail "cancel returned $STATUS: $BODY"
fi

# Clean up the extra bookings from step 6.
for id in "${EXTRA_IDS[@]}"; do
  call POST "/api/appointments/$id/cancel"
  [[ "$STATUS" == 200 ]] || fail "clean-up cancel of appointment $id returned $STATUS"
done

if ((FAILURES == 0)); then
  echo "ALL PASSED"
else
  echo "$FAILURES step(s) FAILED"
  exit 1
fi
