# Structure

Project architecture and file organization for agent-tools (.NET agents).

## Repo Root

```
agent-tools/
├── .claude/
│   └── CLAUDE.md                        # Thin shim → docs/RULES.md, docs/STRUCTURE.md
├── .github/
│   └── copilot-instructions.md          # Thin shim → docs/RULES.md, docs/STRUCTURE.md
├── AGENTS.md                            # Thin shim → docs/RULES.md, docs/STRUCTURE.md
├── README.md                            # Skills installation, agent overview
└── agents/dotnet/                       # .NET agent workspace (see below)
```

All three shim files (CLAUDE.md, copilot-instructions.md, AGENTS.md) point to the canonical docs below. Do not duplicate rules or structure in the shims.

## Solution Layout

```
agents/dotnet/
├── docs/
│   ├── RULES.md                       # Technical constraints, agent patterns, rejected patterns
│   └── STRUCTURE.md                   # This file
├── src/
│   ├── Directory.Build.props          # Shared project settings (TFM, nullable, test auto-wiring)
│   ├── Directory.Packages.props       # Central Package Management — all NuGet versions here
│   ├── Test.Build.props               # Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
│   ├── Agent.SDK/                     # Shared library: logging bootstrap, telemetry, model config
│   │   ├── Configuration/
│   │   │   ├── AgentModelOptions.cs    # Lightweight model config bound from appsettings.json
│   │   │   └── EndpointHealthCheck.cs  # GET /v1/models validation before agent run
│   │   ├── Logging/
│   │   │   └── AgentLogging.cs        # Serilog bootstrap (Configure + CreateLoggerFactory)
│   │   ├── Telemetry/
│   │   │   ├── ActivityExtensions.cs  # Null-safe fluent extensions on Activity?
│   │   │   └── AgentTrace.cs          # Instance-based ActivitySource factory
│   │   └── Agent.SDK.csproj
│   ├── CrimeSceneInvestigator/        # Agent: markdown directory → structured context map
│   │   ├── Telemetry/
│   │   │   └── CsiTelemetry.cs        # CsiTrace (AgentTrace instance) + CsiMetrics (Meter + instruments)
│   │   ├── Tools/
│   │   │   └── FileTools.cs           # File system tools (list, read, extract, write)
│   │   ├── AgentCommandSetup.cs       # System.CommandLine definitions (Argument, Options, RootCommand)
│   │   ├── AgentInCommand.cs          # Agent domain logic (config resolution, pipeline, execution)
│   │   ├── appsettings.json           # Serilog overrides, Models:{key} config sections
│   │   ├── CrimeSceneInvestigator.csproj
│   │   ├── Program.cs                 # Thin bootstrap: config, logging, CLI invoke, flush
│   │   └── SystemPrompt.cs            # Parameterized system prompt builder
│   └── CrimeSceneInvestigator.Tests/  # Unit + integration tests for FileTools and SystemPrompt
│       ├── CrimeSceneInvestigator.Tests.csproj
│       ├── EndpointHealthCheckTests.cs
│       ├── FileToolsTests.cs
│       ├── IntegrationFileToolsTests.cs
│       └── SystemPromptTests.cs
├── .github/
│   └── copilot-instructions.md        # Thin shim (workspace-scoped) → docs/RULES.md, docs/STRUCTURE.md
├── .editorconfig                      # Code style enforcement
├── AgentTools.slnx                    # Solution manifest
├── global.json                        # SDK pin
├── nuget.config                       # Package source mapping
└── README.md                          # Quick start, conventions, adding new agents
```

## Projects

### Agent.SDK

Shared class library. Contains reusable infrastructure that every agent needs but that doesn't belong in each agent's codebase. This is the **one exception** to the "one agent, one project" rule — it exists because logging bootstrap, telemetry primitives, and model configuration are pure plumbing with zero agent-specific logic.

| File | Purpose |
|------|---------|
| `Configuration/AgentModelOptions.cs` | Sealed record bound from `Models:{key}` in `appsettings.json`. Static `Resolve(IConfiguration, configKey?)` does section lookup with `"default"` fallback. |
| `Configuration/EndpointHealthCheck.cs` | Static `ValidateAsync` calls `GET /v1/models` on the configured endpoint. Verifies reachability and that the configured model is loaded. Returns `HealthCheckResult` record. |
| `Logging/AgentLogging.cs` | Static `Configure(IConfiguration?)` bootstraps Serilog with console output. `ReadFrom.Configuration` applies overrides from `appsettings.json`. `CreateLoggerFactory()` bridges Serilog to `ILoggerFactory`. |
| `Telemetry/AgentTrace.cs` | Instance-based `AgentTrace(string sourceName)` — each agent creates one with its own `ActivitySource` name. `StartSpan` helper returns `Activity?`. |
| `Telemetry/ActivityExtensions.cs` | Null-safe fluent extensions: `WithTag`, `RecordError`, `SetSuccess` — borrowed from Continuum Engine. |

**Depends on:** Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Configuration.Binder, Serilog, Serilog.Extensions.Logging, Serilog.Settings.Configuration, Serilog.Sinks.Console

### CrimeSceneInvestigator

Console agent. Scans a markdown directory and produces a structured context map (`CONTEXT.md`) using M.E.AI tool calling.

| File | Purpose |
|------|---------|
| `Program.cs` | Thin bootstrap: builds `IConfiguration` from `appsettings.json` (CWD-based), configures logging, creates `AgentInCommand` with `ILogger<T>` + `IConfiguration`, invokes CLI |
| `AgentCommandSetup.cs` | System.CommandLine `RootCommand` factory — `--config-key` selects `Models:{key}` section, `--output` for output path |
| `AgentInCommand.cs` | Record with `ILogger<AgentInCommand>` + `IConfiguration`. `SetupAsync` resolves config, validates endpoint, registers tools. `RunAsync` builds pipeline and executes agent. |
| `SystemPrompt.cs` | Static `Build(targetPath, outputPath)` — returns the full system prompt with workflow steps and output format |
| `Tools/FileTools.cs` | Four static tools: `ListMarkdownFiles`, `ReadFileContent`, `ExtractStructure`, `WriteOutput`. Path-sandboxed to `RootDirectory`. |
| `Telemetry/CsiTelemetry.cs` | `CsiTrace` — static `AgentTrace` instance named `"CrimeSceneInvestigator"`. `CsiMetrics` — `Meter` with `csi.*` instruments. |
| `appsettings.json` | Serilog overrides + `Models:{key}` sections (`default` = chat model, `embedding` = retrieval model) |

**Depends on:** Agent.SDK (project ref), Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, Microsoft.Extensions.Configuration.Json, OpenAI, System.CommandLine

### CrimeSceneInvestigator.Tests

Unit and integration tests for tool methods and system prompt builder.

| File | Purpose |
|------|---------|
| `EndpointHealthCheckTests.cs` | Tests health check against fake HTTP responses — model loaded, missing, endpoint errors, case-insensitive matching |
| `FileToolsTests.cs` | Tests all four tool methods — valid inputs, edge cases, path traversal rejection |
| `IntegrationFileToolsTests.cs` | Integration tests running tools against the real agent-tools repo root |
| `SystemPromptTests.cs` | Verifies built prompt contains tool names, output format, and runtime paths |

**Depends on:** CrimeSceneInvestigator (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

## Conventions

- **One agent, one project.** Each agent is a standalone console app in its own directory under `src/`.
- **Agent.SDK is the exception.** Shared logging bootstrap, telemetry primitives, model configuration, and endpoint health checks live here. Agent-specific logic never goes in the SDK — only plumbing that every agent would otherwise duplicate identically.
- **Tools in `Tools/`.** Static classes with `[Description]` attributes. Registered via `AIFunctionFactory.Create` in `SetupAsync`.
- **Telemetry in `Telemetry/`.** Agent-specific `AgentTrace` instance + `Meter` class. Zero overhead when no listener is attached.
- **System prompt is testable code.** Static `Build` method with runtime parameters. Unit-tested for expected content.
- **Test auto-wiring.** Projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`.
- **Configuration via `appsettings.json`.** Log-level overrides and runtime settings live in config, not hardcoded in bootstrap code.
