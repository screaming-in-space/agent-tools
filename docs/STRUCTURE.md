# Structure

Project architecture and file organization for agent-tools (.NET agents).

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
│   ├── Agent.SDK/                     # Shared library: logging bootstrap, telemetry primitives
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
│   │   ├── AgentInCommand.cs          # Agent domain logic (CLI resolution, pipeline, execution)
│   │   ├── appsettings.json           # Serilog overrides and runtime configuration
│   │   ├── CrimeSceneInvestigator.csproj
│   │   ├── Program.cs                 # Thin bootstrap: config, logging, CLI invoke, flush
│   │   └── SystemPrompt.cs            # Parameterized system prompt builder
│   └── CrimeSceneInvestigator.Tests/  # Unit tests for FileTools and SystemPrompt
│       ├── CrimeSceneInvestigator.Tests.csproj
│       ├── FileToolsTests.cs
│       └── SystemPromptTests.cs
├── .github/
│   └── copilot-instructions.md        # AI agent project identity and philosophy
├── .editorconfig                      # Code style enforcement
├── AgentTools.slnx                    # Solution manifest
├── global.json                        # SDK pin
├── nuget.config                       # Package source mapping
└── README.md                          # Quick start, conventions, adding new agents
```

## Projects

### Agent.SDK

Shared class library. Contains reusable infrastructure that every agent needs but that doesn't belong in each agent's codebase. This is the **one exception** to the "one agent, one project" rule — it exists because logging bootstrap and telemetry primitives are pure plumbing with zero agent-specific logic.

| File | Purpose |
|------|---------|
| `Logging/AgentLogging.cs` | Static `Configure(IConfiguration?)` bootstraps Serilog with console output. `ReadFrom.Configuration` applies overrides from `appsettings.json`. `CreateLoggerFactory()` bridges Serilog to `ILoggerFactory`. |
| `Telemetry/AgentTrace.cs` | Instance-based `AgentTrace(string sourceName)` — each agent creates one with its own `ActivitySource` name. `StartSpan` helper returns `Activity?`. |
| `Telemetry/ActivityExtensions.cs` | Null-safe fluent extensions: `WithTag`, `RecordError`, `SetSuccess` — borrowed from Continuum Engine. |

**Depends on:** Serilog, Serilog.Extensions.Logging, Serilog.Settings.Configuration, Serilog.Sinks.Console

### CrimeSceneInvestigator

Console agent. Scans a markdown directory and produces a structured context map (`CONTEXT.md`) using M.E.AI tool calling.

| File | Purpose |
|------|---------|
| `Program.cs` | Thin bootstrap: builds `IConfiguration` from `appsettings.json`, configures logging, creates `AgentInCommand` with `ILogger<T>`, invokes CLI |
| `AgentCommandSetup.cs` | System.CommandLine `RootCommand` factory — accepts the agent action as `Func<ParseResult, CancellationToken, Task<int>>` |
| `AgentInCommand.cs` | Record with `ILogger<AgentInCommand>`. Resolves CLI options, falls back to `CSI_*` env vars, builds `IChatClient` pipeline, runs agent |
| `SystemPrompt.cs` | Static `Build(targetPath, outputPath)` — returns the full system prompt with workflow steps and output format |
| `Tools/FileTools.cs` | Four static tools: `ListMarkdownFiles`, `ReadFileContent`, `ExtractStructure`, `WriteOutput`. Path-sandboxed to `RootDirectory`. |
| `Telemetry/CsiTelemetry.cs` | `CsiTrace` — static `AgentTrace` instance named `"CrimeSceneInvestigator"`. `CsiMetrics` — `Meter` with `csi.*` instruments. |
| `appsettings.json` | Serilog overrides (silences `System.Net.Http`, `Microsoft` at Warning level) |

**Depends on:** Agent.SDK (project ref), Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, Microsoft.Extensions.Configuration.Json, OpenAI, System.CommandLine

### CrimeSceneInvestigator.Tests

Unit tests for tool methods and system prompt builder.

| File | Purpose |
|------|---------|
| `FileToolsTests.cs` | Tests all four tool methods — valid inputs, edge cases, path traversal rejection |
| `SystemPromptTests.cs` | Verifies built prompt contains tool names, output format, and runtime paths |

**Depends on:** CrimeSceneInvestigator (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

## Conventions

- **One agent, one project.** Each agent is a standalone console app in its own directory under `src/`.
- **Agent.SDK is the exception.** Shared logging bootstrap and telemetry primitives live here. Agent-specific logic never goes in the SDK — only plumbing that every agent would otherwise duplicate identically.
- **Tools in `Tools/`.** Static classes with `[Description]` attributes. Registered via `AIFunctionFactory.Create`.
- **Telemetry in `Telemetry/`.** Agent-specific `AgentTrace` instance + `Meter` class. Zero overhead when no listener is attached.
- **System prompt is testable code.** Static `Build` method with runtime parameters. Unit-tested for expected content.
- **Test auto-wiring.** Projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`.
- **Configuration via `appsettings.json`.** Log-level overrides and runtime settings live in config, not hardcoded in bootstrap code.
