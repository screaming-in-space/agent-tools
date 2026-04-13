# Agent Tools

Shared [Claude Code](https://claude.ai/claude-code) skills and standalone .NET 10 agents for AI-assisted development workflows.

## Layout

```
agent-tools/
├── .claude/CLAUDE.md              Thin shim → context/RULES.md, context/STRUCTURE.md
├── agents/dotnet/                 .NET agent workspace (projects, tests)
├── AgentTools.slnx                Solution manifest (all projects)
├── benchmarks/                    ModelBoss output (BENCHMARK.md)
├── context/
│   ├── RULES.md                   Technical constraints, coding patterns, rejected patterns
│   ├── STRUCTURE.md               Project architecture, directory tree, file map
│   ├── models/                    Model capability profiles for planner evaluation
│   └── gpu/                       GPU capability profiles for model-fit analysis
├── skills/                        Shared Claude Code skills
│   ├── agentify/                  Bootstrap repos with ML context files
│   ├── claude-api-qa-creator/     Generate QA helpers from repo analysis
│   └── install-skills.sh          Skill installer (local or global)
└── README.md                      This file
```

## Agents

| Agent | Description |
|-------|-------------|
| [`CrimeSceneInvestigator`](agents/dotnet/src/CrimeSceneInvestigator/) | Multi-scanner codebase intelligence agent. Scans a git repo and produces structured context files for LLM consumption. |
| [`ModelBoss`](agents/dotnet/src/ModelBoss/) | Benchmark local LLM models with hybrid scoring (deterministic + LLM-as-judge). Supports multi-turn and context-window benchmarks. Produces ranked scorecards. |

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An OpenAI-compatible local endpoint — [LM Studio](https://lmstudio.ai) or [Ollama](https://ollama.com)
- At least one model loaded at the endpoint (default: `http://localhost:1234/v1`)

All commands run from the **repo root** (`agent-tools/`).

### CrimeSceneInvestigator

Scans a git repo and produces structured context files (MAP.md, RULES.md, STRUCTURE.md, QUALITY.md, JOURNAL.md) for LLM consumption.

```bash
# Scan the current repo with the default model — interactive Spectre tree UI
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator -- .

# Scan a different repo
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator -- /path/to/repo

# Headless mode (CI, piping)
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator -- . --headless

# Use a specific model config and only run selected scanners
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator -- . --config-key gemma --scan markdown,structure,rules

# Override the model name directly (bypasses the LLM planner)
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator -- . --model gemma-4-31b-it
```

| Option | Description |
|--------|-------------|
| `<directory>` | Target directory to scan (must be inside a git repo) |
| `--config-key` | Model config section in `appsettings.json` (default: `"default"`) |
| `--model` | Override model name for all scanners (bypasses planner) |
| `--scan` | Comma-separated scanners: `markdown,rules,structure,quality,journal` |
| `--headless` | Disable rich terminal UI, use plain log output |

### ModelBoss

Benchmarks local LLM models with hybrid scoring: deterministic accuracy checks plus an LLM-as-judge quality pass (MT-Bench inspired). Supports single-turn, multi-turn conversation, and RULER-inspired context-window benchmarks. Produces ranked scorecards with composite scores.

```bash
# Benchmark all configured models — interactive panel UI with streaming tokens
dotnet run --project agents/dotnet/src/ModelBoss

# Quick single-model smoke test (1 category, 1 iteration)
dotnet run --project agents/dotnet/src/ModelBoss -- --models default --category instruction_following --iterations 1

# Benchmark two models head-to-head
dotnet run --project agents/dotnet/src/ModelBoss -- --models default,gemma-26b

# Run just the reasoning suite with 5 iterations
dotnet run --project agents/dotnet/src/ModelBoss -- --category reasoning --iterations 5

# Headless mode — good for CI or piping output
dotnet run --project agents/dotnet/src/ModelBoss -- --headless

# Write results to a custom directory
dotnet run --project agents/dotnet/src/ModelBoss -- --output ./my-results --headless
```

The interactive UI renders a panel per test showing the model's thinking process, response output, accuracy checks, and metrics. Each model gets a scorecard summary at the end. When benchmarking 2+ models, the best-performing model serves as an LLM-as-judge to evaluate the others.

| Option | Description |
|--------|-------------|
| `--models` | Comma-separated config keys to benchmark (default: all configured models) |
| `--iterations` | Number of measured iterations per prompt (default: `3`) |
| `--category` | Benchmark category: `instruction_following`, `extraction`, `markdown_generation`, `reasoning`, `multi_turn`, `context_window`, `all` (default: `all`) |
| `--output` | Output directory for benchmark reports (default: `<repo-root>/benchmarks/`) |
| `--repo-root` | Repository root for loading model/GPU registries (default: auto-detect) |
| `--headless` | Disable rich terminal UI, use plain log output |

### Output storage

Each agent writes to a default output directory relative to the repo root. Override with `--output`.

| Agent | Default output | Files produced |
|-------|----------------|----------------|
| CrimeSceneInvestigator | `<target>/context/` | `MAP.md`, `RULES.md`, `STRUCTURE.md`, `QUALITY.md`, `JOURNAL.md`, `DONE.md` |
| ModelBoss | `<repo-root>/benchmarks/` | `BENCHMARK.md` — ranked scorecards with speed, accuracy, LLM-as-judge quality, and composite scores |

Output directories are created automatically if they don't exist. Benchmark results are overwritten on each run — commit or rename previous reports to preserve history.

### Model configuration

Both agents read model configs from their `appsettings.json` under `Models:{key}`. Each key becomes a selectable model via CLI options.

```json
{
  "Models": {
    "default": {
      "Endpoint": "http://localhost:1234/v1",
      "ApiKey": "no-key",
      "Model": "unsloth/nvidia-nemotron-3-nano-4b",
      "Temperature": 0.3,
      "MaxOutputTokens": 4096
    },
    "gemma": {
      "Endpoint": "http://localhost:1234/v1",
      "ApiKey": "no-key",
      "Model": "gemma-4-31b-it",
      "Temperature": 0.3,
      "MaxOutputTokens": 4096
    }
  }
}
```

Add as many model sections as needed — the `embedding` key is excluded from benchmarks automatically.

### Build & test

```bash
# Build everything
dotnet build

# Run unit tests (xUnit v3 — test projects are self-hosting executables)
dotnet run --project agents/dotnet/src/Agent.SDK.Tests
dotnet run --project agents/dotnet/src/CrimeSceneInvestigator.Tests
dotnet run --project agents/dotnet/src/ModelBoss.Tests

# Run only ModelBoss unit tests (skips integration tests that need LM Studio)
dotnet run --project agents/dotnet/src/ModelBoss.Tests -- -trait "Category!=Integration"
```

Tests use **xUnit v3** (`xunit.v3 3.2.2`). Integration tests in `ModelBoss.Tests` require LM Studio running with `unsloth/nvidia-nemotron-3-nano-4b` loaded — they skip automatically via `Assert.Skip` when the endpoint is unreachable.

## Skills

| Skill | Description |
|-------|-------------|
| [`agentify`](skills/agentify/) | Bootstrap a repo with ML context files (CLAUDE.md, copilot-instructions.md, AGENTS.md) as thin shims pointing to centralized docs |
| [`claude-api-qa-creator`](skills/claude-api-qa-creator/) | Generate QA helpers from repo analysis |

## Installation

### Into current repo (default)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/skills/install-skills.sh)
```

Installs skills into `.claude/skills/` in your current directory. Commit them to share with your team.

### Global (all repos)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/skills/install-skills.sh) --global
```

Installs skills into `~/.claude/skills/` so they're available in every repo.

### Update

Re-run the install command. Existing files are overwritten with the latest versions.

## Usage

After installing or updating skills, restart Claude Code for new skills to register.

```
/agentify
```

## Contributing

Add new skills under `skills/<skill-name>/SKILL.md`. Each skill must include frontmatter:

```yaml
---
name: skill-name
description: One-line description of what the skill does.
user-invocable: true
---
```
