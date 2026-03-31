---
name: claude-api-qa-creator
description: "Create a /claude-qa skill for any Web API repo. Use when the user wants to scaffold a QA testing skill for an API project, build an automated API test harness, or generate a claude-qa skill for a new codebase."
tools: Read, Write, Edit, Bash, Glob, Grep, Agent
---

# API QA Skill Creator

Generate a complete `/claude-qa` skill for any Web API repository. This is a meta-skill — it analyzes a target repo, discovers its API surface, auth patterns, and test infrastructure, then produces a tailored QA skill that can test that API.

## When to ask the developer

Do not guess when information is ambiguous or missing. Instead, ask the developer directly. This applies throughout every phase — if you hit any of these situations, stop and ask before proceeding:

- **Auth patterns are unclear**: You can see auth-related code but can't determine which endpoints use which pattern, or what the credential flow is. Ask: "I see HMAC and Bearer auth in the codebase — which endpoints use which, and where do the test credentials come from?"
- **No test env file exists**: There's no `.env.test` or equivalent. Ask: "I don't see a test env file. What credentials and config values do I need for testing, and where should I create the file?"
- **Multiple possible start commands**: The repo has docker-compose, Makefile targets, and npm scripts. Ask: "Which start command should the QA skill use to bring up the services locally?"
- **Unclear service boundaries**: You can't tell which services are API services vs. background workers vs. CLI tools. Ask: "I found N entry points — which of these are the API services I should test?"
- **Unknown health check endpoint**: The analysis can't find one. Ask: "What health check endpoint should I use to verify services are running?"
- **External dependencies**: You see calls to third-party APIs but don't know if they're available locally. Ask: "Does [service] have a local mock or sandbox, or should I mark those endpoints as expected failures?"
- **Custom error format**: The error response shape isn't standard (not Problem Details, not a common envelope). Ask: "What does your error response format look like? I need this to write accurate assertions."
- **Test data setup**: You need seed data in a database but don't know how it's provisioned. Ask: "How is test data loaded — migrations, seed scripts, fixtures? What IDs should I use in test requests?"

The goal is a skill that works on the first run. A wrong guess baked into the generated SKILL.md is worse than a 30-second conversation to get it right.

## What you are building

The output is a `.claude/skills/claude-qa/` directory in the target repo containing:

```
claude-qa/
├── SKILL.md          # The QA skill prompt — tailored to this repo
├── helpers.sh        # Bash helpers: curl wrapper, assertions, HMAC, env loading
├── qa_json.py        # Extract fields from JSON responses (dot-notation)
├── qa_validate.py    # Validate expected fields in responses
└── qa_pretty.py      # Pretty-print responses, truncating tokens
```

The generated SKILL.md is the brain — it teaches Claude how to test *this specific API*. The helper scripts are portable across any API.

## When a `/claude-qa` skill already exists

**WARNING: An existing `/claude-qa` skill may represent significant investment — manual tuning, edge-case fixes, developer-authored troubleshooting sections, and institutional knowledge that cannot be automatically rediscovered. Overwriting it destroys that work permanently. Always default to reconciliation, never regeneration, unless the developer explicitly approves.**

Before starting, check if `.claude/skills/claude-qa/` already exists in the target repo. If it does, switch to **reconciliation mode** — do not overwrite blindly. The existing skill may contain hard-won knowledge from previous runs, manual tuning by the developer, or fixes for edge cases you won't rediscover automatically.

### Reconciliation workflow

1. **Read the existing SKILL.md fully.** Understand what it already covers: which endpoints, auth patterns, env vars, troubleshooting sections, and test strategies are documented.

2. **Run Phase 1 (repo analysis) as normal.** Compare what the analysis discovers against what the existing skill documents:
   - **New endpoints** — routes added since the skill was written (compare swagger/route registrations against the skill's endpoint lists)
   - **Removed endpoints** — routes the skill tests that no longer exist
   - **Changed auth patterns** — handlers whose auth middleware changed (e.g., switched from vendor key to product key)
   - **New env vars** — credentials or config added to the test env file that the skill doesn't reference
   - **Stale env vars** — vars the skill references that no longer exist
   - **New services** — new API services added to the repo
   - **Changed start commands** — Makefile targets, docker-compose, or IDE configs that have shifted

3. **Read the existing helpers.** Check if `helpers.sh` and the Python scripts are still compatible. If the helper generator has been updated with new features (e.g., new assertion functions), note what's missing.

4. **Present a diff summary to the developer.** Before changing anything, report:
   - What's **new** in the repo that the skill doesn't cover
   - What's **stale** in the skill that no longer matches the repo
   - What's **still accurate** and should be preserved
   - What **custom additions** the developer made that should not be overwritten (look for content that doesn't match the template patterns — custom troubleshooting sections, hand-written examples, repo-specific notes)

5. **Propose changes, don't apply them.** Present the changes as a plan:
   - Sections to add (new endpoints, new auth patterns)
   - Sections to update (changed routes, renamed env vars)
   - Sections to remove (deleted endpoints, obsolete troubleshooting)
   - Helper scripts to regenerate (if the template has improved)

6. **Apply only after developer approval.** Make targeted edits to the existing SKILL.md rather than regenerating from scratch. Preserve any custom content the developer added.

### When to regenerate from scratch

Only regenerate the entire skill if:
- The developer explicitly asks for a full rebuild
- The existing skill is so outdated that patching would be more work than rewriting (e.g., the repo was restructured, framework changed, or majority of endpoints are different)
- The helper scripts are from an incompatible version (missing core functions like `qa_curl` or `qa_assert`)

In these cases, tell the developer you're recommending a full regeneration and why, then proceed with the standard workflow below.

## Workflow

Follow these phases in order. Each phase produces artifacts that feed the next.

---

### Phase 1 — Repo analysis

Before generating anything, you must understand the target repo. Run the analysis script to collect structured facts:

```bash
source .claude/skills/claude-api-qa-creator/analyze_repo.sh
```

This sets environment variables describing what was found. If the script can't determine something, it will print `UNKNOWN` — you must investigate manually.

After the script runs, verify and supplement its findings by reading code directly. The script finds *what exists*; you must understand *how it works*.

#### What to investigate

1. **API framework and router**
   - What HTTP framework? (Chi, Gin, Echo, Express, FastAPI, Spring, etc.)
   - How are routes registered? (file pattern, central router, decorators)
   - Where is `main.go` / `app.py` / `index.ts` / etc.?

2. **OpenAPI / Swagger**
   - Are there swagger/OpenAPI spec files? Where?
   - Are they auto-generated (swag, swagger-jsdoc) or hand-written?
   - What command regenerates them? (check Makefile, package.json scripts)

3. **Services and ports**
   - How many services? What ports?
   - How to start them locally? Not every team uses Make. Check in order:
     - `Makefile` (make run, make dev)
     - `package.json` scripts (npm start, npm run dev)
     - `pyproject.toml` with poethepoet tasks, `[project.scripts]` entry points, or `uv run`
     - `docker-compose.yml` / `compose.yml`
     - `scripts/` or `bin/` directories for standalone start scripts
     - **IDE launch configs** — these are often the most accurate source for how developers actually run services:
       - `.vscode/launch.json`, `.vscode/tasks.json`
       - `.idea/runConfigurations/*.xml`, `.run/*.xml`
       - `.vs/launchSettings.json`, `Properties/launchSettings.json`
     - The analysis script checks all of these automatically
   - Is there a health check endpoint?

4. **Authentication patterns**
   - Read 2-3 handler files to identify auth patterns
   - Common patterns: API key header, HMAC signature, Bearer/JWT, session cookie, OAuth2, none
   - Note which middleware or helper function enforces each pattern
   - Note which endpoints use which pattern

5. **Test configuration**
   - Is there an `.env.test`, `.env.local`, `test.env`, or similar?
   - Are test credentials checked in or generated?
   - What external dependencies exist? (databases, Redis, third-party APIs)

6. **Request/response conventions**
   - What's the error response shape? (Problem Details RFC 7807, custom envelope, plain message)
   - Are responses wrapped in an envelope? (`{data: ..., error: ...}`)
   - What content type? (JSON assumed, but check for GraphQL, protobuf, etc.)

Record your findings — you'll encode them directly into the generated SKILL.md.

---

### Phase 2 — Generate helper scripts

The helper scripts are portable. They use a **temp-file-first architecture** — all inter-function communication goes through files, never pipes or string juggling. This matters because API responses contain JWTs, nested JSON, and special characters that break when passed through shell variables or pipes.

**Temp file layout** (all under a single `mktemp -d`, purged on exit via `trap`):
```
___QA_TMP/
├── body.json     # Last curl response body (overwritten each request)
├── meta.txt      # Last curl status + timing (overwritten each request)
└── cache/        # Persistent across requests for the test run
    ├── <key>.val # Cached value (e.g., a JWT token)
    └── <key>.exp # Expiry timestamp (epoch seconds)
```

**Caching**: The helpers include `qa_cache_set`, `qa_cache_get`, `qa_cache_clear`, and `qa_token`. Tokens and other expensive-to-acquire values should be cached for the duration of the test run rather than re-fetched for every request. `qa_token` handles this automatically — it checks the cache first and only makes a network call on miss or expiry (with a 60-second safety margin before the real TTL).

**Cleanup**: The `trap 'rm -rf "$___QA_TMP"' EXIT` ensures everything is purged when the shell exits, whether the tests pass, fail, or are interrupted. No temp files survive the test run.

Generate them by running:

```bash
source .claude/skills/claude-api-qa-creator/generate_helpers.sh TARGET_DIR ENV_FILE_PATH
```

- `TARGET_DIR`: the target repo's `.claude/skills/claude-qa/` directory (create it if needed)
- `ENV_FILE_PATH`: relative path to the test env file (e.g., `src/.env.test`, `.env.test`, `test.env`)

If the repo has no env file, create a starter `env.test` with placeholder vars based on what you discovered in Phase 1.

---

### Phase 3 — Generate SKILL.md

This is the core output. The generated SKILL.md must be self-contained — when a future Claude session loads it via `/claude-qa`, it should be able to test the API without reading this meta-skill.

Use the template at `.claude/skills/claude-api-qa-creator/templates/SKILL_TEMPLATE.md` as a starting point. Fill in every `{{placeholder}}` with repo-specific values from Phase 1.

#### Structure of the generated SKILL.md

The generated skill must contain these sections, adapted to the target repo:

**Step 1 — Ensure services are running**
- Health check command (curl to health endpoint, or equivalent)
- Start command if services are down
- Timeout and retry logic
- List of services with ports

**Step 2 — Discover endpoints**
- Where to find the API spec (OpenAPI path, or how to enumerate from code)
- Command to regenerate docs if applicable
- Recipe to extract a single endpoint's details (jq for OpenAPI, or grep for decorators)
- Service routing table (port -> service -> spec location)

**Step 3 — Load test configuration**
- Which env file to source
- Identity groups (explain what each set of credentials is for)
- How to find a variable if unsure

**Step 4 — Build and execute**
- Helper function reference (how to use qa_hmac, qa_curl, qa_assert, qa_validate_response)
- Auth pattern catalog — for EACH pattern found in Phase 1, document:
  - Pattern name and description
  - Which endpoints use it
  - Which env vars / credentials are needed
  - Example request using helpers
- Chaining multi-step flows (extracting values between requests)
- Negative test requirements (one per auth pattern, mandatory)
- CRUD lifecycle pattern (if applicable)
- Idempotency checks (if create endpoints exist)
- Response timing baselines

**Step 5 — Validate documentation matches code** (if OpenAPI exists)
- How to compare annotations/decorators to handler code
- What to check: response types, request types, status codes, route paths
- How to fix mismatches

**Step 6 — Report results**
- Status + pass/fail per request
- Key fields (truncate tokens)
- Error details on non-2xx
- Summary table for multi-step flows
- Failure categorization (bug, doc mismatch, external dependency, test data)

**Step 7 — Self-evaluation**
- Evaluate each step for friction, coverage gaps, wasted effort
- Flag false negatives/positives
- Suggest improvements to the skill, env file, or codebase

**Troubleshooting**
- One section per common failure mode observed or anticipated
- Include diagnostic commands (DB queries, env checks, route comparisons)

#### Writing guidelines for the generated SKILL.md

- Be specific to THIS repo — name real files, real env vars, real endpoints
- Include working examples using the helpers, not pseudocode
- Document auth patterns with the actual header names and helper function calls
- Keep it under 500 lines if possible — use the helper scripts for complexity
- Explain the *why* behind each step so future Claude can adapt when things change
- Do NOT include generic advice — every sentence should be actionable for this repo

---

### Phase 4 — Validate the generated skill

After generating all files:

1. Verify the skill directory structure is complete
2. Source the helpers and confirm they load without errors
3. Run a smoke test: pick one endpoint, execute the full flow (health check -> discover -> test -> report)
4. Fix any issues found

If the smoke test passes, the skill is ready.

---

### Phase 5 — Summary

Present to the user:
- What was generated (file list with brief descriptions)
- API surface discovered (endpoint count, auth patterns, services)
- What the skill covers vs. known gaps (external dependencies, missing test data)
- Suggested next steps (populate env vars, add test data to DB, etc.)

## Important notes

- The generated skill must work standalone — it should not reference or depend on this meta-skill
- Helper scripts use only `bash`, `curl`, `python3`, `openssl`, and `jq` — no other dependencies
- The generated SKILL.md frontmatter must be:
  ```yaml
  ---
  name: claude-qa
  description: "QA test {{service_name}} API endpoints against locally running services. Starts services if needed, fires curl requests, validates responses, and reports results."
  tools: Read, Bash, Grep, Glob
  ---
  ```
- Never hardcode secrets in the SKILL.md — always reference env vars
- If the repo uses GraphQL instead of REST, adapt the endpoint discovery and request patterns accordingly (POST to /graphql with query bodies)
- If there is no OpenAPI spec, the skill should discover endpoints by reading route registration code directly