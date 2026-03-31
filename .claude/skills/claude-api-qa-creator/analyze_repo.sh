---
name: claude-qa
description: "QA test {{SERVICE_NAME}} API endpoints against locally running services. Starts services if needed, fires curl requests, validates responses, and reports results."
tools: Read, Bash, Grep, Glob
---

# {{SERVICE_NAME}} QA

Test {{SERVICE_NAME}} API endpoints by firing real HTTP requests against locally running services. The user describes what they want tested in natural language.

## Step 1 — Ensure services are running

Run this single block to health-check and auto-start if needed:

```bash
HEALTHY=true
for PORT in {{PORTS_SPACE_SEPARATED}}; do
  CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:${PORT}{{HEALTH_ENDPOINT}}" 2>/dev/null)
  [ "$CODE" != "200" ] && HEALTHY=false
done

if [ "$HEALTHY" = "true" ]; then
  echo "All services healthy"
else
  echo "Starting services..."
  {{START_COMMAND}} &
  for i in $(seq 1 30); do
    sleep 2
    ALL_UP=true
    for PORT in {{PORTS_SPACE_SEPARATED}}; do
      CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:${PORT}{{HEALTH_ENDPOINT}}" 2>/dev/null)
      [ "$CODE" != "200" ] && ALL_UP=false
    done
    [ "$ALL_UP" = "true" ] && echo "All services healthy after ~$((i*2))s" && break
    [ "$i" = "30" ] && echo "TIMEOUT: services failed to start" && exit 1
  done
fi
```

## Step 2 — Discover endpoints

{{#IF_OPENAPI}}
Each service has OpenAPI docs that are the source of truth for routes, request bodies, response shapes, parameters, and status codes:

{{SERVICE_TABLE_WITH_SWAGGER_PATHS}}

**To find an endpoint:**

1. **Always regenerate first**: Run `{{SWAGGER_REGEN_CMD}}` before reading swagger docs. Routes and contracts shift frequently — stale docs will lead to wrong request shapes.
2. **Never read the full swagger/openapi file** — it's too large and wastes context. Instead, extract just the target endpoint with all referenced definitions resolved in one `jq` call:

```bash
SERVICE="{{DEFAULT_SERVICE}}"
ENDPOINT="/v1/example"  # adjust to target path

jq --arg path "$ENDPOINT" '. as $root |
  .paths[$path].post as $op |
  [ $op.parameters[]?.schema."$ref"? // empty,
    $op.responses[]?.schema."$ref"? // empty
  ] | map(select(type == "string")) | map(split("/") | last) | unique as $l1_refs |
  ($l1_refs | map(. as $r | {($r): $root.definitions[$r]}) | add // {}) as $l1_defs |
  [ $l1_defs | .. | ."$ref"? // empty ] | map(select(type == "string")) | map(split("/") | last) | unique |
  map(select(. as $r | $l1_refs | index($r) | not)) as $l2_refs |
  {
    endpoint: $path,
    parameters: $op.parameters,
    responses: ($op.responses | to_entries | map({(.key): .value.description})),
    definitions: (
      ($l1_refs + $l2_refs) | unique |
      map(. as $r | {($r): $root.definitions[$r]}) | add // {}
    )
  }
' "{{OPENAPI_FILE_PATH}}"
```

This returns one compact JSON object with: parameters (headers + body), response status codes, and all referenced request/response/model definitions resolved two levels deep. Change `.post` to `.get`, `.put`, `.delete` for other methods.

3. If you need to understand **which auth pattern** an endpoint uses, read the handler code — the swagger doc shows the headers but not which validation function is called internally.
4. **Check required fields** before constructing a request body. The swagger definition's `required` array tells you which fields must be present.
{{/IF_OPENAPI}}

{{#IF_NO_OPENAPI}}
This project does not have OpenAPI specs. Discover endpoints by reading route registration code:

```bash
# Find all route registrations
{{ROUTE_DISCOVERY_COMMAND}}
```

For each endpoint, read the handler to understand:
- Required request body fields
- Required headers (auth, content-type)
- Response shape and status codes
- Error response format
{{/IF_NO_OPENAPI}}

### Service routing

| Port | Service | {{#IF_OPENAPI}}Swagger{{/IF_OPENAPI}} |
|------|---------|{{#IF_OPENAPI}}---------{{/IF_OPENAPI}}|
{{SERVICE_ROUTING_TABLE_ROWS}}

### Testing all endpoints

When the user asks to test "all endpoints" or "everything":

1. **Enumerate all routes** across all services:
```bash
{{ENUMERATE_ALL_ROUTES_COMMAND}}
```

2. **Group by test strategy** — don't test endpoints one at a time. Batch them:
{{TEST_STRATEGY_GROUPS}}

3. **CRUD lifecycle pattern** — for any resource CRUD:
```
POST   /v1/{resource}       → capture ID from response
GET    /v1/{resource}/{id}   → verify 200
GET    /v1/{resource}        → verify 200 (list all)
PUT    /v1/{resource}/{id}   → update a field
DELETE /v1/{resource}/{id}   → clean up
```
Always clean up created resources to avoid polluting test data.

4. **External dependencies** — some endpoints depend on services outside {{SERVICE_NAME}}. If these return 403 or timeout, mark as "EXPECTED — external dependency" rather than treating as failures.

## Step 3 — Load test configuration

```bash
source {{ENV_FILE_PATH}}
```

All test credentials live in `{{ENV_FILE_PATH}}`.

{{ENV_IDENTITY_GROUPS_DESCRIPTION}}

If you need a var and aren't sure of the name, grep the file:
```bash
grep -i "{{ENV_GREP_HINTS}}" {{ENV_FILE_PATH}}
```

## Step 4 — Build and execute

### Helper functions

Before firing any requests, source the helpers file. This loads `{{ENV_FILE_PATH}}` and defines all reusable functions in one shot:

```bash
source .claude/skills/claude-qa/helpers.sh
```

All helpers communicate through temp files — not pipes or string variables. This avoids escaping issues with JWTs, nested JSON, and special characters. Everything is written under a single temp directory that is **automatically purged on exit** (pass, fail, or interrupt).

The helpers provide:
- **`qa_curl`**: Fires any request, captures `$QA_CODE` (status), `$QA_BODY` (response), `$QA_TIME_MS` (latency in ms). Response body is also written to a temp file for `qa_json` to read.
- **`qa_json`**: Extracts fields from the last response body (dot-notation). Reads from temp file, not shell variables.
- **`qa_assert`**: One-line pass/fail check with timing
- **`qa_validate_response`**: Validates expected fields exist in response body
- **`qa_token`**: Acquires a bearer token and **caches it** for the test run. If the token is still valid (based on `expires_in` minus a 60s safety margin), returns it from cache without a network call. Use this instead of calling the token endpoint directly.
- **`qa_cache_set`** / **`qa_cache_get`** / **`qa_cache_clear`**: General-purpose cache for any value that's expensive to acquire (tokens, created resource IDs, etc.). Values can have an optional TTL in seconds.
{{#IF_HMAC}}
- **`qa_hmac`**: Computes HMAC signature, sets `$QA_API_KEY`, `$QA_SIGNATURE`, `$QA_TIMESTAMP`, `$QA_KID`
{{/IF_HMAC}}

### Token caching

Do not re-acquire tokens for every request. Use `qa_token` with a cache key:
```bash
TOKEN=$(qa_token "soma_m2m" POST "http://localhost:{{TOKEN_PORT}}{{TOKEN_ENDPOINT}}" \
  "$TOKEN_REQUEST_BODY" \
  "Header1: value1" "Header2: value2")

# Subsequent calls with the same cache key return instantly from cache:
TOKEN=$(qa_token "soma_m2m" POST ...)  # no network call
```

If a test intentionally needs a fresh token (e.g., testing token expiry), clear the cache first:
```bash
qa_cache_clear "soma_m2m"
```

### Auth pattern catalog

{{AUTH_PATTERN_SECTIONS}}

### Chaining multi-step flows

When one endpoint's output feeds the next, use `qa_json` to extract values:
```bash
{{CHAINING_EXAMPLE}}
```

### Negative tests (run by default)

For every auth pattern exercised in the happy path, always run at least one negative test:

{{NEGATIVE_TEST_LIST}}

These are not optional — negative tests catch silent regressions where error paths return 200 with empty data.

### Idempotency checks

For create endpoints, test duplicate handling:
```bash
{{IDEMPOTENCY_EXAMPLE}}
```

### Response timing baseline

`qa_curl` captures `$QA_TIME_MS` on every request. After all tests, flag any endpoint over 500ms:

```bash
[ "$QA_TIME_MS" -gt 500 ] && echo "  WARNING: $label took ${QA_TIME_MS}ms"
```

{{TIMING_EXPECTATIONS}}

## Step 5 — Validate documentation matches code

{{#IF_OPENAPI}}
After testing, verify the {{ANNOTATION_TYPE}} on each handler match what the code actually does. Mismatches between annotations and code mean the generated API docs are incorrect.

For each endpoint tested, check:

1. **Response type**: Does the success annotation reference the same response struct the handler actually returns?
2. **Request type**: Does the request param annotation match the struct parsed in the handler?
3. **Status codes**: Do the failure annotations cover all error paths in the handler?
4. **Route path**: Does the annotated route match what's registered in the router? A documented route returning 404 is the strongest signal of a mismatch.
5. **Description accuracy**: Does the summary still describe what the handler does?

If any mismatch is found:
- Report it with the exact annotation line and what it should be
- Fix the annotation in the handler file
- Re-run `{{SWAGGER_REGEN_CMD}}` to regenerate
- Verify the updated spec reflects the fix
{{/IF_OPENAPI}}

{{#IF_NO_OPENAPI}}
No OpenAPI spec to validate. Instead, verify that error responses match a consistent format across all endpoints and that documented status codes in any README or wiki match actual behavior.
{{/IF_NO_OPENAPI}}

## Step 6 — Report results

1. Per request, report:
   - **Status**: HTTP code with PASS/FAIL
   - **Key fields**: Important values from response (truncate JWTs/tokens to 20 chars + `...`)
   - **Errors**: Full error body on non-2xx
2. For multi-step flows, end with a summary table
3. Categorize any failures:
   - **Bug**: Unexpected error from a correctly-formed request
   - **Swagger mismatch**: Route or schema doesn't match code
   - **External dependency**: Endpoint depends on a service outside {{SERVICE_NAME}}
   - **Test data**: Missing or stale test data in the database

## Step 7 — Self-evaluation

After all testing and reporting is complete, assess:

- **What worked well** — steps or patterns that were efficient and accurate
- **What caused friction** — specific issues with steps, recipes, or missing guidance
- **Suggestions** — concrete improvements to the skill, env file, test data, or codebase

## Troubleshooting

{{TROUBLESHOOTING_SECTIONS}}