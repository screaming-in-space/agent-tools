#!/usr/bin/env bash
# Generate portable QA helper scripts in the target skill directory.
#
# Usage: source .claude/skills/claude-api-qa-creator/generate_helpers.sh TARGET_DIR ENV_FILE_PATH [PREFIX]
#   TARGET_DIR     - where to write the helpers (e.g., .claude/skills/claude-qa)
#   ENV_FILE_PATH  - relative path to the test env file (e.g., src/.env.test)
#   PREFIX         - function/var prefix (default: "qa"). Use this to avoid collisions.
#
# Creates: helpers.sh, qa_json.py, qa_validate.py, qa_pretty.py

TARGET_DIR="${1:-.claude/skills/claude-qa}"
ENV_FILE="${2:-src/.env.test}"
PREFIX="${3:-qa}"

# Uppercase prefix for variable names
UPREFIX=$(echo "$PREFIX" | tr '[:lower:]' '[:upper:]')

mkdir -p "$TARGET_DIR"

# -------------------------------------------------------------------
# helpers.sh
# -------------------------------------------------------------------
cat > "${TARGET_DIR}/helpers.sh" << 'HELPERS_EOF'
#!/usr/bin/env bash
# QA helper functions — source this before running tests.
# Usage: source .claude/skills/claude-qa/helpers.sh
#
# DESIGN: All inter-function communication uses temp files, not pipes or
# string juggling. This avoids escaping issues with JWTs, JSON payloads,
# and multiline responses. Every curl response, metadata value, and cached
# artifact is written to a file under XINTERNAL_TMP, which is purged on exit.
#
# LAYOUT of XINTERNAL_TMP:
#   body.json     — last curl response body (overwritten each request)
#   meta.txt      — last curl status code + timing (overwritten each request)
#   cache/        — cached artifacts (tokens, IDs) that persist across requests
#     <key>.val   — cached value
#     <key>.exp   — expiry timestamp (epoch seconds)

source __ENV_FILE__

# Resolve skill directory (where the .py helpers live) — works in both bash and zsh
XINTERNAL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-${(%):-%x}}")" && pwd)"

# Scratch directory for temp files — ALL cleanup happens here
XINTERNAL_TMP=$(mktemp -d)
mkdir -p "${XINTERNAL_TMP}/cache"
trap 'rm -rf "$XINTERNAL_TMP"' EXIT

# =====================================================================
# CACHE — store and retrieve values that are expensive to acquire
# =====================================================================

# --- __PREFIX___cache_set: store a value with optional TTL ---
# Usage: __PREFIX___cache_set KEY VALUE [TTL_SECONDS]
# If TTL is omitted, the value persists for the entire test run.
__PREFIX___cache_set() {
  local key="$1" value="$2" ttl="${3:-0}"
  echo -n "$value" > "${XINTERNAL_TMP}/cache/${key}.val"
  if [ "$ttl" -gt 0 ] 2>/dev/null; then
    echo -n "$(( $(date +%s) + ttl ))" > "${XINTERNAL_TMP}/cache/${key}.exp"
  else
    rm -f "${XINTERNAL_TMP}/cache/${key}.exp"
  fi
}

# --- __PREFIX___cache_get: retrieve a cached value ---
# Usage: __PREFIX___cache_get KEY
# Prints the value to stdout. Returns 1 if missing or expired.
__PREFIX___cache_get() {
  local key="$1"
  local valfile="${XINTERNAL_TMP}/cache/${key}.val"
  local expfile="${XINTERNAL_TMP}/cache/${key}.exp"
  [ ! -f "$valfile" ] && return 1
  # Check expiry if set
  if [ -f "$expfile" ]; then
    local exp=$(<"$expfile")
    local now=$(date +%s)
    if [ "$now" -ge "$exp" ]; then
      rm -f "$valfile" "$expfile"
      return 1
    fi
  fi
  cat "$valfile"
}

# --- __PREFIX___cache_clear: remove a specific cached value or all ---
# Usage: __PREFIX___cache_clear [KEY]
# Without KEY, clears the entire cache.
__PREFIX___cache_clear() {
  if [ -n "${1:-}" ]; then
    rm -f "${XINTERNAL_TMP}/cache/${1}.val" "${XINTERNAL_TMP}/cache/${1}.exp"
  else
    rm -f "${XINTERNAL_TMP}/cache/"*.val "${XINTERNAL_TMP}/cache/"*.exp
  fi
}

# =====================================================================
# HMAC — compute signatures for HMAC-authenticated endpoints
# =====================================================================

# --- __PREFIX___hmac: compute HMAC-SHA256 signature and set header vars ---
# Usage: __PREFIX___hmac VENDOR_ID CUSTOMER_ID PRODUCT_ID API_KEY HMAC_SECRET_B64 HMAC_KID METHOD URL_PATH
__PREFIX___hmac() {
  __UPREFIX___API_KEY="$4"
  __UPREFIX___KID="$6"
  __UPREFIX___TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  local decoded=$(echo -n "$5" | base64 -d)
  local canonical="$1|$2|$3|$7|$8|${__UPREFIX___TIMESTAMP}"
  __UPREFIX___SIGNATURE=$(echo -n "$canonical" | openssl dgst -sha256 -hmac "$decoded" -hex 2>/dev/null | awk '{print $NF}')
}

# =====================================================================
# HTTP — fire requests, all I/O through temp files
# =====================================================================

# --- __PREFIX___curl: fire request, capture status + body + timing ---
# Usage: __PREFIX___curl METHOD URL [BODY] [EXTRA_HEADERS...]
# Sets $__UPREFIX___CODE, $__UPREFIX___BODY, $__UPREFIX___TIME_MS
# Response body written to XINTERNAL_TMP/body.json (used by __PREFIX___json)
# Metadata written to XINTERNAL_TMP/meta.txt
__PREFIX___curl() {
  local method="$1" url="$2" body="${3:-}"
  shift 2; [ $# -gt 0 ] && shift
  local headers=(-H "Content-Type: application/json")
  for h in "$@"; do headers+=(-H "$h"); done
  local body_arg=()
  [ -n "$body" ] && body_arg=(-d "$body")
  # Write body to file, metadata (status + timing) to separate file
  curl -s -o "${XINTERNAL_TMP}/body.json" -w "%{http_code} %{time_total}" \
    -X "$method" "$url" "${headers[@]}" "${body_arg[@]}" > "${XINTERNAL_TMP}/meta.txt"
  __UPREFIX___BODY=$(<"${XINTERNAL_TMP}/body.json")
  local meta=$(<"${XINTERNAL_TMP}/meta.txt")
  __UPREFIX___CODE=$(echo "$meta" | awk '{print $1}')
  __UPREFIX___TIME_MS=$(echo "$meta" | awk '{printf "%.0f", $2 * 1000}')
}

# =====================================================================
# TOKEN — acquire and cache bearer tokens
# =====================================================================

# --- __PREFIX___token: get a cached token or acquire a new one ---
# Usage: __PREFIX___token CACHE_KEY TOKEN_ENDPOINT BODY HEADER1 HEADER2 ...
# Returns the access_token value. Caches it using expires_in from the response.
# If cached and not expired, returns immediately without a network call.
__PREFIX___token() {
  local cache_key="$1"; shift
  # Try cache first
  local cached
  cached=$(__PREFIX___cache_get "$cache_key" 2>/dev/null)
  if [ $? -eq 0 ] && [ -n "$cached" ]; then
    echo "$cached"
    return 0
  fi
  # Cache miss — acquire a new token
  __PREFIX___curl "$@"
  if [ "$__UPREFIX___CODE" = "200" ]; then
    local token expires_in
    token=$(__PREFIX___json "access_token")
    expires_in=$(__PREFIX___json "expires_in")
    # Cache with a 60-second safety margin
    local ttl=${expires_in:-3600}
    [ "$ttl" -gt 60 ] && ttl=$((ttl - 60))
    __PREFIX___cache_set "$cache_key" "$token" "$ttl"
    echo "$token"
  else
    echo "ERROR: token acquisition failed (HTTP $__UPREFIX___CODE)" >&2
    return 1
  fi
}

# =====================================================================
# ASSERTIONS — validate responses via temp files
# =====================================================================

# --- __PREFIX___json: extract a dot-notation field from the last response ---
# Usage: __PREFIX___json "access_token" or __PREFIX___json "app_config.aws.key"
# Reads from XINTERNAL_TMP/body.json (written by __PREFIX___curl)
__PREFIX___json() {
  python3 "${XINTERNAL_DIR}/__PREFIX___json.py" "$XINTERNAL_TMP" "$1"
}

# --- __PREFIX___assert: check expected status code ---
# Usage: __PREFIX___assert LABEL EXPECTED_CODE
__PREFIX___assert() {
  local label="$1" expected="$2"
  if [ "$__UPREFIX___CODE" = "$expected" ]; then
    echo "PASS  $label -> $__UPREFIX___CODE (${__UPREFIX___TIME_MS}ms)"
  else
    echo "FAIL  $label -> $__UPREFIX___CODE (expected $expected, ${__UPREFIX___TIME_MS}ms)"
    head -3 "${XINTERNAL_TMP}/body.json" | sed 's/^/      /'
  fi
}

# --- __PREFIX___validate_response: check response body has required fields ---
# Usage: __PREFIX___validate_response LABEL FIELD1 FIELD2 ...
# Reads from XINTERNAL_TMP/body.json
__PREFIX___validate_response() {
  python3 "${XINTERNAL_DIR}/__PREFIX___validate.py" "$XINTERNAL_TMP" "$@"
}

# --- __PREFIX___pretty: pretty-print last response, truncating tokens ---
__PREFIX___pretty() {
  python3 "${XINTERNAL_DIR}/__PREFIX___pretty.py" "$XINTERNAL_TMP"
}
HELPERS_EOF

# Apply prefix and env path substitutions (macOS-compatible sed -i)
sed -i '' "s|__ENV_FILE__|${ENV_FILE}|g;s|__PREFIX__|${PREFIX}|g;s|__UPREFIX__|${UPREFIX}|g;s|XINTERNAL_|___${UPREFIX}_|g" \
  "${TARGET_DIR}/helpers.sh"
chmod +x "${TARGET_DIR}/helpers.sh"

# -------------------------------------------------------------------
# qa_json.py
# -------------------------------------------------------------------
cat > "${TARGET_DIR}/${PREFIX}_json.py" << 'PYJSON_EOF'
"""Extract a dot-notation field from body.json in the QA scratch dir."""
import json, sys

tmp_dir, path = sys.argv[1], sys.argv[2]
with open(f"{tmp_dir}/body.json") as f:
    d = json.load(f)
try:
    v = d
    for k in path.split('.'):
        v = v[k]
    print(v if not isinstance(v, (dict, list)) else json.dumps(v))
except:
    pass
PYJSON_EOF

# -------------------------------------------------------------------
# qa_validate.py
# -------------------------------------------------------------------
cat > "${TARGET_DIR}/${PREFIX}_validate.py" << 'PYVAL_EOF'
"""Validate expected fields exist in body.json in the QA scratch dir."""
import json, sys

tmp_dir, label = sys.argv[1], sys.argv[2]
fields = sys.argv[3:]
with open(f"{tmp_dir}/body.json") as f:
    d = json.load(f)
missing = []
for field in fields:
    v = d
    for k in field.split('.'):
        if isinstance(v, dict) and k in v:
            v = v[k]
        else:
            v = None
            break
    if v is None:
        missing.append(field)
if not missing:
    print(f"  SCHEMA OK  {label} — all expected fields present")
else:
    print(f"  SCHEMA FAIL  {label} — missing: {' '.join(missing)}")
PYVAL_EOF

# -------------------------------------------------------------------
# qa_pretty.py
# -------------------------------------------------------------------
cat > "${TARGET_DIR}/${PREFIX}_pretty.py" << 'PYPRETTY_EOF'
"""Pretty-print body.json from the QA scratch dir, truncating JWTs/tokens."""
import json, sys

tmp_dir = sys.argv[1]
with open(f"{tmp_dir}/body.json") as f:
    d = json.load(f)

def truncate(obj):
    if isinstance(obj, dict):
        return {k: truncate(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return [truncate(v) for v in obj]
    if isinstance(obj, str) and len(obj) > 80 and obj.startswith('eyJ'):
        return obj[:20] + '...'
    return obj

print(json.dumps(truncate(d), indent=2))
PYPRETTY_EOF

echo "Generated QA helpers in ${TARGET_DIR}/"
echo "  helpers.sh          — bash curl wrapper, assertions, HMAC"
echo "  ${PREFIX}_json.py      — JSON field extraction"
echo "  ${PREFIX}_validate.py  — response schema validation"
echo "  ${PREFIX}_pretty.py    — pretty-print with token truncation"
echo ""
echo "Functions use prefix '${PREFIX}_' (vars: \$${UPREFIX}_CODE, \$${UPREFIX}_BODY, \$${UPREFIX}_TIME_MS)"