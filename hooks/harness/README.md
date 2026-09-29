# harness

Claude Code hooks that retire the glue a session does by hand, hold every hook's output to a
budget, and let sessions on one machine share it without tripping over each other. They run by
themselves: nothing here is a skill anyone has to remember to call.

Built from the practices in the Continuum Engine repository, and written up on threadunsafe.dev:

- [Don't pay a frontier model to be a shell script.](https://threadunsafe.dev/dev-entries/glue-becomes-code)
- [Sessions that share a machine take leases.](https://threadunsafe.dev/dev-entries/sessions-take-leases)
- [The codebase remembers itself.](https://threadunsafe.dev/dev-entries/the-codebase-remembers-itself)

## Install

Node 18 or later, on macOS, Linux or Windows. From the agent-tools checkout, in a POSIX shell
(zsh, bash, Git Bash):

```bash
node hooks/harness/install.mjs ~/src/my-project
```

or in PowerShell:

```powershell
node hooksharnessinstall.mjs $HOMEsrcmy-project
```

It copies the hooks into `<project>/.claude/hooks/harness/` and merges `settings.json`'s hooks
block into `<project>/.claude/settings.json`. The merge only adds: existing keys, hooks and groups
stay, a hook already there isn't added twice, and a file that doesn't parse is reported and left
alone. The original is backed up to `.claude/.cache/harness/`.

| Flag | Does |
|---|---|
| `--local` | merges into `settings.local.json` instead |
| `--with-claude-md` | appends `claude-block.md` (15 lines: the ladder, the context budget, sharing the machine, spend) to `.claude/CLAUDE.md`, once |
| `--dry-run` | prints the merged settings and writes nothing |
| `--uninstall` | removes the harness hooks from the settings, and leaves the files |

Claude Code's file watcher picks up the new hooks in sessions already running. `SessionStart`
hooks run from the next session.

The merged hooks use exec form (`"command": "node"`, the script in `args`, with
`${CLAUDE_PROJECT_DIR}`), so no shell parses them: the same `settings.json` works for a
teammate on macOS and one on Windows, whether Claude Code runs its hooks through bash or
PowerShell.

## The hooks

| Hook | Events | Does | Silent when |
|---|---|---|---|
| `glue-detector.mjs` | Stop, UserPromptSubmit, SessionStart (startup) | Counts this session's shell commands by shape: the first line, with variable assignments, `cd` and `timeout` prefixes dropped, an inline script collapsed to `<script>`, and strings, paths, ids and numbers replaced, 80 characters at most. On the third repeat of one it appends a row to `GLUE.md`, and the next prompt tells Claude in one line (`Glue ×3 this session: \`…\` → rung 2, MCP tool or CLI verb`). A new session starts with the steps two or more sessions repeated, one line each, at most five. `GLUE.md` itself is never imported: it grows by a row per session per step | nothing repeats. Reading and editing files, `git`, and builds and tests are the work, not glue (`glue.ignore`) |
| `lease-gate.mjs` | PreToolUse (Bash, PowerShell), SessionStart | Maps a command to resources (`leases.resources`) and denies it, naming the holder, when another session holds one. At session start, lists the leases other sessions hold | no lease on the command's resources; no one else holds anything |
| `lease.mjs` | a command | `take <resource> [--for 30m] [--purpose "..."]`, `release <resource>`, `list` | - |
| `session-brief.mjs` | SessionStart (startup, clear, compact) | Injects only the latest handoff's open questions and next steps (the newest file in `brief.dir`), with its path. A resumed session already has it | there is no handoff folder |
| `context-budget.mjs` | SessionStart (startup) | Resolves the always-on set (every CLAUDE.md, its `@imports` to four hops, every rule without `paths:`), totals it, and says one line when it is over `contextBudget.maxTokens`, naming the three largest files. Also says when a harness hook was cut last session | under budget, and nothing was cut |
| `readme-on-touch.mjs` | PreToolUse (Read, Edit, Write, NotebookEdit) | Injects the README of the project that owns the file, once per project per session | the file has no owning project with a README |
| `compaction-rescue.mjs` | PreCompact, PostCompact, SessionStart (compact) | Saves the person's messages since the last compaction, word for word, and hands them back after it | no compaction |
| `local-llm.mjs` | a command | Pipes stdin to an OpenAI-compatible endpoint with every sampler set, saves the request body, refuses a reply from a model it didn't ask for, and `--battery` measures a model on ten jobs before it is trusted | - |

## Constraints

Every hook:

- is Node with no dependencies, and runs on macOS, Linux and Windows: paths go through `node:path`
  and `node:os`, and the lease folder is under the home directory on each;
- exits 0, silently, on any error: a broken hook degrades to silence and never breaks a session;
- runs in milliseconds (about 30 ms each on Linux and 40 to 60 ms on Windows, most of it Node
  starting). Transcripts are
  read backwards from the end, or forwards from where the last run stopped, never whole;
- puts text in front of the model only through `lib/emit.mjs`.

## Why everything goes through emit.mjs

Claude Code caps a hook's `additionalContext` (and its plain stdout) at 10,000 characters. Past
that it saves the text to a file in the session directory and shows the model the path and a
2,000-character preview, and it doesn't ask the model to read the file
([hooks reference](https://code.claude.com/docs/en/hooks), "JSON output"). A hook that grows past
the cap fails without a sound.

`emit.mjs` holds each hook to its budget (`budgets`, never above 9,000 characters whatever
`harness.json` says), cuts at a line boundary, and ends the cut with the path of the
full text on disk. Each emission is logged with its size, and `context-budget.mjs` reports at the
next session start any hook that had to be cut.

## Leases

A lease is a file in `~/.claude/harness/leases/` (`leases.dir` or `HARNESS_LEASE_DIR` to move it)
holding its owner, purpose and expiry. Created with `O_EXCL`, so of two sessions taking at once
exactly one wins, and taken over after expiry under a lock directory, so two sessions can't both
replace the same expired file. The owner is the Claude Code session: `CLAUDE_CODE_SESSION_ID`,
which Claude Code sets for hooks and for Bash and PowerShell commands alike, so `lease.mjs` run by
a session and the gate checking that session agree. A holder that dies lets go when its lease
expires.

A resource is project-scoped (one lease per checkout: `build`) or machine-scoped (one for
everyone: `stack`, `gpu`). A command matching one of its `except` patterns needs no lease: a build
into a scratch folder with `-o` or `--output` leaves the shared `bin` alone. The default
patterns are a start: a project's `harness.json` replaces them with its own `leases.resources`,
the commands its shared resources are touched by.

What needs no lease: cache files keyed by `session_id` (every hook here keeps its state that way),
one handoff file per session or feature, so two sessions never write the same file, tests on
isolated stores, and building to a scratch folder with `-o` when another session runs from `bin`.

## The ladder

`GLUE.md` names a rung for every step it records, cheapest first:

1. plain code, a script or a CLI verb;
2. an MCP tool, or a CLI verb, that returns one compact answer;
3. a local model, measured on the job with a battery: `node local-llm.mjs --battery`;
4. a paid model, only with the person's confirmation.

`local-llm.mjs` follows three rules from Continuum's `LOCAL_LLM.md`: send every sampler on every
request (an omitted one is the runtime's chat default, and LM Studio's repeat penalty of 1.1
punishes JSON); read the request on the wire (it is saved to
`.claude/.cache/harness/local-llm-request.json`, and printed with `--wire`); and check the
response's `model` field, because LM Studio answers a request for a model that isn't loaded with
one that is. It never loads a model: load one with the runtime's own tooling, and a big model
alone on the card.

## Configuration

Every default is in one place, `DEFAULTS` in `lib/common.mjs`. A project's `harness.json`, beside
its copy of the hooks, holds only what it changes: it ships as `{}`, objects merge key by key, and
an array (`glue.ignore`, `leases.resources`) replaces its default whole. A reinstall keeps the
project's `harness.json`.

| Key | Default | For |
|---|---|---|
| `budgets` | session brief 4,000 and README 6,000 characters; rescue 6,000; lease gate 1,000; glue brief 800; glue line and budget line 600 | each hook's output. `emit.mjs` holds any of them at 9,000, under Claude Code's 10,000 |
| `contextBudget.maxTokens` | 25,000 | when `context-budget.mjs` speaks. `HARNESS_CONTEXT_BUDGET` overrides it |
| `contextBudget.bytesPerToken` | 4 | the estimate. Measure yours with `test/proof-sessions.mjs --tokens` |
| `glue.repeats`, `glue.briefMinSessions`, `glue.briefLines` | 3, 2, 5 | when a step is glue, and which steps a new session hears about |
| `glue.file`, `glue.ignore` | `GLUE.md`; file reads and edits, `git`, builds and tests | where rows go, and what never counts |
| `leases.minutes`, `leases.dir` | 30; `~/.claude/harness/leases` | a lease's length unless `--for`, and the folder every session shares |
| `leases.resources` | `build` (project; not with `-o`), `stack` and `gpu` (machine) | name, scope, command `patterns`, `except` |
| `brief.dir`, `brief.headings` | `docs/journal`; open questions, next steps, handoff | where the handoff is |
| `readme.markers`, `readme.skipRoot` | `.csproj`, `.fsproj`, `package.json`, `go.mod`, `pyproject.toml`, `Cargo.toml`; true | what makes a folder a project |
| `localLlm` | LM Studio at `localhost:1234`, no model; temperature 0.2, top_p 0.95, top_k 40, min_p 0.05, repeat penalty 1.0, max_tokens 1,024, seed 42 | endpoint, model id (from `--list`), runtime, samplers |

`rules/example.md` is a rule scoped with `paths:`: copy it to `.claude/rules/` and change its
globs. `claude-block.md` is the CLAUDE.md block.

## Proof

```bash
node hooks/harness/test/smoke.mjs
```

```powershell
node hooksharness	estsmoke.mjs
```

Drives every hook as Claude Code would, with real payloads, in a throwaway project: 34 checks.
They pass on Windows (Node 25) and on Linux (Node 18.20, in a `node:18-alpine` container). macOS
is untested, and uses the same code paths as Linux.

`test/proof-sessions.mjs <project> [--tokens <repo>]` proves the harness inside real sessions.
With session A holding the `build` lease in `<project>`, it starts two headless sessions there:
B1 tries the build and must be refused by name, and reports the session-start context it got; B2
starts with the context budget lowered and reports what `context-budget.mjs` said. `--tokens`
measures a repository's always-on set in tokens, with and without it, on the same model.
