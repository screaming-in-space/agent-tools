# Agents

Standalone .NET agents for AI-assisted development workflows. Each agent is a console app with a narrow tool set and a specific job.

## Architecture

```
agents/dotnet/
├── global.json                    SDK version pin
├── nuget.config                   Package source mapping
├── .editorconfig                  Code style enforcement
├── AgentTools.slnx                Solution manifest
└── src/
    ├── Directory.Build.props      Shared project settings (TFM, nullability, test auto-wiring)
    ├── Directory.Packages.props   Central Package Management - all versions here
    ├── Test.Build.props           Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
    ├── Agent.SDK/                 Shared: logging, telemetry, model config, file/code/git tools
    ├── Agent.SDK.Tests/           Unit + integration tests for Agent.SDK
    ├── CrimeSceneInvestigator/    Multi-scanner codebase intelligence agent
    └── CrimeSceneInvestigator.Tests/
```

## Crime Scene Investigator (CSI)

CSI scans a codebase and produces structured context files in `./context/` for consumption by other LLMs (Claude, GPT, Copilot).

### Output Files

| File | Scanner | Description |
|------|---------|-------------|
| `MAP.md` | Markdown | Context map of all markdown files — index, themes, cross-references, reading order |
| `RULES.md` | Rules | Design principles, stack versions, Do/Don't patterns, rejected patterns, hard constraints |
| `STRUCTURE.md` | Structure | Project dependency graph, directory tree, architecture classification |
| `QUALITY.md` | Quality | Per-project health grades, hotspots, anti-patterns, editorconfig conformance |
| `JOURNAL.md` | Journal | Development journal index with daily entries from git history |
| `journal/*.md` | Journal | Daily entries: work completed, decisions, patterns, open questions |
| `DONE.md` | Done | Completion checklist — what exists, what's missing |
| `REASONING.md` | All | Full reasoning trace organized by scanner (model, duration, tool calls, thinking) |

### Scanner Pipeline

CSI runs scanners sequentially, each with its own system prompt and tool set:

```
1. Planner     → assigns best model to each scanner (if multiple models available)
2. Markdown    → MAP.md        (FileTools: list/read/extract/write)
3. Rules       → RULES.md      (CodeCommentTools: comments, patterns + FileTools)
4. Structure   → STRUCTURE.md  (StructureTools: .csproj parsing, dependency graph)
5. Quality     → QUALITY.md    (QualityTools: Roslyn C# analysis, heuristics)
6. Journal     → JOURNAL.md    (GitTools: LibGit2Sharp log, diff, stats)
7. Done        → DONE.md       (aggregates all prior scanner results)
```

### Model Planner

When multiple model configurations are defined and loaded, CSI runs a **planner step** that assigns the best model to each scanner based on complexity:

- **Light** scanners (Markdown, Structure) → smaller, faster models
- **Heavy** scanners (Rules, Quality) → larger models with better function-calling
- **Medium** scanners (Journal, Done) → mid-range models

The planner queries `GET /v1/models` to see what's loaded, cross-references with `appsettings.json` configs, and produces a model assignment. If only one model is available, the planner step is skipped.

## Quick Start

```bash
# Build everything
dotnet build AgentTools.slnx

# Run tests
dotnet test AgentTools.slnx

# Run CSI (all scanners, default model)
dotnet run --project src/CrimeSceneInvestigator -- <directory>

# Run with a specific model config
dotnet run --project src/CrimeSceneInvestigator -- <directory> --config-key gemma-26b

# Run specific scanners only
dotnet run --project src/CrimeSceneInvestigator -- <directory> --scan markdown,structure

# Run headless (plain log output, no Spectre UI)
dotnet run --project src/CrimeSceneInvestigator -- <directory> --headless

# Publish as a single-file self-contained .exe
dotnet publish src/CrimeSceneInvestigator -c Release -r win-x64 -o publish/
```

### CLI Options

| Option | Description |
|--------|-------------|
| `<directory>` | Target directory to scan (required) |
| `--config-key <key>` | Model config section in appsettings.json (default: `"default"`) |
| `--output <path>` | Custom output path for MAP.md (default: `<dir>/context/MAP.md`) |
| `--scan <list>` | Comma-separated scanners: `markdown,comments,structure,journal` |
| `--headless` | Disable rich terminal UI, use plain Serilog output |

### Model Configuration

Defined in `appsettings.json` under `Models:{key}`. Multiple configs allow the planner to assign different models to different scanners:

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
    "gemma-26b": {
      "Endpoint": "http://localhost:1234/v1",
      "ApiKey": "no-key",
      "Model": "gemma-4-26b-a4b-it",
      "Temperature": 0.3,
      "MaxOutputTokens": 4096
    }
  }
}
```

### Scanner Toggle Configuration

Enable/disable scanners in `appsettings.json`:

```json
{
  "AgentInCommand": {
    "ScanMarkdown": true,
    "ScanCodeComments": true,
    "ScanCodePattern": true,
    "ScanGitHistory": true
  }
}
```

## Conventions

Follows the same patterns as [Continuum Engine](https://github.com/screaming-in-space/continuum-engine):

- **.NET 10 / C# 14.0** - file-scoped namespaces, primary constructors, braces always required
- **Central Package Management** - versions in `Directory.Packages.props`, never in individual `.csproj` files
- **Agent.SDK** - shared library for logging, telemetry, model config, endpoint health checks, and reusable tools (FileTools, CodeCommentTools, StructureTools, QualityTools, GitTools)
- **`appsettings.json`** - Serilog overrides and runtime configuration. No hardcoded log-level overrides.
- **xUnit + NSubstitute** - `Assert.*` assertions, `Method_Condition_Behavior` naming, one test class per file
- **Test auto-wiring** - projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`

## Adding a New Agent

1. Create `src/NewAgent/NewAgent.csproj` - `<ProjectReference>` to Agent.SDK, plus M.E.AI packages without versions (CPM owns them)
2. Add any new package versions to `src/Directory.Packages.props`
3. Create `src/NewAgent/appsettings.json` - Serilog overrides, `<Content CopyToOutputDirectory="PreserveNewest" />`
4. Create `src/NewAgent.Tests/NewAgent.Tests.csproj` - only needs `<ProjectReference>` to the agent
5. Test infrastructure (xUnit, NSubstitute, coverlet) is auto-imported by `Test.Build.props`

Requires .NET 10 SDK and an OpenAI-compatible endpoint (e.g., [LM Studio](https://lmstudio.ai) at `http://localhost:1234/v1`).
